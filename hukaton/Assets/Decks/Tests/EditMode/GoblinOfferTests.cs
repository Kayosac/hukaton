using System;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    /// <summary>Board, «Гоблин-торговец — актуальные правила» (09.11.2026).</summary>
    public sealed class GoblinOfferTests
    {
        static readonly CardSpec[] Specs =
        {
            new CardSpec("fire", "Огонь", 100, 1), new CardSpec("ice", "Лёд", 121, 1), new CardSpec("fog", "Туман", 120, 2),
            new CardSpec("rope", "Верёвка", 180, 3), new CardSpec("updraft", "Поток", 260, 4), new CardSpec("teleport", "Телепорт", 450, 5)
        };

        static DeckWorkshop Workshop(int coins) => new DeckWorkshop(new DeckRules(startingCoins: coins), Specs, 0);

        static GoblinOffer Offer(int seed = 1) => GoblinOffer.Roll(Specs, new Random(seed));

        [Test]
        public void TenCardsWithRepeatsByRankWeights()
        {
            var counts = new int[6];
            for (int seed = 0; seed < 2000; seed++)
            {
                var offer = Offer(seed);
                Assert.That(offer.Count, Is.EqualTo(10));
                for (int i = 0; i < 10; i++) counts[Specs.First(s => s.Id == offer.CardAt(i)).Rank]++;
            }
            double total = counts.Sum();
            Assert.That(counts[1] / total, Is.EqualTo(.5).Within(.02), "Rank 1: 50%.");
            Assert.That(counts[2] / total, Is.EqualTo(.3).Within(.02));
            Assert.That(counts[3] / total, Is.EqualTo(.15).Within(.015));
            Assert.That(counts[4] / total, Is.EqualTo(.04).Within(.01));
            Assert.That(counts[5] / total, Is.EqualTo(.01).Within(.005));
            Assert.That(Enumerable.Range(0, 50).Any(seed => Offer(seed).Save().cards.Distinct().Count() < 10), "Cards may repeat.");
        }

        [Test]
        public void MissingRanksShareTheirChance()
        {
            var commons = new[] { new CardSpec("a", "A", 10, 1), new CardSpec("b", "B", 10, 3) };
            var offer = GoblinOffer.Roll(commons, new Random(4));
            Assert.That(offer.Save().cards.All(c => c == "a" || c == "b"), Is.True);
        }

        [Test]
        public void BlindPurchaseCostsHalfRoundedUpAndLeavesThePlaceEmpty()
        {
            var w = Workshop(1000);
            var offer = Offer();
            int slot = 0;
            var spec = w.Spec(offer.CardAt(slot));
            Assert.That(offer.PriceAt(slot, w), Is.EqualTo((spec.Price + 1) / 2));
            Assert.That(offer.Buy(slot, w, out var copy), Is.Null);
            Assert.That(copy.PaidPrice, Is.EqualTo((spec.Price + 1) / 2));
            Assert.That(w.Coins, Is.EqualTo(1000 - copy.PaidPrice));
            Assert.That(offer.IsSold(slot), Is.True);
            Assert.That(offer.Buy(slot, w, out _), Is.Not.Null, "The place stays empty.");
            Assert.That(offer.Revealed, Is.False, "The other cards stay face down.");
            Assert.That(new CardSpec("x", "X", 121).BlindPrice, Is.EqualTo(61));
        }

        [Test]
        public void RevealingIsFreeFinalAndRaisesPricesToFull()
        {
            var w = Workshop(1000);
            var offer = Offer();
            Assert.That(offer.RevealAll(), Is.True);
            Assert.That(offer.RevealAll(), Is.False);
            Assert.That(w.Coins, Is.EqualTo(1000));
            Assert.That(offer.PriceAt(3, w), Is.EqualTo(w.Spec(offer.CardAt(3)).Price));
            offer.Buy(3, w, out var copy);
            Assert.That(copy.PaidPrice, Is.EqualTo(w.Spec(offer.CardAt(3)).Price));
        }

        [Test]
        public void PoorOrFullBuyerChangesNothing()
        {
            var w = Workshop(10);
            var offer = Offer();
            Assert.That(offer.Buy(0, w, out var copy), Is.EqualTo("Не хватает монет."));
            Assert.That((copy, offer.IsSold(0), w.Coins, w.Owned.Count), Is.EqualTo(((CardCopy)null, false, 10, 0)));
            var full = new DeckWorkshop(new DeckRules(1, 1, 1, 3, 0, 1000), Specs, null, new[] { new StartingCopies("fire", 2, 1) });
            Assert.That(full.CollectionFull, Is.True);
            Assert.That(offer.Buy(1, full, out _), Is.EqualTo("В коллекции нет места."));
            Assert.That(full.Coins, Is.EqualTo(1000));
        }

        [Test]
        public void TheGoblinComesBackAfterAnOutingAndAWorkedShift()
        {
            var visits = new GoblinVisits();
            Assert.That(visits.CanCall, Is.True, "The first call is open at once.");
            Assert.That(visits.Call(Specs, new Random(1)), Is.True);
            var first = visits.Offer;
            Assert.That(visits.CanCall, Is.False);
            visits.ShiftClosed(false);
            visits.OutingReturned();
            Assert.That(visits.CanCall, Is.False, "Opening and closing without a sale is not a worked shift.");
            visits.ShiftClosed(true);
            Assert.That(visits.CanCall, Is.True);
            Assert.That(visits.Call(Specs, new Random(2)), Is.True);
            Assert.That(visits.Offer, Is.Not.SameAs(first), "A new visit, a new ten.");
        }

        [Test]
        public void SaveKeepsTheTenTheRevealAndThePurchases()
        {
            var w = Workshop(1000);
            var visits = new GoblinVisits();
            visits.Call(Specs, new Random(3));
            visits.Offer.Buy(2, w, out _);
            visits.Offer.RevealAll();
            visits.ReadIntroductionLine(4);
            visits.OutingReturned();
            var json = UnityEngine.JsonUtility.ToJson(visits.Save());
            var restored = new GoblinVisits();
            Assert.That(restored.TryRestore(UnityEngine.JsonUtility.FromJson<GoblinSave>(json), id => w.Spec(id) != null), Is.True);
            Assert.That(restored.Offer.Save().cards, Is.EqualTo(visits.Offer.Save().cards));
            Assert.That((restored.Offer.Revealed, restored.Offer.IsSold(2), restored.Offer.IsSold(1)), Is.EqualTo((true, true, false)));
            Assert.That(restored.IntroductionRead, Is.EqualTo(1));
            Assert.That(restored.CanCall, Is.False, "The shift is still owed.");
            restored.ShiftClosed(true);
            Assert.That(restored.CanCall, Is.True);
            Assert.That(restored.TryRestore(new GoblinSave { offer = new GoblinOfferSave { cards = { "nope" } } }, id => false), Is.False);
        }
    }
}
