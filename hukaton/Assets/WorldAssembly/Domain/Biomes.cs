using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>What kind of land a chunk belongs to. Forest is everything that is not a patch of something else.</summary>
    public enum Biome { Forest = 0, Swamp = 1 }

    /// <summary>
    /// The parts of the rotten swamp (Kayospace «Биом: Гнилая топь», подзоны): the mixed mire; the reach (open water,
    /// far views, lilies); the drowned wood (trees close together, dark and foggy); the moss fields (more land than
    /// water, hummocks, young pines); the burnt swamp (charred trunks, ash on the water, smouldering peat).
    /// </summary>
    public enum SwampZone { Mire = 0, Reach = 1, Drowned = 2, Moss = 3, Burnt = 4 }

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

        /// <summary>The corner field of the same world: the swamp's water lies only where the ground is level.</summary>
        public CornerField Corners;

        /// <summary>
        /// The swamp is water with islands, not land with ponds: a chunk is swamp when one of its corners lies inside the
        /// patch (<see cref="Inside"/>). Where all four corners are under water (<see cref="Wet"/>) it is open swamp;
        /// level chunks with some dry corners are its shores; the swamp rises one level in places, and the steps
        /// between are cliffs, dry.
        /// </summary>
        public Biome At(ChunkCoord c)
        {
            if (biomes.TryGetValue(c, out var cached)) return cached;
            var biome = Inside(c.X, c.Y) || Inside(c.X + 1, c.Y) || Inside(c.X, c.Y + 1) || Inside(c.X + 1, c.Y + 1) ? Biome.Swamp : Biome.Forest;
            biomes[c] = biome;
            return biome;
        }

        /// <summary>Corner (i, j) (the south-west corner of chunk (i, j)) lies inside the patch: all four chunks round it are swamp by the noise.</summary>
        public bool Inside(int i, int j) =>
            Trimmed(new ChunkCoord(i, j)) && Trimmed(new ChunkCoord(i - 1, j)) && Trimmed(new ChunkCoord(i, j - 1)) && Trimmed(new ChunkCoord(i - 1, j - 1));

        /// <summary>
        /// Corner (i, j) lies under the swamp's water: inside the patch, and the four chunks round it are level (no cliff
        /// touches the water), on the low ground or the one level up. Two chunks sharing an edge agree on it, so the
        /// water meets the land the same from both sides.
        /// </summary>
        public bool Wet(int i, int j)
        {
            if (!Inside(i, j)) return false;
            if (Corners == null) return true;
            int level = Corners.Level(i, j);
            for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                    if (Corners.Level(i + di, j + dj) != level) return false;
            return true;
        }

        /// <summary>Wet corners of a chunk clockwise NW, NE, SE, SW (as <see cref="CornerField.Corners"/>): 1 wet, 0 dry.</summary>
        public void WetCorners(ChunkCoord c, int[] result)
        {
            result[0] = Wet(c.X, c.Y + 1) ? 1 : 0;
            result[1] = Wet(c.X + 1, c.Y + 1) ? 1 : 0;
            result[2] = Wet(c.X + 1, c.Y) ? 1 : 0;
            result[3] = Wet(c.X, c.Y) ? 1 : 0;
        }

        /// <summary>
        /// Corner (i, j) lifts the swamp one level: deep inside a patch (two chunks of swamp all round, so its cliffs
        /// and their shores stay in the swamp) where the rise noise is high — broad terraces, not single bumps.
        /// </summary>
        public bool Raised(int i, int j)
        {
            // The rise is eroded by one corner, so no terrace is narrower than three corners (two chunks of top).
            for (int x = i - 1; x <= i + 1; x++)
                for (int y = j - 1; y <= j + 1; y++)
                    if (RiseNoise(x, y) <= rules.SwampRise) return false;
            for (int x = i - 3; x <= i + 2; x++)
                for (int y = j - 3; y <= j + 2; y++)
                    if (!Trimmed(new ChunkCoord(x, y))) return false;
            return true;
        }

        double RiseNoise(int i, int j)
        {
            int cell = Math.Max(1, rules.SwampNoiseCell);
            return (Value(i, j, cell, Salt.Biome + 2) * 2 + Value(i, j, Math.Max(1, cell / 2), Salt.Biome + 3)) / 3;
        }

        /// <summary>
        /// Which part of the swamp a chunk is in: the nearest of points jittered in cells of
        /// <see cref="WorldRules.SwampZoneCell"/> chunks, each a zone by its own hash (the mixed mire most often).
        /// Neighbouring chunks mostly share a zone, so the parts are broad and their borders crooked.
        /// </summary>
        public SwampZone Zone(ChunkCoord c)
        {
            int cell = Math.Max(1, rules.SwampZoneCell);
            int gx = CornerField.FloorDiv(c.X, cell), gy = CornerField.FloorDiv(c.Y, cell);
            double best = double.MaxValue;
            int bx = gx, by = gy;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    double px = (gx + i + Hash.Unit(seed, Salt.Biome + 4, gx + i, gy + j)) * cell;
                    double py = (gy + j + Hash.Unit(seed, Salt.Biome + 5, gx + i, gy + j)) * cell;
                    double d = (c.X + .5 - px) * (c.X + .5 - px) + (c.Y + .5 - py) * (c.Y + .5 - py);
                    if (d < best) { best = d; bx = gx + i; by = gy + j; }
                }
            double roll = Hash.Unit(seed, Salt.Biome + 6, bx, by);
            return roll < .3 ? SwampZone.Mire : roll < .48 ? SwampZone.Reach : roll < .66 ? SwampZone.Drowned : roll < .84 ? SwampZone.Moss : SwampZone.Burnt;
        }

        /// <summary>Open swamp: every corner under water.</summary>
        public bool Open(ChunkCoord c) => Wet(c.X, c.Y) && Wet(c.X + 1, c.Y) && Wet(c.X, c.Y + 1) && Wet(c.X + 1, c.Y + 1);

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
