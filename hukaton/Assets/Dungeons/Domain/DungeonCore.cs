using System;
using System.Collections.Generic;

namespace Hakaton.Dungeons
{
    /// <summary>Grid direction, clockwise from north. North is +Y on the dungeon grid (+Z in the scene).</summary>
    public enum Dir { North = 0, East = 1, South = 2, West = 3 }

    public static class Dirs
    {
        public static readonly Dir[] All = { Dir.North, Dir.East, Dir.South, Dir.West };
        public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);
        public static Dir Rotate(this Dir d, int quarterTurnsClockwise) => (Dir)((((int)d + quarterTurnsClockwise) % 4 + 4) & 3);
        public static Dir Left(this Dir d) => d.Rotate(-1);
        public static Dir Right(this Dir d) => d.Rotate(1);
    }

    /// <summary>One square of the dungeon grid (<see cref="DungeonPlan.CellSize"/> metres on a side).</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public Cell Step(Dir d) => d switch
        {
            Dir.North => new Cell(X, Y + 1),
            Dir.East => new Cell(X + 1, Y),
            Dir.South => new Cell(X, Y - 1),
            _ => new Cell(X - 1, Y),
        };
        /// <summary>Rotates about the origin by quarter turns clockwise (north becomes east).</summary>
        public Cell Rotate(int quarterTurnsClockwise)
        {
            int k = ((quarterTurnsClockwise % 4) + 4) % 4;
            int x = X, y = Y;
            for (int i = 0; i < k; i++) { int t = x; x = y; y = -t; }
            return new Cell(x, y);
        }
        public bool Equals(Cell o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Cell o && Equals(o);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663);
        public override string ToString() => $"({X}, {Y})";
        public static Cell operator +(Cell a, Cell b) => new Cell(a.X + b.X, a.Y + b.Y);
        public static Cell operator -(Cell a, Cell b) => new Cell(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>An edge between a cell and its neighbour, stored once (from the cell with the smaller key).</summary>
    public readonly struct Edge : IEquatable<Edge>
    {
        public readonly Cell A; public readonly Dir Side;
        Edge(Cell a, Dir side) { A = a; Side = side; }
        public static Edge Of(Cell c, Dir d) => d == Dir.North || d == Dir.East ? new Edge(c, d) : new Edge(c.Step(d), d.Opposite());
        public bool Equals(Edge o) => A.Equals(o.A) && Side == o.Side;
        public override bool Equals(object obj) => obj is Edge o && Equals(o);
        public override int GetHashCode() => unchecked(A.GetHashCode() * 31 + (int)Side);
    }

    /// <summary>Stateless-seeded random numbers (System.Random is fine: plans are built once per entrance).</summary>
    public sealed class Dice
    {
        readonly Random random;
        public Dice(int seed) { random = new Random(seed); }
        public int Range(int minInclusive, int maxExclusive) => maxExclusive <= minInclusive ? minInclusive : random.Next(minInclusive, maxExclusive);
        public double Unit() => random.NextDouble();
        public bool Chance(double p) => random.NextDouble() < p;
        public T Pick<T>(IReadOnlyList<T> list) => list[random.Next(list.Count)];
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }
    }
}
