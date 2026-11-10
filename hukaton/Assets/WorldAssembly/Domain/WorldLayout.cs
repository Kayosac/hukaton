using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>Grid side of a chunk cell. North is +Y on the layout grid (+Z in the scene).</summary>
    public enum Side { North = 0, East = 1, South = 2, West = 3 }

    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public Cell Step(Side side) => side switch
        {
            Side.North => new Cell(X, Y + 1),
            Side.East => new Cell(X + 1, Y),
            Side.South => new Cell(X, Y - 1),
            _ => new Cell(X - 1, Y),
        };
        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => (X * 73856093) ^ (Y * 19349663);
        public override string ToString() => $"({X}, {Y})";
        public static readonly Side[] Sides = { Side.North, Side.East, Side.South, Side.West };
    }

    /// <summary>One kind of hand-made chunk as the layout sees it: no geometry, only the rules for placing it.</summary>
    public sealed class ChunkType
    {
        public readonly string Id;
        /// <summary>How many of this chunk one outing may hold.</summary>
        public readonly int MaxCount;
        /// <summary>Relative chance when filling free places.</summary>
        public readonly int Weight;
        /// <summary>What the chunk offers, e.g. "hunt", "explore", "fire", "ice".</summary>
        public readonly IReadOnlyList<string> Tags;
        /// <summary>All four edges are the standard edge, so the chunk may be turned by 90° steps.</summary>
        public readonly bool Rotatable;

        public ChunkType(string id, int maxCount, int weight, IReadOnlyList<string> tags, bool rotatable = true)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A chunk type needs an id.", nameof(id));
            Id = id;
            MaxCount = Math.Max(0, maxCount);
            Weight = Math.Max(0, weight);
            Tags = tags ?? Array.Empty<string>();
            Rotatable = rotatable;
        }

        public bool Has(string tag)
        {
            foreach (var t in Tags) if (t == tag) return true;
            return false;
        }
    }

    public sealed class LayoutRules
    {
        /// <summary>Chunks around the start overlook (the start itself not counted).</summary>
        public int ChunkCount = 7;
        /// <summary>Every outing holds at least one chunk with each of these tags.</summary>
        public IReadOnlyList<string> RequiredTags = Array.Empty<string>();
        /// <summary>Furthest a chunk may lie sideways from the start column; keeps the valley in front of the overlook.</summary>
        public int HalfWidth = 2;
        /// <summary>Furthest a chunk may lie ahead of the start row.</summary>
        public int Depth = 3;
    }

    public readonly struct ChunkPlacement
    {
        public readonly string TypeId;
        public readonly Cell Cell;
        /// <summary>Quarter turns clockwise seen from above, 0–3.</summary>
        public readonly int Rotation;
        public ChunkPlacement(string typeId, Cell cell, int rotation) { TypeId = typeId; Cell = cell; Rotation = rotation; }
        public override string ToString() => $"{TypeId} {Cell} r{Rotation}";
    }

    /// <summary>The valley of one outing: the start overlook at (0, 0) looking north and the chunks in front of it.</summary>
    public sealed class WorldLayout
    {
        public readonly int Seed;
        public readonly Cell Start;
        public readonly IReadOnlyList<ChunkPlacement> Chunks;
        readonly HashSet<Cell> occupied;

        public WorldLayout(int seed, Cell start, IReadOnlyList<ChunkPlacement> chunks)
        {
            Seed = seed;
            Start = start;
            Chunks = chunks;
            occupied = new HashSet<Cell> { start };
            foreach (var chunk in chunks) occupied.Add(chunk.Cell);
        }

        public bool IsOccupied(Cell cell) => occupied.Contains(cell);

        /// <summary>Edges facing nothing: each gets a closing border piece.</summary>
        public IEnumerable<(Cell cell, Side side)> OpenEdges()
        {
            foreach (var cell in occupied)
                foreach (var side in Cell.Sides)
                    if (!occupied.Contains(cell.Step(side)))
                        yield return (cell, side);
        }

        /// <summary>True when every chunk can be walked to from the start through shared edges.</summary>
        public bool IsConnected()
        {
            var seen = new HashSet<Cell> { Start };
            var queue = new Queue<Cell>();
            queue.Enqueue(Start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var side in Cell.Sides)
                {
                    var next = cell.Step(side);
                    if (occupied.Contains(next) && seen.Add(next)) queue.Enqueue(next);
                }
            }
            return seen.Count == occupied.Count;
        }

        public int Count(string typeId)
        {
            int n = 0;
            foreach (var chunk in Chunks) if (chunk.TypeId == typeId) n++;
            return n;
        }
    }
}
