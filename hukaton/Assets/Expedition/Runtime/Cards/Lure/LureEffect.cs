using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Манок» (воздух, ранг 1/5). A noise source (<see cref="LureNoise"/>) at the point of impact sounds for 6 s
    /// (upgraded 10 s). Calm creatures within 10 m go to check it but never leave their territory; creatures already
    /// attacking are not distracted.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class LureEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Sound duration, s (board: 6 → 10).")]
        [Min(0)] public float durationBase = 6, durationUpgraded = 10;
        [Tooltip("Calm creatures within this radius come to check the noise, m (same on both levels).")]
        [Min(0)] public float radius = 10;

        [Header("Placement")]
        public LureNoise noisePrefab;
        [Tooltip("The noise sits on the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 4;
        [Tooltip("Surfaces steeper than this (walls) are not ground, degrees.")]
        [Range(0, 90)] public float groundSlope = 60;

        public override void Apply(in ImpactContext c)
        {
            if (!noisePrefab)
            {
                Feedback(c, false);
                return;
            }
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _)) ground = c.Point;
            var noise = c.World.SpawnForOuting(noisePrefab, ground, Quaternion.identity);
            if (noise) noise.Setup(c.Pick(durationBase, durationUpgraded), radius);
            Feedback(c, noise);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Звук";
            baseValue = durationBase;
            upgradedValue = durationUpgraded;
            unit = " с";
            return true;
        }
    }
}
