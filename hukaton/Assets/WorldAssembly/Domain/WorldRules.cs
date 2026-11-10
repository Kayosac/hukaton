using System;

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
        public float InnerRadius = 48f;
        /// <summary>Ring 1 ends here; ring 2 goes on forever.</summary>
        public float MiddleRadius = 112f;
        /// <summary>Corners closer than this stay low, so every ring 0 chunk is flat.</summary>
        public float FlatRadius = 60f;
        /// <summary>Large forms are planned per district of this many chunks a side.</summary>
        public int District = 8;

        public float PlateauHeight = 6f;
        public float CanyonDepth = 5f;

        /// <summary>Corner noise above this lifts the corner to the plateau, per ring (ring 0 unused: flat).</summary>
        public double[] PlateauThreshold = { 2, .6, .55 };
        /// <summary>Corner noise above this lifts a plateau corner to the summit (ring 2 only).</summary>
        public double SummitThreshold = .76;
        /// <summary>Noise feature size, in corners.</summary>
        public int NoiseCell = 6;

        /// <summary>Chance that a river crosses a district border, per ring of the border.</summary>
        public double[] RiverCrossing = { .6, .8, .8 };
        public double[] CanyonCrossing = { 0, .4, .5 };
        /// <summary>Chance of a river that starts and ends inside a district without crossings.</summary>
        public double[] InnerRiver = { .2, .12, .08 };
        public double[] Maze = { .5, .85, .85 };

        /// <summary>A bridge or ford every N river chunks along the river (0 = by chance only), per ring.</summary>
        public int[] RiverCrossingEvery = { 2, 4, 0 };
        public double FarRiverCrossingChance = .1;
        /// <summary>A log bridge or descent every N canyon chunks, per ring.</summary>
        public int[] CanyonCrossingEvery = { 2, 3, 0 };
        public double FarCanyonCrossingChance = .12;
        /// <summary>Chance that a straight cliff is a ramp, per ring; summit cliffs use the last value.</summary>
        public double[] RampChance = { 1, .25, .05 };
        public double SummitRampChance = .02;
        /// <summary>Chance that a straight cliff without a ramp has a ledge (Impulse I twice).</summary>
        public double LedgeChance = .35;

        /// <summary>Chance of a creature per chunk of each habitat, per ring.</summary>
        public double[] CreatureChance = { .12, .2, .33 };
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

        public CornerField(int seed, WorldRules rules) { this.seed = seed; this.rules = rules; }

        public int Level(int i, int j)
        {
            if (!Raised(i, j)) return 0;
            if (!SummitCandidate(i, j)) return 1;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if ((dx != 0 || dy != 0) && !Raised(i + dx, j + dy)) return 1;
            return 2;
        }

        /// <summary>Corner levels of a chunk, clockwise: NW, NE, SE, SW.</summary>
        public void Corners(ChunkCoord c, int[] result)
        {
            result[0] = Level(c.X, c.Y + 1);
            result[1] = Level(c.X + 1, c.Y + 1);
            result[2] = Level(c.X + 1, c.Y);
            result[3] = Level(c.X, c.Y);
        }

        public bool IsFlatLow(ChunkCoord c) =>
            Level(c.X, c.Y) == 0 && Level(c.X + 1, c.Y) == 0 && Level(c.X, c.Y + 1) == 0 && Level(c.X + 1, c.Y + 1) == 0;

        bool Raised(int i, int j)
        {
            double x = (i - .5) * rules.ChunkSize, y = (j - .5) * rules.ChunkSize;
            double d = Math.Sqrt(x * x + y * y);
            if (d < rules.FlatRadius) return false;
            int ring = rules.RingAt(x, y);
            return Noise(i, j) > rules.PlateauThreshold[ring];
        }

        bool SummitCandidate(int i, int j)
        {
            double x = (i - .5) * rules.ChunkSize, y = (j - .5) * rules.ChunkSize;
            return rules.RingAt(x, y) == 2 && Noise(i, j) > rules.SummitThreshold;
        }

        /// <summary>Two octaves of value noise in [0, 1).</summary>
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
