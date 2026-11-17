using System.Collections.Generic;

namespace Hakaton.Dungeons
{
    /// <summary>What a chamber is for. Each dungeon kind has hand-made chambers for some of these roles.</summary>
    public enum RoomRole { Entrance = 0, Junction = 1, Barracks = 2, Store = 3, Nursery = 4, Farm = 5, Gallery = 6, Queen = 7 }

    /// <summary>
    /// A hand-made chamber's footprint as seen by the planner: its cells (in its own frame, entry towards south),
    /// the edges where a corridor may join it, and how many 4 m wall rows tall it is.
    /// </summary>
    public sealed class RoomShape
    {
        public string Id;
        public RoomRole Role;
        public List<Cell> Cells = new List<Cell>();
        /// <summary>Openings: a room cell and the side a corridor may leave through.</summary>
        public List<(Cell cell, Dir side)> Doors = new List<(Cell, Dir)>();
        /// <summary>Wall rows (4 m each). Halls are 2.</summary>
        public int Rows = 1;
        /// <summary>The entrance chamber's way back to the surface (a cell and the side it opens to).</summary>
        public (Cell cell, Dir side)? WayOut;
    }

    /// <summary>A chamber laid in the dungeon: shape, where its origin cell went, how it was turned and how deep it is.</summary>
    public sealed class PlacedRoom
    {
        public int Index;
        public RoomShape Shape;
        public Cell Origin;
        public int Rotation;           // quarter turns clockwise
        public float Floor;            // metres, 0 at the entrance, negative downwards
        public int Level;              // 0 = first floor
        public bool OnMainPath;
        public readonly List<Cell> Cells = new List<Cell>();
        public readonly List<(Cell cell, Dir side)> Doors = new List<(Cell, Dir)>();
        public readonly HashSet<int> UsedDoors = new HashSet<int>();

        public Cell ToWorld(Cell local) => local.Rotate(Rotation) + Origin;
        public Dir ToWorld(Dir local) => local.Rotate(Rotation);
    }

    /// <summary>Passage: the short dark tunnel behind the entrance chamber's way out (the way back to the surface).</summary>
    public enum CellKind { Room = 0, Corridor = 1, Ramp = 2, Passage = 3 }

    public struct CellInfo
    {
        public CellKind Kind;
        /// <summary>Index into <see cref="DungeonPlan.Rooms"/>, or -1 for corridors.</summary>
        public int Room;
        /// <summary>Floor height in metres; for a ramp, the low end.</summary>
        public float Floor;
        /// <summary>For a ramp: the side towards which the floor rises (by <see cref="DungeonPlan.RampRise"/>).</summary>
        public Dir RampUp;
        public int Rows;
    }

    /// <summary>
    /// The whole layout of one dungeon: every cell (rooms, corridors, ramps), which edges are open (walkable through),
    /// and where the way in, the boss and the way out are. Edges between a dungeon cell and anything else are walls.
    /// No two unconnected cells ever touch, so a wall always has open space on one side only.
    /// </summary>
    public sealed class DungeonPlan
    {
        public const float CellSize = 4f;
        public const float RampRise = 1f;

        public int Seed;
        public bool Large;
        public int Levels;
        public readonly Dictionary<Cell, CellInfo> Cells = new Dictionary<Cell, CellInfo>();
        public readonly HashSet<Edge> Open = new HashSet<Edge>();
        /// <summary>Open edges between a room and a corridor: they get a doorway frame.</summary>
        public readonly HashSet<Edge> Doorways = new HashSet<Edge>();
        public readonly List<PlacedRoom> Rooms = new List<PlacedRoom>();
        /// <summary>Cells of the corridors in laying order (for dressing).</summary>
        public readonly List<Cell> CorridorCells = new List<Cell>();
        /// <summary>Cells of the way-out passage, from the entrance chamber outwards (dead end at the last one).</summary>
        public readonly List<Cell> Passage = new List<Cell>();
        /// <summary>The entrance chamber's cell and side the passage leaves through (a doorway).</summary>
        public (Cell cell, Dir side)? PassageMouth;
        /// <summary>Cells in the way-out passage.</summary>
        public const int PassageLength = 2;

        public PlacedRoom Entrance => Rooms.Count > 0 ? Rooms[0] : null;
        public PlacedRoom Boss => Rooms.Find(r => r.Shape.Role == RoomRole.Queen);
        /// <summary>The room at the end of the main path (the queen in a large dungeon, a store in a small one).</summary>
        public PlacedRoom Goal;

        public bool IsOpen(Cell c, Dir d) => Open.Contains(Edge.Of(c, d));
        public bool IsDoorway(Cell c, Dir d) => Doorways.Contains(Edge.Of(c, d));

        /// <summary>Floor height where a walker crosses from <paramref name="c"/> towards <paramref name="d"/>.</summary>
        public float EdgeFloor(Cell c, Dir d)
        {
            var info = Cells[c];
            if (info.Kind != CellKind.Ramp) return info.Floor;
            if (d == info.RampUp) return info.Floor + RampRise;
            if (d == info.RampUp.Opposite()) return info.Floor;
            return info.Floor + RampRise / 2;
        }
    }
}
