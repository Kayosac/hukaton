using System;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Playtest 12.11.2026: the goblin's dummy («Пустышка»), the penalty for buying blind. About one place in ten holds
    /// it (never on his first visit); it costs 25 face down and face up, does nothing, takes a place in the collection and
    /// the deck like any card, never merges, and leaves the deck and the collection for good as the next outing begins.
    /// </summary>
    public sealed class DummyCardTests
    {
        static readonly CardSpec Dummy = new CardSpec("dummy", "Пустышка", 50, 1, true, null, dummy: true);

        /// <summary>The goblin's test catalogue (as in <see cref="GoblinOfferTests"/>) plus the dummy.</summary>
        static readonly CardSpec[] Shop =
        {
            new CardSpec("fire", "Огонь", 100, 1), new CardSpec("ice", "Лёд", 121, 1), new CardSpec("fog", "Туман", 120, 2),
            new CardSpec("rope", "Верёвка", 180, 3), new CardSpec("updraft", "Поток", 260, 4), new CardSpec("teleport", "Телепорт", 450, 5)
        };
        static readonly CardSpec[] ShopWithDummy = Shop.Concat(new[] { Dummy }).ToArray();

        static readonly CardSpec[] Specs = { new CardSpec("fire", "Огонь", 100), new CardSpec("ice", "Лёд", 120), Dummy };

        /// <summary>Deck of 4 (hand 3), collection of 8; the two cave cards start in the deck.</summary>
        static DeckWorkshop Workshop(int coins = 1000) => new DeckWorkshop(new DeckRules(4, 3, 4, 3, 200, coins), Specs, null,
            new[] { new StartingCopies("fire", 1, 1, true), new StartingCopies("ice", 1, 1, true) });

        static bool HasDummy(GoblinOffer offer) => Enumerable.Range(0, offer.Count).Any(i => offer.CardAt(i) == "dummy");

        [Test]
        public void ADummyNeverMergesEvenIfAskedTo()
        {
            Assert.That(Dummy.Mergeable, Is.False);
            Assert.That((Dummy.Dummy, Dummy.BlindPrice), Is.EqualTo((true, 25)));
            Assert.That(new CardSpec("fire", "Огонь", 100).Dummy, Is.False);
        }

        // ---------- the goblin ----------

        [Test]
        public void WithoutAChanceTheTenAreRolledExactlyAsBefore()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var plain = GoblinOffer.Roll(Shop, new Random(seed)).Save().cards;
                Assert.That(GoblinOffer.Roll(ShopWithDummy, new Random(seed)).Save().cards, Is.EqualTo(plain),
                    "The dummy never enters the rank draw.");
                Assert.That(GoblinOffer.Roll(ShopWithDummy, new Random(seed), 0).Save().cards, Is.EqualTo(plain));
                Assert.That(GoblinOffer.Roll(Shop, new Random(seed), .5).Save().cards, Is.EqualTo(plain),
                    "No dummy in the catalogue: the chance takes nothing.");
            }
        }

        [Test]
        public void AboutOnePlaceInTenHoldsADummy()
        {
            int dummies = 0, places = 0;
            for (int seed = 0; seed < 2000; seed++)
            {
                var offer = GoblinOffer.Roll(ShopWithDummy, new Random(seed), .1);
                for (int i = 0; i < offer.Count; i++, places++)
                    if (offer.CardAt(i) == "dummy") dummies++;
            }
            Assert.That(dummies / (double)places, Is.EqualTo(.1).Within(.01));
        }

        [Test]
        public void ADummyCostsTwentyFiveFaceDownAndFaceUpAndGoesIntoTheDeck()
        {
            var w = new DeckWorkshop(new DeckRules(startingCoins: 1000), ShopWithDummy, 0);
            var offer = Enumerable.Range(0, 100).Select(seed => GoblinOffer.Roll(ShopWithDummy, new Random(seed), .3)).First(HasDummy);
            int slot = Enumerable.Range(0, offer.Count).First(i => offer.CardAt(i) == "dummy");
            int other = Enumerable.Range(0, offer.Count).First(i => offer.CardAt(i) != "dummy");
            Assert.That(offer.PriceAt(slot, w), Is.EqualTo(25), "Face down it passes for any card.");
            Assert.That(offer.RevealAll(), Is.True);
            Assert.That(offer.PriceAt(slot, w), Is.EqualTo(25), "Face up: still 25.");
            Assert.That(offer.PriceAt(other, w), Is.EqualTo(w.Spec(offer.CardAt(other)).Price), "The others go up to full.");
            Assert.That(offer.Buy(slot, w, out var copy), Is.Null);
            Assert.That((copy.Spec.Dummy, copy.PaidPrice, copy.Level, w.Coins), Is.EqualTo((true, 25, 1, 975)));
            Assert.That(w.IsSelected(copy.Id), Is.True, "Equipped like any purchase while the deck has room.");
        }

        [Test]
        public void TheFirstVisitNeverLaysOutADummy()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var visits = new GoblinVisits();
                Assert.That(visits.Call(ShopWithDummy, new Random(seed), 1), Is.True);
                Assert.That(HasDummy(visits.Offer), Is.False, "The introduction visit.");
                Assert.That(visits.Offer.Save().cards, Is.EqualTo(GoblinOffer.Roll(Shop, new Random(seed)).Save().cards));
            }
            var later = new GoblinVisits();
            later.Call(ShopWithDummy, new Random(1), 1);
            later.OutingReturned();
            later.ShiftClosed(true);
            Assert.That(later.Call(ShopWithDummy, new Random(2), 1), Is.True);
            Assert.That(later.Offer.Save().cards, Is.All.EqualTo("dummy"), "Later visits use the chance (here: every place).");
        }

        [Test]
        public void AnOfferWithADummySurvivesASave()
        {
            var w = new DeckWorkshop(new DeckRules(startingCoins: 1000), ShopWithDummy, 0);
            var visits = new GoblinVisits();
            visits.Call(ShopWithDummy, new Random(1), .3);
            visits.OutingReturned();
            visits.ShiftClosed(true);
            visits.Call(ShopWithDummy, new Random(5), .9);
            Assert.That(HasDummy(visits.Offer), Is.True);
            var json = UnityEngine.JsonUtility.ToJson(visits.Save());
            var restored = new GoblinVisits();
            Assert.That(restored.TryRestore(UnityEngine.JsonUtility.FromJson<GoblinSave>(json), id => w.Spec(id) != null), Is.True);
            Assert.That(restored.Offer.Save().cards, Is.EqualTo(visits.Offer.Save().cards));
        }

        // ---------- the collection and the deck ----------

        [Test]
        public void ABoughtDummyTakesItsPlaceAndNeverMerges()
        {
            var w = Workshop();
            Assert.That(w.Purchase("dummy", 25, out var a, equip: true), Is.Null);
            Assert.That(w.Purchase("dummy", 25, out var b, equip: true), Is.Null);
            Assert.That(w.IsSelected(a.Id) && w.IsSelected(b.Id), Is.True);
            Assert.That((w.Owned.Count, w.Selected().Count), Is.EqualTo((4, 4)), "Two places in the deck, two in the collection.");
            Assert.That(w.Purchase("fire", 0, out var fire, equip: true), Is.Null);
            Assert.That(w.IsSelected(fire.Id), Is.False, "The dummies fill the deck like any card.");
            Assert.That(w.CanMerge(a, b), Is.EqualTo("Эту карту нельзя объединить."));
            Assert.That(w.MergePartners(a), Is.Empty);
            Assert.That(w.Merge(a.Id, b.Id, out _), Is.Not.Null);
            Assert.That(w.BurnPrice(a), Is.EqualTo(19), "Burning costs 75% of the 25 paid, rounded up.");
            Assert.That(w.Burn(a.Id), Is.Null);
            Assert.That(w.Find(a.Id), Is.Null);
        }

        [Test]
        public void DepartingStripsTheDeckOfDummiesForGood()
        {
            var w = Workshop();
            w.Purchase("dummy", 25, out var a, equip: true);
            w.Purchase("dummy", 25, out var b, equip: true);
            w.Purchase("dummy", 25, out var spare);
            Assert.That(w.IsSelected(spare.Id), Is.False);
            Assert.That(w.Depart(7), Is.Null);
            var trip = w.Current;
            Assert.That(trip.Dummies.Select(c => c.Id), Is.EquivalentTo(new[] { a.Id, b.Id }), "Shown flying off.");
            Assert.That(trip.Deck.Total, Is.EqualTo(2), "The outing leaves with the two real cards.");
            Assert.That(trip.Deck.Hand.Select(c => c.Spec.Id), Is.EquivalentTo(new[] { "fire", "ice" }));
            Assert.That(trip.Deck.StockCount, Is.Zero);
            Assert.That((w.Find(a.Id), w.Find(b.Id)), Is.EqualTo(((CardCopy)null, (CardCopy)null)), "Gone from the collection.");
            Assert.That(w.Find(spare.Id), Is.Not.Null, "A dummy left in the collection stays there.");
            Assert.That(w.Save().copies.Select(c => c.definition), Is.EquivalentTo(new[] { "fire", "ice", "dummy" }));
            Assert.That(trip.ReturnHome(), Is.False);
        }

        [Test]
        public void ADeckOfOnlyDummiesSetsOffWithTheWayHome()
        {
            var w = Workshop();
            foreach (var c in w.Selected()) w.Toggle(c.Id);
            w.Purchase("dummy", 25, out _, equip: true);
            Assert.That(w.Depart(1), Is.Null);
            Assert.That(w.Current.Dummies, Has.Count.EqualTo(1));
            Assert.That(w.Current.Deck.Total, Is.Zero);
            Assert.That(w.Current.Deck.HasReturn, Is.True);
            Assert.That(w.Owned.Select(c => c.Spec.Id), Is.EquivalentTo(new[] { "fire", "ice" }));
        }

        [Test]
        public void AnOutingWithoutDummiesListsNone()
        {
            var w = Workshop();
            Assert.That(w.Depart(3), Is.Null);
            Assert.That(w.Current.Dummies, Is.Empty);
            Assert.That(w.Current.Deck.Total, Is.EqualTo(2));
        }

        [Test]
        public void ASavedDummyLoadsBack()
        {
            var w = Workshop();
            w.Purchase("dummy", 25, out var dummy, equip: true);
            var json = UnityEngine.JsonUtility.ToJson(w.Save());
            var restored = Workshop(0);
            Assert.That(restored.TryRestoreProgress(UnityEngine.JsonUtility.FromJson<WorkshopSave>(json)), Is.True);
            var copy = restored.Find(dummy.Id);
            Assert.That(copy, Is.Not.Null);
            Assert.That((copy.Spec.Dummy, copy.PaidPrice, copy.Level, restored.IsSelected(copy.Id)), Is.EqualTo((true, 25, 1, true)));
            var noDummy = new DeckWorkshop(new DeckRules(4, 3, 4, 3, 200, 0), Specs.Take(2), 0);
            Assert.That(noDummy.TryRestoreProgress(w.Save()), Is.False, "Without the dummy in the catalogue its id is unknown.");
        }

        [Test]
        public void TheDummyIsNeverGivenByTheUnlockOrAtTheStart()
        {
            Assert.That(new DeckWorkshop(new DeckRules(), Specs, 2).Owned.Any(c => c.Spec.Dummy), Is.False,
                "Copies per type skip the dummy.");
            var w = new DeckWorkshop(new DeckRules(4, 3, 4, 3, 200, 0), Specs, null, new[] { new StartingCopies("fire", 1, 1) });
            Assert.That(w.UnlockAllCards(out int added), Is.Null);
            Assert.That(added, Is.EqualTo(1));
            Assert.That(w.Owned.Select(c => c.Spec.Id), Is.EquivalentTo(new[] { "fire", "ice" }));
        }
    }
}
