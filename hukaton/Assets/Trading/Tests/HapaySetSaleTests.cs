using System.Collections.Generic;
using NUnit.Framework;

namespace Hakaton.Trading.Tests
{
    /// <summary>A collector on хаPAY buys a whole set (TradingSession.SellSet).</summary>
    public sealed class HapaySetSaleTests
    {
        static readonly ItemDefinition Horn = new ItemDefinition("horn", "Рог", "Трофеи", 100);
        static readonly ItemDefinition Herb = new ItemDefinition("herb", "Трава", "Растения", 40);

        static List<KeyValuePair<string, int>> Set(params (string id, int count)[] wants)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var w in wants) list.Add(new KeyValuePair<string, int>(w.id, w.count));
            return list;
        }

        [Test]
        public void TakesStorageFirstThenTheDisplayAndPaysIntoRevenue()
        {
            var session = new TradingSession();
            session.SeedInventory(Horn, 3);
            session.SeedInventory(Herb, 1);
            Assert.That(session.PlaceItem(0, 0), Is.True); // a horn on the display
            Assert.That(session.PlaceItem(0, 1), Is.True); // another
            // Storage: horn ×1, herb ×1; display: horn ×2.
            int coins = session.Gold;
            Assert.That(session.SellSet(Set(("horn", 2), ("herb", 1)), 300), Is.True);
            Assert.That(session.StoredCount("horn") + session.StoredCount("herb"), Is.Zero, "Storage goes first.");
            Assert.That(session.DisplayedCount("horn"), Is.EqualTo(1), "Then one from the display.");
            Assert.That(session.Gold, Is.EqualTo(coins + 300));
            Assert.That(session.GrossRevenue, Is.EqualTo(300));
            Assert.That(session.SalesCount, Is.EqualTo(3), "Every piece counts as a sale.");
        }

        [Test]
        public void RefusesWhenSomethingIsMissingOrTheShopIsOpen()
        {
            var session = new TradingSession();
            session.SeedInventory(Horn, 1);
            Assert.That(session.SellSet(Set(("horn", 2)), 100), Is.False);
            Assert.That(session.StoredCount("horn"), Is.EqualTo(1), "Nothing taken.");
            Assert.That(session.SellSet(Set(("nothing", 1)), 100), Is.False);
            session.PlaceItem(0, 0);
            Assert.That(session.Open(), Is.True);
            Assert.That(session.SellSet(Set(("horn", 1)), 100), Is.False, "Not while the shop is open.");
        }
    }
}
