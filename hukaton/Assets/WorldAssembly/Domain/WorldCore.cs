using System;

namespace Hakaton.WorldAssembly
{
    /// <summary>Grid side, clockwise from north. North is +Y on the chunk grid (+Z in the scene).</summary>
    public enum Side { North = 0, East = 1, South = 2, West = 3 }

    /// <summary>What crosses the middle of a chunk edge.</summary>
    public enum Mid { None = 0, River = 1, Canyon = 2, MazeOpen = 3, MazeWall = 4 }

    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int X, Y;
        public ChunkCoord(int x, int y) { X = x; Y = y; }
        public ChunkCoord Step(Side side) => side switch
        {
            Side.North => new ChunkCoord(X, Y + 1),
            Side.East => new ChunkCoord(X + 1, Y),
            Side.South => new ChunkCoord(X, Y - 1),
            _ => new ChunkCoord(X - 1, Y),
        };
        public bool Equals(ChunkCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663);
        public override string ToString() => $"({X}, {Y})";
        public static ChunkCoord operator +(ChunkCoord a, ChunkCoord b) => new ChunkCoord(a.X + b.X, a.Y + b.Y);
    }

    public static class Sides
    {
        public static readonly Side[] All = { Side.North, Side.East, Side.South, Side.West };
        public static Side Opposite(this Side side) => (Side)(((int)side + 2) & 3);
        public static Side Rotate(this Side side, int quarterTurnsClockwise) => (Side)(((int)side + quarterTurnsClockwise) & 3);

        /// <summary>Whether two edge middles may face each other (A on one chunk, B on its neighbour).</summary>
        public static bool Compatible(Mid a, Mid b)
        {
            if (a == b) return true;
            bool AllowsNone(Mid m) => m == Mid.None || m == Mid.MazeOpen || m == Mid.MazeWall;
            return (a == Mid.None && AllowsNone(b)) || (b == Mid.None && AllowsNone(a));
        }
    }

    /// <summary>Stateless hashing: every random decision is a function of the seed and coordinates only.</summary>
    public static class Hash
    {
        public static uint Of(int seed, int a, int b = 0, int c = 0, int d = 0)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u + 0x7F4A7C15u;
                h = Mix(h ^ (uint)a * 0x85EBCA6Bu);
                h = Mix(h ^ (uint)b * 0xC2B2AE35u);
                h = Mix(h ^ (uint)c * 0x27D4EB2Fu);
                h = Mix(h ^ (uint)d * 0x165667B1u);
                return h;
            }
        }

        static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public static double Unit(int seed, int a, int b = 0, int c = 0, int d = 0) => Of(seed, a, b, c, d) / 4294967296.0;

        public static int Range(int seed, int max, int a, int b = 0, int c = 0, int d = 0) => max <= 0 ? 0 : (int)(Of(seed, a, b, c, d) % (uint)max);
    }

    /// <summary>Salts so different decisions on the same coordinates do not correlate.</summary>
    public static class Salt
    {
        public const int Noise = 11, Noise2 = 12, RiverBorder = 21, CanyonBorder = 22, RiverPair = 23, InnerRiver = 24,
            Path = 25, Maze = 31, MazeDfs = 32, MazeLoop = 33, MazeEntrance = 34, Variant = 41, Crossing = 42, Rotation = 43,
            Creature = 51, Loot = 52, Ramp = 53, EndKind = 54, Scene = 61, Landmark = 71, Road = 81, Forest = 91,
            ChestPresence = 101, ChestTier = 102, ChestSpot = 103;
    }
}
