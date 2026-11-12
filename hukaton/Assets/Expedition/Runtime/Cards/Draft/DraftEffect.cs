using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Сквозняк». A gust (<see cref="DraftGust"/>) along a strip from the ground under the impact in the horizontal
    /// direction of the throw: 8 / 12 / 16 m long (I / II / III), 3 m wide, about 1.5 s. In the strip: fog is blown
    /// away (drifts off and thins out), rain clouds are carried to the far end (and rain on there), burning tar goes out
    /// (the puddle stays and can be lit again), fire is carried downwind (what can burn and creatures up to 4 m downwind
    /// of something burning catch fire), the wet dry at once, and loose bodies get a light push along the wind.
    /// </summary>
    public sealed class DraftEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Length of the wind strip, m.")]
        public LevelFloat length = new LevelFloat(8, 12, 16);
        [Tooltip("Width of the wind strip, m.")]
        [Min(.5f)] public float width = 3;
        [Tooltip("How long the gust blows, s: things that get into the strip meanwhile are caught too.")]
        [Min(.1f)] public float gustSeconds = 1.5f;

        [Header("Strip")]
        [Tooltip("The strip starts this far behind the impact, so a creature hit directly is inside it, m.")]
        [Min(0)] public float startBehind = .75f;
        [Tooltip("The strip reaches this far below the ground under the impact (downhill slopes), m.")]
        [Min(0)] public float below = 8;
        [Tooltip("…and this high above it (a rain cloud hangs about 4.2 m up), m.")]
        [Min(.5f)] public float above = 6;
        [Tooltip("How often the gust acts while it blows, s.")]
        [Min(.02f)] public float tickSeconds = .15f;

        [Header("Push")]
        [Tooltip("Impulse along the wind to every loose body in the strip (once per body), board units (Δv = I / μ).")]
        [Min(0)] public float pushImpulse = 4;
        [Tooltip("Share of the push that lifts (so bodies leave the ground instead of scraping it).")]
        [Range(0, 1)] public float lift = .15f;
        [Tooltip("Creatures lose their footing this long, so their legs do not cancel the push, s.")]
        [Min(0)] public float creatureStagger = .3f;

        [Header("Fire")]
        [Tooltip("Things and creatures this far downwind of something burning catch fire, m.")]
        [Min(0)] public float fireReach = 4;
        [Tooltip("…and no further than this to either side of it, m.")]
        [Min(0)] public float fireSpread = 1.5f;
        [Tooltip("Fire damage to a creature the wind sets alight (½ heart = 1).")]
        [Min(0)] public int fireDamage = 1;
        [Tooltip("How long a creature set alight by the wind burns, s.")]
        [Min(0)] public float fireBurnSeconds = 2.5f;
        public VfxBurst igniteSpark;
        [Min(0)] public float igniteSparkScale = .6f;
        public SfxCue igniteSound;

        [Header("Fog and rain")]
        [Tooltip("Fog is blown off at this speed along the wind, m/s…")]
        [Min(0)] public float fogDriftSpeed = 7;
        [Tooltip("…and thins out within this time (then fades as usual), s.")]
        [Min(0)] public float fogDisperseSeconds = .4f;
        [Tooltip("A rain cloud is carried to the far end of the strip in this time, s.")]
        [Min(.1f)] public float rainCarrySeconds = 1.6f;

        [Header("Placement")]
        public DraftGust gustPrefab;
        [Tooltip("The strip starts on the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 6;
        [Tooltip("Surfaces steeper than this (walls) are not ground, degrees.")]
        [Range(0, 90)] public float groundSlope = 60;

        public override void Apply(in ImpactContext c)
        {
            if (!gustPrefab)
            {
                Feedback(c, false);
                return;
            }
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _)) ground = c.Point;
            var camera = c.World.viewCamera ? c.World.viewCamera.transform.forward : Vector3.forward;
            var direction = DraftRules.WindDirection(c.Velocity, c.ThrowOrigin, c.Point, camera);
            var strip = new DraftStrip(ground - direction * startBehind, direction, c.At(length) + startBehind, width, below, above);
            var gust = c.World.SpawnForOuting(gustPrefab, strip.Origin, strip.Rotation);
            bool changed = gust && gust.Setup(this, strip, c.Accent);
            Feedback(c, changed);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Длина";
            values = length;
            unit = " м";
            return true;
        }
    }
}
