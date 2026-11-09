using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The trampoline mushroom (prefab root). Lives <see cref="Lifetime"/> seconds from its appearance, then shrinks
    /// away. Landing on the cap from above (the motor only reports ground under a surface facing up, so touching the
    /// side does nothing) takes the fall over: no fall damage, the vertical speed is replaced by the bounce
    /// (<see cref="MushroomBounce"/>), the horizontal speed is kept. Any number of bounces. Only the player bounces.
    /// Colliders stay put; only the visual squashes and stretches. Registered as a card zone (Kind Mushroom).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushroomTrampoline : CardZoneBehaviour, ILandingSurface
    {
        [Header("Authored parts")]
        [Tooltip("Pivot of the whole visual at the foot of the stem: grows, squashes and stretches.")]
        public Transform visual;
        [Tooltip("The cap's visual: wobbles on its own after a bounce.")]
        public Transform capVisual;
        [Tooltip("The cap's collider. Only standing on it bounces.")]
        public Collider capCollider;
        [Tooltip("Colliders switched off when the mushroom goes.")]
        public Collider[] colliders;
        [Tooltip("Height of the cap's top over the foot, m.")]
        [Min(.1f)] public float capTop = .9f;
        [Tooltip("Cap diameter of the authored meshes and colliders, m.")]
        [Min(.1f)] public float authoredCapDiameter = 1.6f;

        [Header("Feel")]
        public VfxBurst growVfx;
        public VfxBurst bounceVfx;
        public VfxBurst vanishVfx;
        public Color tint = new Color(.93f, .45f, .62f);
        public SfxCue growSound, bounceSound, vanishSound;
        [Min(.01f)] public float growSeconds = .4f;
        [Min(.01f)] public float vanishSeconds = .35f;
        [Tooltip("The last seconds of life: the mushroom trembles so the player sees it is about to go.")]
        [Min(0)] public float warnSeconds = 2.5f;
        [Min(.1f)] public float springFrequency = 3.2f;
        [Range(0, 1)] public float springDamping = .16f;
        [Tooltip("Squash kick of the weakest and of the strongest bounce.")]
        public Vector2 squashKick = new Vector2(4, 9);
        [Tooltip("How far the visual squashes and stretches: y × (1 + s), xz × (1 − s·k).")]
        [Range(0, 1)] public float widthResponse = .55f;
        [Tooltip("Field-of-view punch per metre of bounce.")]
        [Min(0)] public float fovPunchPerMetre = 4;

        public float Lifetime { get; private set; } = 15;
        public float HeightReturn { get; private set; } = .8f;
        public float MinHeight { get; private set; } = 1.5f;
        public float MaxHeight { get; private set; } = 10;
        public float Age => age;
        /// <summary>Bounces so far (for tests and tuning).</summary>
        public int Bounces { get; private set; }

        public override CardZoneKind Kind => CardZoneKind.Mushroom;

        float age, vanishAge = -1, grow;
        int bouncedFrame = -1;
        bool playerWasAirborne = true;
        SpringFloat squash, capWobble;
        Vector3 visualScale = Vector3.one;
        Quaternion capRotation = Quaternion.identity;

        Collider support;
        bool supported;

        bool Alive => vanishAge < 0;
        Vector3 CapCentre => transform.position + transform.up * capTop * transform.lossyScale.y;

        /// <summary>Numbers from the card (base or upgraded copy). Diameter rescales the prefab horizontally.</summary>
        public void Configure(float lifetimeSeconds, float heightReturn, float minHeight, float maxHeight, float capDiameter)
        {
            Lifetime = Mathf.Max(.1f, lifetimeSeconds);
            HeightReturn = heightReturn;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
            float ratio = capDiameter > 0 ? capDiameter / authoredCapDiameter : 1;
            var scale = transform.localScale;
            transform.localScale = new Vector3(scale.x * ratio, scale.y, scale.z * ratio);
            radius = capDiameter * .5f;
            height = capTop;
        }

        /// <summary>
        /// The surface the mushroom grows on. When it goes away (a Branch bridge burns down, a tree awakens and walks
        /// off) the mushroom wilts instead of hanging in the air.
        /// </summary>
        public void RestOn(Collider surface)
        {
            support = surface;
            supported = surface;
        }

        bool SupportLost => supported && (!support || !support.enabled || !support.gameObject.activeInHierarchy);

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            age = 0;
            grow = 0;
            if (visual) { visualScale = visual.localScale; visual.localScale = visualScale * .15f; }
            if (capVisual) capRotation = capVisual.localRotation;
            world.Spawn(growVfx, transform.position + transform.up * .1f, transform.rotation, tint);
            world.Play(growSound, transform.position);
        }

        // ---------- landing ----------

        /// <summary>Called by PlayerVitals for every landing faster than 2.5 m/s on one of this mushroom's colliders.</summary>
        public bool AbsorbLanding(FirstPersonMotor motor, float impactSpeed)
        {
            if (!Alive || !World || !motor || !capCollider || motor.GroundCollider != capCollider) return false;
            Bounce(motor, motor.ToReferenceSpeed(impactSpeed));
            return true;
        }

        void Bounce(FirstPersonMotor motor, float designDownSpeed)
        {
            float bounceHeight = MushroomBounce.BounceHeight(designDownSpeed, HeightReturn, MinHeight, MaxHeight);
            motor.SetVerticalSpeed(MushroomBounce.LaunchSpeed(bounceHeight) * motor.ReferenceScale);
            bouncedFrame = Time.frameCount;
            Bounces++;
            float strength = Mathf.InverseLerp(MinHeight, Mathf.Max(MinHeight + .01f, MaxHeight), bounceHeight);
            squash.Kick(-Mathf.Lerp(squashKick.x, squashKick.y, strength));
            capWobble.Kick(Random.Range(-1f, 1f) * 30 * (.5f + strength));
            World.Spawn(bounceVfx, CapCentre, Quaternion.identity, tint, Mathf.Lerp(.8f, 1.5f, strength));
            World.Play(bounceSound, CapCentre, 1, Mathf.Lerp(1.15f, .8f, strength));
            if (World.feel)
            {
                World.feel.PunchFov(fovPunchPerMetre * bounceHeight);
                World.feel.AddTrauma(.05f + .15f * strength);
            }
        }

        // ---------- life ----------

        void Update()
        {
            if (!World) return;
            float dt = Time.deltaTime;
            if (!Alive) { Vanishing(dt); return; }
            age += dt;
            if (age >= Lifetime || SupportLost) { BeginVanish(); return; }
            CatchSoftLanding();
            Animate(dt);
        }

        /// <summary>Landings slower than the motor reports (a small hop onto the cap) still bounce, at the minimum height.</summary>
        void CatchSoftLanding()
        {
            var motor = World.motor;
            if (!motor || motor.ExternallyControlled) { playerWasAirborne = true; return; }
            bool onCap = motor.IsGrounded && capCollider && motor.GroundCollider == capCollider;
            if (onCap && playerWasAirborne && Time.frameCount != bouncedFrame && World.Running) Bounce(motor, 0);
            playerWasAirborne = !motor.IsGrounded;
        }

        void Animate(float dt)
        {
            grow = Mathf.Min(1, grow + dt / growSeconds);
            float growScale = Ease.OutBack(grow, 2.2f);
            float s = squash.Step(0, springFrequency, springDamping, dt);
            float remaining = Lifetime - age;
            float tremble = remaining < warnSeconds ? Mathf.Sin(age * Mathf.Lerp(18, 46, 1 - remaining / Mathf.Max(.01f, warnSeconds))) * .045f : 0;
            float y = Mathf.Max(.2f, 1 + s + tremble);
            float xz = Mathf.Max(.2f, 1 - (s + tremble) * widthResponse);
            if (visual) visual.localScale = Vector3.Scale(visualScale, new Vector3(xz, y, xz)) * Mathf.Max(.01f, growScale);
            if (capVisual)
            {
                float wobble = capWobble.Step(0, springFrequency * 1.3f, springDamping, dt);
                capVisual.localRotation = capRotation * Quaternion.Euler(wobble, 0, -wobble * .6f);
            }
        }

        void BeginVanish()
        {
            vanishAge = 0;
            if (colliders != null) foreach (var part in colliders) if (part) part.enabled = false;
            if (World) World.Zones.Remove(this);
            World.Spawn(vanishVfx, CapCentre, Quaternion.identity, tint);
            World.Play(vanishSound, transform.position);
        }

        void Vanishing(float dt)
        {
            vanishAge += dt;
            float k = 1 - Ease.InCubic(vanishAge / vanishSeconds);
            if (visual) visual.localScale = Vector3.Scale(visualScale, new Vector3(1 + (1 - k) * .4f, k, 1 + (1 - k) * .4f)) * Mathf.Max(.01f, k);
            if (vanishAge >= vanishSeconds) Destroy(gameObject);
        }

        protected override void OnEnable()
        {
            if (Alive) base.OnEnable();
        }
    }
}
