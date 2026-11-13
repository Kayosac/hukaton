using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>What kind of land a chunk belongs to. Forest is everything that is not a patch of something else.</summary>
    public enum Biome { Forest = 0, Swamp = 1 }

    /// <summary>
    /// Where the biomes lie (Kayospace «Биом: Гнилая топь»): patches of rotten swamp of every size, from a few chunks
    /// to more than a district, from ring 1 outwards and more of them further out; never in ring 0 or at the start.
    /// A pure function of the seed and the chunk, so neighbours always agree. A swamp chunk is flat and low (the
    /// corner field presses its corners down), so the swamp is a basin rimmed by forested slopes.
    /// </summary>
    public sealed class BiomeField
    {
        readonly int seed;
        readonly WorldRules rules;
        readonly Dictionary<ChunkCoord, bool> raw = new Dictionary<ChunkCoord, bool>();
        readonly Dictionary<ChunkCoord, Biome> biomes = new Dictionary<ChunkCoord, Biome>();

        public BiomeField(int seed, WorldRules rules)
        {
            this.seed = seed;
            this.rules = rules;
        }

        public Biome At(ChunkCoord c)
        {
            if (biomes.TryGetValue(c, out var cached)) return cached;
            // A lone swamp chunk would read as a puddle: ragged tips are trimmed (swamp noise with swamp on two sides at
            // least), and what is left keeps only chunks with a trimmed neighbour, so no swamp chunk stands alone.
            var biome = Biome.Forest;
            if (Trimmed(c))
                foreach (var side in Sides.All)
                    if (Trimmed(c.Step(side))) { biome = Biome.Swamp; break; }
            biomes[c] = biome;
            return biome;
        }

        bool Trimmed(ChunkCoord c)
        {
            if (!Raw(c)) return false;
            int around = 0;
            foreach (var side in Sides.All) if (Raw(c.Step(side))) around++;
            return around >= 2;
        }

        public bool IsSwamp(ChunkCoord c) => At(c) == Biome.Swamp;

        /// <summary>Whether the swamp noise asks for swamp here, before lone chunks are dropped.</summary>
        bool Raw(ChunkCoord c)
        {
            if (raw.TryGetValue(c, out bool cached)) return cached;
            bool swamp = false;
            if (rules.Swamp && !WorldRules.IsStartArea(c))
            {
                double d = Math.Sqrt((double)c.X * c.X + (double)c.Y * c.Y) * rules.ChunkSize;
                if (d >= rules.SwampFrom)
                {
                    double t = Math.Min(1, Math.Max(0, (d - rules.SwampFrom) / Math.Max(1, rules.SwampFull - rules.SwampFrom)));
                    double threshold = rules.SwampThresholdNear + (rules.SwampThresholdFar - rules.SwampThresholdNear) * t;
                    swamp = Noise(c.X, c.Y) > threshold;
                }
            }
            raw[c] = swamp;
            return swamp;
        }

        /// <summary>Swamp noise in [0, 1]: large blobs with a finer ragged edge.</summary>
        public double Noise(int x, int y)
        {
            int cell = Math.Max(1, rules.SwampNoiseCell);
            int fine = Math.Max(1, (int)Math.Round(cell / 2.5));
            return (Value(x, y, cell, Salt.Biome) * 2 + Value(x, y, fine, Salt.Biome + 1)) / 3;
        }

        double Value(int i, int j, int cell, int salt)
        {
            int gx = CornerField.FloorDiv(i, cell), gy = CornerField.FloorDiv(j, cell);
            double fx = (i - gx * cell) / (double)cell, fy = (j - gy * cell) / (double)cell;
            double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            double a = Hash.Unit(seed, salt, gx, gy), b = Hash.Unit(seed, salt, gx + 1, gy);
            double c = Hash.Unit(seed, salt, gx, gy + 1), d = Hash.Unit(seed, salt, gx + 1, gy + 1);
            return (a * (1 - sx) + b * sx) * (1 - sy) + (c * (1 - sx) + d * sx) * sy;
        }
    }
}
