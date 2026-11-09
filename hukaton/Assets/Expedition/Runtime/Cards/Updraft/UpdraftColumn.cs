using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The updraft (prefab root): a vertical column of <see cref="CardZoneBehaviour.radius"/> and
    /// <see cref="CardZoneBehaviour.height"/> over the transform for <see cref="Duration"/> seconds. Lifts the player,
    /// creatures and loose objects with μ ≤ <see cref="MaxMass"/> up to <see cref="RiseSpeed"/> (design m/s; the
    /// player's real speed is scaled by the motor's ReferenceScale like every design number). Near the top the lift
    /// eases off so bodies hover there; walking out sideways leaves the column. Faster upward motion (a bounce) is never
    /// slowed. Registered as a card zone (Kind Updraft).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpdraftColumn : CardZoneBehaviour
    {
        [Header("Authored parts")]
        [Tooltip("Visual root, authored for the sizes below; scaled to the configured column.")]
        public Transform visual;
        [Min(.1f)] public float authoredHeight = 6, authoredRadius = 1.5f;
        [Tooltip("Particle systems that stop emitting when the column ends.")]
        public ParticleSystem[] emitters;

        [Header("Lift")]
        [Tooltip("How quickly the column brings a body to its speed, design m/s².")]
        [Min(.1f)] public float liftAcceleration = 28;
        [Tooltip("Over the last metres below the top the lift eases to zero: bodies hover at the top.")]
        [Min(.05f)] public float topEase = 1.2f;
        [Tooltip("A body's axis may be this far outside the radius and still be carried, m.")]
        [Min(0)] public float edgeTolerance = .1f;

        [Header("Feel")]
        public SfxCue startSound;
        public SfxCue enterSound;
        public VfxBurst endVfx;
        public Color tint = new Color(.85f, .95f, 1f);
        [Min(0)] public float fadeSeconds = 1.2f;

        public float Duration { get; private set; } = 12;
        public float RiseSpeed { get; private set; } = 4;
        public float MaxMass { get; private set; } = 2;
        public float Top => transform.position.y + height;
        public bool Ending => ending;

        public override CardZoneKind Kind => CardZoneKind.Updraft;

        float age;
        bool ending, playerInside;
        readonly Collider[] overlaps = new Collider[64];
        readonly HashSet<Rigidbody> carried = new HashSet<Rigidbody>();

        public void Configure(float columnHeight, float columnRadius, float seconds, float riseSpeed, float maxMass)
        {
            height = Mathf.Max(.1f, columnHeight);
            radius = Mathf.Max(.05f, columnRadius);
            Duration = seconds;
            RiseSpeed = riseSpeed;
            MaxMass = maxMass;
            if (visual) visual.localScale = new Vector3(radius / authoredRadius, height / authoredHeight, radius / authoredRadius);
        }

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            world.Play(startSound, transform.position);
        }

        // ---------- the rule (pure, tested) ----------

        /// <summary>The column's target upward speed at this distance below its top (eases to 0 at the top).</summary>
        public static float TargetSpeed(float riseSpeed, float belowTop, float ease) => riseSpeed * Mathf.Clamp01(belowTop / Mathf.Max(.01f, ease));

        /// <summary>
        /// One step of a body's vertical speed in the column (design units): up toward the target with the column's
        /// acceleration; speed the column itself gave (≤ rise speed) eases back toward the target near the top; faster
        /// upward motion from elsewhere is left alone.
        /// </summary>
        public static float Lift(float vertical, float target, float riseSpeed, float acceleration, float dt)
        {
            if (vertical < target) return Mathf.Min(target, vertical + acceleration * dt);
            if (vertical <= riseSpeed + .5f) return Mathf.MoveTowards(vertical, target, acceleration * dt);
            return vertical;
        }

        /// <summary>Is this point (a body's axis or centre) inside the column?</summary>
        public bool Inside(Vector3 point)
        {
            Vector3 p = transform.position;
            float dx = point.x - p.x, dz = point.z - p.z, r = radius + edgeTolerance;
            return dx * dx + dz * dz <= r * r && point.y >= p.y - depth && point.y <= p.y + height;
        }

        // ---------- life ----------

        void Update()
        {
            if (!World) return;
            age += Time.deltaTime;
            if (!ending && age >= Duration) End();
            if (ending) return;
            LiftPlayer(Time.deltaTime);
        }

        void LiftPlayer(float dt)
        {
            var motor = World.motor;
            if (!motor || !World.Running || motor.ExternallyControlled) { playerInside = false; return; }
            var controller = motor.characterController;
            Vector3 feet = motor.transform.TransformPoint(controller.center) - Vector3.up * controller.height * .5f;
            bool inside = Inside(feet) || Inside(feet + Vector3.up * controller.height * .5f);
            if (inside && !playerInside) World.Play(enterSound, feet);
            playerInside = inside;
            if (!inside) return;
            float scale = motor.ReferenceScale;
            float vertical = motor.IsGrounded ? 0 : motor.VerticalSpeed / scale;
            float target = TargetSpeed(RiseSpeed, Top - feet.y, topEase);
            // Faster upward motion from elsewhere (a mushroom bounce) is left alone.
            if (vertical > RiseSpeed + .5f) return;
            float next = Lift(vertical, target, RiseSpeed, liftAcceleration, dt);
            // The motor subtracts the next frame's gravity before it moves: add it back so the speed holds.
            float real = next * scale + motor.gravity * motor.GravityScale * dt;
            if (real <= 0 && motor.IsGrounded) return;
            motor.SetVerticalSpeed(real);
        }

        void FixedUpdate()
        {
            if (!World || ending || !World.Running) return;
            float dt = Time.fixedDeltaTime;
            Vector3 bottom = transform.position - Vector3.up * depth, top = transform.position + Vector3.up * height;
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius + edgeTolerance, overlaps, ~0, QueryTriggerInteraction.Ignore);
            carried.Clear();
            for (int i = 0; i < count; i++)
            {
                var body = overlaps[i].attachedRigidbody;
                if (!body || body.isKinematic || !carried.Add(body)) continue;
                if (body.mass > MaxMass + .001f) continue;
                Vector3 centre = body.worldCenterOfMass;
                if (!Inside(centre)) continue;
                Vector3 velocity = body.linearVelocity;
                float target = TargetSpeed(RiseSpeed, Top - centre.y, topEase);
                if (velocity.y > RiseSpeed + .5f) continue;
                float next = Lift(velocity.y, target, RiseSpeed, liftAcceleration, dt);
                // Gravity is integrated after this step: add it back so the speed holds.
                if (body.useGravity) next -= Physics.gravity.y * dt;
                body.linearVelocity = new Vector3(velocity.x, next, velocity.z);
                body.WakeUp();
            }
        }

        void End()
        {
            ending = true;
            playerInside = false;
            if (World) World.Zones.Remove(this);
            if (emitters != null) foreach (var system in emitters) if (system) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            World.Spawn(endVfx, transform.position + Vector3.up * height * .5f, Quaternion.identity, tint);
            Destroy(gameObject, fadeSeconds);
        }

        protected override void OnEnable()
        {
            if (!ending) base.OnEnable();
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.6f, .9f, 1f, .6f);
            Vector3 p = transform.position;
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2 / 24, b = (i + 1) * Mathf.PI * 2 / 24;
                Vector3 u = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius, v = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius;
                Gizmos.DrawLine(p + u, p + v);
                Gizmos.DrawLine(p + u + Vector3.up * height, p + v + Vector3.up * height);
            }
        }
    }
}
