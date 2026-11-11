using System.Collections;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A sheet of ice on water, made by the Ice card. <see cref="FreezeAt"/> spawns one at the hit (through
    /// <see cref="ExpeditionWorld.SpawnForOuting{T}"/>, like any card-made thing) and freezes the water in a crooked
    /// circle around it — whatever lakes, rivers and chunks the circle spans. A freeze near a sheet that is already
    /// there grows that sheet (sheets never overlap: touching ones merge). Fire melts it back in a radius
    /// (<see cref="MeltAt"/>); a sheet with no ice left is gone. It is a card zone (<see cref="CardZoneKind.Ice"/>), so
    /// the water and the cards find it through <see cref="ExpeditionWorld.Zones"/>; it goes with the outing.
    /// The mesh is built in world space, so the instance sits at the origin. Cells: <see cref="IceCells"/>.
    /// Vertex data for the ice shader: colour.r = how grown (0..1), colour.g = 1 on the rim, colour.b = 1 on the side;
    /// uv0 = world XZ in metres; uv1 = (grown, stable per-cell grain).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class IcePatch : CardZoneBehaviour
    {
        public MeshFilter filter;
        public MeshRenderer meshRenderer;
        [Tooltip("What the player stands on and cards hit: only the ice that holds (not growing in, not thawing).")]
        public MeshCollider meshCollider;

        [Header("Shape")]
        [Tooltip("Side of one ice cell, m. Smaller follows the shore closer and costs more.")]
        [Min(.1f)] public float cellSize = .25f;
        [Tooltip("How far the sheet stands above the water, m (each cell wobbles ±25 %).")]
        [Min(.02f)] public float thickness = .14f;
        [Tooltip("How far the side of the sheet reaches below the water, m.")]
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

        public override CardZoneKind Kind => CardZoneKind.Ice;
        public IceCells Cells => cells ??= new IceCells(cellSize);

        IceCells cells;
        Mesh visual, solid;
        float animateUntil, nextCollider, nextVisibility;
        Bounds bounds;
        bool boundsDirty = true;
        List<WorldObstacle> freezingOver;
        IceCells.WaterQuery waterQuery;

        static readonly List<ICardZone> Found = new List<ICardZone>();
        static readonly List<WorldObstacle> Lakes = new List<WorldObstacle>();

        // ---------------- cards ----------------

        /// <summary>
        /// Freezes the water within <paramref name="radius"/> of <paramref name="point"/>: grows the sheet already
        /// there (merging any it now touches) or spawns a new one from <paramref name="prefab"/>. A lake may insist on
        /// a bigger radius (<see cref="WorldObstacle.minimumIceRadius"/>). True when any water froze.
        /// </summary>
        public static bool FreezeAt(ExpeditionWorld world, IcePatch prefab, Vector3 point, float radius)
        {
            if (!world || !prefab || radius <= 0) return false;
            float reach = radius * (1 + prefab.rimWobble) + prefab.shoreReach;
            CollectLakes(world, point, reach, Lakes);
            if (Lakes.Count == 0) return false;
            float wanted = radius;
            foreach (var lake in Lakes) wanted = Mathf.Max(wanted, lake.minimumIceRadius);
            if (wanted > radius)
            {
                radius = wanted;
                reach = radius * (1 + prefab.rimWobble) + prefab.shoreReach;
                CollectLakes(world, point, reach, Lakes);
            }

            // The sheets this circle reaches (one cell further: touching sheets merge, so a seam never shows).
            float side = (reach + prefab.cellSize) * 2;
            Found.Clear();
            world.Zones.Overlapping(new Bounds(point, new Vector3(side, 40, side)), Found, CardZoneKind.Ice);
            IcePatch host = null;
            foreach (var zone in Found)
            {
                if (!(zone is IcePatch patch) || !patch || !patch.enabled) continue;
                if (!host) host = patch;
                else host.Absorb(patch);
            }
            bool spawned = !host;
            if (spawned)
            {
                host = world.SpawnForOuting(prefab, Vector3.zero, Quaternion.identity);
                if (!host) return false;
                // The mesh is in world space: the sheet sits at the world origin whatever it is parented to.
                host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            bool froze = host.Freeze(point, radius, Lakes);
            if (!froze && spawned) Destroy(host.gameObject);
            return froze;
        }

        /// <summary>Melts the ice of every sheet within <paramref name="radius"/> of the point. True when any ice began to thaw.</summary>
        public static bool MeltAt(ExpeditionWorld world, Vector3 point, float radius)
        {
            if (!world || radius <= 0) return false;
            Found.Clear();
            world.Zones.Near(point, radius, Found, CardZoneKind.Ice);
            bool any = false;
            foreach (var zone in Found)
                if (zone is IcePatch patch && patch && patch.Melt(point, radius)) any = true;
            return any;
        }

        /// <summary>Is there ice that holds right under this point (the player is standing on it, not in the water)?</summary>
        public static bool IsSolidAt(ExpeditionWorld world, Vector3 point) => world && world.Zones.FirstContaining<IcePatch>(point) != null;

        /// <summary>Is any of this box (seen from above) under ice that holds? (a lake «resolved» for the story)</summary>
        public static bool Covers(ExpeditionWorld world, Bounds box)
        {
            if (!world) return false;
            Found.Clear();
            world.Zones.Overlapping(new Bounds(box.center, new Vector3(box.size.x, Mathf.Max(box.size.y, 40), box.size.z)), Found, CardZoneKind.Ice);
            foreach (var zone in Found)
                if (zone is IcePatch patch && patch && patch.cells != null && patch.cells.AnyHolding(box)) return true;
            return false;
        }

        // ---------------- zone ----------------

        public override Bounds Bounds
        {
            get
            {
                if (boundsDirty) MeasureBounds();
                return bounds;
            }
        }

        /// <summary>Exactly over a cell of ice that holds, near its height.</summary>
        public override bool Contains(Vector3 point)
        {
            if (cells == null) return false;
            var key = cells.Key(point);
            return cells.TryGet(key, out var cell) && point.y >= cell.level - 1 && point.y <= cell.level + thickness + 1.5f
                   && cells.IsSolid(key, Time.time, growSeconds, meltSeconds);
        }

        void MeasureBounds()
        {
            boundsDirty = false;
            if (cells == null || cells.Count == 0) { bounds = new Bounds(transform.position, Vector3.zero); return; }
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            float low = float.MaxValue, high = float.MinValue;
            foreach (var pair in cells.All)
            {
                minX = Mathf.Min(minX, pair.Key.x); maxX = Mathf.Max(maxX, pair.Key.x);
                minZ = Mathf.Min(minZ, pair.Key.y); maxZ = Mathf.Max(maxZ, pair.Key.y);
                low = Mathf.Min(low, pair.Value.level); high = Mathf.Max(high, pair.Value.level);
            }
            var min = new Vector3(minX * cellSize - rimJitter, low - skirtDepth - .5f, minZ * cellSize - rimJitter);
            var max = new Vector3((maxX + 1) * cellSize + rimJitter, high + thickness * 1.5f + .5f, (maxZ + 1) * cellSize + rimJitter);
            bounds = new Bounds((min + max) * .5f, max - min);
        }

        // ---------------- freezing and thawing ----------------

        bool Freeze(Vector3 point, float radius, List<WorldObstacle> lakes)
        {
            float now = Time.time;
            float seed = IceCells.Hash(Mathf.RoundToInt(point.x * 7), Mathf.RoundToInt(point.z * 7)) * 100;
            freezingOver = lakes;
            waterQuery ??= WaterAt;
            int added = Cells.Freeze(point, radius, now, spreadSpeed, seed, rimWobble, waterQuery);
            freezingOver = null;
            if (added == 0) return false;
            boundsDirty = true;
            Animate(now + radius * (1 + rimWobble) / spreadSpeed + growSeconds + .05f);
            StartCoroutine(FreezeFeedback(point, radius, seed));
            return true;
        }

        bool Melt(Vector3 point, float radius)
        {
            if (cells == null || cells.Count == 0) return false;
            float now = Time.time;
            if (cells.Melt(point, radius, now, meltSpeed) == 0) return false;
            Animate(now + radius / meltSpeed + meltSeconds + .05f);
            MeltFeedback(point);
            return true;
        }

        /// <summary>Takes over another sheet's ice (they touch now) and retires it.</summary>
        void Absorb(IcePatch other)
        {
            if (other == this || other.cells == null) return;
            Cells.Absorb(other.cells);
            other.cells.Clear();
            other.enabled = false;
            Destroy(other.gameObject);
            boundsDirty = true;
            Animate(Mathf.Max(Time.time, other.animateUntil));
        }

        void Animate(float until)
        {
            if (until > animateUntil) animateUntil = until;
            nextCollider = 0;
        }

        void Update()
        {
            float now = Time.time;
            if (now <= animateUntil + Time.deltaTime)
            {
                bool done = now >= animateUntil;
                bool withCollider = done || now >= nextCollider;
                if (withCollider) nextCollider = now + colliderInterval;
                Build(withCollider);
                if (!done) return;
                // Settled: thawed cells go; a sheet with no ice left is gone.
                animateUntil = float.NegativeInfinity;
                if (cells.RemoveMelted(now, meltSeconds) > 0)
                {
                    boundsDirty = true;
                    Build(true);
                }
                if (cells.Count == 0) { Destroy(gameObject); return; }
            }
            if (now >= nextVisibility) UpdateVisibility(now);
        }

        /// <summary>Hidden while none of its water is loaded (its chunk streamed out); back with the chunk.</summary>
        void UpdateVisibility(float now)
        {
            nextVisibility = now + .5f;
            bool visible = false;
            if (World)
            {
                var box = Bounds;
                foreach (var lake in LakeSource(World))
                {
                    if (!IsLake(lake)) continue;
                    var water = lake.SurfaceBounds;
                    if (water.max.x + 1 >= box.min.x && water.min.x - 1 <= box.max.x && water.max.z + 1 >= box.min.z && water.min.z - 1 <= box.max.z)
                    {
                        visible = true;
                        break;
                    }
                }
            }
            if (meshRenderer.enabled != visible) meshRenderer.enabled = visible;
            if (meshCollider && meshCollider.sharedMesh && meshCollider.enabled != visible) meshCollider.enabled = visible;
        }

        void Build(bool withCollider)
        {
            if (!visual) { visual = new Mesh { name = "Ice sheet" }; visual.MarkDynamic(); }
            IceSheetMesh.Fill(visual, this, false);
            filter.sharedMesh = visual;
            if (!withCollider || !meshCollider) return;
            if (!solid) solid = new Mesh { name = "Ice sheet (solid)" };
            bool any = IceSheetMesh.Fill(solid, this, true);
            meshCollider.sharedMesh = null;
            if (any) meshCollider.sharedMesh = solid;
            meshCollider.enabled = any && meshRenderer.enabled;
        }

        /// <summary>How much of a cell's slab is there now, 0..1 (0: no ice). <paramref name="solidOnly"/>: 1 for ice that holds, else 0.</summary>
        internal float AmountAt(Vector2Int key, float now, bool solidOnly)
        {
            if (cells == null || !cells.TryGet(key, out var cell)) return 0;
            float amount = IceCells.Amount(cell, now, growSeconds, meltSeconds);
            return solidOnly ? (amount >= .5f ? 1 : 0) : amount;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (meshCollider) meshCollider.enabled = false;
        }

        void OnDestroy()
        {
            if (visual) Destroy(visual);
            if (solid) Destroy(solid);
        }

        protected override void OnDrawGizmosSelected()
        {
            var box = Bounds;
            Gizmos.color = new Color(.4f, .9f, 1f, .6f);
            Gizmos.DrawWireCube(box.center, box.size);
        }

        // ---------------- water ----------------

        static IEnumerable<WorldObstacle> LakeSource(ExpeditionWorld world)
            => world.InPrologue ? (IEnumerable<WorldObstacle>)world.prologueObstacles : world.obstacles;

        static bool IsLake(WorldObstacle obstacle)
            => obstacle && obstacle.kind == ObstacleKind.Lake && obstacle.isActiveAndEnabled && obstacle.HasSurface;

        static void CollectLakes(ExpeditionWorld world, Vector3 point, float reach, List<WorldObstacle> into)
        {
            into.Clear();
            foreach (var obstacle in LakeSource(world))
                if (IsLake(obstacle) && SqrDistanceXZ(obstacle.SurfaceBounds, point) <= reach * reach) into.Add(obstacle);
        }

        static float SqrDistanceXZ(Bounds box, Vector3 point)
        {
            float dx = Mathf.Max(box.min.x - point.x, 0, point.x - box.max.x);
            float dz = Mathf.Max(box.min.z - point.z, 0, point.z - box.max.z);
            return dx * dx + dz * dz;
        }

        /// <summary>Water under this point, or within <see cref="shoreReach"/> of it (the sheet runs up to the bank).</summary>
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
            if (freezingOver != null)
                foreach (var lake in freezingOver)
                    if (lake.SurfaceAt(point, out level)) return true;
            level = 0;
            return false;
        }

        // ---------------- feedback ----------------

        IEnumerator FreezeFeedback(Vector3 point, float radius, float seed)
        {
            var world = World;
            if (!world) yield break;
            Vector3 at = AtSurface(point);
            world.Play(spreadSound, at);
            world.Spawn(freezeBurst, at, Quaternion.identity, tint, Mathf.Clamp(radius / 2.5f, .8f, 1.8f));
            // Frost puffs run out to the rim as the ice gets there.
            const int spokes = 6;
            var pending = new List<(float when, Vector3 spot)>();
            for (int i = 0; i < spokes; i++)
            {
                float angle = (i + IceCells.Hash(i, (int)seed)) / spokes * Mathf.PI * 2;
                float r = IceCells.RimRadius(radius, angle, seed, rimWobble) * .85f;
                var p = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * r;
                if (!Cells.Contains(Cells.Key(p))) continue;
                pending.Add((Time.time + r / spreadSpeed, AtSurface(p)));
            }
            pending.Sort((a, b) => a.when.CompareTo(b.when));
            float end = (pending.Count > 0 ? pending[pending.Count - 1].when : Time.time) + growSeconds;
            foreach (var (when, spot) in pending)
            {
                while (Time.time < when) yield return null;
                world.Spawn(rimFrost, spot, Quaternion.identity, tint, .6f);
            }
            while (Time.time < end) yield return null;
            world.Play(settleSound, at, .7f);
        }

        void MeltFeedback(Vector3 point)
        {
            var world = World;
            if (!world) return;
            Vector3 at = AtSurface(point);
            world.Play(meltSound, at, .8f, 1.25f);
            world.Spawn(meltSteam, at, Quaternion.identity, Color.white, 1.2f);
            // A few more puffs over the thawing ice around the hit.
            for (int i = 0; i < 3; i++)
            {
                float angle = i / 3f * Mathf.PI * 2 + IceCells.Hash(Mathf.RoundToInt(point.x), i);
                var p = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.1f;
                if (cells.TryGet(cells.Key(p), out var cell) && cell.Melting)
                    world.Spawn(meltSteam, new Vector3(p.x, cell.level + thickness, p.z), Quaternion.identity, Color.white, .8f);
            }
        }

        Vector3 AtSurface(Vector3 point)
            => Cells.TryGet(Cells.Key(point), out var cell) ? new Vector3(point.x, cell.level + thickness, point.z) : point;
    }

    /// <summary>Builds the slab mesh of an <see cref="IcePatch"/> from its cells: top, crooked rim and side skirt.</summary>
    static class IceSheetMesh
    {
        static readonly List<Vector3> Verts = new List<Vector3>();
        static readonly List<Color> Colours = new List<Color>();
        static readonly List<Vector2> Uv0 = new List<Vector2>(), Uv1 = new List<Vector2>();
        static readonly List<int> Tris = new List<int>();
        static readonly Dictionary<Vector2Int, int> Corners = new Dictionary<Vector2Int, int>();
        static readonly Vector2Int[] Around = { new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(0, 0) };

        public static bool Fill(Mesh mesh, IcePatch patch, bool solidOnly)
        {
            Verts.Clear(); Colours.Clear(); Uv0.Clear(); Uv1.Clear(); Tris.Clear(); Corners.Clear();
            float now = Time.time;
            foreach (var pair in patch.Cells.All)
            {
                var key = pair.Key;
                var cell = pair.Value;
                float amount = patch.AmountAt(key, now, solidOnly);
                if (amount <= 0) continue;
                int a = Corner(patch, key, now, solidOnly);
                int b = Corner(patch, key + new Vector2Int(0, 1), now, solidOnly);
                int c = Corner(patch, key + new Vector2Int(1, 1), now, solidOnly);
                int d = Corner(patch, key + new Vector2Int(1, 0), now, solidOnly);
                Tris.Add(a); Tris.Add(b); Tris.Add(c);
                Tris.Add(a); Tris.Add(c); Tris.Add(d);
                // The side of the slab wherever the neighbour has no ice: the rim reads as a block of ice.
                if (patch.AmountAt(key + Vector2Int.left, now, solidOnly) <= 0) Skirt(patch, cell, amount, a, b, Vector3.left);
                if (patch.AmountAt(key + Vector2Int.up, now, solidOnly) <= 0) Skirt(patch, cell, amount, b, c, Vector3.forward);
                if (patch.AmountAt(key + Vector2Int.right, now, solidOnly) <= 0) Skirt(patch, cell, amount, c, d, Vector3.right);
                if (patch.AmountAt(key + Vector2Int.down, now, solidOnly) <= 0) Skirt(patch, cell, amount, d, a, Vector3.back);
            }
            mesh.Clear();
            if (Tris.Count == 0) return false;
            mesh.indexFormat = Verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(Verts);
            mesh.SetColors(Colours);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return true;
        }

        /// <summary>A shared top vertex at a grid corner: the height of the ice around it; nudged about on the rim.</summary>
        static int Corner(IcePatch patch, Vector2Int corner, float now, bool solidOnly)
        {
            if (Corners.TryGetValue(corner, out int index)) return index;
            float size = patch.cellSize, height = 0, weight = 0, grown = 0, grain = 0;
            int present = 0;
            foreach (var offset in Around)
            {
                var key = corner + offset;
                float amount = patch.AmountAt(key, now, solidOnly);
                if (amount <= 0 || !patch.Cells.TryGet(key, out var cell)) continue;
                present++;
                float thick = patch.thickness * (.75f + .5f * cell.grain);
                height += cell.level + thick * amount;
                grown += amount;
                grain += cell.grain;
                weight++;
            }
            height /= Mathf.Max(1, weight);
            grown /= Mathf.Max(1, weight);
            grain /= Mathf.Max(1, weight);
            var at = new Vector3(corner.x * size, height, corner.y * size);
            bool rim = present < 4;
            if (rim)
            {
                // Crooked: rim corners wander and sag a little, so the edge is never a clean staircase.
                at.x += (IceCells.Hash(corner.x, corner.y + 911) - .5f) * 2 * patch.rimJitter;
                at.z += (IceCells.Hash(corner.x + 377, corner.y) - .5f) * 2 * patch.rimJitter;
                at.y -= patch.thickness * .3f * grown * (4 - present) / 4f;
            }
            index = Verts.Count;
            Verts.Add(at);
            Colours.Add(new Color(grown, rim ? 1 : 0, 0, 1));
            Uv0.Add(new Vector2(at.x, at.z));
            Uv1.Add(new Vector2(grown, grain));
            Corners[corner] = index;
            return index;
        }

        static void Skirt(IcePatch patch, IceCells.Cell cell, float amount, int top0, int top1, Vector3 outward)
        {
            Vector3 a = Verts[top0], b = Verts[top1];
            float bottom = cell.level - patch.skirtDepth * amount;
            var c = new Vector3(b.x, Mathf.Min(bottom, b.y - .01f), b.z);
            var d = new Vector3(a.x, Mathf.Min(bottom, a.y - .01f), a.z);
            int i = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
            for (int k = 0; k < 4; k++) { Colours.Add(new Color(amount, 1, 1, 1)); Uv1.Add(new Vector2(amount, cell.grain)); }
            // Along the edge and down it, so the side has its own texture space.
            float along = Vector3.Dot(new Vector3(a.x, 0, a.z), new Vector3(-outward.z, 0, outward.x));
            float along1 = Vector3.Dot(new Vector3(b.x, 0, b.z), new Vector3(-outward.z, 0, outward.x));
            Uv0.Add(new Vector2(along, a.y)); Uv0.Add(new Vector2(along1, b.y)); Uv0.Add(new Vector2(along1, c.y)); Uv0.Add(new Vector2(along, d.y));
            // Wind so the face points outward.
            var normal = Vector3.Cross(b - a, d - a);
            if (Vector3.Dot(normal, outward) >= 0) { Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2); Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3); }
            else { Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 1); Tris.Add(i); Tris.Add(i + 3); Tris.Add(i + 2); }
        }
    }
}
