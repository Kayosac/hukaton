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

        public Vector3 PlanarVelocity { get; private set; }
        public float Speed01 => Mathf.Clamp01(PlanarVelocity.magnitude / Mathf.Max(.1f, sprintSpeed));
        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public float VerticalSpeed => verticalSpeed;
        /// <summary>Velocity factor between the authored gravity and the reference gravity of the design numbers.</summary>
        public float ReferenceScale => Mathf.Sqrt(gravity / referenceGravity);
        /// <summary>Real landing speed (m/s, positive). Raised after the motor has settled on the ground, so a handler may relaunch the body.</summary>
        public event Action<float> Landed;
        public event Action Jumped;
        float verticalSpeed, lastGrounded = float.NegativeInfinity, jumpBufferedUntil = float.NegativeInfinity;
        float lastJump = float.NegativeInfinity;
        bool hadAirTime;
        Vector3 externalPlanar;
        Collider frameGround;
        readonly TimedModifier[] modifiers = new TimedModifier[4];

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
        public float MoveSpeedScale => Modifier(MotorModifier.MoveSpeed, 1);
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
        }

        /// <summary>
        /// Moves the body to <paramref name="position"/> (capsule base) keeping its velocity, view and modifiers
        /// (Teleport, Swap). An external owner loses control first and its velocity is carried on.
        /// </summary>
        public void TeleportKeepingVelocity(Vector3 position)
        {
            RevokeExternalControl();
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position = position;
            characterController.enabled = wasEnabled;
        }

        /// <summary>Hands the body to a script (rope). Fails while another owner holds it. The motor stops moving the body.</summary>
        public bool TryClaimExternalControl(IExternalMotorControl owner)
        {
            if (owner == null || (ExternalOwner != null && ExternalOwner != owner)) return false;
            ExternalOwner = owner;
            IsSprinting = IsGrounded = false;
            return true;
        }

        /// <summary>Moves the body while it is externally controlled; collides like normal movement.</summary>
        public CollisionFlags MoveExternally(Vector3 delta) => characterController.enabled ? characterController.Move(delta) : CollisionFlags.None;

        /// <summary>The owner lets go: the motor takes over with this world velocity (m/s).</summary>
        public void ReleaseExternalControl(IExternalMotorControl owner, Vector3 velocity)
        {
            if (owner == null || ExternalOwner != owner) return;
            ExternalOwner = null;
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
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0 || !characterController.enabled) return;
            float now = Time.time;
            if (ExternalOwner != null)
            {
                // A destroyed owner gives the body back where it is.
                if (ExternalOwner is UnityEngine.Object owner && !owner) { ExternalOwner = null; hadAirTime = true; }
                else { IsSprinting = IsGrounded = false; GroundCollider = null; stamina.Tick(dt, false); return; }
            }
            bool gameplay = control.GameplayActive && !control.CapturedThisFrame;
            bool grounded = characterController.isGrounded && now - lastJump > .08f;
            if (grounded)
            {
                lastGrounded = now;
                if (verticalSpeed < 0) verticalSpeed = -2;
            }
            else hadAirTime = true;

            if (gameplay && input.JumpPressed) jumpBufferedUntil = now + jumpBufferTime;
            if (!gameplay) jumpBufferedUntil = float.NegativeInfinity;
            Vector2 move = gameplay ? input.Move : Vector2.zero;
            bool wantsSprint = gameplay && input.SprintHeld && move.y > .1f && move.sqrMagnitude > .1f;
            IsSprinting = wantsSprint && stamina.CanSprint && grounded;
            float targetSpeed = (IsSprinting ? sprintSpeed : walkSpeed) * MoveSpeedScale;
            Vector3 target = transform.TransformDirection(new Vector3(move.x, 0, move.y)) * targetSpeed;
            float rate = move.sqrMagnitude > .01f ? acceleration : braking;
            if (!grounded) rate *= airControl;
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
            CollisionFlags collisions = characterController.Move((PlanarVelocity + externalPlanar + Vector3.up * verticalSpeed) * dt);
            if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            IsGrounded = (collisions & CollisionFlags.Below) != 0 && verticalSpeed <= 0;
            GroundCollider = IsGrounded ? frameGround : null;
            if (IsGrounded)
            {
                bool landed = hadAirTime && impactSpeed > 2.5f;
                hadAirTime = false;
                verticalSpeed = -2;
                lastGrounded = now;
                // After settling: a landing handler (mushroom cap) may relaunch the body with SetVerticalSpeed.
                if (landed) Landed?.Invoke(impactSpeed);
            }
            Vector3 actual = (transform.position - before) / dt;
            PlanarVelocity = new Vector3(actual.x, 0, actual.z) - externalPlanar;
            // Running into a wall does not spend stamina or drive running feedback.
            IsSprinting = IsSprinting && PlanarVelocity.sqrMagnitude > .25f;
            stamina.Tick(dt, IsSprinting);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > .5f) frameGround = hit.collider;
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
            lastGrounded = lastJump = jumpBufferedUntil = float.NegativeInfinity;
            input.Clear();
            view.ResetPose(rotation);
        }
    }
}
