using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Pure geometry of the rain (EditMode-tested).</summary>
    public static class RainRules
    {
        /// <summary>Two vertical cylinders overlap in plan view (touching edges do not count).</summary>
        public static bool CirclesOverlap(Vector3 a, float radiusA, Vector3 b, float radiusB)
        {
            float dx = a.x - b.x, dz = a.z - b.z, reach = radiusA + radiusB;
            return dx * dx + dz * dz < reach * reach;
        }
    }

    /// <summary>
    /// A still rain cloud left by «Облако дождя» (prefab <c>Assets/Game/Prefabs/Cards/RainCloud/Rain cloud.prefab</c>).
    /// Every tick while it rains: every fighter under it (player, creatures, allies) is made wet
    /// (<see cref="WetStatus.Soak(Component, ExpeditionWorld, float)"/> — the 30 s count from the last contact), and every
    /// washable zone it reaches (<see cref="IWashable"/>: tar, burning or not; a burning bridge) is washed. A puddle
    /// touching the rain is removed whole (DECISIONS.md) and never comes back. The rain does no damage and heals nobody.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RainCloudZone : CardZoneBehaviour, ICardAreaOutline
    {
        [Header("Rain")]
        [Tooltip("Cloud height above the ground under the impact, m.")]
        [Min(.5f)] public float cloudHeight = 4.2f;
        [Tooltip("How often the rain wets fighters and washes zones, s.")]
        [Min(.02f)] public float tickSeconds = .1f;
        [Tooltip("HUD badge under the hearts while the player is wet (no drops are drawn in first person): full under the " +
                 "rain, its ring drains over the 30 s after leaving it.")]
        public PlayerStatusBadge wetBadge;

        [Header("Look")]
        public Transform cloud;
        [Tooltip("Retro Arsenal rain under the cloud; its shape follows the rain radius.")]
        public ParticleSystem rain;
        [Min(0)] public float dropsPerSquareMetre = 9;
        [Tooltip("Smoke wisps of the cloud (Retro Arsenal).")]
        public ParticleSystem[] wisps;
        [Min(0)] public float gatherSeconds = .7f, fadeSeconds = 1.4f;
        [Tooltip("Slow lumpy breathing of the cloud.")]
        [Min(0)] public float sway = .06f;

        [Header("Feedback")]
        public SfxCue rainLoop;

        static readonly List<ICombatant> Fighters = new List<ICombatant>();
        static readonly List<ICardZone> Touched = new List<ICardZone>();

        float until, linger = WetStatus.DefaultLingerSeconds, nextTick, born, stoppedAt = -1, phase;
        Vector3 cloudScale = Vector3.one;

        public override CardZoneKind Kind => CardZoneKind.Rain;
        /// <summary>Still raining (false once the cloud's time is up and it is fading).</summary>
        public bool Raining => World && stoppedAt < 0;
        public float Remaining => Raining ? Mathf.Max(0, until - Time.time) : 0;
        public float WetLingerSeconds => linger;

        void Awake()
        {
            phase = Random.value * 10;
            if (cloud) cloudScale = cloud.localScale;
        }

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            born = Time.time;
        }

        /// <summary>Numbers from the card asset, right after spawning.</summary>
        public void Setup(float seconds, float rainRadius, float wetLingerSeconds)
        {
            radius = Mathf.Max(.1f, rainRadius);
            height = cloudHeight + .3f;
            until = Time.time + Mathf.Max(0, seconds);
            linger = Mathf.Max(0, wetLingerSeconds);
            nextTick = 0;
            if (cloud)
            {
                cloud.localPosition = Vector3.up * cloudHeight;
                cloudScale = new Vector3(radius, cloudScale.y, radius);
                cloud.localScale = cloudScale * .3f;
            }
            if (rain)
            {
                var shape = rain.shape;
                shape.radius = radius;
                var emission = rain.emission;
                emission.rateOverTime = dropsPerSquareMetre * Mathf.PI * radius * radius;
                var main = rain.main;
                float speed = Mathf.Max(1, main.startSpeed.constantMax);
                main.startLifetime = Mathf.Max(.05f, (cloudHeight - .15f) / speed);
                rain.Play(true);
            }
            if (World && World.sfx && rainLoop) World.sfx.Loop(this, rainLoop, transform.position + Vector3.up * 1.5f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (World && World.sfx) World.sfx.StopLoop(this);
        }

        void Update()
        {
            if (!World) return;
            if (Raining)
            {
                if (Time.time >= nextTick)
                {
                    nextTick = Time.time + tickSeconds;
                    SoakEveryoneUnder();
                    WashWhatItReaches();
                }
                if (Time.time >= until) StopRaining();
            }
            else if (Time.time - stoppedAt >= fadeSeconds && (!rain || !rain.IsAlive(true))) Destroy(gameObject);
            AnimateCloud();
        }

        /// <summary>The rain reaches this zone: plan-view circles overlap (bounds for other shapes).</summary>
        public bool Reaches(ICardZone zone)
        {
            if (zone == null || ReferenceEquals(zone, this) || !zone.Bounds.Intersects(Bounds)) return false;
            return !(zone is CardZoneBehaviour other) || RainRules.CirclesOverlap(transform.position, radius, other.transform.position, other.radius);
        }

        /// <summary>A world point is under the rain (between the ground and the cloud).</summary>
        public bool Covers(Vector3 point) => Raining && Contains(point);

        void SoakEveryoneUnder()
        {
            Fighters.Clear();
            World.Combatants(Fighters);
            foreach (var fighter in Fighters)
            {
                if (!Contains(fighter.Position) && !Contains(fighter.Centre)) continue;
                bool player = ReferenceEquals(fighter, World.Player);
                bool wasWet = player && WetStatus.IsWet(fighter.Root);
                WetStatus.Soak(fighter.Root, World, linger);
                // The badge follows the wet status itself: shown when the player gets wet, it stays while they are.
                if (player && !wasWet && wetBadge) PlayerStatusBadge.Show(World, wetBadge);
            }
        }

        void WashWhatItReaches()
        {
            Touched.Clear();
            World.Zones.Overlapping(Bounds, Touched);
            foreach (var zone in Touched)
                if (zone is IWashable washable && Reaches(zone)) washable.Wash(this);
        }

        void StopRaining()
        {
            stoppedAt = Time.time;
            World.Zones.Remove(this);
            if (rain) rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (wisps != null) foreach (var wisp in wisps) if (wisp) wisp.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (World.sfx) World.sfx.StopLoop(this);
        }

        void AnimateCloud()
        {
            if (!cloud) return;
            float grow = gatherSeconds > 0 ? Ease.OutBack(Mathf.Clamp01((Time.time - born) / gatherSeconds)) : 1;
            float shrink = stoppedAt < 0 ? 1 : 1 - Ease.InCubic(fadeSeconds > 0 ? Mathf.Clamp01((Time.time - stoppedAt) / fadeSeconds) : 1);
            float t = (Time.time + phase) * Mathf.PI * 2;
            var breathe = new Vector3(1 + Mathf.Sin(t * .23f) * sway, 1 + Mathf.Sin(t * .31f + 1) * sway * 1.5f, 1 + Mathf.Sin(t * .19f + 2) * sway);
            cloud.localScale = Vector3.Scale(cloudScale, breathe) * Mathf.Lerp(.3f, 1, grow) * shrink;
            cloud.localRotation = Quaternion.Euler(0, (Time.time + phase) * 4f, 0);
        }

        /// <summary>Outlined on the ground while it works (<see cref="CardAreaDisplay"/>).</summary>
        public bool TryGetOutline(out Vector3 centre, out float radius)
        {
            centre = transform.position;
            radius = this.radius;
            return Raining;
        }
    }
}
