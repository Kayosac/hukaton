using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>How a trampoline mushroom stands and moves.</summary>
    public enum MushroomShape
    {
        /// <summary>A mushroom on a stem: squashes and stretches from the foot of the stem.</summary>
        Upright,
        /// <summary>A bracket shelf grown out of a wall (local +Z out of the wall): bends down around its hinge on the wall.</summary>
        WallShelf,
    }

    /// <summary>
    /// The trampoline mushroom (prefab root): «Mushroom trampoline» on the ground, «Mushroom shelf» on a wall. Lives
    /// <see cref="Lifetime"/> seconds from its appearance, then shrinks away. Landing on the cap from above (the motor
    /// only reports ground under a surface facing up, so touching the side or the underside does nothing) takes the fall
    /// over: no fall damage, the vertical speed is replaced by the bounce (<see cref="MushroomBounce"/>), the horizontal
    /// speed is kept. Consecutive bounces on mushrooms form a series that climbs higher (<see cref="MushroomSeries"/>).
    /// Any number of bounces. Only the player bounces. Colliders stay put; only the visual squashes, stretches or bends.
    /// Registered as a card zone (Kind Mushroom).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushroomTrampoline : CardZoneBehaviour, ILandingSurface
    {
        /// <summary>The stem column of an upright mushroom: what must be clear of static geometry (local, full size).</summary>
        public static readonly Vector3 DefaultRoomCentre = new Vector3(0, .6f, 0);
        public static readonly Vector3 DefaultRoomSize = new Vector3(.44f, .6f, .44f);

        [Header("Authored parts")]
        [Tooltip("Upright: a mushroom on a stem. Wall shelf: a bracket shelf, local +Z out of the wall, the root on the wall surface.")]
        public MushroomShape shape = MushroomShape.Upright;
        [Tooltip("Pivot of the whole visual (the foot of the stem, or the shelf's hinge on the wall): grows, squashes, stretches or bends.")]
        public Transform visual;
        [Tooltip("The cap's visual: wobbles on its own after a bounce (optional).")]
        public Transform capVisual;
        [Tooltip("The cap's collider. Only standing on it bounces.")]
        public Collider capCollider;
        [Tooltip("Colliders switched off when the mushroom goes.")]
        public Collider[] colliders;
        [Tooltip("Height of the cap's top over the root, m.")]
        public float capTop = .9f;
        [Tooltip("How far the cap's centre stands out along local +Z from the root, m (a shelf: out of the wall).")]
        [Min(0)] public float capReach;
        [Tooltip("Cap diameter of the authored meshes and colliders, m (the card's cap diameter rescales width and depth).")]
        [Min(.1f)] public float authoredCapDiameter = 1.6f;

        [Header("Placement")]
        [Tooltip("Local box (centre) that must be clear of static geometry: the stem column, or the shelf's body in front of the wall. The cap's rim may clip into trunks and rocks.")]
        public Vector3 roomCentre = DefaultRoomCentre;
        [Tooltip("Size of that box at the authored cap size (width and depth shrink with a cramped cap).")]
        public Vector3 roomSize = DefaultRoomSize;

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
        [Tooltip("Upright: how far the visual squashes and stretches: y × (1 + s), xz × (1 − s·k).")]
        [Range(0, 1)] public float widthResponse = .55f;
        [Tooltip("Wall shelf: degrees the shelf bends down around its hinge per unit of squash.")]
        [Min(0)] public float hingeBend = 28;
        [Tooltip("Field-of-view punch per metre of bounce.")]
        [Min(0)] public float fovPunchPerMetre = 4;
        [Tooltip("Each bounce of a series raises the bounce sound's pitch by this share.")]
        [Min(0)] public float seriesPitchStep = .04f;

        public float Lifetime { get; private set; } = 15;
        public float HeightReturn { get; private set; } = .8f;
        public float MinHeight { get; private set; } = 1.5f;
        public float MaxHeight { get; private set; } = 10;
        public float SeriesStepShare { get; private set; } = MushroomBounce.SeriesStepShare;
        public int SeriesMaxSteps { get; private set; } = MushroomBounce.SeriesMaxSteps;
        public float Age => age;
        /// <summary>Bounces on this mushroom so far (for tests and tuning).</summary>
        public int Bounces { get; private set; }

        public override CardZoneKind Kind => CardZoneKind.Mushroom;

        float age, vanishAge = -1, grow;
        int bouncedFrame = -1;
        bool playerWasAirborne = true;
        SpringFloat squash, capWobble;
        Vector3 visualScale = Vector3.one;
        Quaternion visualRotation = Quaternion.identity;
        Quaternion capRotation = Quaternion.identity;

        Collider support;
        bool supported;

        bool Alive => vanishAge < 0;
        /// <summary>The point on the ground (or on the wall's face, under the cap's middle) the zone stands on.</summary>
        Vector3 Foot => transform.position + transform.forward * (capReach * transform.lossyScale.z);
        Vector3 CapCentre => Foot + transform.up * capTop * transform.lossyScale.y;

        /// <summary>Numbers from the card (base or upgraded copy). Diameter rescales the prefab horizontally.</summary>
        public void Configure(float lifetimeSeconds, float heightReturn, float minHeight, float maxHeight, float capDiameter,
            float seriesStepShare = MushroomBounce.SeriesStepShare, int seriesMaxSteps = MushroomBounce.SeriesMaxSteps)
        {
            Lifetime = Mathf.Max(.1f, lifetimeSeconds);
            HeightReturn = heightReturn;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
            SeriesStepShare = Mathf.Max(0, seriesStepShare);
            SeriesMaxSteps = Mathf.Max(0, seriesMaxSteps);
            float ratio = capDiameter > 0 ? capDiameter / authoredCapDiameter : 1;
            var scale = transform.localScale;
            transform.localScale = new Vector3(scale.x * ratio, scale.y, scale.z * ratio);
            radius = capDiameter * .5f;
            height = Mathf.Max(.01f, capTop);
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

        public override Bounds Bounds
        {
            get
            {
                Vector3 p = Foot;
                return new Bounds(new Vector3(p.x, p.y + (height - depth) * .5f, p.z), new Vector3(radius * 2, height + depth, radius * 2));
            }
        }

        public override bool Contains(Vector3 point)
        {
            Vector3 p = Foot;
            float dx = point.x - p.x, dz = point.z - p.z;
            return dx * dx + dz * dz <= radius * radius && point.y >= p.y - depth && point.y <= p.y + height;
        }

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            age = 0;
            grow = 0;
            if (visual)
            {
                visualScale = visual.localScale;
                visualRotation = visual.localRotation;
                visual.localScale = visualScale * .15f;
            }
            if (capVisual) capRotation = capVisual.localRotation;
            world.Spawn(growVfx, Foot + transform.up * .1f, transform.rotation, tint);
            world.Play(growSound, transform.position);
        }

        // ---------- landing ----------

        /// <summary>Called by PlayerVitals when the motor reports a real fall (Landed) faster than 2.5 m/s onto one of this mushroom's colliders.</summary>
        public bool AbsorbLanding(FirstPersonMotor motor, float impactSpeed)
        {
            if (!Alive || !World || !motor || !capCollider || motor.GroundCollider != capCollider) return false;
            Bounce(motor, motor.ToReferenceSpeed(impactSpeed));
            return true;
        }

        void Bounce(FirstPersonMotor motor, float designDownSpeed)
        {
            var series = MushroomSeries.Of(World);
            int step = series.Bounces;
            float fallHeight = MushroomBounce.BounceHeight(designDownSpeed, HeightReturn, MinHeight, MaxHeight);
            float bounceHeight = series.Next(fallHeight, MaxHeight, SeriesStepShare, SeriesMaxSteps);
            motor.SetVerticalSpeed(MushroomBounce.LaunchSpeed(bounceHeight) * motor.ReferenceScale);
            series.Touch(Time.frameCount, motor.transform.position, motor.Velocity.magnitude);
            bouncedFrame = Time.frameCount;
            Bounces++;
            float strength = Mathf.InverseLerp(MinHeight, Mathf.Max(MinHeight + .01f, MaxHeight), bounceHeight);
            squash.Kick(-Mathf.Lerp(squashKick.x, squashKick.y, strength));
            capWobble.Kick(Random.Range(-1f, 1f) * 30 * (.5f + strength));
            World.Spawn(bounceVfx, CapCentre, Quaternion.identity, tint, Mathf.Lerp(.8f, 1.5f, strength));
            float pitch = Mathf.Lerp(1.15f, .8f, strength) * (1 + seriesPitchStep * Mathf.Min(step, SeriesMaxSteps));
            World.Play(bounceSound, CapCentre, 1, pitch);
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
            MushroomSeries.Of(World).Observe(World, World.motor);
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
            float growScale = Mathf.Max(.01f, Ease.OutBack(grow, 2.2f));
            float s = squash.Step(0, springFrequency, springDamping, dt);
            float remaining = Lifetime - age;
            float tremble = remaining < warnSeconds ? Mathf.Sin(age * Mathf.Lerp(18, 46, 1 - remaining / Mathf.Max(.01f, warnSeconds))) * .045f : 0;
            if (visual)
            {
                if (shape == MushroomShape.WallShelf)
                {
                    // The shelf bends down around its hinge on the wall and thins a little, then springs back past level.
                    float bend = Mathf.Clamp(-(s + tremble) * hingeBend, -hingeBend, hingeBend);
                    float thickness = Mathf.Max(.3f, 1 + (s + tremble) * .3f);
                    visual.localRotation = visualRotation * Quaternion.Euler(bend, 0, 0);
                    visual.localScale = Vector3.Scale(visualScale, new Vector3(1, thickness, 1)) * growScale;
                }
                else
                {
                    float y = Mathf.Max(.2f, 1 + s + tremble);
                    float xz = Mathf.Max(.2f, 1 - (s + tremble) * widthResponse);
                    visual.localScale = Vector3.Scale(visualScale, new Vector3(xz, y, xz)) * growScale;
                }
            }
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
            if (visual)
            {
                if (shape == MushroomShape.WallShelf)
                {
                    // Wilts: droops and shrinks back into the wall.
                    visual.localRotation = visualRotation * Quaternion.Euler((1 - k) * 35, 0, 0);
                    visual.localScale = visualScale * Mathf.Max(.01f, k);
                }
                else visual.localScale = Vector3.Scale(visualScale, new Vector3(1 + (1 - k) * .4f, k, 1 + (1 - k) * .4f)) * Mathf.Max(.01f, k);
            }
            if (vanishAge >= vanishSeconds) Destroy(gameObject);
        }

        protected override void OnEnable()
        {
            if (Alive) base.OnEnable();
        }
    }
}
