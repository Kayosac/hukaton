using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// Anthills keep out of the calm start (ring 0, out to 232 m): the first outings are not met by their garrison.
    /// They stand in ring 1 and beyond, and the ant compass smells the nearest one from the start.
    /// </summary>
    public sealed class AnthillRingTests
    {
        const int Worlds = 40;

        [Test]
        public void NoAnthillInRingZeroButSomeInRingOne()
        {
            var rules = new WorldRules();
            Assert.AreEqual(0, rules.DungeonChance[0], "no anthills in ring 0");
            Assert.That(rules.DungeonChance[1], Is.GreaterThan(0), "anthills in ring 1");
            Assert.AreEqual(232, rules.InnerRadius, .5, "ring 0 ends 232 m from the start");
            // Ring 0 whole and the inner part of ring 1 (232–580 m), out to 24 chunks (384 m) each way, are looked at chunk by chunk.
            const int ringOneReach = 24;
            int withRingOne = 0, ringOne = 0;
            var nearest = new List<double>();
            for (int seed = 1; seed <= Worlds; seed++)
            {
                var map = new WorldMap(seed, rules);
                for (int x = -ringOneReach; x <= ringOneReach; x++)
                    for (int y = -ringOneReach; y <= ringOneReach; y++)
                    {
                        var p = map.Chunk(x, y);
                        if (p.Event != WorldEvents.Dungeon) continue;
                        Assert.AreNotEqual(0, p.Ring, $"seed {seed}: an anthill in ring 0 at {p.Coord}");
                        Assert.IsFalse(TileShapes.IsPocketScene(p.Variant.Id), $"seed {seed}: an anthill over a card pocket {p}");
                        double d = Math.Sqrt((double)x * x + (double)y * y) * rules.ChunkSize;
                        Assert.That(d, Is.GreaterThanOrEqualTo(rules.InnerRadius), $"seed {seed}: an anthill {d:0} m from the start");
                        if (p.Ring == 1) ringOne++;
                    }
                if (map.NearestEvent(WorldEvents.Dungeon, 0, 0, ringOneReach, out var found))
                {
                    withRingOne++;
                    nearest.Add(Math.Sqrt((double)found.X * found.X + (double)found.Y * found.Y) * rules.ChunkSize);
                    Assert.AreEqual(1, map.Chunk(found).Ring, $"seed {seed}: the nearest anthill is in ring 1");
                }
            }
            nearest.Sort();
            Debug.Log($"[World] anthills in the inner ring 1 of {Worlds} worlds: {ringOne} ({ringOne / (double)Worlds:0.0} a world); " +
                      $"worlds with one within {ringOneReach} chunks: {withRingOne}; nearest from the start: " +
                      (nearest.Count > 0 ? $"min {nearest[0]:0} m, median {nearest[nearest.Count / 2]:0} m, max {nearest[nearest.Count - 1]:0} m" : "none"));
            Assert.That(ringOne, Is.GreaterThan(Worlds), "ring 1 has anthills: more than one a world in its inner part");
            Assert.That(withRingOne, Is.GreaterThanOrEqualTo(Worlds * 9 / 10), "nearly every world has an anthill within reach of the start");
        }

        [Test]
        public void TheAntCompassSmellsAnAnthillFromTheStart()
        {
            var go = new GameObject("compass reach");
            try
            {
                int reach = go.AddComponent<DungeonSite>().compassReach;
                var rules = new WorldRules();
                Assert.That(reach * rules.ChunkSize, Is.GreaterThan(rules.InnerRadius + 3 * rules.ChunkSize),
                    "from the start the compass reaches well into ring 1");
                int smelt = 0;
                for (int seed = 1; seed <= Worlds; seed++)
                    if (new WorldMap(seed, rules).NearestEvent(WorldEvents.Dungeon, 0, 0, reach, out _)) smelt++;
                Debug.Log($"[World] the ant compass ({reach} chunks) smells an anthill from the start in {smelt} of {Worlds} worlds");
                Assert.That(smelt, Is.GreaterThanOrEqualTo(Worlds * 9 / 10));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheNearestEventIsTheNearestByStraightDistance()
        {
            var map = new WorldMap(7);
            var rules = map.Rules;
            foreach (var (x, y) in new[] { (0.0, 0.0), (300.0, -120.0), (-250.0, 260.0), (7.9, -7.9) })
            {
                bool found = map.NearestEvent(WorldEvents.Dungeon, x, y, 30, out var c);
                // Brute force over the whole square.
                double best = double.MaxValue; ChunkCoord? want = null;
                int hx = (int)Math.Round(x / rules.ChunkSize), hy = (int)Math.Round(y / rules.ChunkSize);
                for (int dx = -30; dx <= 30; dx++)
                    for (int dy = -30; dy <= 30; dy++)
                    {
                        var k = new ChunkCoord(hx + dx, hy + dy);
                        if (map.Chunk(k).Event != WorldEvents.Dungeon) continue;
                        double ex = k.X * rules.ChunkSize - x, ey = k.Y * rules.ChunkSize - y, d = ex * ex + ey * ey;
                        if (d < best) { best = d; want = k; }
                    }
                Assert.AreEqual(want.HasValue, found, $"from {x}, {y}");
                if (found)
                {
                    double ex = c.X * rules.ChunkSize - x, ey = c.Y * rules.ChunkSize - y;
                    Assert.AreEqual(best, ex * ex + ey * ey, 1e-6, $"from {x}, {y}: {c} is not the nearest ({want})");
                }
            }
        }
    }
}
