using System;
using System.Linq;
using System.Collections.Generic;
using Hakaton.Economy;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    public sealed class DeckModelTests
    {
        static readonly CardSpec[] Specs = { new CardSpec("wind", "Ветер", DeckKind.Exploration, 30, 20, 2, 1),
            new CardSpec("water", "Вода", DeckKind.Exploration, 40, 25, 4, 2),
            new CardSpec("fire", "Огонь", DeckKind.Combat, 50, 30, 8, 2) };
        static DeckWorkshop Workshop(int copies = 3, int limit = 12) => new DeckWorkshop(
            new DeckRules(limit, 3, 5, 3, 60, 1000), Specs, copies);
        static int[] Ids(ExpeditionDeck deck) => deck.Hand.Select(c => c.Id).ToArray();
        [Test]
        public void StartingHandsCountInsideDeckLimit()
        {
            var w = Workshop();
            Assert.That(w.Depart(42), Is.Null);
            var d = w.Current.Get(DeckKind.Exploration);
            Assert.That(d.Total, Is.EqualTo(6));
            Assert.That(d.Hand.Count, Is.EqualTo(3));
            Assert.That(d.StockCount, Is.EqualTo(3));
        }

        [Test]
        public void UnderfilledDeckDealsOnlyOwnedCopies()
        {
            var w = Workshop(1);
            w.Depart(1);
            Assert.That(w.Current.Active.Hand.Count, Is.EqualTo(2));
            Assert.That(w.Current.Active.StockCount, Is.Zero);
        }

        [Test]
        public void ZeroCardDeckBlocksDepartureButCanBeEdited()
        {
            var w = Workshop();
            foreach (var copy in w.Selected(DeckKind.Exploration)) w.Toggle(copy.Id);
            Assert.That(w.Depart(1), Is.Not.Null);
            Assert.That(w.Current, Is.Null);
        }

        [Test]
        public void BothHandsSurviveRepeatedSwitches()
        {
            var w = Workshop();
            w.Depart(77);
            var e = w.Current;
            var a = Ids(e.Active);
            e.Switch(DeckKind.Combat);
            var b = Ids(e.Active);
            for (int i = 0; i < 8; i++)
            {
                e.Switch(DeckKind.Exploration);
                Assert.That(Ids(e.Active), Is.EqualTo(a));
                e.Switch(DeckKind.Combat);
                Assert.That(Ids(e.Active), Is.EqualTo(b));
            }
        }

        [Test]
        public void ReleaseIsExactlyOnceAndDrawsFromSameDeck()
        {
            var w = Workshop();
            w.Depart(9);
            var e = w.Current;
            int id = e.Active.Hand[0].Id;
            int events = 0;
            e.CardReleased += c => events++;
            Assert.That(e.Release(id), Is.Not.Null);
            Assert.That(e.Release(id), Is.Null);
            Assert.That(e.Active.StockCount, Is.EqualTo(2));
            Assert.That(e.Active.Hand.Count, Is.EqualTo(3));
            Assert.That(e.Active.Discard.Select(c => c.Id), Is.EqualTo(new[] { id }));
            Assert.That(events, Is.EqualTo(1));
            Assert.That(e.Get(DeckKind.Combat).Discard, Is.Empty);
        }

        [Test]
        public void InactiveOrUnownedCopyCannotBeReleased()
        {
            var w = Workshop();
            w.Depart(9);
            var e = w.Current;
            Assert.That(e.Release(e.Get(DeckKind.Combat).Hand[0].Id), Is.Null);
            Assert.That(e.Release(99999), Is.Null);
            Assert.That(e.PlayedCount, Is.Zero);
        }

        [Test]
        public void DiscardAffectsOneHandAndNeverCallsEffects()
        {
            var w = Workshop();
            w.Depart(9);
            var e = w.Current;
            var combat = Ids(e.Get(DeckKind.Combat));
            int events = 0;
            e.CardReleased += c => events++;
            var discarded = e.DiscardHand();
            Assert.That(discarded.Count, Is.EqualTo(3));
            Assert.That(events, Is.Zero);
            Assert.That(e.Active.Hand.Count, Is.EqualTo(3));
            Assert.That(e.Active.Discard.Count, Is.EqualTo(3));
            Assert.That(Ids(e.Get(DeckKind.Combat)), Is.EqualTo(combat));
            Assert.That(e.Completed, Is.False);
        }

        [Test]
        public void EmptyStockDoesNotPrematurelyCreateReturn()
        {
            var w = Workshop();
            w.Depart(2);
            var e = w.Current;
            e.DiscardHand();
            Assert.That(e.Active.StockCount, Is.Zero);
            Assert.That(e.Active.HasReturn, Is.False);
            e.Release(e.Active.Hand[0].Id);
            e.Release(e.Active.Hand[0].Id);
            Assert.That(e.Active.HasReturn, Is.False);
            e.Release(e.Active.Hand[0].Id);
            Assert.That(e.Active.HasReturn, Is.True);
            Assert.That(e.Completed, Is.False);
            Assert.That(e.Get(DeckKind.Combat).Hand.Count, Is.EqualTo(3));
        }

        [Test]
        public void ReturnCannotBeDiscardedAndRequiresExplorationActive()
        {
            var w = Workshop();
            w.Depart(2);
            var e = w.Current;
            e.DiscardHand();
            e.DiscardHand();
            for (int i = 0; i < 5; i++) Assert.That(e.DiscardHand(), Is.Empty);
            Assert.That(e.Active.Hand.Single().IsReturn, Is.True);
            Assert.That(e.Release(-1), Is.Null);
            e.Switch(DeckKind.Combat);
            Assert.That(e.ReturnHome(), Is.False);
            e.Switch(DeckKind.Exploration);
            Assert.That(e.ReturnHome(), Is.True);
            Assert.That(e.ReturnHome(), Is.False);
        }

        [Test]
        public void ExhaustedCombatDoesNotGenerateReturnOrReshuffle()
        {
            var w = Workshop();
            w.Depart(2);
            var e = w.Current;
            e.Switch(DeckKind.Combat);
            e.DiscardHand();
            Assert.That(e.Active.Hand, Is.Empty);
            Assert.That(e.Active.StockCount, Is.Zero);
            Assert.That(e.Active.Discard.Count, Is.EqualTo(3));
            Assert.That(e.DiscardHand(), Is.Empty);
            Assert.That(e.Active.HasReturn, Is.False);
        }

        [Test]
        public void AllCopiesReturnNextOutingAndUpgradesRemain()
        {
            var w = Workshop();
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
            Assert.That(w.Current.Active.Total, Is.EqualTo(6));
            Assert.That(w.Current.Active.Discard, Is.Empty);
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
            w.ImproveHand(DeckKind.Exploration);
            w.ImproveHand(DeckKind.Exploration);
            Assert.That(w.ImproveHand(DeckKind.Exploration), Is.Not.Null);
            w.Depart(1);
            Assert.That(w.Current.Active.Hand.Count, Is.EqualTo(5));
            Assert.That(w.Current.Active.StockCount, Is.EqualTo(1));
            Assert.That(w.Current.Get(DeckKind.Combat).Capacity, Is.EqualTo(3));
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
            Assert.That(w.Selected(DeckKind.Exploration), Does.Contain(copy));
            Assert.That(w.Selected(DeckKind.Combat), Has.No.Member(copy));
        }

        [Test]
        public void CannotOverfillDeckOrEquipUnknownCopy()
        {
            var w = Workshop(3, 6);
            w.Buy("wind", out var copy);
            Assert.That(w.Toggle(copy.Id), Is.Not.Null);
            Assert.That(w.Toggle(998), Is.Not.Null);
            Assert.That(w.Selected(DeckKind.Exploration).Count, Is.EqualTo(6));
        }

        [Test]
        public void EconomyRejectsInsufficientFundsWithoutMutation()
        {
            var w = new DeckWorkshop(new DeckRules(startingCoins: 0), Specs);
            int count = w.Owned.Count;
            Assert.That(w.Buy("fire", out var copy), Is.Not.Null);
            Assert.That(copy, Is.Null);
            Assert.That(w.Upgrade(1), Is.Not.Null);
            Assert.That(w.ImproveHand(DeckKind.Combat), Is.Not.Null);
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
            Assert.That(w.ImproveHand(DeckKind.Combat), Is.Not.Null);
            Assert.That(w.TryRestore(save), Is.False);
            Assert.That(w.Coins, Is.EqualTo(save.coins));
        }

        [Test]
        public void ShuffleIsSeededAndIndependentBetweenDecks()
        {
            var a = Workshop();
            var b = Workshop();
            b.Buy("fire", out var copy);
            b.Toggle(copy.Id);
            a.Depart(468);
            b.Depart(468);
            Assert.That(Ids(a.Current.Active), Is.EqualTo(Ids(b.Current.Active)));
            var c = Workshop();
            c.Depart(469);
            Assert.That(Ids(a.Current.Active), Is.Not.EqualTo(Ids(c.Current.Active)));
        }

        [Test]
        public void CopyConservationHoldsThroughEveryRelease()
        {
            for (int seed = 0; seed < 25; seed++)
            {
                var w = Workshop();
                w.Depart(seed);
                var e = w.Current;
                while (!e.Active.HasReturn)
                {
                    var d = e.Active;
                    e.Release(d.Hand[0].Id);
                    Assert.That(d.StockCount + d.Hand.Count(c => !c.IsReturn) + d.Discard.Count, Is.EqualTo(d.Total));
                    Assert.That(d.Discard.Select(c => c.Id).Distinct().Count(), Is.EqualTo(d.Discard.Count));
                }
            }
        }

        [Test]
        public void SaveRoundTripPreservesIdentitySelectionWalletAndHands()
        {
            var a = Workshop();
            a.Buy("wind", out var copy);
            a.Toggle(copy.Id);
            a.Toggle(2);
            a.Upgrade(copy.Id);
            a.ImproveHand(DeckKind.Combat);
            var b = Workshop();
            Assert.That(b.TryRestore(a.Save()), Is.True);
            Assert.That(b.Coins, Is.EqualTo(a.Coins));
            Assert.That(b.Find(copy.Id).Level, Is.EqualTo(2));
            Assert.That(b.Selected(DeckKind.Exploration).Select(c => c.Id),
                Is.EqualTo(a.Selected(DeckKind.Exploration).Select(c => c.Id)));
            Assert.That(b.HandSize(DeckKind.Combat), Is.EqualTo(4));
            b.Buy("fire", out var newer);
            Assert.That(newer.Id, Is.GreaterThan(copy.Id));
        }

        [Test]
        public void ExplicitStartingCopiesSupportZeroAndDifferentDeckTotals()
        {
            var specs = new[] { new CardSpec("impulse", "Импульс", DeckKind.Exploration, 120, 120, 8, 1),
                new CardSpec("ice", "Лёд", DeckKind.Exploration, 120, 100, 2, 1),
                new CardSpec("fire", "Огонь", DeckKind.Exploration, 100, 100, 1, 1),
                new CardSpec("strike", "Удар", DeckKind.Combat, 120, 100, 3, 1),
                new CardSpec("heavy", "Тяжёлый удар", DeckKind.Combat, 140, 140, 2, 0),
                new CardSpec("heal", "Лечение", DeckKind.Combat, 140, 120, 2, 1) };
            var counts = new Dictionary<string, int> { { "impulse", 3 }, { "ice", 3 }, { "fire", 2 }, { "strike", 5 },
                { "heavy", 0 }, { "heal", 2 } };
            var w = new DeckWorkshop(
                new DeckRules(maxCardLevel: 2, handUpgradePrice: 200, startingCoins: 0), specs, initialCopies: counts);
            Assert.That(w.Owned.Count, Is.EqualTo(15));
            Assert.That(w.Selected(DeckKind.Exploration).Count, Is.EqualTo(8));
            Assert.That(w.Selected(DeckKind.Combat).Count, Is.EqualTo(7));
            Assert.That(w.Owned.Count(c => c.Spec.Id == "heavy"), Is.Zero);
            Assert.That(w.Owned.All(c => w.IsSelected(c.Id)), Is.True);
            Assert.That(w.Depart(42), Is.Null);
            Assert.That(w.Current.Get(DeckKind.Exploration).StockCount, Is.EqualTo(5));
            Assert.That(w.Current.Get(DeckKind.Combat).StockCount, Is.EqualTo(4));
        }

        [Test]
        public void SharedProgressRestoreNeverCreditsOrReplacesSceneWallet()
        {
            var source = Workshop();
            source.Buy("wind", out var copy);
            source.Toggle(copy.Id);
            source.Upgrade(copy.Id);
            source.ImproveHand(DeckKind.Combat);
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
            Assert.That(target.HandSize(DeckKind.Combat), Is.EqualTo(4));
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
            w.ImproveHand(DeckKind.Combat);
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
        }
    }
}
