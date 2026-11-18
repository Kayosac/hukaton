using System.Collections.Generic;
using System.Linq;
using Hakaton.Dungeons;
using NUnit.Framework;

namespace Hakaton.Dungeons.Tests
{
    public class DungeonGeneratorTests
    {
        /// <summary>Simple stand-in chambers (same roles and doors as the anthill's).</summary>
        public static DungeonRecipe Recipe()
        {
            RoomShape Rect(string id, RoomRole role, int w, int h, int rows, params (int x, int y, Dir d)[] doors)
            {
                var s = new RoomShape { Id = id, Role = role, Rows = rows };
                for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) s.Cells.Add(new Cell(x, y));
                foreach (var (x, y, d) in doors) s.Doors.Add((new Cell(x, y), d));
                return s;
            }
            var entrance = Rect("entrance", RoomRole.Entrance, 2, 3, 1, (0, 2, Dir.North), (1, 2, Dir.East));
            entrance.WayOut = (new Cell(0, 0), Dir.South);
            return new DungeonRecipe
            {
                Shapes = new List<RoomShape>
                {
                    entrance,
                    Rect("junction", RoomRole.Junction, 3, 3, 1, (1, 0, Dir.South), (2, 1, Dir.East), (0, 1, Dir.West), (1, 2, Dir.North)),
                    Rect("barracks", RoomRole.Barracks, 4, 4, 2, (1, 0, Dir.South), (2, 3, Dir.North), (3, 2, Dir.East), (0, 1, Dir.West)),
                    Rect("store", RoomRole.Store, 2, 2, 1, (0, 0, Dir.South)),
                    Rect("nursery", RoomRole.Nursery, 3, 3, 1, (1, 0, Dir.South), (1, 2, Dir.North), (2, 1, Dir.East), (0, 1, Dir.West)),
                    Rect("farm", RoomRole.Farm, 5, 6, 2, (2, 0, Dir.South)),
                    Rect("queen", RoomRole.Queen, 5, 5, 2, (2, 0, Dir.South)),
                }
            };
        }

        static IEnumerable<int> Seeds => Enumerable.Range(1, 200);

        [Test]
        public void EverySeedGivesALayout()
        {
            foreach (int seed in Seeds)
                Assert.DoesNotThrow(() => DungeonGenerator.Generate(seed, Recipe()), "seed " + seed);
        }

        [Test]
        public void SameSeedSameLayout()
        {
            var a = DungeonGenerator.Generate(77, Recipe());
            var b = DungeonGenerator.Generate(77, Recipe());
            CollectionAssert.AreEquivalent(a.Cells.Keys, b.Cells.Keys);
            Assert.AreEqual(a.Rooms.Count, b.Rooms.Count);
        }

        [Test]
        public void EveryCellIsReachableFromTheEntrance()
        {
            foreach (int seed in Seeds)
            {
                var plan = DungeonGenerator.Generate(seed, Recipe());
                var start = plan.Entrance.Cells[0];
                var seen = new HashSet<Cell> { start };
                var queue = new Queue<Cell>(seen);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    foreach (var d in Dirs.All)
                        if (plan.IsOpen(c, d) && seen.Add(c.Step(d))) queue.Enqueue(c.Step(d));
                }
                Assert.AreEqual(plan.Cells.Count, seen.Count, "seed " + seed);
            }
        }

        [Test]
        public void UnconnectedCellsNeverTouch()
        {
            foreach (int seed in Seeds)
            {
                var plan = DungeonGenerator.Generate(seed, Recipe());
                foreach (var c in plan.Cells.Keys)
                    foreach (var d in Dirs.All)
                        if (plan.Cells.ContainsKey(c.Step(d)) && !plan.IsOpen(c, d))
                            Assert.Fail($"seed {seed}: {c} touches {c.Step(d)} through a wall");
            }
        }

        [Test]
        public void FloorsMeetAcrossEveryOpenEdge()
        {
            foreach (int seed in Seeds)
            {
                var plan = DungeonGenerator.Generate(seed, Recipe());
                foreach (var c in plan.Cells.Keys)
                    foreach (var d in Dirs.All)
                        if (plan.IsOpen(c, d))
                            Assert.AreEqual(plan.EdgeFloor(c, d), plan.EdgeFloor(c.Step(d), d.Opposite()), 1e-4, $"seed {seed}: step at {c} {d}");
            }
        }

        [Test]
        public void TheWayOutIsAStraightDeadEndPassageBehindADoorway()
        {
            foreach (int seed in Seeds)
            {
                var plan = DungeonGenerator.Generate(seed, Recipe());
                Assert.IsTrue(plan.PassageMouth.HasValue, "seed " + seed);
                var (mouth, side) = plan.PassageMouth.Value;
                Assert.AreEqual(plan.Entrance.Index, plan.Cells[mouth].Room, "seed " + seed);
                Assert.IsTrue(plan.IsDoorway(mouth, side), "seed " + seed);
                Assert.AreEqual(DungeonPlan.PassageLength, plan.Passage.Count, "seed " + seed);
                var at = mouth;
                foreach (var c in plan.Passage)
                {
                    at = at.Step(side);
                    Assert.AreEqual(at, c, $"seed {seed}: the passage runs straight");
                    Assert.AreEqual(CellKind.Passage, plan.Cells[c].Kind, "seed " + seed);
                    Assert.AreEqual(plan.Entrance.Floor, plan.Cells[c].Floor, 1e-4, "seed " + seed);
                    // Only along the passage: nothing else opens into it.
                    foreach (var d in Dirs.All)
                        if (d != side && d != side.Opposite()) Assert.IsFalse(plan.IsOpen(c, d), $"seed {seed}: side opening at {c}");
                }
                Assert.IsFalse(plan.IsOpen(at, side), $"seed {seed}: the passage ends in a wall");
            }
        }

        [Test]
        public void StashesAreStraightDeadEndsBehindADoorway()
        {
            int total = 0;
            foreach (int seed in Seeds)
            {
                var plan = DungeonGenerator.Generate(seed, Recipe());
                foreach (var stash in plan.Stashes)
                {
                    total++;
                    Assert.IsTrue(plan.IsDoorway(stash.Room, stash.Side), "seed " + seed);
                    Assert.AreNotEqual(plan.Entrance.Index, plan.Cells[stash.Room].Room, "seed " + seed);
                    var at = stash.Room;
                    foreach (var c in stash.Cells)
                    {
                        at = at.Step(stash.Side);
                        Assert.AreEqual(at, c, $"seed {seed}: straight");
                        Assert.AreEqual(CellKind.Corridor, plan.Cells[c].Kind);
                        foreach (var d in Dirs.All)
                            if (d != stash.Side && d != stash.Side.Opposite()) Assert.IsFalse(plan.IsOpen(c, d), $"seed {seed}: side opening at {c}");
                    }
                    Assert.IsFalse(plan.IsOpen(at, stash.Side), $"seed {seed}: the stash ends in a wall");
                }
            }
            Assert.Greater(total, 150, "most dungeons have a stash or two");
        }

        [Test]
        public void LargeDungeonsEndWithTheQueenOnTheDeepestLevel()
        {
            int large = 0;
            foreach (int seed in Seeds)
            {
                var plan = DungeonGenerator.Generate(seed, Recipe());
                if (!plan.Large) { Assert.AreEqual(RoomRole.Store, plan.Goal.Shape.Role, "seed " + seed); continue; }
                large++;
                Assert.AreEqual(RoomRole.Queen, plan.Goal.Shape.Role, "seed " + seed);
                Assert.AreEqual(plan.Rooms.Min(r => r.Floor), plan.Goal.Floor, 1e-4, "seed " + seed);
                Assert.GreaterOrEqual(plan.Levels, 2, "seed " + seed);
            }
            Assert.Greater(large, 20);
        }
    }
}
