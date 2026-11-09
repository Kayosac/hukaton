using System;
using System.Linq;
using System.Collections.Generic;
using Hakaton.Economy;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    /// <summary>Board, frame «единая колода»: one deck of chosen copies, one shared hand, Return when both run out.</summary>
    public sealed class DeckModelTests
    {
        static readonly CardSpec[] Specs = { new CardSpec("wind", "Ветер", 30, 20, 2, 1),
            new CardSpec("water", "Вода", 40, 25, 4, 2),
            new CardSpec("fire", "Огонь", 50, 30, 8, 2) };
        static DeckWorkshop Workshop(int copies = 3, int limit = 12) => new DeckWorkshop(
            new DeckRules(limit, 3, 5, 3, 60, 1000), Specs, copies);
        static int[] Ids(ExpeditionDeck deck) => deck.Hand.Select(c => c.Id).ToArray();

        [Test]
        public void GrantedCardJoinsHandWithoutBecomingOwned()
        {
            var w = Workshop();
            w.Depart(5);
            var e = w.Current;
            int owned = w.Owned.Count;
            var card = e.GrantToHand(Specs[2], 2);
            Assert.That(card, Is.Not.Null);
            Assert.That(card.IsReturn, Is.False);
            Assert.That(card.Level, Is.EqualTo(2));
            Assert.That(e.Deck.Hand, Does.Contain(card));
            Assert.That(w.Owned.Count, Is.EqualTo(owned));
            Assert.That(w.Find(card.Id), Is.Null);
            Assert.That(e.Release(card.Id), Is.SameAs(card));
            Assert.That(e.GrantToHand(Specs[0]).Id, Is.Not.EqualTo(card.Id));
        }

        [Test]
        public void StartingHandCountsInsideDeckLimit()
        {
            var w = Workshop();
            Assert.That(w.Selected().Count, Is.EqualTo(9));
            Assert.That(w.Depart(42), Is.Null);
            var d = w.Current.Deck;
            Assert.That(d.Total, Is.EqualTo(9));
            Assert.That(d.Hand.Count, Is.EqualTo(3));
            Assert.That(d.StockCount, Is.EqualTo(6));
        }

        [Test]
        public void CardsOfEveryEffectShareOneDeck()
        {
            var w = Workshop(4);
            Assert.That(w.Selected().Count, Is.EqualTo(12), "The limit covers all cards, whatever they are for.");
            Assert.That(w.Selected().Select(c => c.Spec.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(w.Owned.Count(c => !w.IsSelected(c.Id)), Is.Zero);
        }

        [Test]
        public void UnderfilledDeckDealsOnlyOwnedCopies()
        {
            var w = Workshop(1);
            w.Toggle(w.Owned[0].Id);
            w.Depart(1);
            Assert.That(w.Current.Deck.Hand.Count, Is.EqualTo(2));
            Assert.That(w.Current.Deck.StockCount, Is.Zero);
        }

        [Test]
        public void ZeroCardDeckBlocksDepartureButCanBeEdited()
        {
            var w = Workshop();
            foreach (var copy in w.Selected()) w.Toggle(copy.Id);
            Assert.That(w.Depart(1), Is.Not.Null);
            Assert.That(w.Current, Is.Null);
            w.Toggle(w.Owned[4].Id);
            Assert.That(w.Depart(1), Is.Null);
        }

        [Test]
        public void ReleaseIsExactlyOnceAndDrawsFromTheDeck()
        {
            var w = Workshop();
            w.Depart(9);
            var e = w.Current;
            int id = e.Deck.Hand[0].Id;
            int events = 0;
            e.CardReleased += c => events++;
            Assert.That(e.Release(id), Is.Not.Null);
            Assert.That(e.Release(id), Is.Null);
            Assert.That(e.Deck.StockCount, Is.EqualTo(5));
            Assert.That(e.Deck.Hand.Count, Is.EqualTo(3));
            Assert.That(e.Deck.Discard.Select(c => c.Id), Is.EqualTo(new[] { id }));
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void CardOutsideTheHandCannotBeReleased()
        {
            var w = Workshop();
            w.Depart(9);
            var e = w.Current;
            int inStock = w.Selected().Select(c => c.Id).First(id => !Ids(e.Deck).Contains(id));
            Assert.That(e.Release(inStock), Is.Null);
            Assert.That(e.Release(99999), Is.Null);
            Assert.That(e.PlayedCount, Is.Zero);
        }

        [Test]
        public void DiscardReplacesTheWholeHandAndNeverCallsEffects()
        {
            var w = Workshop();
            w.Depart(9);
            var e = w.Current;
            var before = Ids(e.Deck);
            int events = 0;
            e.CardReleased += c => events++;
            var discarded = e.DiscardHand();
            Assert.That(discarded.Select(c => c.Id), Is.EqualTo(before));
            Assert.That(events, Is.Zero);
            Assert.That(e.Deck.Hand.Count, Is.EqualTo(3));
            Assert.That(Ids(e.Deck).Intersect(before), Is.Empty);
            Assert.That(e.Deck.Discard.Count, Is.EqualTo(3));
            Assert.That(e.DiscardedCount, Is.EqualTo(3));
            Assert.That(e.Completed, Is.False);
        }

        [Test]
        public void EmptyStockDoesNotPrematurelyCreateReturn()
        {
            var w = Workshop(2);
            w.Depart(2);
            var e = w.Current;
            e.DiscardHand();
            Assert.That(e.Deck.StockCount, Is.Zero);
            Assert.That(e.Deck.HasReturn, Is.False);
            e.Release(e.Deck.Hand[0].Id);
            e.Release(e.Deck.Hand[0].Id);
            Assert.That(e.Deck.HasReturn, Is.False, "An ordinary card is still in hand.");
            e.Release(e.Deck.Hand[0].Id);
            Assert.That(e.Deck.HasReturn, Is.True);
            Assert.That(e.Completed, Is.False, "Its appearance does not end the outing.");
        }

        [Test]
        public void ReturnCannotBeDiscardedOrReleasedButTakesYouHome()
        {
            var w = Workshop(2);
            w.Depart(2);
            var e = w.Current;
            e.DiscardHand();
            e.DiscardHand();
            for (int i = 0; i < 5; i++) Assert.That(e.DiscardHand(), Is.Empty);
            Assert.That(e.Deck.Hand.Single().IsReturn, Is.True);
            Assert.That(e.Release(-1), Is.Null);
            Assert.That(e.ReturnHome(), Is.True);
            Assert.That(e.ReturnHome(), Is.False);
        }

        [Test]
        public void ReturnIsUnavailableWhileCardsRemain()
        {
            var w = Workshop();
            w.Depart(2);
            Assert.That(w.Current.ReturnHome(), Is.False);
            Assert.That(w.Current.Completed, Is.False);
        }

        [Test]
        public void AllCopiesReturnNextOutingAndUpgradesRemain()
        {
            var w = Workshop(2);
            var id = w.Owned[0].Id;
            w.Upgrade(id);
            var before = w.Owned.Select(c => c.Id).ToArray();
            w.Depart(2);
            w.Current.DiscardHand();
            w.Current.DiscardHand();
            w.Current.ReturnHome();
            w.Depart(3);
            Assert.That(w.Owned.Select(c => c.Id), Is.EqualTo(before));
            Assert.That(w.Find(id).Level, Is.EqualTo(2));
            Assert.That(w.Current.Deck.Total, Is.EqualTo(6));
            Assert.That(w.Current.Deck.Discard, Is.Empty);
        }

        [Test]
        public void UpgradeTouchesOnlyOneCopyAndChargesOnce()
        {
            var w = Workshop();
            var copy = w.Owned[0];
            Assert.That(w.Upgrade(copy.Id), Is.Null);
            Assert.That(copy.Level, Is.EqualTo(2));
            Assert.That(copy.Power, Is.EqualTo(3));
            Assert.That(w.Coins, Is.EqualTo(980));
            Assert.That(w.Owned[1].Level, Is.EqualTo(1));
            w.Upgrade(copy.Id);
            int coins = w.Coins;
            Assert.That(w.Upgrade(copy.Id), Is.Not.Null);
            Assert.That(w.Coins, Is.EqualTo(coins));
        }

        [Test]
        public void HandCapacityUpgradesIndependentlyWithoutAddingCopies()
        {
            var w = Workshop();
            Assert.That(w.HandPrice, Is.EqualTo(60));
            w.ImproveHand();
            Assert.That(w.HandPrice, Is.EqualTo(120), "Each next place costs more.");
            w.ImproveHand();
            Assert.That(w.ImproveHand(), Is.Not.Null);
            Assert.That(w.Selected().Count, Is.EqualTo(9), "A bigger hand does not raise the deck limit.");
            w.Depart(1);
            Assert.That(w.Current.Deck.Hand.Count, Is.EqualTo(5));
            Assert.That(w.Current.Deck.StockCount, Is.EqualTo(4));
            Assert.That(w.Coins, Is.EqualTo(820));
        }

        [Test]
        public void BuyingProducesUniqueUnequippedCopyWithFixedMembership()
        {
            var w = Workshop();
            Assert.That(w.Buy("wind", out var copy), Is.Null);
            Assert.That(w.Coins, Is.EqualTo(970));
            Assert.That(w.IsSelected(copy.Id), Is.False);
            Assert.That(w.Owned.Select(c => c.Id).Distinct().Count(), Is.EqualTo(w.Owned.Count));
            w.Toggle(copy.Id);
            Assert.That(w.Selected(), Does.Contain(copy));
        }

        [Test]
        public void CannotOverfillDeckOrEquipUnknownCopy()
        {
            var w = Workshop(2, 6);
            w.Buy("wind", out var copy);
            Assert.That(w.Toggle(copy.Id), Is.Not.Null);
            Assert.That(w.Toggle(998), Is.Not.Null);
            Assert.That(w.Selected().Count, Is.EqualTo(6));
        }

        [Test]
        public void EconomyRejectsInsufficientFundsWithoutMutation()
        {
            var w = new DeckWorkshop(new DeckRules(startingCoins: 0), Specs);
            int count = w.Owned.Count;
            Assert.That(w.Buy("fire", out var copy), Is.Not.Null);
            Assert.That(copy, Is.Null);
            Assert.That(w.Upgrade(1), Is.Not.Null);
            Assert.That(w.ImproveHand(), Is.Not.Null);
            Assert.That(w.Coins, Is.Zero);
            Assert.That(w.Owned.Count, Is.EqualTo(count));
            Assert.That(w.Find(1).Level, Is.EqualTo(1));
        }

        [Test]
        public void WorkshopCannotMutateDuringOuting()
        {
            var w = Workshop();
            w.Depart(6);
            var save = w.Save();
            Assert.That(w.Toggle(1), Is.Not.Null);
            Assert.That(w.Upgrade(1), Is.Not.Null);
            Assert.That(w.Buy("fire", out _), Is.Not.Null);
            Assert.That(w.ImproveHand(), Is.Not.Null);
            Assert.That(w.TryRestore(save), Is.False);
            Assert.That(w.Coins, Is.EqualTo(save.coins));
        }

        [Test]
        public void ShuffleIsSeeded()
        {
            var a = Workshop();
            var b = Workshop();
            a.Depart(468);
            b.Depart(468);
            Assert.That(Ids(a.Current.Deck), Is.EqualTo(Ids(b.Current.Deck)));
            var c = Workshop();
            c.Depart(469);
            Assert.That(Ids(a.Current.Deck), Is.Not.EqualTo(Ids(c.Current.Deck)));
        }

        [Test]
        public void CopyConservationHoldsThroughEveryRelease()
        {
            for (int seed = 0; seed < 25; seed++)
            {
                var w = Workshop();
                w.Depart(seed);
                var e = w.Current;
                var d = e.Deck;
                while (!d.HasReturn)
                {
                    e.Release(d.Hand[0].Id);
                    Assert.That(d.StockCount + d.Hand.Count(c => !c.IsReturn) + d.Discard.Count, Is.EqualTo(d.Total));
                    Assert.That(d.Discard.Select(c => c.Id).Distinct().Count(), Is.EqualTo(d.Discard.Count));
                }
                Assert.That(d.Discard.Count, Is.EqualTo(d.Total));
            }
        }

        [Test]
        public void SaveRoundTripPreservesIdentitySelectionWalletAndHand()
        {
            var a = Workshop();
            a.Buy("wind", out var copy);
            a.Toggle(copy.Id);
            a.Toggle(2);
            a.Upgrade(copy.Id);
            a.ImproveHand();
            var save = a.Save();
            Assert.That(save.version, Is.EqualTo(2));
            var b = Workshop();
            Assert.That(b.TryRestore(save), Is.True);
            Assert.That(b.Coins, Is.EqualTo(a.Coins));
            Assert.That(b.Find(copy.Id).Level, Is.EqualTo(2));
            Assert.That(b.Selected().Select(c => c.Id), Is.EqualTo(a.Selected().Select(c => c.Id)));
            Assert.That(b.HandSize, Is.EqualTo(4));
            b.Buy("fire", out var newer);
            Assert.That(newer.Id, Is.GreaterThan(copy.Id));
        }

        [Test]
        public void TwoDeckSaveKeepsCollectionPaidHandAndEveryEquippedType()
        {
            var w = Workshop(6);
            var old = w.Save();
            old.version = 1;
            old.hand = 0;
            old.explorationHand = 3;
            old.combatHand = 4;
            // Version 1 allowed 12 cards in each deck: wind 6, water 6 and fire 2 equipped, fire listed last.
            old.selected = w.Owned.Where(c => c.Spec.Id != "fire" || c.Id <= 14).Select(c => c.Id).ToList();
            Assert.That(old.selected.Count, Is.EqualTo(14));
            var target = Workshop(6);
            Assert.That(target.TryRestore(old), Is.True);
            Assert.That(target.Owned.Count, Is.EqualTo(18));
            Assert.That(target.HandSize, Is.EqualTo(4), "The larger paid hand is kept.");
            var kept = target.Selected();
            Assert.That(kept.Count, Is.EqualTo(12));
            Assert.That(kept.Select(c => c.Id), Is.SubsetOf(old.selected));
            Assert.That(kept.Count(c => c.Spec.Id == "fire"), Is.EqualTo(2), "The rarest type is never dropped.");
            Assert.That(kept.Count(c => c.Spec.Id == "wind"), Is.EqualTo(5));
            Assert.That(kept.Count(c => c.Spec.Id == "water"), Is.EqualTo(5));
            Assert.That(target.Save().version, Is.EqualTo(2));
        }

        [Test]
        public void CurrentSaveCannotExceedTheDeckLimit()
        {
            var w = Workshop(5);
            var bad = w.Save();
            bad.selected = w.Owned.Select(c => c.Id).ToList();
            Assert.That(w.CanRestoreProgress(bad), Is.False);
            bad = w.Save();
            bad.version = 3;
            Assert.That(w.CanRestoreProgress(bad), Is.False);
        }

        [Test]
        public void StartingCollectionAndStartingDeckAreConfiguredSeparately()
        {
            var specs = new[] { new CardSpec("impulse", "Импульс", 120, 120, 8, 1),
                new CardSpec("ice", "Лёд", 120, 100, 2, 1),
                new CardSpec("fire", "Огонь", 100, 100, 1, 1),
                new CardSpec("strike", "Удар", 120, 100, 3, 1),
                new CardSpec("heavy", "Тяжёлый удар", 140, 140, 2, 0),
                new CardSpec("heal", "Лечение", 140, 120, 2, 1) };
            var counts = new Dictionary<string, int> { { "impulse", 3 }, { "ice", 3 }, { "fire", 2 }, { "strike", 5 },
                { "heavy", 0 }, { "heal", 2 } };
            var deck = new Dictionary<string, int> { { "impulse", 2 }, { "ice", 2 }, { "fire", 2 }, { "strike", 4 },
                { "heavy", 0 }, { "heal", 2 } };
            var w = new DeckWorkshop(new DeckRules(maxCardLevel: 2, handUpgradePrice: 200, startingCoins: 0), specs,
                initialCopies: counts, initialDeck: deck);
            Assert.That(w.Owned.Count, Is.EqualTo(15));
            Assert.That(w.Selected().Count, Is.EqualTo(12));
            Assert.That(w.Selected().Count(c => c.Spec.Id == "strike"), Is.EqualTo(4));
            Assert.That(w.Owned.Count(c => !w.IsSelected(c.Id)), Is.EqualTo(3), "The rest waits in the collection.");
            Assert.That(w.HandPrice, Is.EqualTo(200));
            Assert.That(w.Depart(42), Is.Null);
            Assert.That(w.Current.Deck.StockCount, Is.EqualTo(9));
        }

        [Test]
        public void SharedProgressRestoreNeverCreditsOrReplacesSceneWallet()
        {
            var source = Workshop();
            source.Buy("wind", out var copy);
            source.Toggle(copy.Id);
            source.Upgrade(copy.Id);
            source.ImproveHand();
            var wallet = new CoinWallet(73);
            var target = new DeckWorkshop(source.Rules, Specs, 1, wallet);
            var save = source.Save();
            Assert.That(target.TryRestore(save), Is.False);
            Assert.That(target.CanRestoreProgress(save), Is.True);
            Assert.That(target.TryRestoreProgress(save), Is.True);
            Assert.That(target.TryRestoreProgress(save), Is.True, "Restoring twice must not add money twice.");
            Assert.That(wallet.Balance, Is.EqualTo(73));
            Assert.That(target.Coins, Is.EqualTo(73));
            Assert.That(target.Find(copy.Id).Level, Is.EqualTo(2));
            Assert.That(target.HandSize, Is.EqualTo(4));
            Assert.That(target.Buy("wind", out _), Is.Null);
            Assert.That(wallet.Balance, Is.EqualTo(43), "Purchases must still spend the original scene wallet.");
        }

        [Test]
        public void InvalidSharedRestoreDoesNotPartiallyChangeCardsOrNotify()
        {
            var wallet = new CoinWallet(73);
            var w = new DeckWorkshop(new DeckRules(), Specs, 1, wallet);
            int changes = 0;
            w.Changed += () => changes++;
            var bad = w.Save();
            bad.selected.Add(9999);
            Assert.That(w.CanRestoreProgress(bad), Is.False);
            Assert.That(w.TryRestoreProgress(bad), Is.False);
            Assert.That(w.Owned.Count, Is.EqualTo(3));
            Assert.That(wallet.Balance, Is.EqualTo(73));
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void SuccessfulProgressChangesNotifyAfterCompleteMutation()
        {
            var w = Workshop();
            int changes = 0;
            w.Changed += () =>
            {
                changes++;
                Assert.That(w.Save().copies.Count, Is.EqualTo(w.Owned.Count));
            };
            Assert.That(w.Toggle(999), Is.Not.Null);
            Assert.That(changes, Is.Zero);
            w.Toggle(1);
            w.Buy("wind", out var copy);
            w.Upgrade(copy.Id);
            w.ImproveHand();
            Assert.That(changes, Is.EqualTo(4));
            w.Depart(1);
            Assert.That(changes, Is.EqualTo(5));
            Assert.That(w.Upgrade(copy.Id), Is.Not.Null);
            Assert.That(changes, Is.EqualTo(5), "Rejected mutations during an outing cannot request a save.");
        }

        [Test]
        public void InvalidSaveIsRejectedAtomically()
        {
            var w = Workshop();
            var bad = w.Save();
            bad.copies[1].id = bad.copies[0].id;
            Assert.That(w.TryRestore(bad), Is.False);
            Assert.That(w.Owned.Count, Is.EqualTo(9));
            Assert.That(w.Find(2), Is.Not.Null);
            bad = w.Save();
            bad.selected.Add(999);
            Assert.That(w.TryRestore(bad), Is.False);
            bad = w.Save();
            bad.copies[0].level = 999;
            Assert.That(w.TryRestore(bad), Is.False);
            bad = w.Save();
            bad.hand = 9;
            Assert.That(w.TryRestore(bad), Is.False);
        }
    }
}
