using System;
using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Dungeons
{
    /// <summary>Everything the planner needs to know about one dungeon kind (e.g. the anthill).</summary>
    public sealed class DungeonRecipe
    {
        /// <summary>The kind's hand-made chambers. At least one per role used below.</summary>
        public List<RoomShape> Shapes = new List<RoomShape>();
        public double LargeChance = 0.4;
        public double ThreeLevelChance = 0.5;
        public List<RoomRole> SmallPath = new List<RoomRole> { RoomRole.Entrance, RoomRole.Junction, RoomRole.Barracks, RoomRole.Store };
        public List<RoomRole> SmallBranches = new List<RoomRole> { RoomRole.Farm };
        public double SmallBranchChance = 0.5;
        /// <summary>Main path of a large dungeon; <see cref="Descend"/> marks a ramp corridor down to the next level.</summary>
        public List<RoomRole> LargePath2 = new List<RoomRole> { RoomRole.Entrance, RoomRole.Junction, RoomRole.Barracks, Descend, RoomRole.Nursery, RoomRole.Queen };
        public List<RoomRole> LargePath3 = new List<RoomRole> { RoomRole.Entrance, RoomRole.Junction, RoomRole.Barracks, Descend, RoomRole.Nursery, Descend, RoomRole.Barracks, RoomRole.Queen };
        public List<RoomRole> LargeBranches = new List<RoomRole> { RoomRole.Store, RoomRole.Farm, RoomRole.Store };
        public int CorridorMin = 2, CorridorMax = 6;
        /// <summary>Ramp cells (1 m each) between levels.</summary>
        public int LevelDrop = 6;
        /// <summary>Hidden stashes per dungeon: from and to (fewer when no chamber has a free door).</summary>
        public int StashMin = 1, StashMax = 2;
        /// <summary>Cells in a stash's corridor.</summary>
        public int StashLength = 2;
        /// <summary>Chance per corridor step to turn (never on a ramp).</summary>
        public double TurnChance = 0.3;

        /// <summary>Pseudo-role in a path list: the next corridor goes down a level.</summary>
        public const RoomRole Descend = (RoomRole)(-1);
    }

    /// <summary>
    /// Builds a <see cref="DungeonPlan"/>: chambers strung on a main path by winding corridors (ramps where it goes down
    /// a level), dead-end side rooms, and no two unconnected cells touching. Deterministic for a seed.
    /// </summary>
    public static class DungeonGenerator
    {
        const int Attempts = 40;

        public static DungeonPlan Generate(int seed, DungeonRecipe recipe)
        {
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                var plan = TryGenerate(seed, attempt, recipe);
                if (plan != null) return plan;
            }
            throw new InvalidOperationException($"Dungeon seed {seed}: no layout fits after {Attempts} attempts");
        }

        sealed class Builder
        {
            public DungeonPlan Plan;
            public Dice Dice;
            public DungeonRecipe Recipe;
            readonly HashSet<Cell> temp = new HashSet<Cell>();

            bool Taken(Cell c) => Plan.Cells.ContainsKey(c) || temp.Contains(c);

            /// <summary>Free and not touching anything except the listed cells.</summary>
            bool Free(Cell c, Cell allowA, Cell? allowB = null, ICollection<Cell> allowSet = null)
            {
                if (Taken(c)) return false;
                foreach (var d in Dirs.All)
                {
                    var n = c.Step(d);
                    if (!Taken(n)) continue;
                    if (n.Equals(allowA)) continue;
                    if (allowB.HasValue && n.Equals(allowB.Value)) continue;
                    if (allowSet != null && allowSet.Contains(n)) continue;
                    return false;
                }
                return true;
            }

            public RoomShape ShapeFor(RoomRole role)
            {
                var options = Recipe.Shapes.Where(s => s.Role == role).ToList();
                if (options.Count == 0) throw new InvalidOperationException("No chamber for role " + role);
                return Dice.Pick(options);
            }

            public PlacedRoom PlaceEntrance(RoomShape shape)
            {
                int rot = 0;
                if (shape.WayOut.HasValue) while (shape.WayOut.Value.side.Rotate(rot) != Dir.South) rot++;
                var room = NewRoom(shape, new Cell(0, 0), rot, 0f, 0, true);
                Commit(room);
                if (shape.WayOut.HasValue)
                {
                    // The way out: a short straight dead-end passage behind a doorway, towards the south.
                    var w = room.ToWorld(shape.WayOut.Value.cell);
                    var side = room.ToWorld(shape.WayOut.Value.side);
                    Plan.PassageMouth = (w, side);
                    var prev = w;
                    for (int i = 0; i < DungeonPlan.PassageLength; i++)
                    {
                        var c = prev.Step(side);
                        Plan.Cells[c] = new CellInfo { Kind = CellKind.Passage, Room = -1, Floor = room.Floor, Rows = 1 };
                        Plan.Open.Add(Edge.Of(prev, side));
                        Plan.Passage.Add(c);
                        prev = c;
                    }
                    Plan.Doorways.Add(Edge.Of(w, side));
                }
                return room;
            }

            PlacedRoom NewRoom(RoomShape shape, Cell origin, int rot, float floor, int level, bool main)
            {
                var room = new PlacedRoom { Index = Plan.Rooms.Count, Shape = shape, Origin = origin, Rotation = rot, Floor = floor, Level = level, OnMainPath = main };
                foreach (var c in shape.Cells) room.Cells.Add(room.ToWorld(c));
                foreach (var (c, s) in shape.Doors) room.Doors.Add((room.ToWorld(c), room.ToWorld(s)));
                return room;
            }

            void Commit(PlacedRoom room)
            {
                Plan.Rooms.Add(room);
                var set = new HashSet<Cell>(room.Cells);
                foreach (var c in room.Cells)
                {
                    Plan.Cells[c] = new CellInfo { Kind = CellKind.Room, Room = room.Index, Floor = room.Floor, Rows = room.Shape.Rows };
                    foreach (var d in Dirs.All) if (set.Contains(c.Step(d))) Plan.Open.Add(Edge.Of(c, d));
                }
            }

            /// <summary>
            /// From a free door of <paramref name="from"/>, digs a corridor (with <paramref name="drop"/> ramp cells down)
            /// and places a chamber of <paramref name="role"/> at its end. Returns null if nothing fits.
            /// </summary>
            public PlacedRoom Extend(PlacedRoom from, RoomRole role, int drop, bool main)
            {
                var doors = Enumerable.Range(0, from.Doors.Count).Where(i => !from.UsedDoors.Contains(i)).ToList();
                Dice.Shuffle(doors);
                foreach (int di in doors)
                {
                    for (int tries = 0; tries < 6; tries++)
                    {
                        var shape = ShapeFor(role);
                        var result = TryCorridorAndRoom(from, di, shape, drop, main);
                        if (result != null) { from.UsedDoors.Add(di); return result; }
                    }
                }
                return null;
            }

            /// <summary>A straight dead-end stash off a free door of <paramref name="from"/>; false if none fits.</summary>
            public bool TryStash(PlacedRoom from)
            {
                var doors = Enumerable.Range(0, from.Doors.Count).Where(i => !from.UsedDoors.Contains(i)).ToList();
                Dice.Shuffle(doors);
                foreach (int di in doors)
                {
                    var (doorCell, side) = from.Doors[di];
                    temp.Clear();
                    var cells = new List<Cell>();
                    var prev = doorCell;
                    bool ok = true;
                    for (int i = 0; i < Recipe.StashLength && ok; i++)
                    {
                        var next = prev.Step(side);
                        if (!Free(next, prev)) ok = false;
                        else { temp.Add(next); cells.Add(next); prev = next; }
                    }
                    temp.Clear();
                    if (!ok) continue;
                    from.UsedDoors.Add(di);
                    var stash = new Stash { Room = doorCell, Side = side };
                    Plan.Open.Add(Edge.Of(doorCell, side));
                    Plan.Doorways.Add(Edge.Of(doorCell, side));
                    prev = doorCell;
                    foreach (var c in cells)
                    {
                        Plan.Cells[c] = new CellInfo { Kind = CellKind.Corridor, Room = -1, Floor = from.Floor, Rows = 1 };
                        Plan.CorridorCells.Add(c);
                        if (!prev.Equals(doorCell)) Plan.Open.Add(Edge.Of(prev, side));
                        stash.Cells.Add(c);
                        prev = c;
                    }
                    Plan.Stashes.Add(stash);
                    return true;
                }
                return false;
            }

            PlacedRoom TryCorridorAndRoom(PlacedRoom from, int doorIndex, RoomShape shape, int drop, bool main)
            {
                var (doorCell, side) = from.Doors[doorIndex];
                temp.Clear();
                var path = new List<(Cell cell, CellInfo info)>();
                var dir = side;
                var prev = doorCell;
                float h = from.Floor;
                int flat = Dice.Range(Recipe.CorridorMin, Recipe.CorridorMax + 1);
                int ramps = drop;
                int total = flat + ramps;
                bool lastWasRamp = false;
                int straight = 0;
                for (int i = 0; i < total + 3; i++)
                {
                    bool mustStop = i >= total;
                    // Turn only from a flat cell, after at least one straight step, never into the first cell.
                    if (i > 0 && !lastWasRamp && straight >= 1 && Dice.Chance(Recipe.TurnChance))
                    {
                        dir = Dice.Chance(0.5) ? dir.Left() : dir.Right();
                        if (dir == side.Opposite()) dir = dir.Opposite();
                        straight = 0;
                    }
                    var next = prev.Step(dir);
                    if (!Free(next, prev)) { temp.Clear(); return null; }
                    bool ramp = ramps > 0 && i > 0;
                    CellInfo info;
                    if (ramp)
                    {
                        h -= DungeonPlan.RampRise;
                        info = new CellInfo { Kind = CellKind.Ramp, Room = -1, Floor = h, RampUp = dir.Opposite(), Rows = 1 };
                        ramps--;
                    }
                    else info = new CellInfo { Kind = CellKind.Corridor, Room = -1, Floor = h, Rows = 1 };
                    temp.Add(next);
                    path.Add((next, info));
                    prev = next;
                    lastWasRamp = ramp;
                    straight++;
                    if (i + 1 >= total && ramps == 0 && !lastWasRamp)
                    {
                        var room = TryRoomAt(prev, dir, shape, h, from.Level + (drop > 0 ? 1 : 0), main);
                        if (room != null)
                        {
                            CommitCorridor(from, doorIndex, path);
                            Commit(room);
                            int rd = room.Doors.FindIndex(x => x.cell.Equals(prev.Step(dir)) && x.side == dir.Opposite());
                            room.UsedDoors.Add(rd);
                            Plan.Open.Add(Edge.Of(prev, dir));
                            Plan.Doorways.Add(Edge.Of(prev, dir));
                            temp.Clear();
                            return room;
                        }
                        if (mustStop && i > total + 1) break;
                    }
                }
                temp.Clear();
                return null;
            }

            PlacedRoom TryRoomAt(Cell end, Dir move, RoomShape shape, float floor, int level, bool main)
            {
                var doorCellWorld = end.Step(move);
                var want = move.Opposite();
                var options = new List<(int door, int rot)>();
                for (int j = 0; j < shape.Doors.Count; j++)
                    for (int r = 0; r < 4; r++)
                        if (shape.Doors[j].side.Rotate(r) == want) options.Add((j, r));
                Dice.Shuffle(options);
                foreach (var (j, r) in options)
                {
                    var origin = doorCellWorld - shape.Doors[j].cell.Rotate(r);
                    var room = NewRoom(shape, origin, r, floor, level, main);
                    var set = new HashSet<Cell>(room.Cells);
                    bool ok = true;
                    foreach (var c in room.Cells)
                    {
                        if (!Free(c, c.Equals(doorCellWorld) ? end : c, null, set)) { ok = false; break; }
                    }
                    if (ok) return room;
                }
                return null;
            }

            void CommitCorridor(PlacedRoom from, int doorIndex, List<(Cell cell, CellInfo info)> path)
            {
                var (doorCell, side) = from.Doors[doorIndex];
                Plan.Open.Add(Edge.Of(doorCell, side));
                Plan.Doorways.Add(Edge.Of(doorCell, side));
                var prev = doorCell;
                foreach (var (cell, info) in path)
                {
                    Plan.Cells[cell] = info;
                    Plan.CorridorCells.Add(cell);
                    foreach (var d in Dirs.All) if (prev.Step(d).Equals(cell)) Plan.Open.Add(Edge.Of(prev, d));
                    prev = cell;
                }
            }
        }

        static DungeonPlan TryGenerate(int seed, int attempt, DungeonRecipe recipe)
        {
            var dice = new Dice(unchecked(seed * 7919 + attempt * 104729));
            var plan = new DungeonPlan { Seed = seed };
            var sizeDice = new Dice(seed);
            plan.Large = sizeDice.Chance(recipe.LargeChance);
            var path = plan.Large ? (sizeDice.Chance(recipe.ThreeLevelChance) ? recipe.LargePath3 : recipe.LargePath2) : recipe.SmallPath;
            var b = new Builder { Plan = plan, Dice = dice, Recipe = recipe };

            var current = b.PlaceEntrance(b.ShapeFor(path[0]));
            int pendingDrop = 0;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i] == DungeonRecipe.Descend) { pendingDrop += recipe.LevelDrop; continue; }
                var next = b.Extend(current, path[i], pendingDrop, main: true);
                if (next == null) return null;
                pendingDrop = 0;
                current = next;
            }
            plan.Goal = current;
            plan.Levels = plan.Rooms.Max(r => r.Level) + 1;

            var branches = plan.Large ? recipe.LargeBranches : (dice.Chance(recipe.SmallBranchChance) ? recipe.SmallBranches : new List<RoomRole>());
            foreach (var role in branches)
            {
                // Side rooms hang off main-path rooms other than the entrance and the goal; a farm prefers lower levels.
                var hosts = plan.Rooms.Where(r => r.OnMainPath && r != plan.Entrance && r != plan.Goal && r.UsedDoors.Count < r.Doors.Count).ToList();
                if (role == RoomRole.Farm && hosts.Any(r => r.Level > 0)) hosts = hosts.Where(r => r.Level > 0).ToList();
                dice.Shuffle(hosts);
                foreach (var host in hosts)
                    if (b.Extend(host, role, 0, main: false) != null) break;
            }

            // Hidden stashes off any chamber but the entrance with a door to spare.
            int stashes = dice.Range(recipe.StashMin, recipe.StashMax + 1);
            for (int s = 0; s < stashes; s++)
            {
                var hosts = plan.Rooms.Where(r => r != plan.Entrance && r.UsedDoors.Count < r.Doors.Count).ToList();
                dice.Shuffle(hosts);
                foreach (var host in hosts)
                    if (b.TryStash(host)) break;
            }
            return plan;
        }
    }
}
