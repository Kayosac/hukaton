using System;
using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The rope made by «Верёвка»: a cord of particles (Verlet, pull-only links, swept against the world so it drapes
    /// over edges and the extra length lies on the floor) hanging from the point of impact. The anchor follows what it
    /// was attached to (position and rotation): a static surface or a movable non-living object such as the stone cube.
    /// The player grabs it with the usual interaction key (prompt only for a focused, reachable, unobstructed rope),
    /// takes the body over through <see cref="IExternalMotorControl"/>, climbs (W/S while looking up or down), swings
    /// (movement keys relative to the view), climbs off onto a ledge at the top, and lets go with E or Space keeping the
    /// real tangential velocity. Full physics: the hanging player's pull acts on a movable anchor (a cube can be dragged
    /// off a ledge). Lives until the end of the outing; reusable. Detaches (falls and disappears) when its anchor is
    /// gone — see <see cref="DetachFrom"/>.
    /// </summary>
    public sealed class RopeLine : MonoBehaviour, IOutingSpawned, IExternalMotorControl, ICardZone
    {
        [Header("Authored parts")]
        [Tooltip("Visual pieces, one per simulated link, each authored for segmentMeshLength along local Z. Their count limits the length.")]
        public Transform[] segments;
        [Min(.01f)] public float segmentMeshLength = .3f;
        [Tooltip("Knot at the anchor.")]
        public Transform knot;
        [Tooltip("Frayed free end.")]
        public Transform tuft;

        [Header("Simulation")]
        [Tooltip("Preferred link length; the real one divides the rope's length evenly.")]
        [Min(.05f)] public float segmentLength = .3f;
        [Tooltip("Collision radius of the cord.")]
        [Min(.005f)] public float thickness = .05f;
        [Range(1, 40)] public int iterations = 12;
        [Min(10)] public int stepsPerSecond = 60;
        [Range(0, 1)] public float airDamping = .015f;
        [Range(0, 1)] public float groundFriction = .5f;
        [Min(0)] public float gravity = 9.81f;
        [Tooltip("Visual pieces overlap a little so the rope reads as one cord.")]
        [Min(1)] public float segmentOverlap = 1.12f;

        [Header("Climbing and swinging")]
        [Tooltip("The hands stop this far below the anchor (along the cord).")]
        [Min(.1f)] public float topReach = .55f;
        [Tooltip("The hands are this far below the top of the player's capsule.")]
        public float handsBelowTop = .2f;
        [Tooltip("Visual only: the held cord runs to the hands this far in front of the body and this far below the grip, not through the camera.")]
        public float handsForward = .4f, handsDown = .5f;
        [Tooltip("Visual only: pieces of a held rope this close to the camera are hidden.")]
        [Min(0)] public float cameraClearance = .45f;
        [Tooltip("Swinging push from the movement keys, m/s² (real units).")]
        [Min(0)] public float pumpAcceleration = 5;
        [Tooltip("Looking further up/down than this, W/S climb; otherwise they swing forward/back.")]
        [Range(0, 89)] public float climbLookAngle = 25;
        [Tooltip("Air drag of the hanging body, 1/s.")]
        [Range(0, 5)] public float hangingDrag = .12f;
        [Tooltip("The hanging player's mass μ for the pull on a movable anchor (board: the player is μ = 1).")]
        [Min(0)] public float playerMass = 1;
        [Tooltip("Pull on a movable anchor is capped at this many player weights (no explosive yanks).")]
        [Min(1)] public float maxPullWeights = 6;
        [Tooltip("Speed limit of the hanging body, m/s (a jerk of a moving anchor never launches the player).")]
        [Min(1)] public float maxHangingSpeed = 25;
        [Tooltip("The rope slips out of the hands when the body is held this much farther than the rope allows (stuck behind geometry).")]
        [Min(.2f)] public float slipDistance = 1.5f;
        [Tooltip("How far from the crosshair ray the cord may pass and still be grabbed.")]
        [Min(0)] public float aimRadius = .35f;
        [Tooltip("Ledge search at the top of the rope: distances ahead of the anchor along the view.")]
        public float[] ledgeProbe = { .45f, .75f, 1.05f };
        [Min(.05f)] public float mantleSeconds = .45f;

        [Header("Detaching")]
        [Tooltip("When the anchor is gone (an awakened tree, a picked-up item) the rope falls and is removed after this time.")]
        [Min(0)] public float detachedLinger = 3;

        [Header("Feedback")]
        public SfxCue attachSound;
        public SfxCue grabSound, releaseSound;
        public VfxBurst attachVfx;
        public Color dustTint = new Color(.78f, .7f, .55f);
        public string grabPrompt = "<b>E</b>  Схватиться";
        public string holdPrompt = "<b>E</b>  Отпустить";
        [Tooltip("Shown for the first seconds of the first grab in a session.")]
        [TextArea] public string firstHoldPrompt = "<b>W</b>/<b>S</b> — по взгляду: вверх, вниз или раскачка   <b>A</b>/<b>D</b> — вбок   <b>E</b> — отпустить";
        [Min(0)] public float firstHoldPromptSeconds = 5;

        // ---------- registry ----------

        static readonly List<RopeLine> Active = new List<RopeLine>();
        static int coordinatedFrame = -1;
        static bool controlsExplained;

        /// <summary>Raised when a rope lets go of its anchor (the anchor is gone or <see cref="DetachFrom"/> was called).</summary>
        public static event Action<RopeLine> Detached;

        /// <summary>Every rope of the current outing.</summary>
        public static IReadOnlyList<RopeLine> All => Active;

        /// <summary>
        /// The rope the player is hanging on, if any. To make the player let go from another card (Teleport, Swap):
        /// <c>RopeLine.ReleasePlayer(motor)</c>, or simply <c>motor.TeleportKeepingVelocity</c> / <c>motor.RevokeExternalControl()</c>.
        /// </summary>
        public static RopeLine Holding(FirstPersonMotor motor) => motor ? motor.ExternalOwner as RopeLine : null;

        /// <summary>The player lets go of whatever rope holds them, keeping the velocity. False when not on a rope.</summary>
        public static bool ReleasePlayer(FirstPersonMotor motor)
        {
            var rope = Holding(motor);
            if (!rope) return false;
            rope.LetGo();
            return true;
        }

        /// <summary>
        /// Detaches every rope anchored to <paramref name="root"/> or any of its children (an awakened tree, an item
        /// that is about to disappear): the rope falls and is removed shortly. Returns how many ropes let go.
        /// Ropes also notice by themselves when their anchor collider is disabled, deactivated or destroyed.
        /// </summary>
        public static int DetachFrom(Transform root)
        {
            if (!root) return 0;
            int count = 0;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var rope = Active[i];
                if (rope && rope.Attached && rope.anchorTransform && rope.anchorTransform.IsChildOf(root)) { rope.Detach(); count++; }
            }
            return count;
        }

        // ---------- state ----------

        ExpeditionWorld world;
        Collider anchorCollider;
        Transform anchorTransform;
        Rigidbody anchorBody;
        Vector3 localAnchor, lastAnchor;
        bool detached;
        float detachedAt, length, climbSpeed = 2, linkLength = .3f, accumulator;
        int count, sleepSteps;
        bool sleeping;
        Vector3[] positions = Array.Empty<Vector3>(), previous = Array.Empty<Vector3>(), safe = Array.Empty<Vector3>();
        bool[] contact = Array.Empty<bool>();
        Renderer[] pieceRenderers = Array.Empty<Renderer>();
        int collisionMask;

        FirstPersonMotor motor;
        PlayerVitals hurtSource;
        bool holding, mantling;
        float gripLength, holdStart, mantleTime, nextLedgeProbe;
        Vector3 gripVelocity, mantleFrom, mantleTo, pendingImpulse;

        public bool Attached { get; private set; }
        public bool Held => holding;
        public float Length => length;
        public float ClimbSpeed => climbSpeed;
        /// <summary>The collider the rope hangs from (null once detached).</summary>
        public Collider Anchor => Attached ? anchorCollider : null;
        public Vector3 AnchorPoint => Attached && anchorTransform ? anchorTransform.TransformPoint(localAnchor) : count > 0 ? positions[0] : transform.position;
        /// <summary>Simulated points of the cord, anchor first.</summary>
        public int PointCount => count;
        public Vector3 Point(int index) => positions[index];
        /// <summary>The free end of the cord.</summary>
        public Vector3 EndPoint => count > 0 ? positions[count - 1] : transform.position;

        public CardZoneKind Kind => CardZoneKind.Rope;

        public Bounds Bounds
        {
            get
            {
                if (count == 0) return new Bounds(transform.position, Vector3.one * .2f);
                var bounds = new Bounds(positions[0], Vector3.one * thickness * 2);
                for (int i = 1; i < count; i++) bounds.Encapsulate(positions[i]);
                bounds.Expand(thickness * 2);
                return bounds;
            }
        }

        public bool Contains(Vector3 point)
        {
            for (int i = 0; i + 1 < count; i++)
            {
                Vector3 a = positions[i], ab = positions[i + 1] - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0;
                if ((a + ab * t - point).sqrMagnitude <= (thickness + .25f) * (thickness + .25f)) return true;
            }
            return false;
        }

        // ---------- setup ----------

        public void OnSpawned(ExpeditionWorld outingWorld)
        {
            world = outingWorld;
            if (world) world.Zones.Add(this);
            if (!Active.Contains(this)) Active.Add(this);
        }

        void OnEnable() => AwakenableTree.Awakened += OnTreeAwakened;

        /// <summary>The tree this rope hangs from comes alive (cluster D): the rope lets go and falls.</summary>
        void OnTreeAwakened(AwakenableTree tree, ICombatant ally)
        {
            if (Attached && tree && tree.Owns(anchorCollider)) Detach();
        }

        /// <summary>
        /// Hangs the rope from <paramref name="surface"/> at <paramref name="point"/> (just off the surface along the
        /// normal). <paramref name="ropeLength"/> in metres (capped by the authored pieces), climbing speed in m/s.
        /// </summary>
        public void Attach(Collider surface, Vector3 point, Vector3 normal, float ropeLength, float climbingSpeed)
        {
            collisionMask = CardFlight.CollisionMask;
            int water = LayerMask.NameToLayer("Water");
            if (water >= 0) collisionMask &= ~(1 << water);
            climbSpeed = climbingSpeed;
            int pieces = segments != null ? segments.Length : 0;
            int links = Mathf.Clamp(Mathf.RoundToInt(ropeLength / Mathf.Max(.05f, segmentLength)), 1, Mathf.Max(1, pieces));
            length = Mathf.Min(ropeLength, links * segmentLength * 1.5f);
            linkLength = length / links;
            count = links + 1;
            positions = new Vector3[count];
            previous = new Vector3[count];
            safe = new Vector3[count];
            contact = new bool[count];
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 anchor = point + n * (thickness * 1.6f);
            anchorCollider = surface;
            anchorTransform = surface ? surface.transform : null;
            anchorBody = surface ? surface.attachedRigidbody : null;
            localAnchor = anchorTransform ? anchorTransform.InverseTransformPoint(anchor) : anchor;
            // The cord starts bundled at the anchor and unrolls downward under its own weight.
            for (int i = 0; i < count; i++)
            {
                positions[i] = previous[i] = safe[i] = anchor + Vector3.down * (i * .012f) + n * (i * .002f);
                contact[i] = false;
            }
            pieceRenderers = new Renderer[pieces];
            for (int i = 0; i < pieces; i++)
                if (segments[i])
                {
                    segments[i].gameObject.SetActive(i < links);
                    pieceRenderers[i] = segments[i].GetComponent<Renderer>();
                }
            lastAnchor = anchor;
            transform.position = anchor;
            Attached = true;
            detached = sleeping = false;
            if (world)
            {
                world.Play(attachSound, anchor);
                world.Spawn(attachVfx, anchor, Quaternion.LookRotation(n), dustTint, .5f);
            }
            UpdateVisual();
        }

        // ---------- the loop ----------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0 || count == 0) return;
            if (Attached && AnchorLost()) Detach();
            Vector3 anchor = AnchorPoint;
            if (Attached)
            {
                Vector3 jump = anchor - lastAnchor;
                // The anchor was carried far in one frame (Swap): carry the cord along instead of whipping it.
                if (jump.sqrMagnitude > 9)
                    for (int i = 0; i < count; i++) { positions[i] += jump; previous[i] += jump; safe[i] += jump; }
                if (jump.sqrMagnitude > 1e-8f) Wake();
                lastAnchor = anchor;
                transform.position = anchor;
            }
            Coordinate();
            if (holding) UpdateHolding(dt, anchor);
            Simulate(dt, anchor);
            UpdateVisual();
            if (detached && Time.time - detachedAt > detachedLinger) Destroy(gameObject);
        }

        void FixedUpdate()
        {
            if (pendingImpulse.sqrMagnitude < 1e-10f) return;
            if (Attached && anchorBody && !anchorBody.isKinematic)
            {
                // Real impulse (player gravity) → body units (bodies fall with the board's g).
                float toBody = motor ? motor.referenceGravity / Mathf.Max(.1f, motor.gravity) : 9.81f / 25f;
                anchorBody.AddForceAtPosition(pendingImpulse * toBody, AnchorPoint, ForceMode.Impulse);
            }
            pendingImpulse = Vector3.zero;
        }

        bool AnchorLost() => !anchorCollider || !anchorCollider.enabled || !anchorCollider.gameObject.activeInHierarchy;

        /// <summary>Lets go of the anchor: the rope falls and disappears after <see cref="detachedLinger"/> seconds.</summary>
        public void Detach()
        {
            if (!Attached) return;
            if (holding) LetGo();
            Attached = false;
            detached = true;
            detachedAt = Time.time;
            Wake();
            Detached?.Invoke(this);
        }

        void Wake()
        {
            sleeping = false;
            sleepSteps = 0;
        }

        // ---------- focus and grabbing (one pass per frame for all ropes) ----------

        void Coordinate()
        {
            if (coordinatedFrame == Time.frameCount) return;
            coordinatedFrame = Time.frameCount;
            var w = world;
            if (!w || !w.Running || !w.control || !w.control.GameplayActive || !w.motor || !w.viewCamera) return;
            if (w.motor.ExternallyControlled || w.FocusedLoot) return;
            var eye = w.viewCamera.transform;
            float reach = w.settings ? w.settings.interactionReach : 3;
            RopeLine best = null;
            float bestScore = float.MaxValue, bestArc = 0;
            foreach (var rope in Active)
            {
                if (!rope || !rope.Attached || rope.world != w) continue;
                if (rope.TryFocus(eye.position, eye.forward, reach, out float score, out float arc) && score < bestScore)
                { best = rope; bestScore = score; bestArc = arc; }
            }
            if (!best) return;
            w.ShowCardPrompt(best.grabPrompt);
            var keys = Keyboard.current;
            if (keys != null && keys.eKey.wasPressedThisFrame) best.Grab(bestArc);
        }

        bool TryFocus(Vector3 eye, Vector3 forward, float reach, out float score, out float arc)
        {
            score = float.MaxValue;
            arc = 0;
            int bestIndex = -1;
            float bestU = 0, bestT = 0;
            for (int i = 0; i + 1 < count; i++)
            {
                float distance = RopeMath.RayToSegment(eye, forward, reach, positions[i], positions[i + 1], out float t, out float u);
                if (distance > aimRadius + thickness || t < .1f) continue;
                float candidate = distance + t * .05f;
                if (candidate < score) { score = candidate; bestIndex = i; bestU = u; bestT = t; }
            }
            if (bestIndex < 0 || (world && world.motor && !WithinRopeLength(world.motor))) return false;
            Vector3 target = Vector3.Lerp(positions[bestIndex], positions[bestIndex + 1], bestU);
            Vector3 toTarget = target - eye;
            float d = toTarget.magnitude;
            if (d > reach + thickness) return false;
            // Unobstructed: nothing solid between the eye and that piece of rope.
            if (d > .15f && Physics.Raycast(eye, toTarget / d, d - .1f, collisionMask, QueryTriggerInteraction.Ignore)) return false;
            arc = (bestIndex + bestU) * linkLength;
            return true;
        }

        void Grab(float arc)
        {
            var m = world ? world.motor : null;
            if (!m || holding || !WithinRopeLength(m) || !m.TryClaimExternalControl(this)) return;
            motor = m;
            holding = true;
            mantling = false;
            // The hands take the cord where they are: never shorter than the straight line to the anchor (that would yank the body).
            gripLength = Mathf.Clamp(Mathf.Max(arc, Vector3.Distance(AnchorPoint, GripPoint())), topReach, length);
            gripVelocity = m.Velocity;
            holdStart = Time.time;
            hurtSource = world.vitals;
            if (hurtSource) hurtSource.Changed += OnPlayerHealthChanged;
            world.Play(grabSound, GripPoint());
            Wake();
        }

        Vector3 GripPoint() => GripPointOf(motor);

        Vector3 GripPointOf(FirstPersonMotor body)
        {
            var controller = body.characterController;
            return body.transform.position + Vector3.up * (controller.center.y + controller.height * .5f - handsBelowTop);
        }

        /// <summary>The hands can reach the cord only if the anchor is no farther than the rope is long.</summary>
        bool WithinRopeLength(FirstPersonMotor body) => Vector3.Distance(AnchorPoint, GripPointOf(body)) <= length + .3f;

        /// <summary>Where the held cord is drawn to: the hands in front of the chest (the body hangs from <see cref="GripPoint"/>).</summary>
        Vector3 HandPoint()
        {
            Vector3 forward = world && world.viewCamera ? world.viewCamera.transform.forward : motor.transform.forward;
            forward.y = 0;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.zero;
            return GripPoint() + forward * handsForward + Vector3.down * handsDown;
        }

        void OnPlayerHealthChanged(int delta)
        {
            // A hit knocks the player off the rope.
            if (delta < 0 && holding) LetGo();
        }

        /// <summary>The player lets go now, keeping the real tangential velocity (no bonus).</summary>
        public void LetGo()
        {
            if (!holding) return;
            var m = motor;
            Vector3 velocity = m ? RopeMath.ReleaseVelocity(AnchorPoint, GripPoint(), gripVelocity, gripLength) : gripVelocity;
            EndHold();
            if (m) m.ReleaseExternalControl(this, velocity);
            if (world) world.Play(releaseSound, m ? m.transform.position : transform.position, .7f);
        }

        /// <summary>Teleport, the end of the outing or a full pose reset took the body away: keep its velocity.</summary>
        public Vector3 OnControlRevoked()
        {
            Vector3 velocity = holding && motor ? RopeMath.ReleaseVelocity(AnchorPoint, GripPoint(), gripVelocity, gripLength) : gripVelocity;
            EndHold();
            return velocity;
        }

        void EndHold()
        {
            if (hurtSource) hurtSource.Changed -= OnPlayerHealthChanged;
            hurtSource = null;
            holding = mantling = false;
            motor = null;
            Wake();
        }

        // ---------- hanging, climbing, swinging ----------

        void UpdateHolding(float dt, Vector3 anchor)
        {
            if (!motor || !ReferenceEquals(motor.ExternalOwner, this)) { EndHold(); return; }
            bool gameplay = world && world.Running && world.control && world.control.GameplayActive;
            var input = motor.input;
            Vector2 move = gameplay && input ? input.Move : Vector2.zero;
            var keys = Keyboard.current;
            bool letGo = gameplay && ((input && input.JumpPressed) || (keys != null && keys.eKey.wasPressedThisFrame && !world.FocusedLoot));
            if (letGo) { LetGo(); return; }
            if (!controlsExplained && Time.time - holdStart >= firstHoldPromptSeconds) controlsExplained = true;
            if (world) world.ShowCardPrompt(controlsExplained ? holdPrompt : firstHoldPrompt, false);
            if (mantling) { UpdateMantle(dt); return; }

            var eye = world.viewCamera.transform;
            float pitch = Mathf.Asin(Mathf.Clamp(eye.forward.y, -1, 1)) * Mathf.Rad2Deg;
            gripLength = Mathf.Clamp(gripLength - RopeMath.ClimbRate(move, pitch, climbLookAngle, climbSpeed) * dt, topReach, length);
            if (gripLength <= topReach + .03f && move.y > .5f && Time.time >= nextLedgeProbe)
            {
                nextLedgeProbe = Time.time + .2f;
                if (TryFindLedge(anchor, eye.forward)) { BeginMantle(); return; }
            }
            Vector3 pump = RopeMath.PumpAcceleration(eye.forward, eye.right, move, pitch, climbLookAngle, pumpAcceleration);
            float g = motor.gravity * motor.GravityScale;
            float maxImpulse = maxPullWeights * playerMass * motor.gravity;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120));
            float h = dt / steps;
            Vector3 grip = GripPoint();
            for (int s = 0; s < steps; s++)
            {
                Vector3 velocity = (gripVelocity + (Vector3.down * g + pump) * h) / (1 + hangingDrag * h);
                Vector3 predicted = grip + velocity * h;
                Vector3 constrained = RopeMath.ConstrainToLength(anchor, predicted, gripLength);
                pendingImpulse += Vector3.ClampMagnitude(RopeMath.TensionImpulse(constrained - predicted, playerMass, h), maxImpulse * h);
                Vector3 before = motor.transform.position;
                motor.MoveExternally(constrained - grip);
                Vector3 moved = motor.transform.position - before;
                grip += moved;
                gripVelocity = Vector3.ClampMagnitude(moved / h, maxHangingSpeed);
            }
            // Stuck behind something while the anchor moved on: the rope slips out of the hands.
            if ((grip - anchor).magnitude > gripLength + slipDistance) LetGo();
        }

        bool TryFindLedge(Vector3 anchor, Vector3 viewForward)
        {
            Vector3 forward = new Vector3(viewForward.x, 0, viewForward.z);
            if (forward.sqrMagnitude < 1e-4f) return false;
            forward.Normalize();
            var controller = motor.characterController;
            float radius = controller.radius;
            Vector3 feet = motor.transform.position;
            foreach (float distance in ledgeProbe)
            {
                Vector3 probe = anchor + forward * (distance + radius) + Vector3.up * 1.2f;
                if (!Physics.Raycast(probe, Vector3.down, out var hit, 2.8f, collisionMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < .65f || hit.point.y < feet.y + .25f || hit.point.y > anchor.y + 1) continue;
                Vector3 stand = hit.point + Vector3.up * .04f;
                Vector3 rise = new Vector3(feet.x, stand.y + .06f, feet.z);
                if (!CapsuleFree(stand, controller) || !CapsuleFree(rise, controller)) continue;
                mantleTo = stand;
                return true;
            }
            return false;
        }

        bool CapsuleFree(Vector3 feet, CharacterController controller)
        {
            float radius = controller.radius;
            Vector3 bottom = feet + Vector3.up * (radius + .03f), top = feet + Vector3.up * Mathf.Max(radius + .03f, controller.height - radius);
            return !Physics.CheckCapsule(bottom, top, radius - .02f, collisionMask, QueryTriggerInteraction.Ignore);
        }

        void BeginMantle()
        {
            mantling = true;
            mantleTime = 0;
            mantleFrom = motor.transform.position;
            gripVelocity = Vector3.zero;
            world.Play(grabSound, GripPoint(), .8f, 1.15f);
        }

        void UpdateMantle(float dt)
        {
            mantleTime += dt / mantleSeconds;
            float t = Mathf.Clamp01(mantleTime);
            Vector3 lift = new Vector3(mantleFrom.x, mantleTo.y + .06f, mantleFrom.z);
            Vector3 over = mantleTo + Vector3.up * .06f;
            Vector3 target = t < .5f ? Vector3.Lerp(mantleFrom, lift, t / .5f) : Vector3.Lerp(lift, over, (t - .5f) / .5f);
            Vector3 before = motor.transform.position;
            Vector3 wanted = target - before;
            motor.MoveExternally(wanted);
            Vector3 moved = motor.transform.position - before;
            bool blocked = wanted.sqrMagnitude > 1e-4f && moved.sqrMagnitude < wanted.sqrMagnitude * .25f;
            if (t >= 1 || blocked)
            {
                // On the ledge (or stopped by something): stand there with no speed of its own.
                var m = motor;
                EndHold();
                if (m) m.ReleaseExternalControl(this, Vector3.zero);
            }
        }

        // ---------- the cord ----------

        int GripIndex() => Mathf.Clamp(Mathf.RoundToInt(gripLength / linkLength), 1, count - 1);

        void Simulate(float dt, Vector3 anchor)
        {
            if (sleeping && !holding) return;
            float h = 1f / stepsPerSecond;
            accumulator += dt;
            int steps = 0;
            while (accumulator >= h && steps < 4) { Step(h, anchor); accumulator -= h; steps++; }
            if (steps == 4) accumulator = 0;
        }

        void Step(float h, Vector3 anchor)
        {
            int pinned = holding && motor ? GripIndex() : -1;
            Vector3 grip = pinned >= 0 ? HandPoint() : Vector3.zero;
            Vector3 fall = Vector3.down * (gravity * h * h);
            for (int i = 0; i < count; i++)
            {
                Vector3 current = positions[i];
                if (i == 0 && Attached) { previous[0] = current; positions[0] = safe[0] = anchor; continue; }
                if (i == pinned) { previous[i] = current; positions[i] = safe[i] = grip; continue; }
                Vector3 velocity = (current - previous[i]) * (1 - airDamping);
                if (contact[i]) velocity *= 1 - groundFriction;
                previous[i] = current;
                safe[i] = Sweep(current, current + velocity + fall, out contact[i]);
                positions[i] = safe[i];
            }
            for (int k = 0; k < iterations; k++)
                for (int i = 0; i + 1 < count; i++)
                {
                    Vector3 offset = positions[i + 1] - positions[i];
                    float distance = offset.magnitude;
                    if (distance <= linkLength || distance < 1e-6f) continue;
                    float wa = Fixed(i, pinned) ? 0 : 1, wb = Fixed(i + 1, pinned) ? 0 : 1;
                    if (wa + wb <= 0) continue;
                    Vector3 correction = offset * ((distance - linkLength) / distance / (wa + wb));
                    positions[i] += correction * wa;
                    positions[i + 1] -= correction * wb;
                }
            float moved = 0;
            for (int i = 0; i < count; i++)
            {
                if (Fixed(i, pinned)) continue;
                positions[i] = Sweep(safe[i], positions[i], out bool touched);
                contact[i] |= touched;
                moved = Mathf.Max(moved, (positions[i] - previous[i]).sqrMagnitude);
            }
            if (!holding && Attached && moved < .0004f * .0004f) { if (++sleepSteps > stepsPerSecond) sleeping = true; }
            else sleepSteps = 0;
        }

        bool Fixed(int index, int pinned) => (index == 0 && Attached) || index == pinned;

        Vector3 Sweep(Vector3 from, Vector3 to, out bool touched)
        {
            touched = false;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-5f) return to;
            Vector3 direction = delta / distance;
            if (!Physics.SphereCast(from, thickness, direction, out var hit, distance, collisionMask, QueryTriggerInteraction.Ignore)) return to;
            touched = true;
            Vector3 stop = from + direction * Mathf.Max(0, hit.distance - .002f);
            Vector3 rest = to - stop;
            float into = Vector3.Dot(rest, hit.normal);
            if (into < 0) rest -= hit.normal * into;
            float slide = rest.magnitude;
            if (slide > 1e-5f && !Physics.SphereCast(stop, thickness, rest / slide, out _, slide, collisionMask, QueryTriggerInteraction.Ignore))
                stop += rest;
            return stop;
        }

        // ---------- visual ----------

        void UpdateVisual()
        {
            if (count == 0 || segments == null) return;
            float fade = detached ? Mathf.Clamp01((detachedLinger - (Time.time - detachedAt)) / .5f) : 1;
            int links = count - 1;
            Vector3 eye = holding && world && world.viewCamera ? world.viewCamera.transform.position : new Vector3(1e6f, 1e6f, 1e6f);
            for (int i = 0; i < links && i < segments.Length; i++)
            {
                var piece = segments[i];
                if (!piece) continue;
                Vector3 a = positions[i], b = positions[i + 1], offset = b - a;
                float distance = offset.magnitude;
                piece.position = (a + b) * .5f;
                if (i < pieceRenderers.Length && pieceRenderers[i])
                    pieceRenderers[i].enabled = (piece.position - eye).sqrMagnitude > cameraClearance * cameraClearance;
                if (distance > 1e-4f)
                {
                    Vector3 along = offset / distance;
                    Vector3 up = Mathf.Abs(along.y) > .95f ? Vector3.forward : Vector3.up;
                    piece.rotation = Quaternion.LookRotation(along, up) * Quaternion.Euler(0, 0, i * 47f);
                }
                // A stretched link (the cord pulled between two held points) is capped: long thin pieces, never blades.
                float stretch = Mathf.Clamp(distance / segmentMeshLength, .25f, 2.5f) * segmentOverlap;
                piece.localScale = new Vector3(fade, fade, stretch * Mathf.Max(.05f, fade));
            }
            if (knot)
            {
                knot.position = positions[0];
                knot.localScale = Vector3.one * fade;
            }
            if (tuft)
            {
                Vector3 end = positions[count - 1], before = positions[Mathf.Max(0, count - 2)];
                tuft.position = end;
                if ((end - before).sqrMagnitude > 1e-8f) tuft.rotation = Quaternion.LookRotation(end - before, Vector3.up);
                tuft.localScale = Vector3.one * fade;
            }
        }

        void OnDestroy()
        {
            Active.Remove(this);
            if (holding && motor && ReferenceEquals(motor.ExternalOwner, this))
            {
                var m = motor;
                Vector3 velocity = gripVelocity;
                EndHold();
                m.ReleaseExternalControl(this, velocity);
            }
            else if (hurtSource) hurtSource.Changed -= OnPlayerHealthChanged;
        }

        void OnDisable()
        {
            AwakenableTree.Awakened -= OnTreeAwakened;
            if (world) world.Zones.Remove(this);
        }
    }
}
