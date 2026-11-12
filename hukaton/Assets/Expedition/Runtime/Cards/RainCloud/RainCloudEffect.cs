using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Облако дождя» (вода*, ранг 2/5*). A still rain cloud (<see cref="RainCloudZone"/>) over the point of impact:
    /// radius 3 / 4.5 / 6 m (I / II / III), 15 s. Rain washes tar away, puts fire out and makes everyone under it wet (player,
    /// creatures, allies); wet lasts 30 s after the last contact. Rain does no damage and does not heal.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class RainCloudEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Cloud duration, s (same on every level).")]
        [Min(0)] public float durationSeconds = 15;
        [Tooltip("Rain radius, m: 3 / 4,5 / 6 (the area grows ×1 / ×2,25 / ×4).")]
        public LevelFloat radius = new LevelFloat(3, 4.5f, 6);
        [Tooltip("Wet after leaving the rain, s (confirmed 30 on both levels).")]
        [Min(0)] public float wetLingerSeconds = WetStatus.DefaultLingerSeconds;

        [Header("Placement")]
        public RainCloudZone cloudPrefab;
        [Tooltip("The cloud hangs over the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 12;
        [Tooltip("Surfaces steeper than this (walls) are not ground: the effect settles on the ground below, degrees.")]
        [Range(0, 90)] public float groundSlope = 60;

        public override void Apply(in ImpactContext c)
        {
            if (!cloudPrefab)
            {
                Feedback(c, false);
                return;
            }
            // Over the ground under the impact ; in mid-air hits the cloud forms at the point itself.
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _)) ground = c.Point;
            var cloud = c.World.SpawnForOuting(cloudPrefab, ground, Quaternion.identity);
            if (cloud) cloud.Setup(durationSeconds, c.At(radius), wetLingerSeconds);
            Feedback(c, cloud);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Радиус";
            values = radius;
            unit = " м";
            return true;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = this.radius.At(level);
            return true;
        }

        bool ICardArea.LeavesOutlinedZone => true;
    }
}
