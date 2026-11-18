using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Эхолот» (trophy card of the anthill, pg_anthill-loot-cards). Not thrown at anything: the moment it leaves the
    /// hand it goes up in a puff and a sound wave spreads from the player (a ring along the ground, a sonar ping and its
    /// echo) — above ground and in a dungeon alike. Every enemy within 25 / 40 / 60 m (levels I / II / III) lights up as
    /// a turquoise silhouette seen through walls and rock for 10 s, as the wave reaches it (<see cref="SonarMark"/>):
    /// aggressive and territorial creatures, any creature attacking the player's side, and chest mimics (a shut mimic
    /// is found out). Recruits, peaceful and timid creatures are left dark. The wave is one: whoever comes in later is
    /// not lit. Nobody in reach: a short note.
    /// </summary>
    public sealed class SonarEffect : CardEffect, ICardStatLine, ICustomFlight
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Enemies within this distance of the player light up, m (pg_anthill-loot-cards: 25 / 40 / 60).")]
        public LevelFloat radius = new LevelFloat(25, 40, 60);
        [Tooltip("How long an enemy stays lit, s.")]
        [Min(1)] public float seconds = 10;

        [Header("Wave")]
        [Tooltip("The ring that runs out along the ground.")]
        public SonarWave wavePrefab;
        [Tooltip("How fast the wave spreads, m/s: an enemy lights up when it gets there.")]
        [Min(1)] public float waveSpeed = 32;
        [Tooltip("The silhouette drawn over a lit enemy: dim where it stands in the open, bright behind walls.")]
        public Material silhouette;
        public Color colour = new Color(.3f, 1f, .9f, 1f);
        [Tooltip("A soft blip as the wave finds each enemy (spatial, from the enemy).")]
        public SfxCue foundSound;
        [Range(0, 1)] public float foundVolume = .55f;
        [Tooltip("Shown when nothing is in reach.")]
        public string nothingNote = "Эхолот никого не слышит";

        static readonly List<Component> Found = new List<Component>();

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            Vector3 centre = world.motor ? world.motor.transform.position : c.Point;
            float reach = c.At(radius);
            Collect(world, centre, reach, Found);
            if (wavePrefab)
            {
                var wave = world.SpawnForOuting(wavePrefab, centre + Vector3.up * .15f, Quaternion.identity);
                if (wave) wave.Run(reach, waveSpeed, colour);
            }
            foreach (var target in Found)
            {
                float distance = Vector3.Distance(centre, target.transform.position);
                SonarMark.Light(world, target, silhouette, colour, distance / waveSpeed, seconds, foundSound, foundVolume);
            }
            if (Found.Count == 0 && !string.IsNullOrEmpty(nothingNote)) world.Announce(nothingNote, 2.5f);
            // The ping is the player's own: played without a position.
            world.Play(impactSound);
            if (world.feel) world.feel.AddTrauma(Found.Count > 0 ? hitShake : shake);
            world.Spawn(impactVfx, centre + Vector3.up * .1f, Quaternion.identity, colour, vfxScale);
            Found.Clear();
        }

        /// <summary>
        /// The enemies within <paramref name="reach"/> of <paramref name="centre"/>: hostile creatures (and any attacking
        /// the player's side), shut or awake chest mimics. Not recruits, not the peaceful and timid.
        /// </summary>
        public static void Collect(ExpeditionWorld world, Vector3 centre, float reach, List<Component> into)
        {
            into.Clear();
            var creatures = world.ActiveCreatures;
            if (creatures != null)
                foreach (var creature in creatures)
                {
                    if (!creature || !creature.isActiveAndEnabled || !creature.Alive || creature.Recruited) continue;
                    if (!SonarRules.Lights(creature.temperament, Attacking(creature))) continue;
                    if (Vector3.Distance(centre, creature.Position) <= reach) into.Add(creature);
                }
            foreach (var fighter in world.Wild.All)
                if (fighter is ChestMimic mimic && mimic && mimic.isActiveAndEnabled && mimic.Alive
                    && Vector3.Distance(centre, mimic.transform.position) <= reach)
                    into.Add(mimic);
        }

        static bool Attacking(WorldCreature creature)
        {
            var target = creature.Target;
            return Combat.IsAlive(target) && target.Faction == Faction.Player && creature.State != CreatureState.Idle
                && creature.State != CreatureState.Wander && creature.State != CreatureState.Graze;
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Радиус";
            values = radius;
            unit = " м";
            return true;
        }

        // ---------- not thrown: goes off as it leaves the hand ----------

        bool ICustomFlight.MayComeBack => false;

        ICardFlightPath ICustomFlight.BeginFlight(in CardFlightLaunch launch) => new AtOnce();

        int ICustomFlight.PreviewPath(in CardFlightLaunch launch, Vector3[] points)
        {
            if (points != null && points.Length > 0) points[0] = launch.Origin;
            return 1;
        }

        /// <summary>The first physics step resolves the card where it is: the wave starts from the player.</summary>
        sealed class AtOnce : ICardFlightPath
        {
            public void Step(CardProjectile card, float dt) => card.ResolveInPlace(Vector3.up);
            public float SpinMultiplier => 1;
            public SfxCue FlightLoop => null;
        }
    }

    /// <summary>Plain rules of «Эхолот» (<see cref="SonarEffect"/>), for EditMode tests.</summary>
    public static class SonarRules
    {
        /// <summary>A creature lights up: aggressive or territorial, or any that is attacking the player's side.</summary>
        public static bool Lights(Temperament temperament, bool attackingPlayerSide)
            => attackingPlayerSide || temperament == Temperament.Aggressive || temperament == Temperament.Territorial;

        /// <summary>
        /// The silhouette's strength at <paramref name="t"/> seconds after the wave reached it (negative: not yet),
        /// lit for <paramref name="seconds"/>: a quick rise, a slow breathing pulse, a fade over the last second.
        /// </summary>
        public static float Strength(float t, float seconds, float rise = .25f, float fade = 1)
        {
            if (t < 0 || t >= seconds) return 0;
            float k = Mathf.Clamp01(t / Mathf.Max(.01f, rise));
            float end = Mathf.Clamp01((seconds - t) / Mathf.Max(.01f, fade));
            float pulse = .85f + .15f * Mathf.Cos(t * 4.2f);
            return k * end * pulse;
        }
    }
}
