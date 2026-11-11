using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A place in a ready-made location where a chest may turn up. Each outing it rolls its table: maybe nothing,
    /// maybe a chest of a weighted tier, at one of its alternative spots, set down on the ground.
    /// </summary>
    public sealed class ChestSite : MonoBehaviour
    {
        public ChestSpawnTable table;
        [Tooltip("Alternative spots; one is picked each outing. The chest's front (the lock) faces the spot's -Z. Empty: this transform.")]
        public Transform[] spawnPoints = new Transform[0];

        public TreasureChest Current { get; private set; }

        /// <summary>Clears the last chest and rolls a new one; returns it or null.</summary>
        public TreasureChest ResetForOuting(ExpeditionWorld world, System.Random random)
        {
            Clear();
            if (!table) return null;
            var prefab = table.Roll(random.NextDouble(), random.NextDouble());
            if (!prefab) return null;
            Transform point = spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints[random.Next(spawnPoints.Length)] : null;
            if (!point) point = transform;
            Vector3 position = Ground(point.position);
            Current = Instantiate(prefab, position, point.rotation, transform);
            Current.world = world;
            return Current;
        }

        public void Clear()
        {
            if (Current) Destroy(Current.gameObject);
            Current = null;
        }

        /// <summary>Straight down onto whatever solid is under the spot (water and triggers ignored).</summary>
        static Vector3 Ground(Vector3 point)
        {
            const int mask = ~((1 << 2) | (1 << 4));
            return Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out var hit, 6, mask, QueryTriggerInteraction.Ignore)
                ? hit.point : point;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, .75f, .2f, .85f);
            if (spawnPoints == null || spawnPoints.Length == 0) Box(transform);
            else foreach (var p in spawnPoints) if (p) Box(p);
        }

        static void Box(Transform t)
        {
            Gizmos.matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.up * .25f, new Vector3(.9f, .5f, .55f));
            Gizmos.DrawLine(Vector3.up * .25f, Vector3.up * .25f - Vector3.forward * .6f);
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
