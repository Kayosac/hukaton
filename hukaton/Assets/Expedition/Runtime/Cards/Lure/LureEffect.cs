using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Манок» (воздух, ранг 1/5). A noise source (<see cref="LureNoise"/>) at the point of impact sounds for
    /// 6 / 12 / 20 s. Every calm creature (aggressive ones too) within 20 / 40 / 70 m (levels I / II / III) comes to it, even from outside their
    /// territory, and settle there; creatures already attacking are not distracted.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class LureEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Sound duration, s (board: 6 → 10).")]
        public LevelFloat duration = new LevelFloat(6, 12, 20);
        [Tooltip("Calm creatures within this radius come to check the noise, m (board: 20 → 40 → 70).")]
        public LevelFloat radius = new LevelFloat(20, 40, 70);

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
            if (noise) noise.Setup(c.At(duration), c.At(radius));
            Feedback(c, noise);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            // The radius is what grows most with the level, so the PC shows it.
            label = "Радиус";
            values = radius;
            unit = " м";
            return true;
        }
    }
}
