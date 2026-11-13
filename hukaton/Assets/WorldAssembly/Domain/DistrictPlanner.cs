using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    public enum Feature { None, River, Canyon, Maze, Landmark }

    /// <summary>What the large-form plan says about one chunk; terrain heights come from the corner field.</summary>
    public sealed class ChunkFeature
    {
        public Feature Feature;
        public readonly Mid[] Mids = { Mid.None, Mid.None, Mid.None, Mid.None };
        /// <summary>Along a river: a bridge or ford goes here. Along a canyon: a log or a descent.</summary>
        public bool Crossing;
        public bool MazeHeart;
        /// <summary>A cell of a giant tangled forest (a maze of giant trees, roots and deadfall), and its sunny glades.</summary>
        public bool Forest, Glade;
        /// <summary>Landmark part: which landmark, its canonical part and the landmark's quarter turns.</summary>
        public LandmarkKind Landmark;
        public int PartI, PartJ, LandmarkRotation;
    }

    /// <summary>
    /// Plans rivers, canyons and thicket mazes for one district (District × District chunks). Where a river or canyon
    /// leaves the district is decided by a hash of the shared border, so neighbouring districts agree without
    /// knowing each other's plans. Rivers and canyons are simple paths over flat low chunks; mazes stay one chunk away
    /// from the district's edge.
    /// </summary>
    public sealed class DistrictPlanner
    {
        readonly int seed;
        readonly WorldRules rules;
        readonly CornerField corners;
        readonly BiomeField biomes;
        readonly int size, half;

        public DistrictPlanner(int seed, WorldRules rules, CornerField corners, BiomeField biomes = null)
        {
            this.seed = seed; this.rules = rules; this.corners = corners; this.biomes = biomes;
            size = rules.District;
            half = size / 2;
        }

        public static (int dx, int dy) DistrictOf(ChunkCoord c, int size) =>
            (CornerField.FloorDiv(c.X + size / 2, size), CornerField.FloorDiv(c.Y + size / 2, size));

        ChunkCoord Origin(int dx, int dy) => new ChunkCoord(dx * size - half, dy * size - half);

        // ---------- borders ----------

        /// <summary>
        /// Offset (1 … size − 2) along a district border where a river / canyon crosses it, or −1.
        /// Vertical border (vertical = true) lies east of district (dx, dy); horizontal lies north of it.
        /// </summary>
        public (int river, int canyon) BorderCrossings(bool vertical, int dx, int dy)
        {
            var origin = Origin(dx, dy);
            ChunkCoord Inside(int offset) => vertical ? new ChunkCoord(origin.X + size - 1, origin.Y + offset) : new ChunkCoord(origin.X + offset, origin.Y + size - 1);
            ChunkCoord Outside(int offset) => vertical ? new ChunkCoord(origin.X + size, origin.Y + offset) : new ChunkCoord(origin.X + offset, origin.Y + size);
            double mx = (vertical ? origin.X + size - .5 : origin.X + half) * rules.ChunkSize;
            double my = (vertical ? origin.Y + half : origin.Y + size - .5) * rules.ChunkSize;
            int ring = rules.RingAt(mx, my);
            int kind = vertical ? 0 : 1;

            int Pick(int salt, double chance, Func<int, bool> extra)
            {
                if (Hash.Unit(seed, salt, kind, dx, dy) >= chance) return -1;
                var options = new List<int>();
                for (int o = 1; o <= size - 2; o++)
                {
                    var a = Inside(o); var b = Outside(o);
                    if (!Usable(a) || !Usable(b) || !extra(o)) continue;
                    options.Add(o);
                }
                return options.Count == 0 ? -1 : options[Hash.Range(seed, options.Count, salt + 100, kind, dx, dy)];
            }

            int river = Pick(Salt.RiverBorder, rules.RiverCrossing[ring], _ => true);
            int canyon = Pick(Salt.CanyonBorder, rules.CanyonCrossing[ring],
                o => Math.Abs(o - river) > 1 && CanyonAllowed(Inside(o)) && CanyonAllowed(Outside(o)));
            return (river, canyon);
        }

        /// <summary>Rivers and canyons keep to dry land: the swamp is water already.</summary>
        bool Usable(ChunkCoord c) => !WorldRules.IsStartArea(c) && corners.IsFlatLow(c) && !Swamp(c);
        /// <summary>Canyons stay out of the swamp: it is a basin, not a gorge.</summary>
        bool CanyonAllowed(ChunkCoord c) => rules.RingOf(c) >= 1 && !Swamp(c);

        bool Swamp(ChunkCoord c) => biomes != null && biomes.IsSwamp(c);

        Biome BiomeOf(ChunkCoord c) => biomes?.At(c) ?? Biome.Forest;

        // ---------- the district ----------

        public Dictionary<ChunkCoord, ChunkFeature> Plan(int dx, int dy)
        {
            var origin = Origin(dx, dy);
            var plan = new Dictionary<ChunkCoord, ChunkFeature>();
            var used = new HashSet<ChunkCoord>();
            bool Inside(ChunkCoord c) => c.X >= origin.X && c.Y >= origin.Y && c.X < origin.X + size && c.Y < origin.Y + size;
            ChunkFeature At(ChunkCoord c) { if (!plan.TryGetValue(c, out var f)) plan[c] = f = new ChunkFeature(); return f; }

            // Crossing points on the four borders: (inside chunk, side facing out).
            var riverPoints = new List<(ChunkCoord, Side)>();
            var canyonPoints = new List<(ChunkCoord, Side)>();
            void Gather(bool vertical, int bx, int by, bool ownSideIsFirst, Side side)
            {
                var (river, canyon) = BorderCrossings(vertical, bx, by);
                var o = Origin(bx, by);
                ChunkCoord Chunk(int offset) => vertical
                    ? new ChunkCoord(ownSideIsFirst ? o.X + size - 1 : o.X + size, o.Y + offset)
                    : new ChunkCoord(o.X + offset, ownSideIsFirst ? o.Y + size - 1 : o.Y + size);
                if (river >= 0) riverPoints.Add((Chunk(river), side));
                if (canyon >= 0) canyonPoints.Add((Chunk(canyon), side));
            }
            Gather(true, dx, dy, true, Side.East);
            Gather(true, dx - 1, dy, false, Side.West);
            Gather(false, dx, dy, true, Side.North);
            Gather(false, dx, dy - 1, false, Side.South);
            var reserved = new HashSet<ChunkCoord>();
            foreach (var (c, _) in riverPoints) reserved.Add(c);
            foreach (var (c, _) in canyonPoints) reserved.Add(c);

            int ring = rules.RingAt((origin.X + half) * rules.ChunkSize, (origin.Y + half) * rules.ChunkSize);

            PlanPaths(Feature.River, riverPoints, Salt.RiverPair, c => Inside(c) && Usable(c), dx, dy, plan, used, reserved, At,
                Hash.Unit(seed, Salt.InnerRiver, dx, dy) < rules.InnerRiver[ring]);
            PlanPaths(Feature.Canyon, canyonPoints, Salt.RiverPair + 1, c => Inside(c) && Usable(c) && CanyonAllowed(c), dx, dy, plan, used, reserved, At, false);
            // Landmarks first: they need a whole square of even ground; mazes grow over whatever is left.
            if (Hash.Unit(seed, Salt.Landmark, dx, dy) < rules.Landmark[ring]) PlanLandmark(origin, dx, dy, used, At, 0);
            if (Hash.Unit(seed, Salt.Landmark + 1, dx, dy) < rules.SecondLandmark[ring]) PlanLandmark(origin, dx, dy, used, At, 1);
            // Away from the start a giant tangled forest may take the first maze's place; it grows bigger.
            bool forest = Hash.Unit(seed, Salt.Forest, dx, dy) < rules.Forest[ring] && PlanMaze(origin, dx, dy, plan, used, At, 2, true);
            if (!forest && Hash.Unit(seed, Salt.Maze, dx, dy) < rules.Maze[ring]) PlanMaze(origin, dx, dy, plan, used, At, 0);
            if (Hash.Unit(seed, Salt.Maze + 9, dx, dy) < rules.SecondMaze[ring]) PlanMaze(origin, dx, dy, plan, used, At, 1);
            return plan;
        }

        void PlanPaths(Feature feature, List<(ChunkCoord cell, Side side)> points, int salt, Func<ChunkCoord, bool> allowed, int dx, int dy,
            Dictionary<ChunkCoord, ChunkFeature> plan, HashSet<ChunkCoord> used, HashSet<ChunkCoord> reserved,
            Func<ChunkCoord, ChunkFeature> at, bool innerIfNone)
        {
            Mid mid = feature == Feature.River ? Mid.River : Mid.Canyon;
            // Deterministic shuffle so pairs differ between districts.
            for (int i = points.Count - 1; i > 0; i--)
            {
                int j = Hash.Range(seed, i + 1, salt, dx, dy, i);
                (points[i], points[j]) = (points[j], points[i]);
            }
            foreach (var (cell, side) in points) { at(cell).Feature = feature; at(cell).Mids[(int)side] = mid; used.Add(cell); }
            // Pair far-apart points first so rivers and canyons run across the district instead of hooking back.
            for (int i = 0; i + 1 < points.Count; i += 2)
            {
                int far = i + 1;
                for (int j = i + 1; j < points.Count; j++)
                    if (Distance(points[i].cell, points[j].cell) > Distance(points[i].cell, points[far].cell)) far = j;
                (points[i + 1], points[far]) = (points[far], points[i + 1]);
            }

            var paths = new List<List<ChunkCoord>>();
            int k = 0;
            for (; k + 1 < points.Count; k += 2)
            {
                var path = FindPath(points[k].cell, points[k + 1].cell, allowed, used, reserved, salt, dx, dy);
                if (path != null) { Mark(path, feature, mid, used, at); paths.Add(path); }
                else
                {
                    paths.Add(Stub(points[k].cell, feature, mid, allowed, used, reserved, at, salt, 0));
                    paths.Add(Stub(points[k + 1].cell, feature, mid, allowed, used, reserved, at, salt, 1));
                }
            }
            if (k < points.Count) paths.Add(Stub(points[k].cell, feature, mid, allowed, used, reserved, at, salt, 2));

            if (points.Count == 0 && innerIfNone)
            {
                var origin = Origin(dx, dy);
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    var start = new ChunkCoord(origin.X + 1 + Hash.Range(seed, size - 2, salt + 7, dx, dy, attempt),
                        origin.Y + 1 + Hash.Range(seed, size - 2, salt + 8, dx, dy, attempt));
                    if (!allowed(start) || used.Contains(start) || reserved.Contains(start)) continue;
                    at(start).Feature = feature;
                    used.Add(start);
                    var walk = Stub(start, feature, mid, allowed, used, reserved, at, salt, 3, minLength: 2);
                    if (walk.Count >= 3) { paths.Add(walk); break; }
                    foreach (var c in walk) { plan.Remove(c); used.Remove(c); }
                }
            }

            foreach (var path in paths) PlaceCrossings(path, feature, at, salt);
        }

        List<ChunkCoord> FindPath(ChunkCoord from, ChunkCoord to, Func<ChunkCoord, bool> allowed, HashSet<ChunkCoord> used,
            HashSet<ChunkCoord> reserved, int salt, int dx, int dy)
        {
            // Dijkstra with random cell costs: meandering, deterministic.
            var dist = new Dictionary<ChunkCoord, double> { [from] = 0 };
            var prev = new Dictionary<ChunkCoord, ChunkCoord>();
            var open = new List<ChunkCoord> { from };
            var done = new HashSet<ChunkCoord>();
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[best]]) best = i;
                var cell = open[best];
                open.RemoveAt(best);
                if (!done.Add(cell)) continue;
                if (cell.Equals(to)) break;
                foreach (var side in Sides.All)
                {
                    var next = cell.Step(side);
                    if (done.Contains(next)) continue;
                    bool target = next.Equals(to);
                    if (!target && (!allowed(next) || used.Contains(next) || reserved.Contains(next))) continue;
                    double cost = dist[cell] + 1 + 1.5 * Hash.Unit(seed, salt + 50, next.X, next.Y, dx * 31 + dy);
                    if (dist.TryGetValue(next, out var old) && old <= cost) continue;
                    dist[next] = cost;
                    prev[next] = cell;
                    open.Add(next);
                }
            }
            if (!prev.ContainsKey(to)) return null;
            var path = new List<ChunkCoord> { to };
            var at = to;
            while (!at.Equals(from)) { at = prev[at]; path.Add(at); }
            path.Reverse();
            return path;
        }

        void Mark(List<ChunkCoord> path, Feature feature, Mid mid, HashSet<ChunkCoord> used, Func<ChunkCoord, ChunkFeature> at)
        {
            for (int i = 0; i < path.Count; i++)
            {
                at(path[i]).Feature = feature;
                used.Add(path[i]);
                if (i + 1 < path.Count)
                {
                    var side = SideBetween(path[i], path[i + 1]);
                    at(path[i]).Mids[(int)side] = mid;
                    at(path[i + 1]).Mids[(int)side.Opposite()] = mid;
                }
            }
        }

        /// <summary>A short dead-end run inward from a crossing (ends in a lake or spring, or a canyon end).</summary>
        List<ChunkCoord> Stub(ChunkCoord start, Feature feature, Mid mid, Func<ChunkCoord, bool> allowed, HashSet<ChunkCoord> used,
            HashSet<ChunkCoord> reserved, Func<ChunkCoord, ChunkFeature> at, int salt, int which, int minLength = 0)
        {
            var path = new List<ChunkCoord> { start };
            int length = minLength + Hash.Range(seed, 4, salt + 20, start.X, start.Y, which);
            var cell = start;
            for (int step = 0; step < length; step++)
            {
                var options = new List<ChunkCoord>();
                foreach (var side in Sides.All)
                {
                    var next = cell.Step(side);
                    if (!allowed(next) || used.Contains(next) || reserved.Contains(next)) continue;
                    // Keep the run from touching itself so every cell has at most two water edges.
                    int touching = 0;
                    foreach (var s2 in Sides.All) if (path.Contains(next.Step(s2))) touching++;
                    if (touching == 1) options.Add(next);
                }
                if (options.Count == 0) break;
                var chosen = options[Hash.Range(seed, options.Count, salt + 21, cell.X, cell.Y, step)];
                path.Add(chosen);
                cell = chosen;
            }
            Mark(path, feature, mid, used, at);
            return path;
        }

        static int Distance(ChunkCoord a, ChunkCoord b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        static Side SideBetween(ChunkCoord a, ChunkCoord b) =>
            b.X > a.X ? Side.East : b.X < a.X ? Side.West : b.Y > a.Y ? Side.North : Side.South;

        void PlaceCrossings(List<ChunkCoord> path, Feature feature, Func<ChunkCoord, ChunkFeature> at, int salt)
        {
            int[] every = feature == Feature.River ? rules.RiverCrossingEvery : rules.CanyonCrossingEvery;
            double far = feature == Feature.River ? rules.FarRiverCrossingChance : rules.FarCanyonCrossingChance;
            if (path.Count == 0) return;
            int sinceLast = Hash.Range(seed, 2, Salt.Crossing, path[0].X, path[0].Y, salt);
            foreach (var cell in path)
            {
                var f = at(cell);
                int edges = 0;
                foreach (var m in f.Mids) if (m != Mid.None) edges++;
                if (edges < 2) continue; // ends have their own way across or down
                int ring = rules.RingOf(cell);
                sinceLast++;
                bool place = every[ring] > 0 ? sinceLast >= every[ring] : Hash.Unit(seed, Salt.Crossing, cell.X, cell.Y, salt + 1) < far;
                if (place) { f.Crossing = true; sinceLast = 0; }
            }
        }

        // ---------- landmarks ----------

        /// <summary>A landmark on a square of free even chunks inside the district (low ground or a plateau top), turned at random.</summary>
        void PlanLandmark(ChunkCoord origin, int dx, int dy, HashSet<ChunkCoord> used, Func<ChunkCoord, ChunkFeature> at, int which)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int key = attempt + 37 * which;
                var kind = Landmarks.Placed[Hash.Range(seed, Landmarks.Placed.Length, Salt.Landmark + 2, dx, dy, key)];
                int n = kind.Size;
                int ox = origin.X + 1 + Hash.Range(seed, Math.Max(1, size - 1 - n), Salt.Landmark + 3, dx, dy, key);
                int oy = origin.Y + 1 + Hash.Range(seed, Math.Max(1, size - 1 - n), Salt.Landmark + 4, dx, dy, key);
                if (ox + n > origin.X + size - 1 || oy + n > origin.Y + size - 1) continue;
                // A landmark belongs to one land: all its parts in the biome it is made for.
                bool ok = true;
                for (int x = ox; x < ox + n && ok; x++)
                    for (int y = oy; y < oy + n && ok; y++)
                        if (BiomeOf(new ChunkCoord(x, y)) != kind.Biome) ok = false;
                // A swamp landmark stands in open water: every corner of it wet.
                if (kind.Biome == Biome.Swamp)
                    for (int x = ox; x <= ox + n && ok; x++)
                        for (int y = oy; y <= oy + n && ok; y++)
                            if (biomes == null || !biomes.Wet(x, y)) ok = false;
                if (!ok) continue;
                int level = corners.Level(ox, oy);
                for (int x = ox; x <= ox + n && ok; x++)
                    for (int y = oy; y <= oy + n && ok; y++)
                        if (corners.Level(x, y) != level) ok = false;
                for (int x = ox; x < ox + n && ok; x++)
                    for (int y = oy; y < oy + n && ok; y++)
                    {
                        var c = new ChunkCoord(x, y);
                        if (used.Contains(c) || WorldRules.IsStartArea(c)) ok = false;
                    }
                if (!ok) continue;
                int rotation = Hash.Range(seed, 4, Salt.Landmark + 5, dx, dy, key);
                for (int wi = 0; wi < n; wi++)
                    for (int wj = 0; wj < n; wj++)
                    {
                        var c = new ChunkCoord(ox + wi, oy + wj);
                        var (i, j) = kind.PartAt(rotation, wi, wj);
                        var f = at(c);
                        f.Feature = Feature.Landmark;
                        f.Landmark = kind;
                        f.PartI = i;
                        f.PartJ = j;
                        f.LandmarkRotation = rotation;
                        used.Add(c);
                    }
                return;
            }
        }

        // ---------- mazes ----------

        /// <summary>
        /// A maze over free flat low ground: a stone maze of 4…9 chunks, or a giant tangled forest of 8…16 (at least 6)
        /// with more loops, a third way in and one or two glades. Every cell is reached on foot from an entrance; the
        /// heart is the cell furthest from them.
        /// </summary>
        bool PlanMaze(ChunkCoord origin, int dx, int dy, Dictionary<ChunkCoord, ChunkFeature> plan, HashSet<ChunkCoord> used,
            Func<ChunkCoord, ChunkFeature> at, int which, bool forest = false)
        {
            bool Free(ChunkCoord c)
            {
                if (c.X <= origin.X || c.Y <= origin.Y || c.X >= origin.X + size - 1 || c.Y >= origin.Y + size - 1) return false;
                if (used.Contains(c) || WorldRules.IsStartArea(c) || !corners.IsFlatLow(c)) return false;
                // No thicket mazes and no giant forest in the swamp.
                if (Swamp(c)) return false;
                // A second maze keeps a chunk of open ground from the first.
                foreach (var side in Sides.All)
                    if (plan.TryGetValue(c.Step(side), out var near) && near.Feature == Feature.Maze) return false;
                return true;
            }
            for (int attempt = 0; attempt < (forest ? 16 : 12); attempt++)
            {
                // A patch grown over whatever flat ground is free, so it fits between rivers and plateaus.
                int key = attempt + 31 * which;
                var first = new ChunkCoord(origin.X + 1 + Hash.Range(seed, size - 2, Salt.Maze + 3, dx, dy, key),
                    origin.Y + 1 + Hash.Range(seed, size - 2, Salt.Maze + 4, dx, dy, key));
                if (!Free(first)) continue;
                int target = forest ? 8 + Hash.Range(seed, 9, Salt.Forest + 1, dx, dy, key) : 4 + Hash.Range(seed, 6, Salt.Maze + 1, dx, dy, key);
                var cells = new List<ChunkCoord> { first };
                var taken = new HashSet<ChunkCoord> { first };
                int tries = forest ? 192 : 64;
                for (int grow = 0; grow < tries && cells.Count < target; grow++)
                {
                    var from = cells[Hash.Range(seed, cells.Count, Salt.Maze + 2, dx, dy, key * tries + grow)];
                    var next = from.Step((Side)Hash.Range(seed, 4, Salt.Maze + 5, dx, dy, key * tries + grow));
                    if (taken.Contains(next) || !Free(next)) continue;
                    taken.Add(next);
                    cells.Add(next);
                }
                if (cells.Count < (forest ? 6 : 4)) continue;
                var set = new HashSet<ChunkCoord>(cells);

                // Entrances: patch edges facing a free flat chunk.
                var entrances = new List<(ChunkCoord, Side)>();
                foreach (var c in cells)
                    foreach (var side in Sides.All)
                    {
                        var outside = c.Step(side);
                        if (set.Contains(outside) || used.Contains(outside) || !corners.IsFlatLow(outside)) continue;
                        entrances.Add((c, side));
                    }
                if (entrances.Count < 2) continue;
                var chosen = ChooseEntrances(entrances, dx, dy, forest && cells.Count >= 10);

                // Spanning tree by randomized depth-first search, then a few loops.
                var passages = new HashSet<(ChunkCoord, ChunkCoord)>();
                var visited = new HashSet<ChunkCoord>();
                var stack = new Stack<ChunkCoord>();
                stack.Push(cells[Hash.Range(seed, cells.Count, Salt.MazeDfs, dx, dy, which)]);
                visited.Add(stack.Peek());
                int step = 0;
                while (stack.Count > 0)
                {
                    var cell = stack.Peek();
                    var options = new List<ChunkCoord>();
                    foreach (var side in Sides.All) { var n = cell.Step(side); if (set.Contains(n) && !visited.Contains(n)) options.Add(n); }
                    if (options.Count == 0) { stack.Pop(); continue; }
                    var next = options[Hash.Range(seed, options.Count, Salt.MazeDfs + 1, dx, dy, step++ + 997 * which)];
                    passages.Add(Key(cell, next));
                    visited.Add(next);
                    stack.Push(next);
                }
                foreach (var c in cells)
                    foreach (var side in new[] { Side.East, Side.North })
                    {
                        var n = c.Step(side);
                        if (set.Contains(n) && Hash.Unit(seed, Salt.MazeLoop, n.X, n.Y, (int)side) < (forest ? .24 : .15)) passages.Add(Key(c, n));
                    }

                foreach (var c in cells)
                {
                    var f = at(c);
                    f.Feature = Feature.Maze;
                    f.Forest = forest;
                    used.Add(c);
                    foreach (var side in Sides.All)
                    {
                        var n = c.Step(side);
                        bool open = set.Contains(n) ? passages.Contains(Key(c, n)) : chosen.Contains((c, side));
                        f.Mids[(int)side] = open ? Mid.MazeOpen : Mid.MazeWall;
                    }
                }
                // The heart: furthest from every entrance along the corridors.
                var distance = new Dictionary<ChunkCoord, int>();
                var queue = new Queue<ChunkCoord>();
                foreach (var (c, _) in chosen) if (!distance.ContainsKey(c)) { distance[c] = 0; queue.Enqueue(c); }
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    foreach (var side in Sides.All)
                    {
                        var n = c.Step(side);
                        if (!set.Contains(n) || distance.ContainsKey(n) || !passages.Contains(Key(c, n))) continue;
                        distance[n] = distance[c] + 1;
                        queue.Enqueue(n);
                    }
                }
                ChunkCoord heart = cells[0];
                foreach (var c in cells) if (distance.TryGetValue(c, out int d) && d > distance[heart]) heart = c;
                at(heart).MazeHeart = true;
                if (forest)
                {
                    // Glades: one, two in a big forest; never the heart, never next to another glade.
                    int glades = cells.Count >= 11 ? 2 : 1;
                    var order = new List<ChunkCoord>(cells);
                    order.Sort((a, b) => Hash.Unit(seed, Salt.Forest + 2, a.X, a.Y).CompareTo(Hash.Unit(seed, Salt.Forest + 2, b.X, b.Y)));
                    var glade = new List<ChunkCoord>();
                    foreach (var c in order)
                    {
                        if (glade.Count >= glades) break;
                        if (c.Equals(heart) || glade.Exists(g => Distance(g, c) < 2)) continue;
                        glade.Add(c);
                        at(c).Glade = true;
                    }
                }
                return true;
            }
            return false;
        }

        HashSet<(ChunkCoord, Side)> ChooseEntrances(List<(ChunkCoord cell, Side side)> options, int dx, int dy, bool three = false)
        {
            var result = new HashSet<(ChunkCoord, Side)>();
            int first = Hash.Range(seed, options.Count, Salt.MazeEntrance, dx, dy);
            result.Add(options[first]);
            // Second entrance on another side of the patch when possible, as far from the first as it gets.
            var a = options[first];
            (ChunkCoord, Side) best = default;
            int bestScore = -1;
            foreach (var o in options)
            {
                if (result.Contains(o)) continue;
                int score = Math.Abs(o.cell.X - a.cell.X) + Math.Abs(o.cell.Y - a.cell.Y) + (o.side != a.side ? 10 : 0);
                if (score > bestScore) { bestScore = score; best = o; }
            }
            result.Add(best);
            if (options.Count > 2 && (three || Hash.Unit(seed, Salt.MazeEntrance + 1, dx, dy) < .5))
            {
                var third = options[Hash.Range(seed, options.Count, Salt.MazeEntrance + 2, dx, dy)];
                result.Add(third);
            }
            return result;
        }

        static (ChunkCoord, ChunkCoord) Key(ChunkCoord a, ChunkCoord b) =>
            a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a, b) : (b, a);
    }
}
