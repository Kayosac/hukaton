using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Тягучий час». A zone of radius 4 m for 10 s on the ground where the card lands (<see cref="StickyHourZone"/>):
    /// creatures inside walk, turn and attack (wind-up, strike, recovery, aiming) 2 / 3 / 4 times slower, enemy
    /// projectiles inside fly 2 / 3 / 4 times slower along the same path; the player moves as usual.
    /// </summary>
    public sealed class StickyHourEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Card numbers (I / II / III)")]
        [Tooltip("How many times slower creatures and enemy projectiles are inside.")]
        public LevelFloat slowdown = new LevelFloat(2, 3, 4);
        [Tooltip("Zone radius, m.")]
        [Min(.1f)] public float radius = 4;
        [Tooltip("How long the zone lasts, s.")]
        [Min(0)] public float duration = 10;

        [Header("Placement")]
        public StickyHourZone zonePrefab;
        [Tooltip("The zone settles on the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 6;
        [Tooltip("Surfaces steeper than this (walls) are not ground: the zone settles on the ground below, degrees.")]
        [Range(0, 90)] public float groundSlope = 60;

        public override void Apply(in ImpactContext c)
        {
            if (!zonePrefab)
            {
                Feedback(c, false);
                return;
            }
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _)) ground = c.Point;
            var zone = c.World.SpawnForOuting(zonePrefab, ground, Quaternion.Euler(0, Random.Range(0, 360f), 0));
            if (zone) zone.Setup(duration, radius, StickyHourRules.TimeScale(c.At(slowdown)));
            Feedback(c, zone);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Медленнее в";
            values = slowdown;
            unit = " раза";
            return true;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float area)
        {
            area = radius;
            return true;
        }

        bool ICardArea.LeavesOutlinedZone => true;
    }
}
