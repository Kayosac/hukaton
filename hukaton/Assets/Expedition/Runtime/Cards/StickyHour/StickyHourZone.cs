using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The sticky hour left by «Тягучий час» (prefab <c>Assets/Game/Prefabs/Cards/StickyHour/Sticky hour.prefab</c>):
    /// a viscous violet haze of radius 4 m for 10 s. Every physics step it slows the clock of every creature
    /// (<see cref="WorldCreature.ApplyTimeScale"/>: walking, turning, wind-up, strike, recovery, aiming) and of every
    /// enemy projectile (<see cref="EnemyProjectile.ApplyTimeScale"/>: same path, slower) inside it; other wild
    /// fighters that can be slowed (<see cref="IMoveSpeedScalable"/>) walk slower. The player and allies are not
    /// touched. Leaving the zone ends the slowdown at once (the modifier is refreshed for a moment only).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickyHourZone : CardZoneBehaviour, ICardAreaOutline
    {
        [Header("Slowdown")]
        [Tooltip("Each refresh holds the slowdown this long: leaving the zone ends it almost at once, s.")]
        [Min(.02f)] public float holdSeconds = .15f;

        [Header("Look")]
        [Tooltip("Crooked haze dome, unit radius and height, scaled to the zone; seen from both sides.")]
        public Transform dome;
        public Renderer domeRenderer;
        public Color colour = new Color(.63f, .46f, .86f, 1);
        [Range(0, 1)] public float density = .35f;
        [Tooltip("Height of the dome, m (the zone itself reaches higher for flyers and shots).")]
        [Min(.1f)] public float domeHeight = 2.4f;
        [Tooltip("Retro Arsenal slow-zone sigil on the ground; scaled by the radius.")]
        public Transform sigil;
        [Min(0)] public float sigilScalePerMetre = .5f;
        [Tooltip("Retro Arsenal particles inside (motes, sparkles): spread over the radius and slowed down themselves.")]
        public ParticleSystem[] motes;
        [Tooltip("Simulation speed of the particles inside: they drift like in syrup.")]
        [Min(.01f)] public float moteSpeed = .3f;
        [Min(0)] public float gatherSeconds = .7f, fadeSeconds = 1.2f;
        [Min(0)] public float swirlDegreesPerSecond = 4, breathe = .06f;

        static readonly int TintId = Shader.PropertyToID("_Tint");

        float until, born, endedAt = -1, phase, timeScale = .5f;
        MaterialPropertyBlock block;

        public override CardZoneKind Kind => CardZoneKind.Other;
        public bool Active => World && endedAt < 0;
        public float Remaining => Active ? Mathf.Max(0, until - Time.time) : 0;
        /// <summary>Clock multiplier inside (½, ⅓, ¼).</summary>
        public float TimeScale => timeScale;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            phase = Random.value * 10;
        }

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            born = Time.time;
        }

        /// <summary>Numbers from the card asset, right after spawning.</summary>
        public void Setup(float seconds, float zoneRadius, float clockScale)
        {
            radius = Mathf.Max(.1f, zoneRadius);
            until = Time.time + Mathf.Max(0, seconds);
            timeScale = Mathf.Clamp(clockScale, .05f, 1);
            if (sigil) sigil.localScale = Vector3.one * radius * sigilScalePerMetre;
            if (motes != null)
                foreach (var mote in motes)
                {
                    if (!mote) continue;
                    var shape = mote.shape;
                    shape.radius = radius * .85f;
                    var main = mote.main;
                    main.simulationSpeed = moteSpeed;
                }
            Animate();
        }

        public override bool Contains(Vector3 point) => StickyHourRules.Inside(point, transform.position, radius, height, depth);

        void FixedUpdate()
        {
            if (!Active || !World.Running) return;
            Vector3 centre = transform.position;
            var creatures = World.ActiveCreatures;
            for (int i = 0; i < creatures.Count; i++)
            {
                var creature = creatures[i];
                if (!creature || !creature.isActiveAndEnabled || !creature.Alive) continue;
                if (StickyHourRules.Inside(creature.Position, centre, radius, height, depth, creature.CapsuleRadius))
                    creature.ApplyTimeScale(timeScale, holdSeconds);
            }
            foreach (var other in World.Wild.All)
                if (other is IMoveSpeedScalable slowed && other.Alive && StickyHourRules.Inside(other.Position, centre, radius, height, depth))
                    slowed.ApplyMoveSpeedScale(timeScale, holdSeconds);
            var shots = EnemyProjectile.Active;
            for (int i = 0; i < shots.Count; i++)
            {
                var shot = shots[i];
                if (shot && !shot.Ended && Contains(shot.transform.position)) shot.ApplyTimeScale(timeScale, holdSeconds);
            }
        }

        void Update()
        {
            if (!World) return;
            if (Active && Time.time >= until)
            {
                endedAt = Time.time;
                World.Zones.Remove(this);
                if (motes != null) foreach (var mote in motes) if (mote) mote.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            Animate();
            if (!Active && Time.time - endedAt >= fadeSeconds) Destroy(gameObject);
        }

        void Animate()
        {
            float grow = gatherSeconds > 0 ? Ease.OutCubic((Time.time - born) / gatherSeconds) : 1;
            float fade = Active ? 1 : 1 - (fadeSeconds > 0 ? Mathf.Clamp01((Time.time - endedAt) / fadeSeconds) : 1);
            float t = (Time.time + phase) * Mathf.PI * 2;
            if (dome)
            {
                // A slow, lopsided breathing: the haze sags and bulges like syrup.
                float b = 1 + Mathf.Sin(t * .11f) * breathe;
                float r = radius * Mathf.Lerp(.3f, 1, grow) * b;
                dome.localScale = new Vector3(r, domeHeight * Mathf.Lerp(.2f, 1, grow) * (2 - b) * fade, r * (1 + Mathf.Sin(t * .07f + 1) * breathe));
                dome.localRotation = Quaternion.Euler(Mathf.Sin(t * .05f) * 3, (Time.time + phase) * swirlDegreesPerSecond, Mathf.Cos(t * .06f) * 3);
            }
            if (sigil) sigil.localScale = Vector3.one * radius * sigilScalePerMetre * Mathf.Lerp(.2f, 1, grow) * Mathf.Lerp(.6f, 1, fade);
            if (domeRenderer)
            {
                if (block == null) block = new MaterialPropertyBlock();
                domeRenderer.GetPropertyBlock(block);
                block.SetColor(TintId, new Color(colour.r, colour.g, colour.b, colour.a * density * Mathf.Clamp01(grow * 1.5f) * fade));
                domeRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>Outlined on the ground while it works (<see cref="CardAreaDisplay"/>).</summary>
        public bool TryGetOutline(out Vector3 centre, out float radius)
        {
            centre = transform.position;
            radius = this.radius;
            return Active;
        }
    }
}
