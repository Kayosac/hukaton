using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Hapay.Tests
{
    public sealed class HapayMarketTests
    {
        sealed class Stock : IHapayStock
        {
            public readonly Dictionary<string, int> stored = new Dictionary<string, int>(), shown = new Dictionary<string, int>();
            public readonly Dictionary<string, int> price = new Dictionary<string, int>(), rarity = new Dictionary<string, int>();
            public int Stored(string item) => stored.TryGetValue(item, out var n) ? n : 0;
            public int Displayed(string item) => shown.TryGetValue(item, out var n) ? n : 0;
            public int Price(string item) => price.TryGetValue(item, out var n) ? n : 0;
            public int Rarity(string item) => rarity.TryGetValue(item, out var n) ? n : 0;
            public IEnumerable<string> Stocked() => stored.Where(p => p.Value > 0).Select(p => p.Key)
                .Concat(shown.Where(p => p.Value > 0).Select(p => p.Key)).Distinct();
        }

        static HapayPool Pool(int collections = 6)
        {
            var pool = ScriptableObject.CreateInstance<HapayPool>();
            for (int i = 0; i < collections; i++)
                pool.collections.Add(new HapayCollection
                {
                    id = "c" + i, nick = "n" + i,
                    wants = new List<HapayWant> { new HapayWant("a", 1), new HapayWant("b", i + 1) }
                });
            return pool;
        }

        [Test]
        public void FillsTheSiteWithDifferentLots()
        {
            var market = new HapayMarket(Pool(), 7);
            market.Fill();
            Assert.That(market.Lots.Count, Is.EqualTo(4));
            Assert.That(market.Lots.Select(l => l.Collection.id).Distinct().Count(), Is.EqualTo(4));
        }

        [Test]
        public void TimeGrowsWithTheSize()
        {
            var pool = Pool();
            Assert.That(pool.SecondsFor(2), Is.EqualTo(8 * 60).Within(.01f));
            Assert.That(pool.SecondsFor(9), Is.EqualTo(25 * 60).Within(.01f));
            Assert.That(pool.SecondsFor(30), Is.EqualTo(25 * 60).Within(.01f));
            Assert.That(pool.SecondsFor(5), Is.GreaterThan(8 * 60).And.LessThan(25 * 60));
        }

        [Test]
        public void ExpiredLotIsReplacedAndNotRepeatedSoon()
        {
            var market = new HapayMarket(Pool(10), 3);
            market.Fill();
            var first = market.Lots[0];
            market.Tick(first.SecondsLeft + 1);
            Assert.That(market.Lots.Count, Is.EqualTo(4));
            Assert.That(market.Lots.Any(l => l.Collection == first.Collection), Is.False, "A lot that left does not come back at once.");
        }

        [Test]
        public void PriceIsSumTimesMultiplierRounded()
        {
            var pool = Pool();
            var market = new HapayMarket(pool, 1);
            var stock = new Stock();
            stock.price["a"] = 100; stock.price["b"] = 50;
            // c2 wants a×1 + b×3: 250, 4 pieces, rarity 0 → ×(1.3 + 0.08) = 345.
            market.Restore(new HapaySave { lots = new List<HapayLotSave> { new HapayLotSave { collection = "c2", seconds = 600, left = 600 } } });
            var lot = market.Lots.First(l => l.Collection.id == "c2");
            Assert.That(market.PriceOf(lot, stock), Is.EqualTo(345));
            stock.rarity["a"] = 3; stock.rarity["b"] = 3;
            // rarity 3 → +0.15: ×1.53 → 382.5 → 385 (rounded to 5).
            Assert.That(market.PriceOf(lot, stock), Is.EqualTo(385));
            Assert.That(market.Multiplier(40, 3), Is.EqualTo(1.6f).Within(.001f), "Capped.");
        }

        [Test]
        public void CompleteCountsStorageAndDisplay()
        {
            var market = new HapayMarket(Pool(), 1);
            market.Restore(new HapaySave { lots = new List<HapayLotSave> { new HapayLotSave { collection = "c1", seconds = 600, left = 600 } } });
            var lot = market.Lots.First(l => l.Collection.id == "c1"); // a×1, b×2
            var stock = new Stock();
            stock.stored["a"] = 1; stock.stored["b"] = 1;
            Assert.That(HapayMarket.Complete(lot, stock), Is.False);
            stock.shown["b"] = 1;
            Assert.That(HapayMarket.Complete(lot, stock), Is.True);
        }

        [Test]
        public void FirstLotIsMadeFromWhatThePlayerHasOnce()
        {
            var pool = Pool();
            var market = new HapayMarket(pool, 1);
            market.Fill();
            var stock = new Stock();
            Assert.That(market.MakeFirstLot(stock), Is.False, "Nothing to make it from.");
            stock.price["x"] = 10; stock.price["y"] = 90; stock.price["z"] = 40; stock.price["w"] = 5;
            stock.stored["x"] = 1; stock.stored["y"] = 2; stock.shown["z"] = 1; stock.stored["w"] = 1;
            Assert.That(market.MakeFirstLot(stock), Is.True);
            var first = market.Lots[0];
            Assert.That(first.Collection, Is.SameAs(pool.firstLot));
            Assert.That(first.Wants.Select(w => w.item), Is.EqualTo(new[] { "y", "z", "x" }), "The dearest three, one each.");
            Assert.That(HapayMarket.Complete(first, stock), Is.True);
            Assert.That(market.MakeFirstLot(stock), Is.False, "Only once.");
        }

        [Test]
        public void SaveAndRestoreKeepLotsTimesAndTheFirstLot()
        {
            var pool = Pool();
            var market = new HapayMarket(pool, 5);
            var stock = new Stock();
            stock.price["q"] = 30; stock.stored["q"] = 1;
            market.Fill();
            market.MakeFirstLot(stock);
            market.Tick(30);
            market.MarkSold(market.Lots[1], 200);
            var save = market.Save();
            var copy = new HapayMarket(pool, 9);
            copy.Restore(JsonUtility.FromJson<HapaySave>(JsonUtility.ToJson(save)));
            Assert.That(copy.Lots.Select(l => l.Collection.id), Is.EqualTo(market.Lots.Select(l => l.Collection.id)));
            Assert.That(copy.Lots.Select(l => l.SecondsLeft), Is.EqualTo(market.Lots.Select(l => l.SecondsLeft)));
            Assert.That(copy.Lots[0].Wants.Single().item, Is.EqualTo("q"));
            Assert.That((copy.FirstLotDone, copy.Sold, copy.Earned), Is.EqualTo((true, 1, 200L)));
        }

        [Test]
        public void TheShippedPoolOnlyAsksForRealItems()
        {
            var pool = AssetDatabase.LoadAssetAtPath<HapayPool>("Assets/Hapay/Data/Hapay pool.asset");
            Assert.That(pool, Is.Not.Null);
            Assert.That(pool.collections.Count, Is.GreaterThanOrEqualTo(30));
            var ids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/Loot/Items" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(System.IO.Path.GetFileNameWithoutExtension).ToHashSet();
            foreach (var c in pool.collections)
            {
                Assert.That(c.wants.Count, Is.GreaterThan(0), c.id);
                foreach (var want in c.wants) Assert.That(ids, Does.Contain(want.item), $"{c.id}: {want.item}");
                Assert.That(c.Pieces, Is.InRange(2, 12), c.id);
            }
            Assert.That(pool.collections.Select(c => c.id).Distinct().Count(), Is.EqualTo(pool.collections.Count));
            Assert.That(pool.avatars.Length, Is.EqualTo(4));
            Assert.That(pool.avatars.All(a => a), Is.True);
        }
    }
}
