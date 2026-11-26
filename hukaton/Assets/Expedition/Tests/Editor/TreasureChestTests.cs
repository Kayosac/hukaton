using System.Collections.Generic;
using System.Linq;
using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    public sealed class TreasureChestTests
    {
        static readonly string[] Tiers = { "Chest 1 Beggar", "Chest 2 Common", "Chest 3 Sturdy", "Chest 4 Rich", "Chest 5 Legendary" };

        static ChestTier Tier(string name) => AssetDatabase.LoadAssetAtPath<ChestTier>("Assets/Game/Data/Chests/" + name + ".asset");
        static TreasureChest Prefab(string name) => AssetDatabase.LoadAssetAtPath<TreasureChest>("Assets/Expedition/Prefabs/Chests/" + name + ".prefab");

        [Test]
        public void SplitAddsUpExactlyAndRespectsThePieceLimit()
        {
            var random = new System.Random(7);
            foreach (var name in Tiers)
            {
                var tier = Tier(name);
                Assert.That(tier, Is.Not.Null, name);
                for (int run = 0; run < 200; run++)
                {
                    int total = tier.RollCoins(() => (float)random.NextDouble());
                    Assert.That(total, Is.InRange(tier.coins.x, tier.coins.y), name);
                    var pieces = new List<(WorldCoin prefab, int value)>();
                    tier.Split(total, () => (float)random.NextDouble(), pieces);
                    Assert.That(pieces.Sum(p => p.value), Is.EqualTo(total), name);
                    Assert.That(pieces.Count, Is.LessThanOrEqualTo(tier.maxPieces), name);
                    Assert.That(pieces.All(p => p.prefab && p.value > 0), name);
                }
            }
        }

        [Test]
        public void RicherTiersPayMoreAndShowMore()
        {
            for (int i = 1; i < Tiers.Length; i++)
            {
                ChestTier poorer = Tier(Tiers[i - 1]), richer = Tier(Tiers[i]);
                Assert.That(richer.rank, Is.GreaterThan(poorer.rank));
                Assert.That(richer.coins.x, Is.GreaterThan(poorer.coins.y), "payouts do not overlap: " + richer.name);
                Assert.That(richer.maxPieces, Is.GreaterThanOrEqualTo(poorer.maxPieces), richer.name);
                Assert.That(richer.bursts.Length, Is.GreaterThanOrEqualTo(poorer.bursts.Length), richer.name);
            }
        }

        [Test]
        public void ChestPrefabsAreWired()
        {
            foreach (var name in Tiers)
            {
                var chest = Prefab(name);
                Assert.That(chest, Is.Not.Null, name);
                Assert.That(chest.tier, Is.EqualTo(Tier(name)), name);
                Assert.That(chest.lid && chest.lid.Lid, name + ": lid");
                Assert.That(chest.mouth && chest.mouth.IsChildOf(chest.transform), name + ": mouth");
                Assert.That(chest.GetComponentsInChildren<Collider>().Any(c => !c.isTrigger), name + ": solid to aim at");
                Assert.That(chest.tier.idleMarker, Is.Not.Null, name + ": marker");
                Assert.That(chest.tier.bursts.All(b => b.prefab), name + ": bursts");
                foreach (var kind in chest.tier.kinds)
                {
                    Assert.That(kind.prefab && kind.prefab.body && kind.prefab.visual, name + ": coin prefab");
                    Assert.That(kind.prefab.value, Is.EqualTo(kind.value), name + ": coin value");
                }
            }
        }

        [Test]
        public void SpawnTablePicksByWeightAndChance()
        {
            var table = ScriptableObject.CreateInstance<ChestSpawnTable>();
            try
            {
                TreasureChest a = Prefab(Tiers[0]), b = Prefab(Tiers[4]);
                table.chance = .5f;
                table.entries = new[] { new ChestSpawnTable.Entry { prefab = a, weight = 3 }, new ChestSpawnTable.Entry { prefab = b, weight = 1 } };
                Assert.That(table.Roll(.6, 0), Is.Null, "above the chance: nothing");
                Assert.That(table.Roll(.1, .5), Is.EqualTo(a));
                Assert.That(table.Roll(.1, .8), Is.EqualTo(b));
                Assert.That(table.Probability(a), Is.EqualTo(.375f).Within(1e-5));
                Assert.That(table.Probability(b), Is.EqualTo(.125f).Within(1e-5));
            }
            finally { Object.DestroyImmediate(table); }
        }
    
        static float ExpectedRank(ChestSpawnTable table)
        {
            float total = table.entries.Sum(e => e.weight), sum = table.entries.Sum(e => e.weight * e.prefab.tier.rank);
            return total > 0 ? sum / total : 0;
        }

        static float ExpectedCoins(ChestSpawnTable table)
        {
            float total = table.entries.Sum(e => e.weight), sum = table.entries.Sum(e => e.weight * (e.prefab.tier.coins.x + e.prefab.tier.coins.y) * .5f);
            return total > 0 ? sum / total : 0;
        }

        static float Riches() => new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("Assets/WorldAssembly/World content.asset"))
            .FindProperty("sunkenRiches").floatValue;

        [Test]
        public void RingZeroChestsAreFewerButNotBeggarlyAndOneInTenBites()
        {
            var table = AssetDatabase.LoadAssetAtPath<ChestSpawnTable>("Assets/Game/Data/Chests/Chests world ring 1 centre.asset");
            Assert.That(table.chance, Is.EqualTo(.1f).Within(1e-6f));
            float total = table.entries.Sum(e => e.weight);
            float Share(int rank, bool mimic) => table.entries
                .Where(e => e.prefab.tier.rank == rank && (e.prefab.GetComponent<ChestMimic>() != null) == mimic).Sum(e => e.weight) / total;
            float mimics = Enumerable.Range(1, 5).Sum(r => Share(r, true));
            Assert.That(mimics, Is.EqualTo(.1f).Within(.001f), "a tenth of the chests are mimics");
            float[] plain = { .30f, .45f, .25f };
            for (int rank = 1; rank <= 3; rank++)
            {
                Assert.That(Share(rank, false) / (1 - mimics), Is.EqualTo(plain[rank - 1]).Within(.001f), $"ordinary chests of rank {rank}");
                Assert.That(Share(rank, true) / mimics, Is.EqualTo(plain[rank - 1]).Within(.001f), $"mimics of rank {rank}: as the ordinary ones");
            }
            Assert.That(Share(4, false) + Share(5, false), Is.Zero, "nothing rich in the calm ring");
        }

        [Test]
        public void EndlessWorldIsRicherFurtherOut()
        {
            string[] rings = { "Chests world ring 1 centre", "Chests world ring 2 middle", "Chests world ring 3 far" };
            var tables = rings.Select(r => AssetDatabase.LoadAssetAtPath<ChestSpawnTable>("Assets/Game/Data/Chests/" + r + ".asset")).ToArray();
            Assert.That(tables.All(t => t && t.entries.All(e => e.prefab)), "ring tables");
            for (int i = 1; i < tables.Length; i++)
            {
                Assert.That(tables[i].chance, Is.GreaterThan(tables[i - 1].chance), rings[i]);
                Assert.That(ExpectedRank(tables[i]), Is.GreaterThan(ExpectedRank(tables[i - 1])), rings[i]);
            }
            var content = AssetDatabase.LoadAssetAtPath<Object>("Assets/WorldAssembly/World content.asset");
            var serialized = new SerializedObject(content).FindProperty("chestTables");
            Assert.That(serialized.arraySize, Is.EqualTo(3));
            for (int i = 0; i < 3; i++) Assert.That(serialized.GetArrayElementAtIndex(i).objectReferenceValue, Is.EqualTo(tables[i]));
        }

        [Test]
        public void SunkenChestsAreRichButLegendaryOnlyFarOut()
        {
            string[] rings = { "Chests sunken ring 1 centre", "Chests sunken ring 2 middle", "Chests sunken ring 3 far" };
            string[] land = { "Chests world ring 1 centre", "Chests world ring 2 middle", "Chests world ring 3 far" };
            var tables = rings.Select(r => AssetDatabase.LoadAssetAtPath<ChestSpawnTable>("Assets/Game/Data/Chests/" + r + ".asset")).ToArray();
            Assert.That(tables.All(t => t && t.entries.All(e => e.prefab && e.prefab.tier)), "sunken tables");
            Assert.That(tables.SelectMany(t => t.entries).Any(e => e.weight > 0 && e.prefab.GetComponent<ChestMimic>()), Is.False,
                "no mimic lies under water");
            float Legendary(ChestSpawnTable table)
            {
                float total = table.entries.Sum(e => e.weight);
                return total > 0 ? table.entries.Where(e => e.prefab.tier.rank >= 5).Sum(e => e.weight) / total : 0;
            }
            Assert.That(Legendary(tables[0]), Is.Zero, "no legendary chest under water in the centre");
            Assert.That(Legendary(tables[1]), Is.GreaterThan(0).And.LessThanOrEqualTo(.05f), "a very rare one in the middle");
            for (int i = 0; i < tables.Length; i++)
            {
                var landTable = AssetDatabase.LoadAssetAtPath<ChestSpawnTable>("Assets/Game/Data/Chests/" + land[i] + ".asset");
                // Richer than the land: each sunken chest pays its coins Sunken Riches times over (the calm ring 0 table on
                // land has sturdier chests, but fewer of them and no beggar's share under water).
                Assert.That(ExpectedCoins(tables[i]) * Riches(), Is.GreaterThan(ExpectedCoins(landTable) * 2), rings[i] + " is richer than the land");
                if (i > 0) Assert.That(ExpectedRank(tables[i]), Is.GreaterThan(ExpectedRank(landTable)), rings[i] + " has better chests than the land");
                if (i > 0) Assert.That(ExpectedRank(tables[i]), Is.GreaterThan(ExpectedRank(tables[i - 1])), rings[i]);
            }
            var content = AssetDatabase.LoadAssetAtPath<Object>("Assets/WorldAssembly/World content.asset");
            var serialized = new SerializedObject(content).FindProperty("sunkenTables");
            Assert.That(serialized.arraySize, Is.EqualTo(3));
            for (int i = 0; i < 3; i++) Assert.That(serialized.GetArrayElementAtIndex(i).objectReferenceValue, Is.EqualTo(tables[i]));
        }

        [Test]
        public void OldApiaryHasChestSites()
        {
            var apiary = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OldApiary/Prefabs/Old Apiary.prefab");
            var sites = apiary.GetComponentsInChildren<ChestSite>(true);
            Assert.That(sites.Length, Is.GreaterThanOrEqualTo(8));
            foreach (var site in sites)
            {
                Assert.That(site.table, Is.Not.Null, site.name);
                Assert.That(site.spawnPoints.Length, Is.GreaterThan(0), site.name);
                Assert.That(site.spawnPoints.All(p => p && p.IsChildOf(site.transform)), site.name);
            }
            var hidden = AssetDatabase.LoadAssetAtPath<ChestSpawnTable>("Assets/Game/Data/Chests/Chests Old Apiary hidden.asset");
            var normal = AssetDatabase.LoadAssetAtPath<ChestSpawnTable>("Assets/Game/Data/Chests/Chests Old Apiary.asset");
            Assert.That(ExpectedRank(hidden), Is.GreaterThan(ExpectedRank(normal)), "hard-to-reach places hide richer chests");

            // The first chest of the first loop (the broken cart, ~31 m from the deck) teaches, it does not bite:
            // its own table — always there, a beggar's or a common chest, never a mimic. The other ordinary sites keep the shared table.
            var cart = sites.Single(s => s.name.StartsWith("Chest site 2 "));
            var cartTable = AssetDatabase.LoadAssetAtPath<ChestSpawnTable>("Assets/Game/Data/Chests/Chests Old Apiary cart.asset");
            Assert.That(cartTable, Is.Not.Null, "the cart's own table");
            Assert.That(cart.table, Is.EqualTo(cartTable), cart.name);
            Assert.That(cartTable.chance, Is.EqualTo(1f), "the cart always has a chest");
            Assert.That(cartTable.entries, Is.Not.Empty);
            foreach (var entry in cartTable.entries)
            {
                Assert.That(entry.prefab, Is.Not.Null);
                Assert.That(entry.prefab.GetComponent<ChestMimic>(), Is.Null, entry.prefab.name + " is not a mimic");
                Assert.That(entry.prefab.tier.rank, Is.InRange(1, 2), entry.prefab.name + " is a beggar's or a common chest");
            }
            Assert.That(sites.Where(s => s != cart && s.table != hidden).All(s => s.table == normal), "the other ordinary sites keep the shared table");
        }
    }
}
