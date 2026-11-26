using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using Hakaton.Loot;
using Hakaton.WorldAssembly.Unity;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.WorldAssembly.Tests
{
    /// <summary>
    /// The demo balance of the endless world (stage 7 of the balance queue): what its spots give and how it changes
    /// from ring to ring, on the real content and the real rules (ring scale 7.25, ring 0 out to 232 m).
    /// </summary>
    public sealed class WorldBalanceTests
    {
        const string ContentPath = "Assets/WorldAssembly/World content.asset";

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
