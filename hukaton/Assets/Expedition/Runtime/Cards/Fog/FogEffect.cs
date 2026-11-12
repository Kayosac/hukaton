using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Туман» (вода, ранг 2/5). A fog area (<see cref="FogZone"/>) of radius 3 m for 8 s (upgraded 12 s) at the point
    /// of impact (on the ground below it) blocks vision for the player and for creatures. Losing sight does not erase
    /// aggression: creatures search at the last known place. Fog does not make anyone wet.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class FogEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Fog duration, s (board: 8 → 12).")]
        public LevelFloat duration = new LevelFloat(8, 14, 24);
        [Tooltip("Fog radius, m (same on both levels).")]
        [Min(0)] public float radius = 3;

        [Header("Placement")]
        public FogZone fogPrefab;
        [Tooltip("The fog settles on the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 6;
        [Tooltip("Surfaces steeper than this (walls) are not ground: the effect settles on the ground below, degrees.")]
        [Range(0, 90)] public float groundSlope = 60;

        public override void Apply(in ImpactContext c)
        {
            if (!fogPrefab)
            {
                Feedback(c, false);
                return;
            }
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _)) ground = c.Point;
            var fog = c.World.SpawnForOuting(fogPrefab, ground, Quaternion.identity);
            if (fog) fog.Setup(c.At(duration), radius);
            Feedback(c, fog);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Длительность";
            values = duration;
            unit = " с";
            return true;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = this.radius;
            return true;
        }

        bool ICardArea.LeavesOutlinedZone => true;
    }
}
