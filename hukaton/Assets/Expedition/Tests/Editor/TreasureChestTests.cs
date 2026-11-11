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
    }
}
