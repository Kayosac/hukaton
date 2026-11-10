using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Ветвь-мост» (земля, ранг 3/5). From the immovable solid surface that was hit, a branch grows horizontally back toward the throw: length 5 m
    /// (upgraded 7 m), width 1.2 m, until the end of the outing; the player and creatures walk on it. It never passes
    /// through geometry: an obstacle on the way shortens it (<see cref="BranchBridgeMath"/>); too little room — a miss.
    /// Creatures, movable bodies and triggers are not a footing. Fire (and Lightning) burn it (<see cref="BranchBridge"/>).
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class BranchBridgeEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Branch length, m (board: 5 → 7).")]
        public LevelFloat length = new LevelFloat(5, 8, 12);
        [Tooltip("Branch width, m (same on both levels).")]
        [Min(0)] public float width = 1.2f;

        [Header("Prefab and placement")]
        public BranchBridge branchPrefab;
        [Tooltip("Thickness of the branch, m.")]
        [Min(.05f)] public float thickness = .42f;
        [Tooltip("A branch shorter than this (no room) is not grown: a miss.")]
        [Min(.1f)] public float minimumLength = 1;
        [Tooltip("From a wall the branch grows at most this many degrees away from the wall's outward direction.")]
        [Range(0, 89)] public float maxAngleFromWall = 70;
        [Tooltip("How deep the root sits in a wall, m (not counted in the board length).")]
        [Min(0)] public float rootDepth = .18f;
        [Tooltip("The branch stops this far before an obstacle.")]
        [Min(0)] public float obstacleMargin = .08f;
        public string noRoomNotice = "Ветвь-мост: нет места";
        public string noFootingNotice = "Ветвь-мост: нужна неподвижная твёрдая поверхность";

        static readonly RaycastHit[] Hits = new RaycastHit[32];

        public override void Apply(in ImpactContext c)
        {
            var target = c.Target;
            bool movable = c.Body && !c.Body.isKinematic;
            if (!branchPrefab || !target || target.isTrigger || movable || c.Creature || c.HitCombatant != null)
            {
                if (!branchPrefab) Debug.LogWarning("BranchBridgeEffect: no branch prefab assigned.", this);
                Miss(c, noFootingNotice);
                return;
            }
            Vector3 direction = BranchBridgeMath.GrowthDirection(c.Point, c.Normal, c.ThrowOrigin, c.Direction, maxAngleFromWall);
            float top = BranchBridgeMath.TopOffset(c.Normal, thickness);
            Vector3 wall = new Vector3(c.Normal.x, 0, c.Normal.z);
            bool fromWall = Mathf.Abs(c.Normal.normalized.y) <= .5f && wall.sqrMagnitude > 1e-4f;
            wall = fromWall ? wall.normalized : Vector3.zero;
            Vector3 topPoint = c.Point + Vector3.up * top;
            float desired = c.At(this.length);
            float length = BranchBridgeMath.ClampLength(desired, ObstacleDistance(topPoint + wall * .06f, direction, desired), obstacleMargin, minimumLength);
            if (length <= 0) { Miss(c, noRoomNotice); return; }
            float embed = fromWall ? rootDepth : 0;
            Vector3 root = topPoint - wall * embed;
            var branch = c.World.SpawnForOuting(branchPrefab, root, Quaternion.LookRotation(direction, Vector3.up));
            if (!branch) return;
            branch.GrowsFrom(target);
            branch.Grow(length + embed, width, thickness);
            Feedback(c, true);
        }

        /// <summary>
        /// Distance along the growth to the first solid obstacle, swept with a slab in the upper part of the branch.
        /// Bodies that move (crates, creatures, loot) are pushed aside rather than stopping it; the player is ignored.
        /// </summary>
        float? ObstacleDistance(Vector3 topPoint, Vector3 direction, float distance)
        {
            var halfExtents = new Vector3(width * .45f, .05f, .05f);
            Vector3 centre = topPoint + Vector3.down * .09f;
            int count = Physics.BoxCastNonAlloc(centre, halfExtents, direction, Hits, Quaternion.LookRotation(direction),
                distance, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore);
            float? nearest = null;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                // Overlaps at the start (the surface it grows from) are reported with distance 0 and no point.
                if (hit.distance <= 0 && hit.point == Vector3.zero) continue;
                if (hit.rigidbody && !hit.rigidbody.isKinematic) continue;
                if (!nearest.HasValue || hit.distance < nearest.Value) nearest = hit.distance;
            }
            return nearest;
        }

        void Miss(in ImpactContext c, string notice)
        {
            Feedback(c, false, .6f);
            if (!string.IsNullOrEmpty(notice)) c.World.Announce(notice, 1.4f);
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
