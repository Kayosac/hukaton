using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The pure bookkeeping of <see cref="IceField"/>: a sparse world-space grid of ice cells over water. A freeze adds
    /// cells inside a crooked circle (a wave out from the hit: each cell is born a little later the further out it is);
    /// a melt schedules cells inside a circle to thaw, the same wave the other way. No Unity objects, so it is tested
    /// in EditMode.
    /// </summary>
    public sealed class IceCells
    {
        /// <summary>One frozen square of water.</summary>
        public struct Cell
        {
            /// <summary>Water surface height under the cell.</summary>
            public float level;
            /// <summary>When the ice reaches this cell (the spreading wave).</summary>
            public float bornAt;
            /// <summary>When it starts to thaw; +∞ while it holds.</summary>
            public float meltAt;
            /// <summary>0..1 hash: thickness wobble and such, stable for the cell.</summary>
            public float grain;

            public bool Melting => !float.IsPositiveInfinity(meltAt);
        }

        /// <summary>Is there water here, and at what height? (over the loaded lakes)</summary>
        public delegate bool WaterQuery(Vector3 point, out float level);

        public readonly float cellSize;
        readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
        readonly List<Vector2Int> scratch = new List<Vector2Int>();

        public IceCells(float cellSize) { this.cellSize = Mathf.Max(.05f, cellSize); }

        public int Count => cells.Count;
        public IEnumerable<KeyValuePair<Vector2Int, Cell>> All => cells;

        public Vector2Int Key(Vector3 point) => new Vector2Int(Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.z / cellSize));
        public Vector3 Centre(Vector2Int key) => new Vector3((key.x + .5f) * cellSize, 0, (key.y + .5f) * cellSize);
        public bool TryGet(Vector2Int key, out Cell cell) => cells.TryGetValue(key, out cell);
        public bool Contains(Vector2Int key) => cells.ContainsKey(key);
        public void Clear() => cells.Clear();

        /// <summary>Radius of the crooked rim in this direction: the circle wobbles by ±<paramref name="wobble"/> of itself.</summary>
        public static float RimRadius(float radius, float angle, float seed, float wobble)
        {
            float n = Mathf.PerlinNoise(Mathf.Cos(angle) * 1.4f + seed, Mathf.Sin(angle) * 1.4f + seed * .37f) * .7f
                      + Mathf.PerlinNoise(Mathf.Cos(angle) * 3.3f + seed * 1.7f, Mathf.Sin(angle) * 3.3f - seed) * .3f;
            return radius * (1 + wobble * (n * 2 - 1));
        }

        /// <summary>
        /// Freezes the water inside the crooked circle. A thawing cell inside it holds again; a frozen one is left as it
        /// is. Returns how many cells froze; their keys go to <paramref name="changed"/> when given.
        /// </summary>
        public int Freeze(Vector3 centre, float radius, float now, float spreadSpeed, float seed, float wobble, WaterQuery water,
            List<Vector2Int> changed = null)
        {
            if (radius <= 0 || water == null) return 0;
            float reach = radius * (1 + Mathf.Abs(wobble));
            var min = Key(centre - new Vector3(reach, 0, reach));
            var max = Key(centre + new Vector3(reach, 0, reach));
            float speed = Mathf.Max(.01f, spreadSpeed);
            int added = 0;
            for (int x = min.x; x <= max.x; x++)
                for (int z = min.y; z <= max.y; z++)
                {
                    var key = new Vector2Int(x, z);
                    var at = Centre(key);
                    float dx = at.x - centre.x, dz = at.z - centre.z;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distance > RimRadius(radius, Mathf.Atan2(dz, dx), seed, wobble)) continue;
                    if (cells.TryGetValue(key, out var old))
                    {
                        if (!old.Melting) continue;
                        old.meltAt = float.PositiveInfinity;
                        old.bornAt = now + distance / speed;
                        cells[key] = old;
                        added++;
                        changed?.Add(key);
                        continue;
                    }
                    at.y = centre.y;
                    if (!water(at, out float level)) continue;
                    cells[key] = new Cell { level = level, bornAt = now + distance / speed, meltAt = float.PositiveInfinity, grain = Hash(x, z) };
                    added++;
                    changed?.Add(key);
                }
            return added;
        }

        /// <summary>Starts thawing the ice inside the circle (a wave out from the centre). Returns how many cells began to melt.</summary>
        public int Melt(Vector3 centre, float radius, float now, float meltSpeed, List<Vector2Int> changed = null)
        {
            if (radius <= 0 || cells.Count == 0) return 0;
            float speed = Mathf.Max(.01f, meltSpeed);
            scratch.Clear();
            var min = Key(centre - new Vector3(radius, 0, radius));
            var max = Key(centre + new Vector3(radius, 0, radius));
            // Walk whichever is smaller: the square around the circle or the cells there are.
            if ((long)(max.x - min.x + 1) * (max.y - min.y + 1) < cells.Count)
            {
                for (int x = min.x; x <= max.x; x++)
                    for (int z = min.y; z <= max.y; z++)
                        if (cells.ContainsKey(new Vector2Int(x, z))) scratch.Add(new Vector2Int(x, z));
            }
            else scratch.AddRange(cells.Keys);
            int started = 0;
            foreach (var key in scratch)
            {
                var cell = cells[key];
                var at = Centre(key);
                float dx = at.x - centre.x, dz = at.z - centre.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance > radius) continue;
                float when = now + distance / speed;
                if (cell.Melting && cell.meltAt <= when) continue;
                cell.meltAt = when;
                cells[key] = cell;
                started++;
                changed?.Add(key);
            }
            return started;
        }

        /// <summary>
        /// How much of the slab is there, 0..1: grows in over <paramref name="growSeconds"/> once the wave arrives,
        /// thins away over <paramref name="meltSeconds"/> once it thaws.
        /// </summary>
        public static float Amount(in Cell cell, float now, float growSeconds, float meltSeconds)
        {
            float grow = growSeconds <= 0 ? (now >= cell.bornAt ? 1 : 0) : Mathf.Clamp01((now - cell.bornAt) / growSeconds);
            if (!cell.Melting) return grow;
            float melt = meltSeconds <= 0 ? (now >= cell.meltAt ? 1 : 0) : Mathf.Clamp01((now - cell.meltAt) / meltSeconds);
            return Mathf.Min(grow, 1 - melt);
        }

        /// <summary>Solid enough to stand on: at least half grown and not half gone.</summary>
        public bool IsSolid(Vector2Int key, float now, float growSeconds, float meltSeconds)
            => cells.TryGetValue(key, out var cell) && Amount(cell, now, growSeconds, meltSeconds) >= .5f;

        /// <summary>Drops the cells that have melted away completely. Returns how many went.</summary>
        public int RemoveMelted(float now, float meltSeconds, List<Vector2Int> removed = null)
        {
            scratch.Clear();
            foreach (var pair in cells)
                if (pair.Value.Melting && now >= pair.Value.meltAt + meltSeconds) scratch.Add(pair.Key);
            foreach (var key in scratch) { cells.Remove(key); removed?.Add(key); }
            return scratch.Count;
        }

        /// <summary>Is there ice (not thawing) over any part of the box seen from above?</summary>
        public bool AnyHolding(Bounds box)
        {
            var min = Key(box.min);
            var max = Key(box.max);
            if ((long)(max.x - min.x + 1) * (max.y - min.y + 1) < cells.Count)
            {
                for (int x = min.x; x <= max.x; x++)
                    for (int z = min.y; z <= max.y; z++)
                        if (cells.TryGetValue(new Vector2Int(x, z), out var cell) && !cell.Melting) return true;
                return false;
            }
            foreach (var pair in cells)
                if (!pair.Value.Melting && pair.Key.x >= min.x && pair.Key.x <= max.x && pair.Key.y >= min.y && pair.Key.y <= max.y) return true;
            return false;
        }

        /// <summary>A stable 0..1 hash of two integers.</summary>
        public static float Hash(int x, int z)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)z * 0xd8163841u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
    }
}
