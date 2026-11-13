using System;
using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>Timed modifiers cards put on the player. See <see cref="FirstPersonMotor.ApplyModifier"/>.</summary>
    public enum MotorModifier
    {
        /// <summary>Gravity multiplier (Moon gravity ×0.25). The jump's take-off speed keeps the authored gravity.</summary>
        Gravity,
        /// <summary>Multiplier of every received impulse, knockback included (Lightness ×1.5).</summary>
        ReceivedImpulse,
        /// <summary>Limit of the falling speed in real m/s (Lightness). Use <see cref="FirstPersonMotor.ApplyFallSpeedLimit"/> for design units.</summary>
        FallSpeedLimit,
        /// <summary>Walking / sprinting speed multiplier (tar ×0.5).</summary>
        MoveSpeed
    }

    [DefaultExecutionOrder(-100)]
    public sealed class FirstPersonMotor : MonoBehaviour
    {
        [Header("Authored components")]
        public CharacterController characterController;
        public FirstPersonInput input;
        public PlayerControlGate control;
        public PlayerStamina stamina;
        public FirstPersonView view;
        [Header("Ground movement")]
        [Min(.1f)] public float walkSpeed = 4;
        [Min(.1f)] public float sprintSpeed = 6.5f;
        [Min(.1f)] public float acceleration = 18;
        [Min(.1f)] public float braking = 24;
        [Range(0, 1)] public float airControl = .32f;
        [Header("Jump")]
        [Min(.1f)] public float jumpHeight = 1.2f;
        [Min(.1f)] public float gravity = 25;
        [Min(0)] public float coyoteTime = .12f;
        [Min(0)] public float jumpBufferTime = .14f;
        [Min(1)] public float terminalSpeed = 40;
        [Tooltip("Gravity the design numbers (impulse I, fall thresholds) were specified for. Normalized impulses are " +
                 "scaled by sqrt(gravity / referenceGravity) so launch heights and safe-fall heights stay as designed " +
                 "while the actual gravity can be snappier.")]
        [Min(.1f)] public float referenceGravity = 9.81f;
        [Header("Ground contact")]
        [Tooltip("Longest drop (m) the body is pulled back down onto walkable ground it walked off, so it follows slopes and " +
                 "steps downhill instead of hopping from frame to frame. The reach per frame is the step offset, or the drop of " +
                 "the steepest walkable slope over the distance just moved, whichever is larger, capped by this value.")]
        [Min(0)] public float maxGroundSnap = .5f;
        [Tooltip("A touchdown is a landing (Landed: dip, landing sound, fall damage, mushroom bounce) only after a drop of at " +
                 "least this many metres below the highest point since the body left the ground...")]
        [Min(0)] public float minLandingDrop = .3f;
        [Tooltip("...or after at least this many seconds in the air.")]
        [Min(0)] public float minLandingAirTime = .2f;
        [Tooltip("Head bob, footsteps and the card hand keep treating the body as walking for this long after it lost the " +
                 "ground without being launched (no jump, bounce or upward push).")]
        [Min(0)] public float feedbackGraceTime = .1f;

        public Vector3 PlanarVelocity { get; private set; }
        public float Speed01 => Mathf.Clamp01(PlanarVelocity.magnitude / Mathf.Max(.1f, sprintSpeed));
        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public float VerticalSpeed => verticalSpeed;
        /// <summary>Seconds the body has been off the ground (0 while it stands).</summary>
        public float AirTime => IsGrounded ? 0 : airTime;
        /// <summary>
        /// Ground contact for presentation (head bob, footsteps, hand bob): <see cref="IsGrounded"/>, or only just off the
        /// ground (<see cref="feedbackGraceTime"/>) without having been launched. Gameplay keeps reading <see cref="IsGrounded"/>.
        /// </summary>
        public bool GroundedForFeedback => IsGrounded || (!ExternallyControlled && Time.time - lastGrounded <= feedbackGraceTime);
        /// <summary>
        /// Normal of the face under the capsule's axis while standing: a step's flat tread rather than the slanted normal of
        /// its edge, a ramp's surface on a ramp. Up in the air.
        /// </summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        /// <summary>Velocity factor between the authored gravity and the reference gravity of the design numbers.</summary>
        public float ReferenceScale => Mathf.Sqrt(gravity / referenceGravity);
        /// <summary>Real landing speed (m/s, positive). Raised after the motor has settled on the ground, so a handler may relaunch the body.</summary>
        public event Action<float> Landed;
        public event Action Jumped;
        float verticalSpeed, lastGrounded = float.NegativeInfinity, jumpBufferedUntil = float.NegativeInfinity;
        float lastJump = float.NegativeInfinity;
        bool hadAirTime;
        // Airborne phase: seconds since the body left the ground, and the highest point (or the height a fall from rest
        // needs for the current downward speed). Set to the body's height while it stands.
        float airTime, airPeakY;
        // Ground probes (snap capsule cast, edge check, face under the feet) share one buffer.
        const int MaxGroundHits = 8;
        const float EdgeProbeInset = .03f, EdgeProbeHeight = .06f;
        readonly RaycastHit[] groundHits = new RaycastHit[MaxGroundHits];
        int groundMaskLayer = -1, layerCollisionMask;
        Vector3 externalPlanar;
        Collider frameGround;
        readonly TimedModifier[] modifiers = new TimedModifier[4];
        // Horizontal normals of the walls (anything too steep to walk on) the capsule touched during this frame's move.
        const int MaxWallContacts = 8;
        readonly Vector3[] wallNormals = new Vector3[MaxWallContacts];
        int wallContacts;
        bool collectingWalls;

        /// <summary>World velocity the body carries into the next frame (walking + pushes + vertical). Zero vertical while standing.</summary>
        public Vector3 Velocity => PlanarVelocity + externalPlanar + Vector3.up * (IsGrounded && verticalSpeed < 0 ? 0 : verticalSpeed);
        /// <summary>The collider the body stood on after this frame's move; null in the air.</summary>
        public Collider GroundCollider { get; private set; }
        /// <summary>The script that drives the body instead of the motor (rope), or null.</summary>
        public IExternalMotorControl ExternalOwner { get; private set; }
        public bool ExternallyControlled => ExternalOwner != null;

        /// <summary>Current gravity multiplier (1 without Moon gravity).</summary>
        public float GravityScale => Modifier(MotorModifier.Gravity, 1);
        /// <summary>Current multiplier of received impulses (1 without Lightness).</summary>
        public float ImpulseScale => Modifier(MotorModifier.ReceivedImpulse, 1);
        /// <summary>Current walking speed multiplier (1 when nothing slows the player).</summary>
        public float MoveSpeedScale => Modifier(MotorModifier.MoveSpeed, 1) * Mathf.Max(0, StatusSpeedScale);
        /// <summary>
        /// A second, independent speed multiplier owned by a game layer's status system (the outing's Slow debuff), so it
        /// stacks with the timed modifiers instead of replacing them. 1 when nothing slows the player.
        /// </summary>
        public float StatusSpeedScale { get; set; } = 1;
        /// <summary>Current falling-speed limit in real m/s (∞ when none).</summary>
        public float FallSpeedLimit => Modifier(MotorModifier.FallSpeedLimit, float.PositiveInfinity);

        float Modifier(MotorModifier kind, float neutral) => modifiers[(int)kind].Current(Time.time, neutral);

        /// <summary>
        /// Starts or refreshes a timed modifier. Repeating refreshes the timer to the longer of the remaining and the
        /// new duration (never adds up); the new value replaces the old one (never multiplies). See <see cref="TimedModifier"/>.
        /// </summary>
        public void ApplyModifier(MotorModifier kind, float value, float seconds) => modifiers[(int)kind].Apply(value, seconds, Time.time);

        /// <summary>Lightness: limits falling to a design speed (m/s at <see cref="referenceGravity"/>), converted to real speed.</summary>
        public void ApplyFallSpeedLimit(float referenceSpeed, float seconds) => ApplyModifier(MotorModifier.FallSpeedLimit, referenceSpeed * ReferenceScale, seconds);

        public bool HasModifier(MotorModifier kind) => modifiers[(int)kind].IsActive(Time.time);
        public float ModifierRemaining(MotorModifier kind) => modifiers[(int)kind].Remaining(Time.time);
        public void ClearModifier(MotorModifier kind) => modifiers[(int)kind].Clear();

        /// <summary>Ends every card modifier (the outing is over).</summary>
        public void ClearModifiers()
        {
            for (int i = 0; i < modifiers.Length; i++) modifiers[i].Clear();
        }

        /// <summary>Replaces the whole world velocity (rope release, updraft). The horizontal part decays like a push.</summary>
        public void SetVelocity(Vector3 velocity)
        {
            PlanarVelocity = Vector3.zero;
            externalPlanar = new Vector3(velocity.x, 0, velocity.z);
            SetVerticalSpeed(velocity.y);
        }

        /// <summary>Replaces only the vertical speed (mushroom bounce). Positive leaves the ground.</summary>
        public void SetVerticalSpeed(float speed)
        {
            verticalSpeed = speed;
            if (speed > 0) { lastJump = Time.time; lastGrounded = float.NegativeInfinity; hadAirTime = true; IsGrounded = false; }
            else RaiseFallStart();
        }

        /// <summary>
        /// Moves the body to <paramref name="position"/> (capsule base) keeping its velocity, view and modifiers
        /// (Teleport, Swap). An external owner loses control first and its velocity is carried on.
        /// </summary>
        public void TeleportKeepingVelocity(Vector3 position)
        {
            RevokeExternalControl();
            // A fall in progress keeps counting from the same height above the body.
            airPeakY += position.y - transform.position.y;
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position = position;
            characterController.enabled = wasEnabled;
            if (view) view.ClearStepSmoothing();
        }

        /// <summary>Hands the body to a script (rope). Fails while another owner holds it. The motor stops moving the body.</summary>
        public bool TryClaimExternalControl(IExternalMotorControl owner)
        {
            if (owner == null || (ExternalOwner != null && ExternalOwner != owner)) return false;
            ExternalOwner = owner;
            IsSprinting = IsGrounded = false;
            lastGrounded = float.NegativeInfinity;
            GroundNormal = Vector3.up;
            return true;
        }

        /// <summary>Moves the body while it is externally controlled; collides like normal movement.</summary>
        public CollisionFlags MoveExternally(Vector3 delta) => characterController.enabled ? characterController.Move(delta) : CollisionFlags.None;

        /// <summary>The owner lets go: the motor takes over with this world velocity (m/s).</summary>
        public void ReleaseExternalControl(IExternalMotorControl owner, Vector3 velocity)
        {
            if (owner == null || ExternalOwner != owner) return;
            ExternalOwner = null;
            StartAirborne();
            SetVelocity(velocity);
            hadAirTime = true;
        }

        /// <summary>Takes control away from the current owner (it is told, and its velocity is kept).</summary>
        public void RevokeExternalControl()
        {
            var owner = ExternalOwner;
            if (owner == null) return;
            ExternalOwner = null;
            Vector3 velocity = owner is UnityEngine.Object alive && !alive ? Vector3.zero : owner.OnControlRevoked();
            StartAirborne();
            SetVelocity(velocity);
            hadAirTime = true;
        }

        /// <summary>Shared physics impulse API; game layers never implement their own movement. Scaled by <see cref="ImpulseScale"/>.</summary>
        public void ApplyImpulse(Vector3 velocity)
        {
            velocity *= ImpulseScale;
            externalPlanar += new Vector3(velocity.x, 0, velocity.z);
            verticalSpeed = velocity.y > 0 ? Mathf.Max(0, verticalSpeed) + velocity.y : verticalSpeed + velocity.y;
            if (velocity.y > 0) { lastJump = Time.time; lastGrounded = float.NegativeInfinity; hadAirTime = true; }
            else RaiseFallStart();
        }

        /// <summary>
        /// Applies a design-space velocity change (specified for <see cref="referenceGravity"/>). Heights and ranges
        /// match the design even when <see cref="gravity"/> differs.
        /// </summary>
        public void ApplyNormalizedImpulse(Vector3 velocity) => ApplyImpulse(velocity * ReferenceScale);

        /// <summary>Converts a real landing speed to the speed the design's fall-damage thresholds expect.</summary>
        public float ToReferenceSpeed(float speed) => speed / ReferenceScale;

        void Awake()
        {
            if (!characterController || !input || !control || !stamina || !view)
            { Debug.LogError("FirstPersonMotor needs its authored controller, input, control gate, stamina and view references.", this); enabled = false; }
            StartAirborne();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0 || !characterController.enabled) return;
            float now = Time.time;
            if (ExternalOwner != null)
            {
                // A destroyed owner gives the body back where it is.
                if (ExternalOwner is UnityEngine.Object owner && !owner) { ExternalOwner = null; hadAirTime = true; StartAirborne(); }
                else
                {
                    IsSprinting = IsGrounded = false; GroundCollider = null; GroundNormal = Vector3.up;
                    // Whatever the owner does with the body, a fall after it lets go counts from where it let go.
                    StartAirborne();
                    stamina.Tick(dt, false);
                    return;
                }
            }
            bool wasGrounded = IsGrounded;
            bool gameplay = control.GameplayActive && !control.CapturedThisFrame;
            bool grounded = characterController.isGrounded && now - lastJump > .08f;
            if (grounded)
            {
                lastGrounded = now;
                if (verticalSpeed < 0) verticalSpeed = -2;
            }
            else { hadAirTime = true; airTime += dt; }

            if (gameplay && input.JumpPressed) jumpBufferedUntil = now + jumpBufferTime;
            if (!gameplay) jumpBufferedUntil = float.NegativeInfinity;
            Vector2 move = gameplay ? input.Move : Vector2.zero;
            // On foot: standing, or only just off the ground without a jump (coyote time). Sprint and full acceleration
            // survive a frame without contact, so the sprint FOV and the running speed do not flicker on bumps.
            bool onFoot = grounded || now - lastGrounded <= coyoteTime;
            bool wantsSprint = gameplay && input.SprintHeld && move.y > .1f && move.sqrMagnitude > .1f;
            IsSprinting = wantsSprint && stamina.CanSprint && onFoot;
            float targetSpeed = (IsSprinting ? sprintSpeed : walkSpeed) * MoveSpeedScale;
            Vector3 target = transform.TransformDirection(new Vector3(move.x, 0, move.y)) * targetSpeed;
            float rate = move.sqrMagnitude > .01f ? acceleration : braking;
            if (!onFoot) rate *= airControl;
            if (!gameplay) { PlanarVelocity = Vector3.zero; target = Vector3.zero; }
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, target, rate * dt);

            if (gameplay && now <= jumpBufferedUntil && now - lastGrounded <= coyoteTime && stamina.TryJump())
            {
                verticalSpeed = Mathf.Sqrt(2 * gravity * jumpHeight);
                jumpBufferedUntil = lastGrounded = float.NegativeInfinity;
                lastJump = now; grounded = false; hadAirTime = true;
                IsSprinting = false;
                Jumped?.Invoke();
            }
            // Card modifiers: lighter gravity (Moon) and a falling-speed limit (Lightness). Fall damage keeps reading
            // the real landing speed.
            verticalSpeed = Mathf.Max(-Mathf.Min(terminalSpeed, FallSpeedLimit), verticalSpeed - gravity * GravityScale * dt);
            float impactSpeed = -verticalSpeed;
            var before = transform.position;
            externalPlanar = Vector3.MoveTowards(externalPlanar, Vector3.zero, (grounded ? 12 : 2) * dt);
            frameGround = null;
            wallContacts = 0;
            collectingWalls = true;
            CollisionFlags collisions = characterController.Move((PlanarVelocity + externalPlanar + Vector3.up * verticalSpeed) * dt);
            collectingWalls = false;
            // Measured right after the move: pulling the body down onto the ground below never becomes running speed.
            Vector3 moved = transform.position - before;
            if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            bool contact = (collisions & CollisionFlags.Below) != 0;
            // Downhill the ground falls away faster than one frame of gravity pulls the body: it would hop off every slope
            // and step. A body that stood last frame and was not launched (jump, bounce, upward push) is put back down.
            if (!contact && wasGrounded && grounded && verticalSpeed <= 0)
                contact = SnapToGround(new Vector2(moved.x, moved.z).magnitude);
            IsGrounded = contact && verticalSpeed <= 0;
            GroundCollider = IsGrounded ? frameGround : null;
            GroundNormal = IsGrounded ? FaceBelow() : Vector3.up;
            float y = transform.position.y;
            if (IsGrounded)
            {
                // Only a real fall lands: far enough below the highest point, or long enough in the air. Losing the
                // ground for a moment over a bump or an edge is no landing (no dip, landing sound or fall damage).
                bool fell = airPeakY - y >= minLandingDrop || airTime >= minLandingAirTime;
                bool landed = hadAirTime && impactSpeed > 2.5f && fell;
                hadAirTime = false;
                airTime = 0;
                airPeakY = y;
                verticalSpeed = -2;
                lastGrounded = now;
                // After settling: a landing handler (mushroom cap) may relaunch the body with SetVerticalSpeed.
                if (landed) Landed?.Invoke(impactSpeed);
            }
            else airPeakY = Mathf.Max(airPeakY, y);
            RemoveBlockedVelocity(new Vector3(moved.x, 0, moved.z) / dt);
            // Running into a wall does not spend stamina or drive running feedback.
            IsSprinting = IsSprinting && PlanarVelocity.sqrMagnitude > .25f;
            stamina.Tick(dt, IsSprinting);
        }

        /// <summary>
        /// Puts the body back down onto walkable ground within reach: the step offset, or the drop of the steepest walkable
        /// slope over <paramref name="run"/> (the horizontal distance just moved), whichever is larger, plus the skin, capped
        /// by <see cref="maxGroundSnap"/>. A real ledge is out of reach, so the body falls from it. True when it stands again.
        /// </summary>
        bool SnapToGround(float run)
        {
            var controller = characterController;
            float skin = controller.skinWidth;
            float steepest = Mathf.Tan(Mathf.Clamp(controller.slopeLimit, 0, 89) * Mathf.Deg2Rad);
            float reach = Mathf.Min(maxGroundSnap, Mathf.Max(controller.stepOffset, run * steepest) + skin);
            if (reach <= 0) return false;
            float radius = controller.radius;
            Vector3 centre = transform.TransformPoint(controller.center);
            Vector3 axis = Vector3.up * Mathf.Max(0, controller.height * .5f - radius);
            // Slightly thinner and a skin higher than the body, so a wall it leans on or ground it hovers over is not lost as
            // an overlap at the start of the sweep. The controller's own move stops at the real contact.
            Vector3 lift = Vector3.up * skin;
            int count = Physics.CapsuleCastNonAlloc(centre - axis + lift, centre + axis + lift, radius * .95f, Vector3.down,
                groundHits, reach + skin, GroundMask, QueryTriggerInteraction.Ignore);
            if (!NearestHit(count, out var hit)) return false;
            var ground = hit.collider;
            float distance = hit.distance;
            if (!Walkable(hit)) return false;
            // The sweep started a skin higher: moving its distance asks for a skin more than the gap, so the controller
            // surely touches down. Walls are not collected (collectingWalls is off): the snap never limits running speed.
            frameGround = null;
            CollisionFlags snapped = controller.Move(Vector3.down * Mathf.Max(distance, skin));
            if ((snapped & CollisionFlags.Below) == 0) return false;
            if (!frameGround) frameGround = ground;
            return true;
        }

        /// <summary>
        /// Walkable ground for the snap: a face no steeper than the slope limit, or the edge of a step, which the rounded
        /// capsule bottom touches with a slanted normal: then the face just behind the edge must be walkable.
        /// </summary>
        bool Walkable(RaycastHit hit)
        {
            float minUp = Mathf.Cos(characterController.slopeLimit * Mathf.Deg2Rad);
            if (hit.normal.y >= minUp) return true;
            if (hit.normal.y <= .05f) return false;
            var planar = new Vector3(hit.normal.x, 0, hit.normal.z).normalized;
            Vector3 origin = hit.point - planar * EdgeProbeInset + Vector3.up * EdgeProbeHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, EdgeProbeHeight * 2, GroundMask,
                QueryTriggerInteraction.Ignore);
            return NearestHit(count, out var face) && face.normal.y >= minUp;
        }

        /// <summary>The face straight under the capsule's axis (see <see cref="GroundNormal"/>); up when none is near.</summary>
        Vector3 FaceBelow()
        {
            var controller = characterController;
            float radius = controller.radius;
            Vector3 origin = transform.TransformPoint(controller.center) - Vector3.up * Mathf.Max(0, controller.height * .5f - radius);
            float length = radius + controller.skinWidth + Mathf.Max(controller.stepOffset, .3f) + .1f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, length, GroundMask, QueryTriggerInteraction.Ignore);
            return NearestHit(count, out var face) ? face.normal : Vector3.up;
        }

        /// <summary>Nearest hit in <see cref="groundHits"/>, skipping the body's own colliders and shapes a sweep starts in.</summary>
        bool NearestHit(int count, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                // A sweep reports shapes it starts inside at distance zero (with a made-up normal): never ground.
                if (hit.distance <= 0 || (found && hit.distance >= nearest.distance)) continue;
                var other = hit.collider;
                if (!other || other == characterController || other.transform.IsChildOf(transform)) continue;
                nearest = hit;
                found = true;
            }
            return found;
        }

        /// <summary>Layers the body collides with: the physics matrix for its layer plus the controller's include/exclude layers.</summary>
        int GroundMask
        {
            get
            {
                int layer = gameObject.layer;
                if (layer != groundMaskLayer)
                {
                    layerCollisionMask = 0;
                    for (int i = 0; i < 32; i++)
                        if (!Physics.GetIgnoreLayerCollision(layer, i)) layerCollisionMask |= 1 << i;
                    groundMaskLayer = layer;
                }
                return (layerCollisionMask | characterController.includeLayers.value) & ~characterController.excludeLayers.value;
            }
        }

        /// <summary>The airborne phase starts here: air time from zero, the fall measured from the current height.</summary>
        void StartAirborne()
        {
            airTime = 0;
            airPeakY = transform.position.y;
        }

        /// <summary>A body sent downward in the air lands as hard as one that fell from rest from the matching height.</summary>
        void RaiseFallStart()
        {
            if (IsGrounded || verticalSpeed >= 0) return;
            float pull = Mathf.Max(.01f, gravity * GravityScale);
            airPeakY = Mathf.Max(airPeakY, transform.position.y + verticalSpeed * verticalSpeed / (2 * pull));
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > .5f) frameGround = hit.collider;
            // Walls: everything too steep to walk on (a ceiling has no horizontal part worth keeping), only from the main
            // move: the ground snap's contacts (a step's riser, an edge) never limit the running speed.
            if (!collectingWalls || wallContacts >= MaxWallContacts
                || hit.normal.y >= Mathf.Cos(characterController.slopeLimit * Mathf.Deg2Rad)) return;
            var planar = new Vector3(hit.normal.x, 0, hit.normal.z);
            float length = planar.magnitude;
            if (length > .2f) wallNormals[wallContacts++] = planar / length;
        }

        /// <summary>
        /// Velocity stays what the player and pushes asked for; a wall only takes away the part that drove into it, and
        /// only as far as the capsule did not get through (a climbed step or a wall reached mid-frame keeps its speed).
        /// The capsule's displacement is never turned back into velocity: the controller pushing it out of a corner or a
        /// narrow gap is a one-frame correction, and carrying it on bounced the body from wall to wall.
        /// </summary>
        void RemoveBlockedVelocity(Vector3 actualPlanar)
        {
            if (wallContacts == 0) return;
            Vector3 allowed = PlanarVelocity + externalPlanar, push = externalPlanar;
            // Twice round: sliding off one wall can still drive into the other one of a corner.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < wallContacts; i++)
                {
                    Vector3 normal = wallNormals[i];
                    float limit = Mathf.Max(0, -Vector3.Dot(actualPlanar, normal));
                    float into = -Vector3.Dot(allowed, normal);
                    if (into > limit) allowed += normal * (into - limit);
                    float pushInto = -Vector3.Dot(push, normal);
                    if (pushInto > limit) push += normal * (pushInto - limit);
                }
            // Still driving into a wall that stopped it: the capsule sits in a crease between two walls and stays put.
            for (int i = 0; i < wallContacts; i++)
                if (-Vector3.Dot(actualPlanar, wallNormals[i]) < .01f && -Vector3.Dot(allowed, wallNormals[i]) > .01f)
                {
                    allowed = push = Vector3.zero;
                    break;
                }
            externalPlanar = push;
            PlanarVelocity = allowed - push;
        }

        /// <summary>Full pose reset (departure, back home): stops all motion and takes control from any external owner.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            RevokeExternalControl();
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, rotation.eulerAngles.y, 0));
            characterController.enabled = wasEnabled;
            PlanarVelocity = externalPlanar = Vector3.zero; verticalSpeed = 0;
            IsSprinting = IsGrounded = hadAirTime = false;
            GroundCollider = null;
            GroundNormal = Vector3.up;
            lastGrounded = lastJump = jumpBufferedUntil = float.NegativeInfinity;
            StartAirborne();
            input.Clear();
            view.ResetPose(rotation);
        }
    }
}
