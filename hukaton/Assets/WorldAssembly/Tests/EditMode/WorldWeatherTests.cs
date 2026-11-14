using System;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// The rain fronts of the endless world: they keep off the start, drift on with the wind (a shower passes over
    /// whoever stays put), cover about the share of the land asked for, thunderstorms only far out, and wet and dry
    /// spells come and go.
    /// </summary>
    public sealed class WorldWeatherTests
    {
        static readonly WorldRules Rules = new WorldRules { RingScale = 1 };
        static WeatherRules W => Rules.Weather;

        static (double x, double y) Point(Random rnd, double from, double to)
        {
            double angle = rnd.NextDouble() * Math.PI * 2, d = from + rnd.NextDouble() * (to - from);
            return (Math.Cos(angle) * d, Math.Sin(angle) * d);
        }

        [Test]
        public void TheStartStaysDry()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var rnd = new Random(seed);
                for (int i = 0; i < 3000; i++)
                {
                    var (x, y) = Point(rnd, 0, Rules.InnerRadius * .88);
                    var sky = WorldWeather.At(Rules, seed, x, y, rnd.NextDouble() * 7200);
                    Assert.That(sky.Rain, Is.EqualTo(0), $"seed {seed}: rain at ({x:0}, {y:0})");
                }
            }
        }

        [Test]
        public void FrontsDriftOnWithTheWindAndPassOverWhoeverStaysPut()
        {
            const double later = 45;
            int found = 0, follow = 0, stay = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var (wx, wy) = WorldWeather.WindOf(seed, W);
                double speed = Math.Sqrt(wx * wx + wy * wy);
                Assert.That(speed, Is.InRange(W.WindMin - 1e-6, W.WindMax + 1e-6), $"seed {seed}: wind {speed:0.0} m/s");
                var rnd = new Random(seed);
                int here = 0;
                for (int k = 0; k < 40000 && here < 30; k++)
                {
                    var (x, y) = Point(rnd, 320, 620);
                    double t = rnd.NextDouble() * 3000;
                    if (WorldWeather.At(Rules, seed, x, y, t).Rain < .95) continue;
                    here++;
                    if (WorldWeather.At(Rules, seed, x + wx * later, y + wy * later, t + later).Rain >= .5) follow++;
                    if (WorldWeather.At(Rules, seed, x, y, t + later).Rain >= .5) stay++;
                }
                found += here;

                // Standing far out for an hour: showers come and go.
                int showers = 0;
                bool was = false;
                for (double t = 0; t < 3600; t += 2)
                {
                    bool wet = WorldWeather.At(Rules, seed, 250, -180, t).Wets(W);
                    if (wet && !was) showers++;
                    was = wet;
                }
                Assert.That(showers, Is.InRange(3, 60), $"seed {seed}: showers over an hour at one spot");
            }
            Assert.That(found, Is.GreaterThan(200), "full rain far out");
            Assert.That(follow / (double)found, Is.GreaterThan(.7), "following the wind, the rain is still there");
            Assert.That(stay / (double)found, Is.LessThan(follow / (double)found - .2), "staying put, the shower passes");
        }

        [Test]
        public void RainCoversAboutTheShareAskedForAndThunderOnlyFarOut()
        {
            int middle = 0, middleWet = 0, far = 0, farWet = 0, farStorm = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var rnd = new Random(seed * 31);
                for (int i = 0; i < 20000; i++)
                {
                    double t = rnd.NextDouble() * 7200;
                    var (mx, my) = Point(rnd, Rules.InnerRadius + 30, Rules.MiddleRadius - 10);
                    var near = WorldWeather.At(Rules, seed, mx, my, t);
                    middle++;
                    if (near.Wets(W)) middleWet++;
                    Assert.That(near.Storm, Is.EqualTo(0), "no thunderstorm in the middle ring");
                    var (fx, fy) = Point(rnd, 220, 700);
                    var sky = WorldWeather.At(Rules, seed, fx, fy, t);
                    far++;
                    if (sky.Wets(W)) farWet++;
                    if (sky.Storm > .35f) farStorm++;
                }
            }
            double m = middleWet / (double)middle, f = farWet / (double)far, s = farStorm / (double)far;
            Assert.That(m, Is.InRange(.08, .3), "rain in the middle ring");
            Assert.That(f, Is.InRange(.2, .45), "rain far out");
            Assert.That(f, Is.GreaterThan(m), "more rain further out");
            Assert.That(s, Is.InRange(.04, .25), "thunderstorms far out");
        }

        [Test]
        public void WetAndDrySpellsComeAndGo()
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var rnd = new Random(seed);
                var shares = Enumerable.Range(0, 120).Select(minute =>
                {
                    int wet = 0;
                    for (int i = 0; i < 200; i++)
                    {
                        var (x, y) = Point(rnd, 250, 550);
                        if (WorldWeather.At(Rules, seed, x, y, minute * 60).Wets(W)) wet++;
                    }
                    return wet / 200.0;
                }).ToList();
                Assert.That(shares.Min(), Is.LessThan(.06), $"seed {seed}: a dry spell in two hours");
                Assert.That(shares.Max(), Is.GreaterThan(.45), $"seed {seed}: a wet spell in two hours");
            }
        }
    }
}
