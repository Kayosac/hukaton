using System;
using System.Collections.Generic;
using System.Linq;

namespace Hakaton.WorldAssembly
{
    /// <summary>
    /// Lays out one outing's valley from a seed. The start overlook sits at (0, 0) looking north; chunks grow
    /// in front of it as one compact connected blob, so every chunk is walkable from the start through shared
    /// standard edges. The same seed and catalogue always give the same valley.
    /// Rules: no type above its MaxCount; every required tag present; neighbours avoid repeating a type when
    /// the bag allows it.
    /// </summary>
    public static class LayoutGenerator
    {
        public static WorldLayout Generate(int seed, IReadOnlyList<ChunkType> types, LayoutRules rules)
        {
            if (types == null || types.Count == 0) throw new ArgumentException("The catalogue is empty.", nameof(types));
            rules ??= new LayoutRules();
            var random = new Random(seed);
            var start = new Cell(0, 0);

            var cells = GrowCells(random, start, rules);
            var bag = FillBag(random, types, rules, cells.Count);
            var placements = Assign(random, types, cells, bag);
            return new WorldLayout(seed, start, placements);
        }

        /// <summary>Grows the blob one cell at a time, favouring cells that touch more of it (compact, few corridors).</summary>
        static List<Cell> GrowCells(Random random, Cell start, LayoutRules rules)
        {
            int capacity = (2 * rules.HalfWidth + 1) * rules.Depth;
            int target = Math.Min(Math.Max(0, rules.ChunkCount), capacity);
            var taken = new HashSet<Cell> { start };
            var order = new List<Cell>();
            var frontier = new List<Cell>();
            var weights = new List<int>();

            while (order.Count < target)
            {
                frontier.Clear();
                weights.Clear();
                foreach (var cell in taken)
                    foreach (var side in Cell.Sides)
                    {
                        var next = cell.Step(side);
                        if (taken.Contains(next) || frontier.Contains(next) || !Inside(next, rules)) continue;
                        // The overlook only opens forward: nothing beside or behind it.
                        if (next.Y == 0) continue;
                        int touching = Cell.Sides.Count(s => taken.Contains(next.Step(s)));
                        frontier.Add(next);
                        weights.Add(touching * touching);
                    }
                if (frontier.Count == 0) break;
                var chosen = frontier[Pick(random, weights)];
                taken.Add(chosen);
                order.Add(chosen);
            }
            return order;
        }

        static bool Inside(Cell cell, LayoutRules rules) => Math.Abs(cell.X) <= rules.HalfWidth && cell.Y >= 0 && cell.Y <= rules.Depth;

        /// <summary>Which chunks this outing gets: first one per required tag, then weighted picks within MaxCount.</summary>
        static List<ChunkType> FillBag(Random random, IReadOnlyList<ChunkType> types, LayoutRules rules, int size)
        {
            var bag = new List<ChunkType>();
            var counts = new Dictionary<ChunkType, int>();
            bool Room(ChunkType t) => (counts.TryGetValue(t, out int n) ? n : 0) < t.MaxCount;
            void Take(ChunkType t) { bag.Add(t); counts[t] = (counts.TryGetValue(t, out int n) ? n : 0) + 1; }

            foreach (var tag in rules.RequiredTags ?? Array.Empty<string>())
            {
                if (bag.Count >= size || bag.Any(t => t.Has(tag))) continue;
                var options = types.Where(t => t.Has(tag) && Room(t) && t.Weight > 0).ToList();
                if (options.Count > 0) Take(options[Pick(random, options.Select(t => t.Weight).ToList())]);
            }
            while (bag.Count < size)
            {
                var options = types.Where(t => Room(t) && t.Weight > 0).ToList();
                if (options.Count == 0) break;
                Take(options[Pick(random, options.Select(t => t.Weight).ToList())]);
            }
            // Shuffle so the required ones do not always sit next to the start.
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            return bag;
        }

        /// <summary>Puts the bag on the grown cells in growth order, avoiding a type next to itself where possible.</summary>
        static List<ChunkPlacement> Assign(Random random, IReadOnlyList<ChunkType> types, List<Cell> cells, List<ChunkType> bag)
        {
            var placed = new Dictionary<Cell, string>();
            var result = new List<ChunkPlacement>();
            int count = Math.Min(cells.Count, bag.Count);
            for (int i = 0; i < count; i++)
            {
                var cell = cells[i];
                int choice = 0;
                for (int k = 0; k < bag.Count; k++)
                {
                    var id = bag[k].Id;
                    bool repeats = Cell.Sides.Any(s => placed.TryGetValue(cell.Step(s), out var other) && other == id);
                    if (!repeats) { choice = k; break; }
                }
                var type = bag[choice];
                bag.RemoveAt(choice);
                int rotation = type.Rotatable ? random.Next(4) : 0;
                placed[cell] = type.Id;
                result.Add(new ChunkPlacement(type.Id, cell, rotation));
            }
            return result;
        }

        static int Pick(Random random, IList<int> weights)
        {
            int total = 0;
            foreach (int w in weights) total += Math.Max(0, w);
            if (total <= 0) return random.Next(weights.Count);
            int roll = random.Next(total);
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= Math.Max(0, weights[i]);
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }
    }
}
