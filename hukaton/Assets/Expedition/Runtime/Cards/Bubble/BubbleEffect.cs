using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Пузырь» (ранг 2). A soap dome of radius 3 m stands on the ground where the card lands (below a creature or a
    /// wall it hit) for 4 / 6 / 9 s (<see cref="BubbleDome"/>): enemy projectiles flying in pop, creatures are pushed
    /// out and cannot get in (by script — the dome has no collider), the player walks through freely.
    /// Brief: tmp/new-cards-2/BRIEF.md (6); shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class BubbleEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Card numbers (I / II / III)")]
        [Tooltip("How long the dome stands, s.")]
        public LevelFloat duration = new LevelFloat(4, 8, 16);
        [Tooltip("Radius of the dome, m.")]
        [Min(.5f)] public float radius = 3;

        [Header("Placement")]
        public BubbleDome domePrefab;
        [Tooltip("The dome stands on the ground found this far below a wall or creature hit, m.")]
        [Min(0)] public float dropDistance = 4;
        [Tooltip("Surfaces steeper than this (walls) are not ground, degrees.")]
        [Range(0, 90)] public float groundSlope = 60;

        public override void Apply(in ImpactContext c)
        {
            if (!domePrefab)
            {
                Debug.LogWarning("Bubble: no dome prefab assigned.", this);
                Feedback(c, false);
                return;
            }
            if (!CardGround.TryFind(c.Target, c.Point, c.Normal, groundSlope, dropDistance, out var ground, out _)) ground = c.Point;
            var dome = c.World.SpawnForOuting(domePrefab, ground, Quaternion.Euler(0, Random.Range(0, 360f), 0));
            if (dome) dome.Setup(c.At(duration), radius);
            Feedback(c, dome);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Длительность";
            values = duration;
            unit = " с";
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
