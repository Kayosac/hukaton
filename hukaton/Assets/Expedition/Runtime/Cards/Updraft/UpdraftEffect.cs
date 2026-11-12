using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Восходящий поток» (воздух, ранг 4/5). A vertical air column at the point of impact: radius 1.5 m, height 6 m
    /// (upgraded 8 m), 12 s. Lifts the player, creatures and objects with μ ≤ 2 at up to 4 m/s; one can step out
    /// sideways. A card that hits a wall, a ceiling, a creature or a movable body (e.g. the top of a Stone cube)
    /// stands the column on the ground below the contact, so it lifts what was hit.
    /// See <see cref="UpdraftColumn"/>. Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class UpdraftEffect : CardEffect, ICardStatLine, ICardArea
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Column height, m (board: 6 → 8).")]
        public LevelFloat height = new LevelFloat(6, 10, 15);
        [Tooltip("Column radius, m.")]
        [Min(0)] public float radius = 1.5f;
        [Tooltip("Duration, s.")]
        [Min(0)] public float durationSeconds = 12;
        [Tooltip("Upward speed limit, m/s.")]
        [Min(0)] public float riseSpeed = 4;
        [Tooltip("Heaviest body it lifts (μ).")]
        [Min(0)] public float maxMass = 2;

        [Header("Placement")]
        [Tooltip("A surface flatter than this is the column's floor; otherwise the column stands on the ground below.")]
        [Range(0, 1)] public float floorNormal = .6f;
        [Tooltip("How far below a wall or creature hit the ground is looked for, m.")]
        [Min(0)] public float groundSearch = 8;

        [Header("Prefab")]
        public UpdraftColumn columnPrefab;

        public override void Apply(in ImpactContext c)
        {
            Vector3 position = c.Point;
            bool onBody = (c.Body && !c.Body.isKinematic) || c.Creature != null || c.HitCombatant != null;
            if ((c.Normal.y < floorNormal || onBody) && FindGround(c.Point + c.Normal * .15f, groundSearch, out var ground)) position = ground;
            var column = c.World.SpawnForOuting(columnPrefab, position, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            if (!column) { Feedback(c, false); return; }
            column.Configure(c.At(height), radius, durationSeconds, riseSpeed, maxMass);
            Feedback(c, true);
        }

        /// <summary>The nearest static surface below the point (loose bodies and creatures are looked through).</summary>
        static bool FindGround(Vector3 from, float distance, out Vector3 point)
        {
            point = from;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, distance, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody && !hit.rigidbody.isKinematic) continue;
                if (hit.distance < best) { best = hit.distance; point = hit.point; }
            }
            return best < float.MaxValue;
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Высота";
            values = height;
            unit = " м";
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
