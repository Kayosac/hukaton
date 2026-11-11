using System.Collections;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Ice on water, in world space. The Ice card freezes the water within a radius of the hit — whatever lakes,
    /// rivers and chunks that circle spans — as a crooked slab that spreads out from the hit; Fire melts it back
    /// inside its radius. Ice holds for the rest of the outing (and comes back with its chunk), and the player walks
    /// on it; when it melts under them they are in the water again. Cells live in <see cref="IceCells"/>; the slab is
    /// drawn and collided by <see cref="IceFieldBlock"/>s spawned from <see cref="blockPrefab"/>.
    /// </summary>
    public sealed class IceField : MonoBehaviour
    {
        public ExpeditionWorld world;
        [Tooltip("One block of the slab: MeshFilter + MeshRenderer (the ice material) + MeshCollider.")]
        public IceFieldBlock blockPrefab;

        [Header("Shape")]
        [Tooltip("Side of one ice cell, m. Smaller follows the shore closer and costs more.")]
        [Min(.1f)] public float cellSize = .25f;
        [Tooltip("Cells per side of a block (one mesh and collider).")]
        [Range(4, 32)] public int blockCells = 16;
        [Tooltip("How far the slab stands above the water, m (each cell wobbles ±25 %).")]
        [Min(.02f)] public float thickness = .14f;
        [Tooltip("How far the side of the slab reaches below the water, m.")]
        [Min(0)] public float skirtDepth = .12f;
        [Tooltip("The rim of the circle wobbles in and out by this share of the radius.")]
        [Range(0, .5f)] public float rimWobble = .18f;
        [Tooltip("Rim corners wander this far, m: a crooked edge, not a staircase.")]
        [Range(0, .2f)] public float rimJitter = .08f;
        [Tooltip("Ice also forms this far onto the shore around the water mesh, m, so it meets the bank.")]
        [Range(0, 1)] public float shoreReach = .3f;

        [Header("Timing")]
        [Tooltip("How fast the ice runs out from the hit, m/s.")]
        [Min(.1f)] public float spreadSpeed = 6;
        [Tooltip("How long one cell takes to grow to full thickness once the ice reaches it.")]
        [Min(0)] public float growSeconds = .25f;
        [Tooltip("How fast the thaw runs out from the fire, m/s.")]
        [Min(.1f)] public float meltSpeed = 4;
        [Tooltip("How long one cell takes to thin away.")]
        [Min(0)] public float meltSeconds = .6f;
        [Tooltip("While growing or thawing, the collider is rebuilt this often, s.")]
        [Min(.02f)] public float colliderInterval = .1f;

        [Header("Feedback")]
        public VfxBurst freezeBurst;
        [Tooltip("Small frost puffs where the spreading ice reaches its rim.")]
        public VfxBurst rimFrost;
        public VfxBurst meltSteam;
        public SfxCue spreadSound, settleSound, meltSound;
        public Color tint = new Color(.55f, .85f, 1f);

        IceCells cells;
        readonly Dictionary<Vector2Int, IceFieldBlock> blocks = new Dictionary<Vector2Int, IceFieldBlock>();
        readonly List<IceFieldBlock> animating = new List<IceFieldBlock>();
        readonly List<Vector2Int> changed = new List<Vector2Int>();
        readonly List<WorldObstacle> lakes = new List<WorldObstacle>();
        float nextVisibility;

        public IceCells Cells => cells ??= new IceCells(cellSize);

        /// <summary>Freezes the water within <paramref name="radius"/> of <paramref name="point"/>. True when any water froze.</summary>
        public bool Freeze(Vector3 point, float radius)
        {
            if (!world || radius <= 0) return false;
            float reach = radius * (1 + rimWobble);
            CollectLakes(point, reach + shoreReach);
            if (lakes.Count == 0) return false;
            // A lake may insist on more (the cave's flooded stretch must be crossable with one card).
            foreach (var lake in lakes) radius = Mathf.Max(radius, lake.minimumIceRadius);
            if (lakes.Exists(l => l.minimumIceRadius > 0)) CollectLakes(point, radius * (1 + rimWobble) + shoreReach);

            float now = Time.time;
            float seed = IceCells.Hash(Mathf.RoundToInt(point.x * 7), Mathf.RoundToInt(point.z * 7)) * 100;
            changed.Clear();
            int added = Cells.Freeze(point, radius, now, spreadSpeed, seed, rimWobble, WaterAt, changed);
            if (added == 0) return false;
            float until = now + radius * (1 + rimWobble) / spreadSpeed + growSeconds + .05f;
            foreach (var key in changed) Animate(BlockOf(key), until);
            StartCoroutine(FreezeFeedback(point, radius, seed));
            return true;
        }

        /// <summary>Melts the ice within <paramref name="radius"/> of <paramref name="point"/>. True when any ice melted.</summary>
        public bool Melt(Vector3 point, float radius)
        {
            if (cells == null || cells.Count == 0 || radius <= 0) return false;
            float now = Time.time;
            changed.Clear();
            if (Cells.Melt(point, radius, now, meltSpeed, changed) == 0) return false;
            float until = now + radius / meltSpeed + meltSeconds + .05f;
            foreach (var key in changed) Animate(BlockOf(key), until);
            MeltFeedback(point);
            return true;
        }

        /// <summary>Removes all ice (outing start / end).</summary>
        public void Clear()
        {
            StopAllCoroutines();
            cells?.Clear();
            animating.Clear();
            foreach (var block in blocks.Values) if (block) Destroy(block.gameObject);
            blocks.Clear();
        }

        /// <summary>Is there ice that holds (not thawing) anywhere over this box seen from above?</summary>
        public bool Covers(Bounds box) => cells != null && cells.Count > 0 && cells.AnyHolding(box);

        /// <summary>Can one stand on the ice at this point?</summary>
        public bool IsSolidAt(Vector3 point) => cells != null && cells.IsSolid(cells.Key(point), Time.time, growSeconds, meltSeconds);

        /// <summary>How much of a cell's slab is there now, 0..1 (0: no ice). The collider counts only solid cells.</summary>
        public float AmountAt(Vector2Int key, float now, bool solidOnly)
        {
            if (!Cells.TryGet(key, out var cell)) return 0;
            float amount = IceCells.Amount(cell, now, growSeconds, meltSeconds);
            if (solidOnly) return amount >= .5f ? 1 : 0;
            return amount;
        }

        // ---------- water ----------

        void CollectLakes(Vector3 point, float reach)
        {
            lakes.Clear();
            var source = world.InPrologue ? (IEnumerable<WorldObstacle>)world.prologueObstacles : world.obstacles;
            foreach (var obstacle in source)
                if (obstacle && obstacle.kind == ObstacleKind.Lake && obstacle.isActiveAndEnabled && obstacle.HasSurface
                    && SqrDistanceXZ(obstacle.SurfaceBounds, point) <= reach * reach)
                    lakes.Add(obstacle);
        }

        /// <summary>Water under this point, or within <see cref="shoreReach"/> of it (the slab runs up to the bank).</summary>
        bool WaterAt(Vector3 point, out float level)
        {
            if (Surface(point, out level)) return true;
            if (shoreReach <= 0) return false;
            float r = shoreReach;
            return Surface(point + new Vector3(r, 0, 0), out level) || Surface(point - new Vector3(r, 0, 0), out level)
                || Surface(point + new Vector3(0, 0, r), out level) || Surface(point - new Vector3(0, 0, r), out level);
        }

        bool Surface(Vector3 point, out float level)
        {
            foreach (var lake in lakes)
                if (lake.SurfaceAt(point, out level)) return true;
            level = 0;
            return false;
        }

        static float SqrDistanceXZ(Bounds box, Vector3 point)
        {
            float dx = Mathf.Max(box.min.x - point.x, 0, point.x - box.max.x);
            float dz = Mathf.Max(box.min.z - point.z, 0, point.z - box.max.z);
            return dx * dx + dz * dz;
        }

        // ---------- blocks ----------

        Vector2Int BlockOf(Vector2Int cell) => new Vector2Int(Mathf.FloorToInt(cell.x / (float)blockCells), Mathf.FloorToInt(cell.y / (float)blockCells));

        IceFieldBlock Animate(Vector2Int coord, float until)
        {
            if (!blocks.TryGetValue(coord, out var block) || !block)
            {
                if (!blockPrefab) return null;
                block = Instantiate(blockPrefab, transform);
                block.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                block.name = $"Ice block {coord.x},{coord.y}";
                block.Coord = coord;
                blocks[coord] = block;
            }
            if (until > block.AnimateUntil) block.AnimateUntil = until;
            if (!animating.Contains(block)) animating.Add(block);
            return block;
        }

        void Update()
        {
            if (animating.Count == 0)
            {
                if (blocks.Count > 0 && Time.time >= nextVisibility) UpdateVisibility();
                return;
            }
            float now = Time.time;
            bool settled = false;
            for (int i = animating.Count - 1; i >= 0; i--)
            {
                var block = animating[i];
                if (!block) { animating.RemoveAt(i); continue; }
                bool done = now >= block.AnimateUntil;
                bool collider = done || now >= block.NextCollider;
                if (collider) block.NextCollider = now + colliderInterval;
                block.Build(this, collider);
                if (done) { animating.RemoveAt(i); settled = true; }
            }
            if (settled) DropMelted();
        }

        void DropMelted()
        {
            changed.Clear();
            if (Cells.RemoveMelted(Time.time, meltSeconds, changed) == 0) return;
            var touched = new HashSet<Vector2Int>();
            foreach (var key in changed) touched.Add(BlockOf(key));
            foreach (var coord in touched)
            {
                if (!blocks.TryGetValue(coord, out var block) || !block || animating.Contains(block)) continue;
                block.Build(this, true);
                if (!block.HasIce) { blocks.Remove(coord); Destroy(block.gameObject); }
            }
        }

        /// <summary>A block whose water is not loaded (its chunk streamed out) is hidden until the chunk is back.</summary>
        void UpdateVisibility()
        {
            nextVisibility = Time.time + .5f;
            var source = world ? (world.InPrologue ? (IEnumerable<WorldObstacle>)world.prologueObstacles : world.obstacles) : null;
            float side = blockCells * cellSize;
            foreach (var pair in blocks)
            {
                if (!pair.Value) continue;
                var min = new Vector3(pair.Key.x * side, 0, pair.Key.y * side);
                bool visible = false;
                if (source != null)
                    foreach (var obstacle in source)
                    {
                        if (!obstacle || obstacle.kind != ObstacleKind.Lake || !obstacle.isActiveAndEnabled || !obstacle.HasSurface) continue;
                        var box = obstacle.SurfaceBounds;
                        if (box.max.x + 1 >= min.x && box.min.x - 1 <= min.x + side && box.max.z + 1 >= min.z && box.min.z - 1 <= min.z + side) { visible = true; break; }
                    }
                pair.Value.Show(visible);
            }
        }

        // ---------- feedback ----------

        IEnumerator FreezeFeedback(Vector3 point, float radius, float seed)
        {
            Vector3 at = AtSurface(point);
            world.Play(spreadSound, at);
            world.Spawn(freezeBurst, at, Quaternion.identity, tint, Mathf.Clamp(radius / 2.5f, .8f, 1.8f));
            // Frost puffs run out to the rim as the ice gets there.
            const int spokes = 6;
            var pending = new List<(float when, Vector3 where)>();
            for (int i = 0; i < spokes; i++)
            {
                float angle = (i + IceCells.Hash(i, (int)seed)) / spokes * Mathf.PI * 2;
                float r = IceCells.RimRadius(radius, angle, seed, rimWobble) * .85f;
                var p = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * r;
                if (!Cells.Contains(Cells.Key(p))) continue;
                pending.Add((Time.time + r / spreadSpeed, AtSurface(p)));
            }
            pending.Sort((a, b) => a.when.CompareTo(b.when));
            foreach (var (when, where) in pending)
            {
                while (Time.time < when) yield return null;
                world.Spawn(rimFrost, where, Quaternion.identity, tint, .6f);
            }
            while (Time.time < (pending.Count > 0 ? pending[pending.Count - 1].when : Time.time) + growSeconds) yield return null;
            world.Play(settleSound, at, .7f);
        }

        void MeltFeedback(Vector3 point)
        {
            Vector3 at = AtSurface(point);
            world.Play(meltSound, at, .8f, 1.25f);
            world.Spawn(meltSteam, at, Quaternion.identity, Color.white, 1.2f);
            // A few more puffs over the thawing ice around the hit.
            for (int i = 0; i < 3; i++)
            {
                float angle = i / 3f * Mathf.PI * 2 + IceCells.Hash(Mathf.RoundToInt(point.x), i);
                var p = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.1f;
                if (Cells.TryGet(Cells.Key(p), out var cell) && cell.Melting)
                    world.Spawn(meltSteam, new Vector3(p.x, cell.level + thickness, p.z), Quaternion.identity, Color.white, .8f);
            }
        }

        Vector3 AtSurface(Vector3 point)
            => Cells.TryGet(Cells.Key(point), out var cell) ? new Vector3(point.x, cell.level + thickness, point.z) : point;
    }
}
