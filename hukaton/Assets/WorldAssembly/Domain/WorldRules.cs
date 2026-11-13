using System;
using System.Collections.Generic;

namespace Hakaton.WorldAssembly
{
    /// <summary>
    /// Tuning of the endless world: sizes, rings and how often each form and variant appears per ring.
    /// Defaults are the spec «Открытый мир v2» (Kayospace pg_open-world-v2); everything here is for playtest.
    /// </summary>
    public sealed class WorldRules
    {
        public float ChunkSize = 16f;
        /// <summary>Ring 0 ends here (metres from the start, chunk centre).</summary>
        public float InnerRadius = 64f;
        /// <summary>Ring 1 ends here; ring 2 goes on forever.</summary>
        public float MiddleRadius = 112f;
        /// <summary>Corners closer than this stay low, so every ring 0 chunk is flat.</summary>
        public float FlatRadius = 76f;
        /// <summary>Large forms are planned per district of this many chunks a side.</summary>
        public int District = 8;

        /// <summary>Patches of rotten swamp (<see cref="BiomeField"/>); off: the whole world is forest.</summary>
        public bool Swamp = true;
        /// <summary>Swamp noise feature size, in chunks: bigger cells, bigger patches.</summary>
        public int SwampNoiseCell = 7;
        /// <summary>No swamp nearer than this to the start (metres, chunk centre); its share grows out to SwampFull.</summary>
        public float SwampFrom = 72f, SwampFull = 176f;
        /// <summary>Swamp noise above this makes swamp: near (at SwampFrom) and far (SwampFull and beyond). About a seventh
        /// of ring 1 and over a quarter of ring 2.</summary>
        public double SwampThresholdNear = .68, SwampThresholdFar = .6;

        public float PlateauHeight = 6f;
        /// <summary>Rain fronts and thunderstorms (<see cref="WorldWeather"/>).</summary>
        public WeatherRules Weather = new WeatherRules();
        public float CanyonDepth = 5f;

        /// <summary>
        /// Corner noise above these lifts a corner one level (6 m) more, per ring: ring 0 stays low (but for the start),
        /// ring 1 rises to two levels, ring 2 to three. Neighbouring corners never differ by more than one level.
        /// </summary>
        public double[][] LevelThresholds =
        {
            new double[0],
            new[] { .6, .74 },
            new[] { .55, .67, .79 },
        };
        /// <summary>The start area (3 × 3 chunks round the centre) stands on this level.</summary>
        public int StartLevel = 1;
        /// <summary>Noise feature size, in corners.</summary>
        public int NoiseCell = 6;

        /// <summary>Chance that a river crosses a district border, per ring of the border.</summary>
        public double[] RiverCrossing = { .6, .8, .8 };
        public double[] CanyonCrossing = { 0, .4, .5 };
        /// <summary>Chance of a river that starts and ends inside a district without crossings.</summary>
        public double[] InnerRiver = { .2, .12, .08 };
        public double[] Maze = { .5, .85, .85 };
        /// <summary>Chance that a district away from the start grows a giant tangled forest instead of its first maze.</summary>
        public double[] Forest = { 0, .5, .55 };
        /// <summary>Share of a forest's ordinary cells with deadfall closing a blind alcove (a reward behind), and with a hollow trunk across the lane.</summary>
        public double ForestDeadfall = .18, ForestHollow = .2;

        /// <summary>A bridge or ford every N river chunks along the river (0 = by chance only), per ring.</summary>
        public int[] RiverCrossingEvery = { 2, 4, 0 };
        public double FarRiverCrossingChance = .1;
        /// <summary>A log bridge or descent every N canyon chunks, per ring.</summary>
        public int[] CanyonCrossingEvery = { 2, 3, 0 };
        public double FarCanyonCrossingChance = .12;
        /// <summary>Chance that a straight cliff is a ramp, per ring; summit cliffs use the last value.</summary>
        public double[] RampChance = { .7, .25, .05 };
        /// <summary>Ramps between the first and second level are this much rarer; above that almost none.</summary>
        public double UpperRampFactor = .5;
        public double SummitRampChance = .02;
        /// <summary>Chance that a straight cliff without a ramp has a ledge (Impulse I twice).</summary>
        public double LedgeChance = .35;
        /// <summary>A straight cliff without ramp or ledge has water falling over it into a pool at its foot.</summary>
        public double FallsChance = .3;

        /// <summary>Chance of a creature per chunk of each habitat, per ring.</summary>
        public double[] CreatureChance = { .12, .2, .33 };
        /// <summary>Peaceful and timid creatures (<see cref="CreatureThemes.IsCalm"/>) come this many times over: each
        /// of their groups is repeated at other spots of the chunk.</summary>
        public int CalmCrowd = 3;
        /// <summary>Chance of a beast brawl on a chunk where one fits (roomy flat scenes) by ring; any ring.</summary>
        public double[] BrawlChance = { .12, .12, .165 };
        /// <summary>Chance of a payphone or a television on a chunk where one fits, by ring: very rare, never in ring 0.</summary>
        public double[] OddityChance = { 0, .015, .015 };
        public double[] FindChance = { .35, .45, .5 };
        public double[] SceneFindChance = { .6, .65, .7 };
        public double[] SecondMaze = { 0, .5, .6 };
        /// <summary>Chance of a landmark (2 × 2 chunks) in a district, and of a second one, per ring.</summary>
        public double[] Landmark = { .6, .85, .85 };
        public double[] SecondLandmark = { 0, .45, .5 };

        public int RingOf(ChunkCoord c)
        {
            double d = Math.Sqrt((double)c.X * c.X + (double)c.Y * c.Y) * ChunkSize;
            return d < InnerRadius ? 0 : d < MiddleRadius ? 1 : 2;
        }

        public int RingAt(double metresX, double metresY)
        {
            double d = Math.Sqrt(metresX * metresX + metresY * metresY);
            return d < InnerRadius ? 0 : d < MiddleRadius ? 1 : 2;
        }

        /// <summary>The start overlook and its neighbours stay plain.</summary>
        public static bool IsStartArea(ChunkCoord c) => Math.Abs(c.X) <= 1 && Math.Abs(c.Y) <= 1;
    }

    /// <summary>
    /// Heights at the corners of the chunk grid: 0 low, 1 plateau, 2 summit. A function of the seed and the corner
    /// only, so neighbours always agree. Corner (i, j) is the south-west corner of chunk (i, j).
    /// Neighbouring corners never differ by more than one level.
    /// </summary>
    public sealed class CornerField
    {
        readonly int seed;
        readonly WorldRules rules;
        readonly Dictionary<(int, int), int> levels = new Dictionary<(int, int), int>();
        readonly int maxLevel;
        readonly BiomeField biomes;

        public CornerField(int seed, WorldRules rules, BiomeField biomes = null)
        {
            this.seed = seed;
            this.rules = rules;
            this.biomes = biomes;
            foreach (var t in rules.LevelThresholds) maxLevel = Math.Max(maxLevel, t.Length);
            maxLevel = Math.Max(maxLevel, rules.StartLevel);
        }

        /// <summary>
        /// Level of a corner: the level its noise asks for, lowered so that no two neighbouring corners (diagonals
        /// too) differ by more than one — every chunk then has at most two levels, which the cliff shapes cover.
        /// A pure function of the coordinate (the lower envelope of the wanted levels), cached.
        /// </summary>
        public int Level(int i, int j)
        {
            if (levels.TryGetValue((i, j), out int cached)) return cached;
            int level = Wanted(i, j);
            for (int r = 1; r <= maxLevel && level > r - 1; r++)
                for (int di = -r; di <= r; di++)
                    for (int dj = -r; dj <= r; dj++)
                    {
                        if (Math.Max(Math.Abs(di), Math.Abs(dj)) != r) continue;
                        level = Math.Min(level, Wanted(i + di, j + dj) + r);
                    }
            levels[(i, j)] = level;
            return level;
        }

        public void Corners(ChunkCoord c, int[] result)
        {
            result[0] = Level(c.X, c.Y + 1);
            result[1] = Level(c.X + 1, c.Y + 1);
            result[2] = Level(c.X + 1, c.Y);
            result[3] = Level(c.X, c.Y);
        }

        public bool IsFlatLow(ChunkCoord c) =>
            Level(c.X, c.Y) == 0 && Level(c.X + 1, c.Y) == 0 && Level(c.X, c.Y + 1) == 0 && Level(c.X + 1, c.Y + 1) == 0;

        /// <summary>Corners of the start area (chunks −1…1 round the centre).</summary>
        public static bool IsStartCorner(int i, int j) => i >= -1 && i <= 2 && j >= -1 && j <= 2;

        int Wanted(int i, int j)
        {
            if (IsStartCorner(i, j)) return rules.StartLevel;
            // The swamp is a basin: every corner of a swamp chunk stays low; the slopes round it are forest.
            if (biomes != null && (biomes.IsSwamp(new ChunkCoord(i, j)) || biomes.IsSwamp(new ChunkCoord(i - 1, j)) ||
                                   biomes.IsSwamp(new ChunkCoord(i, j - 1)) || biomes.IsSwamp(new ChunkCoord(i - 1, j - 1)))) return 0;
            double x = (i - .5) * rules.ChunkSize, y = (j - .5) * rules.ChunkSize;
            if (Math.Sqrt(x * x + y * y) < rules.FlatRadius) return 0;
            var thresholds = rules.LevelThresholds[rules.RingAt(x, y)];
            double noise = Noise(i, j);
            int level = 0;
            while (level < thresholds.Length && noise > thresholds[level]) level++;
            return level;
        }

        double Noise(int i, int j) => (Value(i, j, rules.NoiseCell, Salt.Noise) * 2 + Value(i, j, Math.Max(1, rules.NoiseCell / 2), Salt.Noise2)) / 3;

        double Value(int i, int j, int cell, int salt)
        {
            int gx = FloorDiv(i, cell), gy = FloorDiv(j, cell);
            double fx = (i - gx * cell) / (double)cell, fy = (j - gy * cell) / (double)cell;
            double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            double a = Hash.Unit(seed, salt, gx, gy), b = Hash.Unit(seed, salt, gx + 1, gy);
            double c = Hash.Unit(seed, salt, gx, gy + 1), d = Hash.Unit(seed, salt, gx + 1, gy + 1);
            return (a * (1 - sx) + b * sx) * (1 - sy) + (c * (1 - sx) + d * sx) * sy;
        }

        internal static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
