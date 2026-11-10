using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>The endless world plan: matching edges, rings, large forms and the ring 0 guarantee over many seeds.</summary>
    public sealed class WorldMapTests
    {
        const int Area = 22;

        static IEnumerable<ChunkCoord> Square(int r)
        {
            for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                    yield return new ChunkCoord(x, y);
        }

        [Test]
        public void SameSeedSameWorldWhateverTheOrder()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var a = new WorldMap(seed);
                var b = new WorldMap(seed);
                var far = Square(Area).ToList();
                var reversed = Enumerable.Reverse(far).ToList();
                foreach (var c in reversed) b.Chunk(c);
                foreach (var c in far) Assert.AreEqual(a.Chunk(c).ToString(), b.Chunk(c).ToString(), $"seed {seed} {c}");
            }
        }

        [Test]
        public void NeighboursAlwaysMatch()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var map = new WorldMap(seed);
                foreach (var c in Square(Area))
                {
                    var a = map.Chunk(c);
                    Assert.LessOrEqual(a.Corners.Max() - a.Corners.Min(), 1, $"seed {seed} {a}: corners differ by two levels");
                    foreach (var side in new[] { Side.East, Side.North })
                    {
                        var b = map.Chunk(c.Step(side));
                        var opposite = side.Opposite();
                        Assert.IsTrue(Sides.Compatible(a.Mids[(int)side], b.Mids[(int)opposite]),
                            $"seed {seed}: {a} {side} {a.Mids[(int)side]} vs {b} {b.Mids[(int)opposite]}");
                        for (int s = 0; s < 3; s++)
                        {
                            int ra = a.Port(side, s), rb = b.Port(opposite, 2 - s);
                            if (a.Shape.Kind != ShapeKind.Maze && b.Shape.Kind != ShapeKind.Maze)
                                Assert.AreEqual(ra < 0, rb < 0, $"seed {seed}: {a} {side}[{s}] open {ra >= 0} vs {b} open {rb >= 0}");
                            if (ra >= 0 && rb >= 0)
                                Assert.AreEqual(a.AbsoluteLevel(ra), b.AbsoluteLevel(rb), $"seed {seed}: {a} {side}[{s}] level vs {b}");
                        }
                    }
                }
            }
        }

        [Test]
        public void WaterAndCanyonsNeverLeakOrBranch()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var map = new WorldMap(seed);
                foreach (var c in Square(Area))
                {
                    var p = map.Chunk(c);
                    int rivers = p.Mids.Count(m => m == Mid.River), canyons = p.Mids.Count(m => m == Mid.Canyon);
                    Assert.LessOrEqual(rivers, 2, $"seed {seed} {p}");
                    Assert.LessOrEqual(canyons, 2, $"seed {seed} {p}");
                    Assert.IsFalse(rivers > 0 && canyons > 0, $"seed {seed} {p}");
                    if (rivers > 0) Assert.AreEqual(ShapeKind.River, p.Shape.Kind, $"seed {seed} {p}");
                    if (canyons > 0) Assert.AreEqual(ShapeKind.Canyon, p.Shape.Kind, $"seed {seed} {p}");
                    if (p.Shape.Kind == ShapeKind.River || p.Shape.Kind == ShapeKind.Canyon || p.Shape.Kind == ShapeKind.Maze)
                        Assert.AreEqual(0, p.Corners.Max(), $"seed {seed} {p}: forms lie on flat low ground");
                }
            }
        }

        [Test]
        public void RingsShapeTheWorld()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var map = new WorldMap(seed);
                foreach (var c in Square(Area))
                {
                    var p = map.Chunk(c);
                    if (p.Ring == 0)
                    {
                        if (!WorldRules.IsStartArea(c) && Math.Max(Math.Abs(c.X), Math.Abs(c.Y)) > 2)
                            Assert.AreEqual(0, p.Corners.Max(), $"seed {seed} {p}: ring 0 is low but for the start");
                        foreach (var spawn in p.Spawns) Assert.IsTrue(CreatureThemes.IsCalm(spawn.Creature), $"seed {seed} {p}: {spawn} in ring 0");
                        Assert.AreNotEqual(ShapeKind.Canyon, p.Shape.Kind, $"seed {seed} {p}");
                        Assert.AreNotEqual("island", p.Variant.Id, $"seed {seed} {p}");
                    }
                    if (p.Corners.Max() == 3)
                        Assert.GreaterOrEqual(Math.Sqrt(c.X * c.X + c.Y * c.Y) * 16, map.Rules.MiddleRadius - 16, $"seed {seed} {p}: the third level only far out");
                    Assert.LessOrEqual(p.Corners.Max() - p.Corners.Min(), 1, $"seed {seed} {p}: one level at a time");
                    if (p.Shape.Kind == ShapeKind.Canyon) Assert.GreaterOrEqual(p.Ring, 1, $"seed {seed} {p}");
                }
                Assert.AreEqual(Landmarks.StartHill.PartId(1, 1), map.Chunk(0, 0).Shape.Id);
                Assert.AreEqual(map.Rules.StartLevel, map.Chunk(0, 0).BaseLevel, "the start stands above ring 0");
                foreach (var c in Square(1)) Assert.AreEqual(ShapeKind.Landmark, map.Chunk(c).Shape.Kind, $"seed {seed} {c}: the start hill");
            }
        }

        [Test]
        public void RingZeroIsWalkableAndLeadsOut()
        {
            int bridged = 0;
            for (int seed = 1; seed <= 300; seed++)
            {
                var map = new WorldMap(seed);
                Assert.IsFalse(map.Diagnostics.Any(d => d.StartsWith("guarantee not met")), $"seed {seed}: {string.Join("; ", map.Diagnostics)}");
                bridged += map.Diagnostics.Count;
                var graph = new WalkGraph(map, -9, -9, 9, 9);
                var reach = graph.Reachable(new ChunkCoord(0, 0), 0, walkOnly: true);
                bool exit = false;
                foreach (var node in graph.Nodes)
                {
                    var plan = map.Chunk(node.Coord);
                    if (plan.RegionAt(node.Region).Pocket) continue;
                    if (plan.Ring == 0) Assert.IsTrue(reach.Contains(node), $"seed {seed}: {plan} region {node.Region} not walkable");
                    if (plan.Ring == 1 && reach.Contains(node)) exit = true;
                }
                Assert.IsTrue(exit, $"seed {seed}: no walk out of ring 0");
            }
            UnityEngine.Debug.Log($"[World] 300 worlds, {bridged} bridges added by the guarantee");
        }

        [Test]
        public void LargeFormsShowUp()
        {
            int seeds = 60, longRivers = 0, plateaus = 0, canyons = 0, mazes = 0, summits = 0;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var map = new WorldMap(seed);
                var all = Square(Area).Select(map.Chunk).ToList();
                if (LargestComponent(map, all, p => p.Shape.Kind == ShapeKind.River) >= 8) longRivers++;
                if (LargestComponent(map, all, p => p.Corners.Max() >= 1) >= 6) plateaus++;
                if (LargestComponent(map, all, p => p.Shape.Kind == ShapeKind.Canyon) >= 4) canyons++;
                if (all.Any(p => p.Shape.Kind == ShapeKind.Maze)) mazes++;
                if (all.Any(p => p.Corners.Max() == 2)) summits++;
            }
            UnityEngine.Debug.Log($"[World] of {seeds}: long rivers {longRivers}, plateaus {plateaus}, canyons {canyons}, mazes {mazes}, summits {summits}");
            Assert.Greater(longRivers, seeds * .7);
            Assert.Greater(plateaus, seeds * .8);
            Assert.Greater(canyons, seeds * .5);
            Assert.Greater(mazes, seeds * .8);
            Assert.Greater(summits, seeds * .5);
        }

        [Test]
        public void MazesHaveTwoWaysInAndOneHeart()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var map = new WorldMap(seed);
                var seen = new HashSet<ChunkCoord>();
                foreach (var c in Square(Area))
                {
                    var p = map.Chunk(c);
                    if (p.Shape.Kind != ShapeKind.Maze || seen.Contains(c)) continue;
                    var component = Flood(map, c, q => q.Shape.Kind == ShapeKind.Maze);
                    foreach (var m in component) seen.Add(m);
                    if (component.Any(m => Math.Abs(m.X) >= Area || Math.Abs(m.Y) >= Area)) continue;
                    int entrances = 0, hearts = 0;
                    foreach (var m in component)
                    {
                        var mp = map.Chunk(m);
                        if (mp.MazeHeart) { hearts++; Assert.IsNotNull(mp.PocketReward, $"seed {seed} heart {mp}"); }
                        foreach (var side in Sides.All)
                            if (mp.Mids[(int)side] == Mid.MazeOpen && map.Chunk(m.Step(side)).Shape.Kind != ShapeKind.Maze) entrances++;
                    }
                    Assert.GreaterOrEqual(entrances, 2, $"seed {seed} maze at {c}");
                    Assert.AreEqual(1, hearts, $"seed {seed} maze at {c}");
                    // Every corridor is reachable on foot from an entrance.
                    var open = Flood(map, c, q => q.Shape.Kind == ShapeKind.Maze, (from, side) => map.Chunk(from).Mids[(int)side] == Mid.MazeOpen);
                    Assert.AreEqual(component.Count, open.Count, $"seed {seed} maze at {c} has sealed cells");
                }
            }
        }

        [Test]
        public void GiantForestsGrowAwayFromTheStartWithAHeartAndAGlade()
        {
            int worlds = 0, forests = 0, biggest = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var map = new WorldMap(seed);
                var seen = new HashSet<ChunkCoord>();
                bool any = false;
                foreach (var c in Square(Area))
                {
                    var p = map.Chunk(c);
                    if (!p.Forest) continue;
                    Assert.Greater(p.Ring, 0, $"seed {seed}: a giant forest at the start {p}");
                    if (seen.Contains(c)) continue;
                    var forest = Flood(map, c, q => q.Shape.Kind == ShapeKind.Maze);
                    foreach (var m in forest) seen.Add(m);
                    if (forest.Any(m => Math.Abs(m.X) >= Area || Math.Abs(m.Y) >= Area)) continue;
                    var cells = forest.Select(map.Chunk).ToList();
                    Assert.IsTrue(cells.All(q => q.Forest), $"seed {seed}: a forest grown into a stone maze at {c}");
                    Assert.GreaterOrEqual(cells.Count, 6, $"seed {seed}: forest at {c}");
                    Assert.LessOrEqual(cells.Count, 16, $"seed {seed}: forest at {c}");
                    Assert.AreEqual(1, cells.Count(q => q.Variant.Id == TileShapes.ForestHeart), $"seed {seed}: hearts of the forest at {c}");
                    Assert.GreaterOrEqual(cells.Count(q => q.Variant.Id == TileShapes.ForestGlade), 1, $"seed {seed}: glades of the forest at {c}");
                    foreach (var glade in cells.Where(q => q.Variant.Id == TileShapes.ForestGlade))
                        Assert.IsNotEmpty(glade.Loot, $"seed {seed}: an empty glade {glade}");
                    var heart = cells.First(q => q.MazeHeart);
                    Assert.IsTrue(heart.Spawns.Count > 0, $"seed {seed}: an unguarded heart {heart}");
                    biggest = Math.Max(biggest, cells.Count);
                    forests++;
                    any = true;
                }
                worlds += any ? 1 : 0;
            }
            UnityEngine.Debug.Log($"[World] giant forests: {forests} in {worlds} of 40 worlds, the biggest {biggest} chunks");
            Assert.Greater(worlds, 30);
            Assert.GreaterOrEqual(biggest, 12);
        }

        [Test]
        public void RoadsRunOverFlatGroundFromLandmarkToLandmark()
        {
            int roads = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var map = new WorldMap(seed);
                var seen = new HashSet<int>();
                foreach (var c in Square(Area))
                    foreach (var road in map.RoadsThrough(c))
                    {
                        Assert.IsTrue(road.Contains(c));
                        if (!seen.Add(road.Id)) continue;
                        roads++;
                        for (int k = 1; k < road.Cells.Count; k++)
                        {
                            var a = road.Cells[k - 1];
                            var b = road.Cells[k];
                            Assert.AreEqual(1, System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y), $"seed {seed}: road steps one chunk at a time");
                        }
                        foreach (var cell in road.Cells)
                        {
                            var p = map.Chunk(cell);
                            bool flat = p.Shape.Kind == ShapeKind.Plain || p.Shape.Kind == ShapeKind.PlateauTop || p.Shape.Kind == ShapeKind.Landmark;
                            bool crossing = p.Shape.Kind == ShapeKind.River && (p.Variant.Id == "bridge" || p.Variant.Id == "ford" || p.Shape.Id == "river-straight");
                            bool ramp = p.Shape.Id == "cliff-edge" && p.Variant.Id == "ramp";
                            Assert.IsTrue(flat || crossing || ramp, $"seed {seed}: road over {p}");
                        }
                        foreach (var end in new[] { road.Cells[0], road.Cells[road.Cells.Count - 1] })
                        {
                            var p = map.Chunk(end);
                            bool start = end.X == 0 && end.Y == 0;
                            Assert.IsTrue(start || p.Shape.Kind == ShapeKind.Landmark, $"seed {seed}: road ends at {p}");
                        }
                    }
            }
            Assert.GreaterOrEqual(roads, 6 * 2, "roads in every world");
        }

        [Test]
        public void LandmarksComeWholeAndOften()
        {
            int found = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var map = new WorldMap(seed);
                foreach (var c in Square(Area))
                {
                    var p = map.Chunk(c);
                    if (p.Shape.Kind != ShapeKind.Landmark || WorldRules.IsStartArea(c)) continue;
                    var (kind, i, j) = Landmarks.Parse(p.Shape.Id);
                    if (i == 0 && j == 0) found++;
                    // Find where this part sits in its landmark, then every other part must be where the turn puts it.
                    ChunkCoord? origin = null;
                    for (int wi = 0; wi < kind.Size; wi++)
                        for (int wj = 0; wj < kind.Size; wj++)
                            if (kind.PartAt(p.Rotation, wi, wj) == (i, j)) origin = new ChunkCoord(c.X - wi, c.Y - wj);
                    Assert.IsNotNull(origin, $"seed {seed} {p}");
                    for (int wi = 0; wi < kind.Size; wi++)
                        for (int wj = 0; wj < kind.Size; wj++)
                        {
                            var other = map.Chunk(origin.Value.X + wi, origin.Value.Y + wj);
                            var (oi, oj) = kind.PartAt(p.Rotation, wi, wj);
                            Assert.AreEqual(kind.PartId(oi, oj), other.Shape.Id, $"seed {seed} {p}: part at +{wi},{wj}");
                            Assert.AreEqual(p.Rotation, other.Rotation, $"seed {seed} {other}");
                        }
                }
            }
            Assert.GreaterOrEqual(found, 40 * 3, "a few landmarks in every world");
        }

        [Test]
        public void IslandsAndHeartsAlwaysPay()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var map = new WorldMap(seed);
                foreach (var c in Square(Area))
                {
                    var p = map.Chunk(c);
                    if (p.Variant.Id == "island") Assert.IsNotNull(p.PocketReward, $"seed {seed} {p}");
                    if (WorldRules.IsStartArea(c)) { Assert.IsEmpty(p.Spawns); Assert.IsEmpty(p.Loot); }
                }
            }
        }

        [Test]
        public void PlanningIsCheap()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var map = new WorldMap(12345);
            double guarantee = clock.Elapsed.TotalMilliseconds;
            foreach (var c in Square(30)) map.Chunk(c);
            double total = clock.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log($"[World] guarantee {guarantee:0} ms, 61×61 chunks {total:0} ms");
            Assert.Less(guarantee, 1500);
        }

        static int LargestComponent(WorldMap map, List<ChunkPlan> all, Func<ChunkPlan, bool> member)
        {
            var seen = new HashSet<ChunkCoord>();
            int best = 0;
            foreach (var p in all)
            {
                if (!member(p) || seen.Contains(p.Coord)) continue;
                var component = Flood(map, p.Coord, member, limit: Area);
                foreach (var c in component) seen.Add(c);
                best = Math.Max(best, component.Count);
            }
            return best;
        }

        static List<ChunkCoord> Flood(WorldMap map, ChunkCoord start, Func<ChunkPlan, bool> member, Func<ChunkCoord, Side, bool> through = null, int limit = 1000)
        {
            var seen = new HashSet<ChunkCoord> { start };
            var queue = new Queue<ChunkCoord>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var side in Sides.All)
                {
                    var n = c.Step(side);
                    if (Math.Abs(n.X) > limit || Math.Abs(n.Y) > limit || seen.Contains(n)) continue;
                    if (!member(map.Chunk(n))) continue;
                    if (through != null && !through(c, side)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            return seen.ToList();
        }
    }
}
