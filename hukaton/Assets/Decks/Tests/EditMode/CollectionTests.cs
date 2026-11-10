using System.Linq;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Board, «Гоблин-торговец»: the collection holds 2 × the deck limit; the brazier burns a copy for 75% of what was
    /// paid for it, rounded up; the three starting copies are protected. Deck limit and hand grow on the PC.
    /// </summary>
    public sealed class CollectionTests
    {
        static readonly CardSpec[] Specs =
        {
            new CardSpec("fire", "Огонь", 100), new CardSpec("ice", "Лёд", 120), new CardSpec("strike", "Удар", 120),
            new CardSpec("teleport", "Телепорт", 450, 5, true, new[] { 1, 2, 3 })
        };

        static DeckWorkshop Workshop(int coins, int limit = 4, int[] steps = null, int[] prices = null) => new DeckWorkshop(
            new DeckRules(limit, 3, 4, 3, 200, coins, steps, prices), Specs, null,
            new[] { new StartingCopies("fire", 1, 1, true), new StartingCopies("ice", 1, 1, true), new StartingCopies("strike", 1, 1, true) });

        [Test]
        public void NewGameStartsWithThreeProtectedCopiesInTheDeck()
        {
            var w = Workshop(0, 12);
            Assert.That(w.Owned.Select(c => c.Spec.Id), Is.EqualTo(new[] { "fire", "ice", "strike" }));
            Assert.That(w.Selected(), Has.Count.EqualTo(3));
            Assert.That(w.Owned.All(c => c.Protected && c.Level == 1), Is.True);
            Assert.That((w.Coins, w.DeckLimit, w.CollectionLimit, w.HandSize), Is.EqualTo((0, 12, 24, 3)));
        }

        [Test]
        public void CollectionIsTwiceTheDeckLimit()
        {
            var w = Workshop(10000);
            Assert.That(w.CollectionLimit, Is.EqualTo(8));
            for (int i = 0; i < 5; i++) Assert.That(w.Purchase("ice", 1, out _), Is.Null);
            Assert.That(w.CollectionFull, Is.True);
            int coins = w.Coins;
            Assert.That(w.Purchase("ice", 1, out var refused), Is.Not.Null);
            Assert.That(refused, Is.Null);
            Assert.That(w.Coins, Is.EqualTo(coins), "No room: nothing is charged.");
        }

        [Test]
        public void DeckLimitStepsRaiseTheCollectionToo()
        {
            var w = Workshop(1000, 4, new[] { 6, 8 }, new[] { 300, 500 });
            Assert.That((w.NextDeckLimit, w.DeckLimitPrice), Is.EqualTo((6, 300)));
            Assert.That(w.ImproveDeckLimit(), Is.Null);
            Assert.That((w.DeckLimit, w.CollectionLimit, w.Coins), Is.EqualTo((6, 12, 700)));
            Assert.That(w.ImproveDeckLimit(), Is.Null);
            Assert.That(w.DeckAtMaximum, Is.True);
            Assert.That(w.ImproveDeckLimit(), Is.Not.Null);
            Assert.That(w.Coins, Is.EqualTo(200));
            var restored = Workshop(0, 4, new[] { 6, 8 }, new[] { 300, 500 });
            Assert.That(restored.TryRestoreProgress(w.Save()), Is.True);
            Assert.That(restored.DeckLimit, Is.EqualTo(8));
        }

        [Test]
        public void HandGrowsForTwoHundredThenFourHundred()
        {
            var w = new DeckWorkshop(new DeckRules(12, 3, 5, 3, 200, 600), Specs, 1);
            Assert.That(w.HandPrice, Is.EqualTo(200));
            w.ImproveHand();
            Assert.That(w.HandPrice, Is.EqualTo(400));
            w.ImproveHand();
            Assert.That((w.HandSize, w.Coins), Is.EqualTo((5, 0)));
        }

        [Test]
        public void BurningCostsThreeQuartersOfThePaidPriceRoundedUp()
        {
            var w = Workshop(1000);
            w.Purchase("fire", 50, out var blind);
            Assert.That(w.BurnPrice(blind), Is.EqualTo(38), "Board example: 50 → 37,5 → 38.");
            w.Purchase("ice", 120, out var full);
            w.Toggle(full.Id);
            Assert.That(w.BurnPrice(full), Is.EqualTo(90));
            int coins = w.Coins;
            Assert.That(w.Burn(full.Id), Is.Null);
            Assert.That(w.Find(full.Id), Is.Null);
            Assert.That(w.IsSelected(full.Id), Is.False, "A burnt copy leaves the deck.");
            Assert.That(w.Coins, Is.EqualTo(coins - 90));
        }

        [Test]
        public void StartingCopiesCannotBurnAndPovertyRefusesWithoutChanges()
        {
            var w = Workshop(30);
            foreach (var start in w.Owned.ToList()) Assert.That(w.Burn(start.Id), Is.Not.Null);
            w.Purchase("fire", 0, out var free);
            Assert.That(w.Burn(free.Id), Is.Null, "A free copy burns for free.");
            w.Purchase("ice", 0, out var copy);
            Assert.That(w.Owned, Has.Count.EqualTo(4));
            Assert.That(w.Coins, Is.EqualTo(30));
        }

        [Test]
        public void ThePoorCannotBurnAPricyCopy()
        {
            var w = Workshop(100);
            w.Purchase("teleport", 100, out var copy);
            w.Wallet.Credit(10);
            Assert.That(w.BurnPrice(copy), Is.EqualTo(75));
            Assert.That(w.Coins, Is.EqualTo(10));
            Assert.That(w.Burn(copy.Id), Is.Not.Null);
            Assert.That(w.Find(copy.Id), Is.Not.Null);
        }

        [Test]
        public void TeleportReturnsToTheHandWhileUsesRemain()
        {
            var w = Workshop(10000);
            foreach (var c in w.Selected()) w.Toggle(c.Id);
            w.Purchase("teleport", 0, out var a);
            w.Purchase("teleport", 0, out var b);
            w.Merge(a.Id, b.Id, out var two);
            w.Toggle(two.Id);
            w.Depart(1);
            var trip = w.Current;
            Assert.That(trip.Deck.Hand.Single().UsesLeft, Is.EqualTo(2));
            Assert.That(trip.Release(two.Id), Is.Not.Null);
            Assert.That(trip.Deck.Hand, Is.Empty, "In the air the card is out of the hand…");
            Assert.That(trip.Deck.HasReturn, Is.False, "…but its place is kept: no Return yet.");
            Assert.That(trip.Settle(two.Id), Is.True);
            Assert.That(trip.Settle(two.Id), Is.False, "Settling twice does nothing.");
            var back = trip.Deck.Hand.Single();
            Assert.That(back.UsesLeft, Is.EqualTo(1));
            trip.Release(back.Id);
            Assert.That(trip.Settle(back.Id), Is.False, "The last use goes to the spent pile.");
            Assert.That(trip.Deck.Discard.Single().Id, Is.EqualTo(two.Id));
            Assert.That(trip.Deck.HasReturn, Is.True);
            Assert.That(w.Find(two.Id).UsesLeft, Is.EqualTo(2), "The owned copy is untouched: full uses next outing.");
        }

        [Test]
        public void HeldTeleportKeepsItsPlaceAndBlocksAnExtraDraw()
        {
            var w = new DeckWorkshop(new DeckRules(12, 1, 1, 3, 0, 0), Specs, null,
                new[] { new StartingCopies("teleport", 2, 2), new StartingCopies("fire", 1, 1) });
            w.Merge(w.Owned[0].Id, w.Owned[1].Id, out var teleport);
            // A hand of one; find a shuffle that deals the teleport first.
            var trip = Enumerable.Range(0, 50).Select(seed => new Expedition(w.Selected(), 1, seed))
                .First(e => e.Deck.Hand.Single().Id == teleport.Id);
            trip.Release(teleport.Id);
            Assert.That(trip.Deck.Hand, Is.Empty, "The fire is not drawn into the teleport's place.");
            Assert.That(trip.Deck.StockCount, Is.EqualTo(1));
            trip.Settle(teleport.Id);
            Assert.That(trip.Deck.Hand.Single().Id, Is.EqualTo(teleport.Id));
            trip.Release(teleport.Id);
            Assert.That(trip.Deck.Hand.Single().Spec.Id, Is.EqualTo("fire"), "After the last use the hand draws as usual.");
        }
    }
}
