using System;

namespace Hakaton.WorldAssembly
{
    /// <summary>Numbers of the endless world's weather (playtest).</summary>
    public sealed class WeatherRules
    {
        /// <summary>Size of a rain front, m.</summary>
        public double FrontCell = 240;
        /// <summary>The wind that drives the fronts over the land, m/s: one direction and speed per outing, from the seed.</summary>
        public double WindMin = 2.5, WindMax = 4.5;
        /// <summary>Seconds for the fronts to change their shape: they grow, break up and merge as they go.</summary>
        public double Evolve = 180;
        /// <summary>Seconds of a wet or a dry spell over the whole land.</summary>
        public double Spell = 200;
        /// <summary>Share of the land under rain at an average moment: in the middle ring and far out (the start stays dry).</summary>
        public double CoverMiddle = .18, CoverFar = .32;
        /// <summary>Wet and dry spells: the share above is multiplied by this much at the driest and the wettest.</summary>
        public double SpellDry = .15, SpellWet = 1.7;
        /// <summary>Share of the rain far out that is a downpour with thunder and lightning.</summary>
        public double StormShare = .38;
        /// <summary>Soft edge of a front, as a share of the land: the rain thickens from nothing to full over it.</summary>
        public double Edge = .06;
        /// <summary>Rain at least this strong wets bodies and puts fires out.</summary>
        public double Wetting = .35;
    }

    /// <summary>The sky over a point at a moment: how hard it rains there and how much of it is a thunderstorm (0…1 each).</summary>
    public readonly struct Sky
    {
        public readonly float Rain, Storm;
        public Sky(float rain, float storm) { Rain = rain; Storm = storm; }
        public override string ToString() => $"rain {Rain:0.00}, storm {Storm:0.00}";
    }

    /// <summary>
    /// The weather of the endless world: rain fronts the wind drives across the land. A front is a patch of a noise
    /// field that drifts with the outing's wind and slowly changes its shape as it goes, so rain comes, passes over and
    /// goes on; over the whole land wet and dry spells come and go. None of it falls on the start: the fronts thin out
    /// on the way in. Far out the heart of a front is a thunderstorm. A pure function of the seed, the place and the
    /// time since the outing began, the same everywhere for everyone.
    /// </summary>
    public static class WorldWeather
    {
        const int Field = 121, Wind = 122, Spells = 123;

        /// <summary>The wind that drives the fronts this outing, m/s.</summary>
        public static (double X, double Y) WindOf(int seed, WeatherRules rules)
        {
            double angle = Hash.Unit(seed, Wind) * Math.PI * 2;
            double speed = rules.WindMin + (rules.WindMax - rules.WindMin) * Hash.Unit(seed, Wind, 1);
            return (Math.Cos(angle) * speed, Math.Sin(angle) * speed);
        }

        /// <summary>How wet the land is as a whole now: 0 the driest spell, 1 the wettest.</summary>
        public static double SpellAt(int seed, WeatherRules rules, double seconds)
        {
            double t = seconds / rules.Spell, g = Math.Floor(t), f = Smooth(t - g);
            return Lerp(Hash.Unit(seed, Spells, (int)g), Hash.Unit(seed, Spells, (int)g + 1), f);
        }

        /// <summary>Share of the land under rain round a point now (the start dry, more of it further out).</summary>
        public static double CoverAt(WorldRules rules, int seed, double x, double y, double seconds)
        {
            var w = rules.Weather;
            double d = Math.Sqrt(x * x + y * y);
            double near = SmoothStep(rules.InnerRadius * .9, rules.InnerRadius + 24 * rules.RingScale, d);
            if (near <= 0) return 0;
            double far = SmoothStep(rules.MiddleRadius - 8 * rules.RingScale, rules.MiddleRadius + 48 * rules.RingScale, d);
            double spell = Lerp(w.SpellDry, w.SpellWet, SpellAt(seed, w, seconds));
            return Math.Min(.85, near * Lerp(w.CoverMiddle, w.CoverFar, far) * spell);
        }

        /// <summary>The sky over a point (metres from the start) <paramref name="seconds"/> after the outing began.</summary>
        public static Sky At(WorldRules rules, int seed, double x, double y, double seconds)
        {
            var w = rules.Weather;
            double cover = CoverAt(rules, seed, x, y, seconds);
            if (cover <= 0) return default;
            var (wx, wy) = WindOf(seed, w);
            // Where this point was when the fronts set out: they drift with the wind.
            double px = (x - wx * seconds) / w.FrontCell, py = (y - wy * seconds) / w.FrontCell, pt = seconds / w.Evolve;
            double q = Rank(Front(seed, px, py, pt));
            double rain = Clamp01((q - (1 - cover)) / w.Edge);
            if (rain <= 0) return default;
            double far = SmoothStep(rules.MiddleRadius - 8 * rules.RingScale, rules.MiddleRadius + 48 * rules.RingScale, Math.Sqrt(x * x + y * y));
            double stormCover = cover * w.StormShare * far;
            double storm = stormCover > 0 ? Clamp01((q - (1 - stormCover)) / (w.Edge * .8)) : 0;
            return new Sky((float)rain, (float)storm);
        }

        /// <summary>Rain at least this strong wets bodies and puts fires out.</summary>
        public static bool Wets(this Sky sky, WeatherRules rules) => sky.Rain >= rules.Wetting;

        // ---------- the field ----------

        /// <summary>Two layers of smooth noise in plan and time, about 0…1.</summary>
        static double Front(int seed, double x, double y, double t)
            => .8 * Noise(seed, 0, x, y, t) + .2 * Noise(seed, 1, x * 2.3 + 31.7, y * 2.3 - 12.9, t * 1.6 + 5);

        static double Noise(int seed, int layer, double x, double y, double t)
        {
            double gx = Math.Floor(x), gy = Math.Floor(y), gt = Math.Floor(t);
            double fx = Smooth(x - gx), fy = Smooth(y - gy), ft = Smooth(t - gt);
            int ix = (int)gx, iy = (int)gy, it = (int)gt;
            double V(int a, int b, int c) => Hash.Unit(seed, Field + layer * 7, a, b, c);
            double Plane(int c) => Lerp(Lerp(V(ix, iy, c), V(ix + 1, iy, c), fx), Lerp(V(ix, iy + 1, c), V(ix + 1, iy + 1, c), fx), fy);
            return Lerp(Plane(it), Plane(it + 1), ft);
        }

        // The field's values are not spread evenly (smooth noise keeps to the middle); ranking a value among many
        // samples of the field makes "the share of the land" exact: rank above 1 - c holds on a share c of it.
        static readonly double[] Ranks = BuildRanks();

        static double[] BuildRanks()
        {
            const int count = 8192;
            var values = new double[count];
            for (int i = 0; i < count; i++)
            {
                double x = Hash.Unit(7, 900, i) * 400, y = Hash.Unit(7, 901, i) * 400, t = Hash.Unit(7, 902, i) * 400;
                values[i] = Front(7 + i % 13, x, y, t);
            }
            Array.Sort(values);
            return values;
        }

        static double Rank(double value)
        {
            int i = Array.BinarySearch(Ranks, value);
            if (i < 0) i = ~i;
            if (i <= 0) return 0;
            if (i >= Ranks.Length) return 1;
            double a = Ranks[i - 1], b = Ranks[i];
            double f = b > a ? (value - a) / (b - a) : 0;
            return (i - 1 + f) / (Ranks.Length - 1);
        }

        static double Smooth(double f) => f * f * (3 - 2 * f);
        static double Lerp(double a, double b, double f) => a + (b - a) * f;
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double SmoothStep(double from, double to, double v) => Smooth(Clamp01((v - from) / (to - from)));
    }
}
