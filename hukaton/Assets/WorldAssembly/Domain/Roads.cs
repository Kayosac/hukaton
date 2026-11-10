using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>A worn road from one landmark (or the start) to another: the chunks it runs through, in order.</summary>
    public sealed class Road
    {
        public readonly int Id;
        public readonly List<ChunkCoord> Cells;
        readonly HashSet<ChunkCoord> set;

        public Road(int id, List<ChunkCoord> cells) { Id = id; Cells = cells; set = new HashSet<ChunkCoord>(cells); }

        public bool Contains(ChunkCoord c) => set.Contains(c);
    }

    /// <summary>
    /// Roads between landmarks (Kayospace «Открытый мир v2»: дороги ведут к ориентирам): every landmark is joined to
    /// its nearest neighbour, the start to its three nearest, along the cheapest way over flat ground — open country,
    /// plateaus, landmark parts, river crossings (straight over the bridge or ford) and the ramps of straight cliffs.
    /// Where no crossing is near, a road may meet a straight river on both banks: the way over is by card (ice).
    /// Other cliffs, mazes and canyons are in the way. A road belongs to the district of its lower end, so neighbouring districts agree without talking.
    /// </summary>
    sealed class RoadPlanner
    {
        const int MaxLength = 28;
        readonly WorldMap map;
        readonly int seed, district;
        readonly Dictionary<(int, int), List<ChunkCoord>> foci = new Dictionary<(int, int), List<ChunkCoord>>();
        readonly Dictionary<(int, int), List<Road>> roads = new Dictionary<(int, int), List<Road>>();
        readonly Dictionary<(ChunkCoord, ChunkCoord), List<ChunkCoord>> paths = new Dictionary<(ChunkCoord, ChunkCoord), List<ChunkCoord>>();
        readonly Dictionary<ChunkCoord, List<(ChunkCoord to, List<ChunkCoord> cells)>> joins = new Dictionary<ChunkCoord, List<(ChunkCoord, List<ChunkCoord>)>>();

        public RoadPlanner(WorldMap map, int seed, int district) { this.map = map; this.seed = seed; this.district = district; }

        /// <summary>Roads running through chunk <paramref name="c"/>.</summary>
        public List<Road> Through(ChunkCoord c)
        {
            var result = new List<Road>();
            var (dx, dy) = DistrictPlanner.DistrictOf(c, district);
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                    foreach (var road in Of(dx + i, dy + j))
                        if (road.Contains(c)) result.Add(road);
            return result;
        }

        /// <summary>Roads owned by district (dx, dy).</summary>
        public List<Road> Of(int dx, int dy)
        {
            if (roads.TryGetValue((dx, dy), out var list)) return list;
            list = new List<Road>();
            var pairs = new List<(ChunkCoord a, ChunkCoord b)>();
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    foreach (var f in Foci(dx + i, dy + j))
                        foreach (var (g, cells) in Joins(f))
                        {
                            var (a, b) = Less(f, g) ? (f, g) : (g, f);
                            if (DistrictPlanner.DistrictOf(a, district) != (dx, dy) || pairs.Contains((a, b))) continue;
                            pairs.Add((a, b));
                            list.Add(new Road((int)Hash.Of(seed, Salt.Road, a.X, a.Y, b.X * 7919 + b.Y), cells));
                        }
            roads[(dx, dy)] = list;
            return list;
        }

        /// <summary>
        /// The roads leaving focus <paramref name="f"/>: to the nearest focus a road can reach (from the start, to the
        /// three nearest), trying the next nearest when rivers or cliffs are in the way.
        /// </summary>
        List<(ChunkCoord to, List<ChunkCoord> cells)> Joins(ChunkCoord f)
        {
            if (joins.TryGetValue(f, out var list)) return list;
            list = new List<(ChunkCoord, List<ChunkCoord>)>();
            int want = f.X == 0 && f.Y == 0 ? 3 : 1;
            foreach (var g in Picks(f))
            {
                var (a, b) = Less(f, g) ? (f, g) : (g, f);
                if (!paths.TryGetValue((a, b), out var cells)) paths[(a, b)] = cells = Path(a, b);
                if (cells == null) continue;
                list.Add((g, cells));
                if (list.Count >= want) break;
            }
            joins[f] = list;
            return list;
        }

        /// <summary>Debug: where roads from a focus were tried and how far each got.</summary>
        public string Report(ChunkCoord f)
        {
            var text = new System.Text.StringBuilder();
            foreach (var g in Picks(f))
            {
                var (a, b) = Less(f, g) ? (f, g) : (g, f);
                if (!paths.TryGetValue((a, b), out var cells)) paths[(a, b)] = cells = Path(a, b);
                text.Append($"{f}->{g}: {(cells == null ? "none" : cells.Count + " cells")}; ");
            }
            return text.ToString();
        }

        static bool Less(ChunkCoord a, ChunkCoord b) => a.X < b.X || (a.X == b.X && a.Y < b.Y);

        /// <summary>Landmark rewards in a district, and the start in its own.</summary>
        List<ChunkCoord> Foci(int dx, int dy)
        {
            if (foci.TryGetValue((dx, dy), out var list)) return list;
            list = new List<ChunkCoord>();
            foreach (var (focus, _, _) in map.LandmarkFociOf(dx, dy)) list.Add(focus);
            if (DistrictPlanner.DistrictOf(new ChunkCoord(0, 0), district) == (dx, dy)) list.Add(new ChunkCoord(0, 0));
            foci[(dx, dy)] = list;
            return list;
        }

        /// <summary>Where a road from <paramref name="f"/> may go, nearest first.</summary>
        List<ChunkCoord> Picks(ChunkCoord f)
        {
            var (dx, dy) = DistrictPlanner.DistrictOf(f, district);
            var near = new List<(int d, ChunkCoord c)>();
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    foreach (var g in Foci(dx + i, dy + j))
                    {
                        if (g.Equals(f)) continue;
                        int d = Math.Abs(g.X - f.X) + Math.Abs(g.Y - f.Y);
                        if (d <= MaxLength) near.Add((d, g));
                    }
            near.Sort((p, q) => p.d != q.d ? p.d.CompareTo(q.d) : Less(p.c, q.c) ? -1 : 1);
            int count = f.X == 0 && f.Y == 0 ? 6 : 3;
            var result = new List<ChunkCoord>();
            for (int k = 0; k < near.Count && k < count; k++) result.Add(near[k].c);
            return result;
        }

        bool Passable(ChunkPlan plan)
        {
            switch (plan.Shape.Kind)
            {
                case ShapeKind.Plain:
                case ShapeKind.PlateauTop:
                case ShapeKind.Landmark:
                    return true;
                case ShapeKind.River:
                    // Over the bridge or the ford; straight across a straight river too, where ice makes the crossing.
                    return plan.Variant.Id == "bridge" || plan.Variant.Id == "ford" || plan.Shape.Id == "river-straight";
                case ShapeKind.Cliff:
                    // A straight cliff's ramp joins the flat ground at its foot to the flat ground on top.
                    return plan.Shape.Id == "cliff-edge" && plan.Variant.Id == "ramp";
                default:
                    return false;
            }
        }

        /// <summary>Cheapest way from a to b over passable chunks (A*), or null.</summary>
        List<ChunkCoord> Path(ChunkCoord a, ChunkCoord b)
        {
            int minX = Math.Min(a.X, b.X) - 6, maxX = Math.Max(a.X, b.X) + 6, minY = Math.Min(a.Y, b.Y) - 6, maxY = Math.Max(a.Y, b.Y) + 6;
            var cost = new Dictionary<ChunkCoord, double> { [a] = 0 };
            var from = new Dictionary<ChunkCoord, ChunkCoord>();
            var open = new SortedSet<(double f, int x, int y)> { (Distance(a, b), a.X, a.Y) };
            int expanded = 0;
            while (open.Count > 0 && expanded++ < 3000)
            {
                var top = open.Min;
                open.Remove(top);
                var c = new ChunkCoord(top.x, top.y);
                if (c.Equals(b))
                {
                    var cells = new List<ChunkCoord> { c };
                    while (from.TryGetValue(c, out var p)) { c = p; cells.Add(c); }
                    cells.Reverse();
                    return cells.Count <= MaxLength * 2 ? cells : null;
                }
                var here = map.Chunk(c);
                foreach (var side in Sides.All)
                {
                    var n = c.Step(side);
                    if (n.X < minX || n.X > maxX || n.Y < minY || n.Y > maxY) continue;
                    var next = map.Chunk(n);
                    if (!Passable(next) || here.Mids[(int)side] == Mid.River || next.Mids[(int)side.Opposite()] == Mid.River) continue;
                    // Through other landmarks only if there is no other way; a little wander so roads are not ruled.
                    bool foreign = next.Shape.Kind == ShapeKind.Landmark && !n.Equals(b) && !(n.X >= -1 && n.X <= 1 && n.Y >= -1 && n.Y <= 1 && a.X == 0 && a.Y == 0);
                    bool wade = next.Shape.Kind == ShapeKind.River && next.Variant.Id != "bridge" && next.Variant.Id != "ford";
                    double step = 1 + (foreign ? 2 : 0) + (wade ? 5 : 0) + Hash.Unit(seed, Salt.Road + 1, n.X, n.Y) * .5;
                    double g = cost[c] + step;
                    if (cost.TryGetValue(n, out var old) && old <= g) continue;
                    if (cost.ContainsKey(n)) open.Remove((old + Distance(n, b), n.X, n.Y));
                    cost[n] = g;
                    from[n] = c;
                    open.Add((g + Distance(n, b), n.X, n.Y));
                }
            }
            return null;
        }

        static double Distance(ChunkCoord a, ChunkCoord b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }
}
