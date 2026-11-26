using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Loot;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// The demo balance of the endless world (stage 7 of the balance queue): what its spots give and how it changes
    /// from ring to ring, on the real content and the real rules (ring scale 7.25, ring 0 out to 232 m).
    /// </summary>
    public sealed class WorldBalanceTests
    {
        const string ContentPath = "Assets/WorldAssembly/World content.asset";
        const string CataloguePath = "Assets/WorldAssembly/Chunk catalogue.asset";
        /// <summary>Worlds measured, with the seeds of the balance audit's world model (seed × 7919 + 13).</summary>
        const int Seeds = 10;
        /// <summary>Ring 2 is measured out to here (its near part, 580–1000 m: where the demo goes).</summary>
        const double FarEdge = 1000;

        /// <summary>One ring of the measured worlds, per chunk (the start area left out).</summary>
        sealed class RingStats
        {
            public int chunks, brawls, chests, mimics;
            public double finds, findValue, pocketValue;
            public double Per(double total) => total / Math.Max(1, chunks);
        }

        static RingStats[] measured;

        /// <summary>
        /// Plans the measured worlds as WorldStreamer would load them: a chunk's finds lie only if its scene
        /// (<see cref="ChunkCatalog.Find(string, string, string)"/>) has find spots, a pocket reward only if it has a pocket; its
        /// chest is the ring's table roll on a find spot no find uses (<c>WorldStreamer.SpawnChest</c>). A find is worth the
        /// mean base price of its pool.
        /// </summary>
        static RingStats[] Measure()
        {
            if (measured != null) return measured;
            var content = AssetDatabase.LoadAssetAtPath<WorldContent>(ContentPath);
            var catalog = AssetDatabase.LoadAssetAtPath<ChunkCatalog>(CataloguePath);
            Assert.That(content && catalog, Is.True, "world content and chunk catalogue");
            var value = new Dictionary<string, double>();
            foreach (var id in new[] { "find", "find-hard", "crystal" })
                value[id] = content.Loot(id).pool.Chances().Sum(c => c.chance * (double)c.item.price);
            var views = new Dictionary<(string, string, string), ChunkView>();
            var rings = new[] { new RingStats(), new RingStats(), new RingStats() };
            var free = new List<int>();
            for (int s = 1; s <= Seeds; s++)
            {
                int seed = s * 7919 + 13;
                var map = new WorldMap(seed);
                int r = (int)Math.Ceiling(FarEdge / map.Rules.ChunkSize);
                for (int x = -r; x <= r; x++)
                    for (int y = -r; y <= r; y++)
                    {
                        var c = new ChunkCoord(x, y);
                        if (Math.Sqrt((double)x * x + (double)y * y) * map.Rules.ChunkSize >= FarEdge || WorldRules.IsStartArea(c)) continue;
                        var plan = map.Chunk(c);
                        var key = (plan.Shape.Id, plan.Variant.Id, ChunkCatalog.Land(plan.Biome, plan.Zone));
                        if (!views.TryGetValue(key, out var view))
                            views[key] = view = catalog.Find(key.Item1, key.Item2, key.Item3) ?? catalog.Find("plain", "grove");
                        var ring = rings[plan.Ring];
                        ring.chunks++;
                        int n = view ? view.findSockets.Length : 0;
                        if (n > 0)
                            foreach (var id in plan.Loot) { ring.finds++; ring.findValue += value[id]; }
                        if (plan.PocketReward != null && view && view.pocketSocket) ring.pocketValue += value[plan.PocketReward];
                        if (plan.Event == WorldEvents.Brawl) ring.brawls++;
                        var chest = content.ChestTable(plan.Ring).Roll(Hash.Unit(seed, Salt.ChestPresence, x, y), Hash.Unit(seed, Salt.ChestTier, x, y));
                        if (!chest) continue;
                        free.Clear();
                        for (int i = 0; i < n; i++) if (view.findSockets[i]) free.Add(i);
                        for (int i = 0; i < plan.Loot.Count && n > 0; i++)
                            free.Remove(plan.Loot[i] != "find" ? 0 : (int)((Hash.Of(seed, 900 + i, x, y) + (uint)i) % (uint)n));
                        if (free.Count == 0) continue;
                        ring.chests++;
                        if (chest.GetComponent<ChestMimic>()) ring.mimics++;
                    }
            }
            for (int i = 0; i < 3; i++)
            {
                var g = rings[i];
                Debug.Log($"[WorldBalance] ring {i}: {g.chunks} chunks; finds {g.Per(g.finds):0.000}/chunk worth {g.Per(g.findValue):0.0}, " +
                          $"pockets {g.Per(g.pocketValue):0.0}; chests {100 * g.Per(g.chests):0.00}% (mimics {100.0 * g.mimics / Math.Max(1, g.chests):0.0}%); " +
                          $"brawls {100 * g.Per(g.brawls):0.00}%");
            }
            return measured = rings;
        }

        /// <summary>Share of chunks of the swamp with their centre this far from the start, over many worlds.</summary>
        static double SwampShare(double from, double to, int seeds)
        {
            int total = 0, swamp = 0;
            for (int s = 1; s <= seeds; s++)
            {
                var map = new WorldMap(s * 7919 + 13);
                int r = (int)Math.Ceiling(to / map.Rules.ChunkSize);
                for (int x = -r; x <= r; x++)
                    for (int y = -r; y <= r; y++)
                    {
                        double d = Math.Sqrt((double)x * x + (double)y * y) * map.Rules.ChunkSize;
                        if (d < from || d >= to) continue;
                        total++;
                        if (map.Biomes.IsSwamp(new ChunkCoord(x, y))) swamp++;
                    }
            }
            return swamp / (double)total;
        }

        [Test]
        public void FindsAreRicherFurtherFromTheStart()
        {
            var rings = Measure();
            double[] worth = rings.Select(g => g.Per(g.findValue)).ToArray();
            Assert.That(worth[0], Is.InRange(15, 19), "ring 0: finds worth about 17 a chunk");
            Assert.That(worth[1], Is.InRange(27, 33), "ring 1: about 30");
            Assert.That(worth[2], Is.InRange(31, 39), "ring 2 (580–1000 m): about 33–37");
            Assert.That(worth[1], Is.GreaterThan(worth[0] * 1.5), "going out pays");
            Assert.That(worth[2], Is.GreaterThan(worth[1]));
        }

        [Test]
        public void MeasureTheRings()
        {
            var rings = Measure();
            Debug.Log($"[WorldBalance] swamp 250–300 m {100 * SwampShare(250, 300, 40):0.0}%, 300–400 m {100 * SwampShare(300, 400, 20):0.0}%");
            Assert.That(rings.All(g => g.chunks > 0), Is.True);
        }

        [Test]
        public void CrystalSpotsGiveRareAndLegendaryCurios()
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldContent>(ContentPath);
            var crystal = content.Loot("crystal");
            Assert.That(crystal, Is.Not.Null, "the crystal role has a find");
            Assert.That(AssetDatabase.GetAssetPath(crystal), Is.EqualTo("Assets/Loot/Prefabs/Finds/Find crystal.prefab"));
            var pool = AssetDatabase.LoadAssetAtPath<LootPool>("Assets/Loot/Pools/Pool crystal.asset");
            Assert.That(pool, Is.Not.Null);
            Assert.That(crystal.pool, Is.EqualTo(pool), "a crystal spot picks from the crystal pool");
            Assert.That(content.Loot("find-hard").pool, Is.Not.EqualTo(pool), "the valuable find keeps its own pool");
            var chances = pool.Chances().ToList();
            Assert.That(chances.All(c => c.item.rarity >= LootRarity.Rare), Is.True, "only rare and legendary curios");
            Assert.That(chances.Select(c => c.item.id), Does.Contain("hypercube").And.Contain("diamond_apple"));
            // The world picks the item as WorldStreamer.PlaceLoot does: the spot's seeded random into the prefab's pool.
            var seen = new HashSet<string>();
            for (int i = 0; i < 2000; i++)
            {
                var item = crystal.pool.Pick(new System.Random(i));
                Assert.That(chances.Any(c => c.item == item), Is.True, item ? item.id : "nothing");
                seen.Add(item.id);
            }
            Assert.That(seen, Does.Contain("hypercube").And.Contain("diamond_apple"), "the legendary curios do turn up");
            // And the world has crystal spots: the summits and ring 2's best rewards.
            int spots = 0;
            var map = new WorldMap(7932);
            for (int x = -70; x <= 70 && spots == 0; x++)
                for (int y = -70; y <= 70; y++)
                {
                    var plan = map.Chunk(new ChunkCoord(x, y));
                    if (plan.Loot.Contains("crystal") || plan.PocketReward == "crystal") spots++;
                }
            Assert.That(spots, Is.GreaterThan(0), "crystal spots in the world");
        }
    }
}
