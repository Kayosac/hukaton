using System;
using System.Linq;
using System.Text.RegularExpressions;
using Hakaton.Economy;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Trading.Tests
{
    /// <summary>
    /// Playtest: the rare bulk buyer («Скупщик»). After the first revenue goal a visitor may be him (5% a visitor, at most
    /// once a shift, for a display of 3+ items, never in the tutorial). He offers 75% of every item's base price for the whole
    /// display; the player sells everything or refuses. The PC raises his chance, +5% a step up to 30%.
    /// </summary>
    public sealed class BulkBuyerTests
    {
        static readonly ItemDefinition Horn = new ItemDefinition("horn", "Рог", "Трофеи", 100);
        static readonly ItemDefinition Herb = new ItemDefinition("herb", "Трава", "Растения", 41);
        static readonly ItemDefinition Feather = new ItemDefinition("feather", "Перо", "Находки", 6);
        static readonly ItemDefinition Shell = new ItemDefinition("shell", "Ракушка", "Находки", 14);
        static readonly ItemDefinition Bead = new ItemDefinition("bead", "Бусина", "Находки", 1);

        static TradeSettings Settings(float chance = .05f, bool upgrades = true) => new TradeSettings {
            ShiftSeconds = 30, BulkBuyerChance = chance,
            BulkBuyerUpgradeChances = upgrades ? new[] { .05f, .05f, .05f, .05f, .05f } : new float[0],
            BulkBuyerUpgradePrices = upgrades ? new[] { 500, 1000, 2000, 3000, 5000 } : new int[0]
        };

        /// <summary>A closed shop with <paramref name="onDisplay"/> horns on display and the rest of eight in stock.</summary>
        static TradingSession Shop(int onDisplay, TradeSettings settings = null, CoinWallet wallet = null, int seed = 3)
        {
            var session = new TradingSession(settings ?? Settings(), seed, wallet);
            session.SeedInventory(Horn, 8);
            for (int i = 0; i < onDisplay; i++) Assert.That(session.PlaceItem(0, i), Is.True);
            return session;
        }

        /// <summary>Unlocked as the game loop does it: the first revenue goal (666), reached by the cheat.</summary>
        static void ReachGoal(TradingSession session)
        {
            session.BulkBuyerUnlockRevenue = 666;
            Assert.That(session.BulkBuyerUnlocked, Is.False);
            Assert.That(session.CompleteRevenueGoal(666), Is.True);
            Assert.That(session.BulkBuyerUnlocked, Is.True);
        }

        static Customer OpenWithBuyer(TradingSession session)
        {
            session.ForceBulkBuyerNext = true;
            Assert.That(session.Open(), Is.True);
            var buyer = session.CurrentCustomer;
            Assert.That(buyer.IsBulkBuyer, Is.True);
            Assert.That(session.CurrentDeal, Is.Null, "He names no item and never haggles.");
            Assert.That(session.ForceBulkBuyerNext, Is.False, "The cheat is spent on him.");
            return buyer;
        }

        /// <summary>Fresh loot from an outing starts a new shift.</summary>
        static void NewShift(TradingSession session)
        {
            long outing = session.BeginExpedition();
            Assert.That(session.CollectLoot(outing, Horn), Is.True);
            Assert.That(session.CompleteExpedition(outing), Is.True);
        }

        static bool NextIsBuyer(TradingSession session)
        {
            if (session.CurrentCustomer != null) Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            Assert.That(session.AdvanceCustomer(), Is.True);
            return session.CurrentCustomer.IsBulkBuyer;
        }

        [Test]
        public void PriceIsThreeQuartersOfEveryItemRoundedHalfAwayFromZeroAtLeastOneCoin()
        {
            var session = new TradingSession(Settings());
            foreach (var item in new[] { Horn, Feather, Shell, Herb, Bead }) session.SeedInventory(item);
            for (int i = 0; i < 5; i++) Assert.That(session.PlaceItem(0, i), Is.True);
            // 100 → 75, 6 → 4.5 → 5, 14 → 10.5 → 11 (not the banker's 10), 41 → 30.75 → 31, 1 → 0.75 → 1.
            Assert.That(session.BulkOfferForDisplay(), Is.EqualTo(75 + 5 + 11 + 31 + 1));

            var buyer = OpenWithBuyer(session);
            Assert.That((buyer.BulkPrice, buyer.BulkCount), Is.EqualTo((123, 5)));
            Assert.That(buyer.Lines.Last(), Is.EqualTo("Вся витрина — 123 монеты. По рукам?"));

            // A steep discount still pays a coin an item.
            var cheap = new TradingSession(new TradeSettings { BulkBuyerPriceRatio = .3f });
            cheap.SeedInventory(Bead, 2);
            Assert.That(cheap.PlaceItem(0, 0) && cheap.PlaceItem(0, 1), Is.True);
            Assert.That(cheap.BulkOfferForDisplay(), Is.EqualTo(2));
            Assert.That(new TradingSession().BulkOfferForDisplay(), Is.Zero, "An empty display is worth nothing.");
        }

        [Test]
        public void HeComesOnlyAfterTheGoalForThreeItemsOnceAShift()
        {
            var session = Shop(3, Settings(1, upgrades: false));
            Assert.That(session.Open(), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.False, "Before the revenue goal he never comes.");
            Assert.That(NextIsBuyer(session), Is.False);
            session.Close();

            ReachGoal(session);
            Assert.That(session.RemoveItem(2), Is.True);
            Assert.That(session.Open(), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.False, "Two items on display are not worth his trip.");
            session.Close();

            Assert.That(session.PlaceItem(0, 2), Is.True);
            Assert.That(session.Open(), Is.True);
            var buyer = session.CurrentCustomer;
            Assert.That(buyer.IsBulkBuyer, Is.True);
            Assert.That(session.BulkBuyerCameThisShift, Is.True);
            Assert.That(session.Refuse(buyer.Id), Is.True);
            for (int i = 0; i < 20; i++) Assert.That(NextIsBuyer(session), Is.False, "Once a shift, visit " + i);
            session.Close();
            Assert.That(session.Open(), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.False, "Reopening is the same shift.");
            session.Close();

            NewShift(session);
            Assert.That(session.BulkBuyerCameThisShift, Is.False);
            Assert.That(session.Open(), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.True, "A new shift, a new chance.");
        }

        [Test]
        public void NeverWithStagedVisitorsOrAPausedClockAndExplicitTermsWin()
        {
            var session = Shop(4, Settings(1, upgrades: false));
            ReachGoal(session);
            session.ForceBulkBuyerNext = true;
            session.CustomerScript = () => null;
            Assert.That(session.Open(), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.False, "The tutorial's script stands even when it has nobody.");
            session.CustomerScript = null;
            session.ClockPaused = true;
            Assert.That(NextIsBuyer(session), Is.False, "Nor while the tutorial holds the clock.");
            session.ClockPaused = false;
            Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            Assert.That(session.AdvanceCustomer(new CustomerTerms { RequestedSlot = 1, MaxPrice = 200 }), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.False, "Authored terms are a regular visitor.");
            Assert.That(NextIsBuyer(session), Is.True, "The cheat waited for the tutorial to end.");
        }

        [Test]
        public void TheCheatIgnoresChanceGoalAndShift()
        {
            var session = Shop(1, Settings(0, upgrades: false));
            var buyer = OpenWithBuyer(session);
            Assert.That((buyer.BulkCount, session.BulkBuyerUnlocked), Is.EqualTo((1, false)));
            Assert.That(session.Refuse(buyer.Id), Is.True);
            session.ForceBulkBuyerNext = true;
            Assert.That(NextIsBuyer(session), Is.True, "Even a second time in the same shift.");
        }

        [Test]
        public void TheChanceIsFivePercentAVisitor()
        {
            var session = Shop(3, Settings(), seed: 11);
            ReachGoal(session);
            Assert.That(session.BulkBuyerChance, Is.EqualTo(.05f).Within(1e-5f));
            int buyers = 0;
            const int rounds = 1000;
            for (int round = 0; round < rounds; round++)
            {
                NewShift(session); // so «once a shift» never holds him back
                Assert.That(session.Open(), Is.True);
                if (session.CurrentCustomer.IsBulkBuyer) buyers++;
                session.Close();
            }
            Assert.That(buyers, Is.InRange(20, 90), "About 50 of 1000.");
            Assert.That(Shop(3, Settings(0)).BulkBuyerChance, Is.Zero);
        }

        [Test]
        public void SellingAllCreditsTheWalletRevenueAndSalesClearsTheDisplayAndClosesTheShift()
        {
            var wallet = new CoinWallet(10);
            var session = Shop(5, wallet: wallet);
            var stock = session.Inventory.ToArray();
            var displayed = session.Slots.Where(item => item != null).ToArray();
            var buyer = OpenWithBuyer(session);
            Assert.That((buyer.BulkPrice, buyer.BulkCount), Is.EqualTo((375, 5)));
            Assert.That(session.Accept(0, 0), Is.False, "There is no deal to accept.");
            Assert.That(session.AcceptBulkOffer(buyer.Id + 1), Is.False);
            float clock = session.RemainingSeconds;

            Assert.That(session.AcceptBulkOffer(buyer.Id), Is.True);
            Assert.That((wallet.Balance, session.Gold), Is.EqualTo((385, 385)));
            Assert.That((session.GrossRevenue, session.SalesCount, session.SettlementSequence), Is.EqualTo((375L, 5, 1)));
            Assert.That((session.LastSalePrice, session.LastSaleBulk), Is.EqualTo((375, true)));
            Assert.That(session.LastSoldItems, Is.EquivalentTo(displayed));
            Assert.That(session.LastSoldItem, Is.SameAs(displayed.Last()));
            Assert.That(session.DisplayCount, Is.Zero);
            Assert.That(session.Inventory, Is.EqualTo(stock), "The stock in the back is not his.");
            Assert.That((session.IsOpen, session.CurrentCustomer), Is.EqualTo((false, (Customer)null)), "The empty display closes the shop.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(clock), "The rest of the shift is kept.");
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Purchased));
            Assert.That(session.LastDeparture.BulkBuyer, Is.True);
            Assert.That(session.LastMessage, Does.StartWith("Скупщик забрал всё: 5 шт. за 375 монет."));
            Assert.That(session.BulkBuyerMet, Is.True);
            Assert.That(session.AcceptBulkOffer(buyer.Id), Is.False, "One payment.");
            Assert.That(wallet.Balance, Is.EqualTo(385));
            Assert.That(session.CanRestore(session.Save()), Is.True, "Revenue still covers the sales count.");

            // A regular sale afterwards is a single item again.
            Assert.That(session.PlaceItem(0, 0), Is.True);
            Assert.That(session.Open(), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.False);
            Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            Assert.That(session.AdvanceCustomer(new CustomerTerms { RequestedSlot = 0, AsksPrice = true, MaxPrice = 200 }), Is.True);
            var deal = session.CurrentDeal;
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True);
            Assert.That((session.SettlementSequence, session.LastSaleBulk, session.LastSoldItems.Count), Is.EqualTo((2, false, 1)));
        }

        [Test]
        public void TheNoticeCountsCoinsInRussian()
        {
            Assert.That(TradingSession.BulkSaleNotice(1, 75), Is.EqualTo("Скупщик забрал всё: 1 шт. за 75 монет."));
            Assert.That(TradingSession.BulkSaleNotice(3, 21), Is.EqualTo("Скупщик забрал всё: 3 шт. за 21 монету."));
            Assert.That(TradingSession.BulkSaleNotice(3, 22), Is.EqualTo("Скупщик забрал всё: 3 шт. за 22 монеты."));
            Assert.That(TradingSession.BulkSaleNotice(4, 112), Is.EqualTo("Скупщик забрал всё: 4 шт. за 112 монет."));
        }

        [Test]
        public void RefusingKeepsTheGoodsAndHeLeavesCalmly()
        {
            var wallet = new CoinWallet(10);
            var session = Shop(4, wallet: wallet);
            var displayed = session.Slots.ToArray();
            var buyer = OpenWithBuyer(session);
            Assert.That(session.Refuse(buyer.Id), Is.True);
            Assert.That(session.Slots, Is.EqualTo(displayed));
            Assert.That((wallet.Balance, session.GrossRevenue, session.SalesCount, session.SettlementSequence), Is.EqualTo((10, 0L, 0, 0)));
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Refused));
            Assert.That(session.LastDeparture.BulkBuyer, Is.True, "The presenter lets him go without anger.");
            Assert.That(session.LastMessage, Is.EqualTo("Скупщик ушёл. Товар остался на витрине."));
            Assert.That(session.BulkBuyerMet, Is.True, "Hearing him out and saying no is meeting him.");
            Assert.That(session.IsOpen, Is.True, "The shift goes on.");
            Assert.That(session.AdvanceCustomer(), Is.True);
            Assert.That(session.CurrentCustomer.IsBulkBuyer, Is.False);
        }

        [Test]
        public void FirstMeetingExplainsLaterVisitsOnlyOffer()
        {
            var session = Shop(3);
            var first = OpenWithBuyer(session);
            Assert.That(first.FirstMeeting, Is.True);
            Assert.That(first.Lines, Is.EqualTo(new[] {
                "Я скупщик. Забираю всё, что лежит на витрине. Разом.",
                "Плачу три четверти цены. Зато сразу и за всё.",
                "Не торгуюсь. Да — да, нет — нет.",
                "Вся витрина — 225 монет. По рукам?" }));
            // Closing the shop in the middle of his speech is no meeting: next time he explains again.
            session.Close();
            Assert.That(session.BulkBuyerMet, Is.False);
            Assert.That(session.LastDeparture.BulkBuyer, Is.True);
            var again = OpenWithBuyer(session);
            Assert.That(again.FirstMeeting, Is.True);
            Assert.That(session.Refuse(again.Id), Is.True);
            session.Close();

            var later = OpenWithBuyer(session);
            Assert.That(later.FirstMeeting, Is.False);
            Assert.That(later.Lines, Is.EqualTo(new[] { "Снова я. Вся витрина — 225 монет. По рукам?" }));
        }

        [Test]
        public void ChanceUpgradeSpendsTheWalletFiveToThirtyPercent()
        {
            var wallet = new CoinWallet(12000);
            var session = Shop(3, wallet: wallet);
            Assert.That((session.BulkBuyerLevel, session.BulkBuyerSteps, session.BulkBuyerAtMaximum), Is.EqualTo((0, 5, false)));
            int changes = 0;
            session.Changed += () => changes++;
            var expected = new[] { .05f, .10f, .15f, .20f, .25f, .30f };
            var prices = new[] { 500, 1000, 2000, 3000, 5000 };
            int balance = 12000;
            for (int step = 0; step < 5; step++)
            {
                Assert.That(session.BulkBuyerChance, Is.EqualTo(expected[step]).Within(1e-4f), "step " + step);
                Assert.That(session.NextBulkBuyerChance, Is.EqualTo(expected[step + 1]).Within(1e-4f));
                Assert.That(session.BulkBuyerUpgradePrice, Is.EqualTo(prices[step]));
                Assert.That(session.ImproveBulkBuyer(), Is.Null);
                balance -= prices[step];
                Assert.That((session.BulkBuyerLevel, wallet.Balance), Is.EqualTo((step + 1, balance)));
            }
            Assert.That(changes, Is.EqualTo(5));
            Assert.That(session.BulkBuyerChance, Is.EqualTo(.30f).Within(1e-4f));
            Assert.That((session.BulkBuyerAtMaximum, session.BulkBuyerUpgradePrice), Is.EqualTo((true, 0)));
            Assert.That(session.NextBulkBuyerChance, Is.EqualTo(.30f).Within(1e-4f));
            Assert.That(session.ImproveBulkBuyer(), Is.Not.Null, "Nothing left to buy.");
            Assert.That((session.BulkBuyerLevel, wallet.Balance), Is.EqualTo((5, 500)));
        }

        [Test]
        public void ChanceUpgradeIsRefusedWhileTradingOnAnOutingOrWithoutCoins()
        {
            var wallet = new CoinWallet(5000);
            var session = Shop(3, wallet: wallet);
            Assert.That(session.Open(), Is.True);
            Assert.That(session.ImproveBulkBuyer(), Is.Not.Null, "Not while the shop is open.");
            session.Close();
            long outing = session.BeginExpedition();
            Assert.That(session.ImproveBulkBuyer(), Is.Not.Null, "Not during an outing.");
            Assert.That(session.CompleteExpedition(outing), Is.True);
            Assert.That(wallet.TrySpend(4501), Is.True);
            Assert.That(session.ImproveBulkBuyer(), Is.Not.Null, "499 coins do not buy a 500-coin step.");
            Assert.That((session.BulkBuyerLevel, wallet.Balance), Is.EqualTo((0, 499)));
        }

        [Test]
        public void LevelMeetingAndShiftVisitRoundTripAndOldSavesLoadAtDefaults()
        {
            var wallet = new CoinWallet(5000);
            var session = Shop(3, wallet: wallet);
            Assert.That(session.ImproveBulkBuyer(), Is.Null);
            Assert.That(session.ImproveBulkBuyer(), Is.Null);
            var buyer = OpenWithBuyer(session);
            Assert.That(session.Refuse(buyer.Id), Is.True);
            session.Close();
            var save = session.Save();
            Assert.That((save.bulkBuyerLevel, save.bulkBuyerMet, save.bulkBuyerThisShift), Is.EqualTo((2, true, true)));

            string json = JsonUtility.ToJson(save);
            var restored = Shop(0, wallet: wallet);
            Assert.That(restored.TryRestore(JsonUtility.FromJson<TradingSave>(json)), Is.True);
            Assert.That((restored.BulkBuyerLevel, restored.BulkBuyerMet, restored.BulkBuyerCameThisShift), Is.EqualTo((2, true, true)));
            Assert.That(restored.BulkBuyerChance, Is.EqualTo(.15f).Within(1e-4f));
            Assert.That(restored.BulkBuyerUpgradePrice, Is.EqualTo(2000));

            // A save written before the bulk buyer existed has none of his fields.
            string old = Regex.Replace(json, ",?\"bulkBuyer(Level|Met|ThisShift)\":(\\d+|true|false)", "");
            Assert.That(old, Does.Not.Contain("bulkBuyer"));
            var oldSave = JsonUtility.FromJson<TradingSave>(old);
            var fromOld = Shop(0, wallet: wallet);
            Assert.That(fromOld.TryRestore(oldSave), Is.True);
            Assert.That((fromOld.BulkBuyerLevel, fromOld.BulkBuyerMet, fromOld.BulkBuyerCameThisShift), Is.EqualTo((0, false, false)));
            Assert.That(fromOld.BulkBuyerChance, Is.EqualTo(.05f).Within(1e-5f));

            // More upgrades than the settings sell now clamp; a negative level is damage.
            save.bulkBuyerLevel = 99;
            Assert.That(restored.TryRestore(save), Is.True);
            Assert.That((restored.BulkBuyerLevel, restored.BulkBuyerAtMaximum), Is.EqualTo((5, true)));
            save.bulkBuyerLevel = -1;
            Assert.That(restored.CanRestore(save), Is.False);
            Assert.That(restored.TryRestore(save), Is.False);
        }

        [Test]
        public void CheatMaximizesTheChanceForFree()
        {
            var wallet = new CoinWallet(5);
            var session = Shop(3, wallet: wallet);
            Assert.That(session.MaximizeBulkBuyer(), Is.True);
            Assert.That((session.BulkBuyerLevel, wallet.Balance), Is.EqualTo((5, 5)));
            Assert.That(session.BulkBuyerChance, Is.EqualTo(.30f).Within(1e-4f));
            Assert.That(session.MaximizeBulkBuyer(), Is.False);
            Assert.That(session.BulkBuyerMet, Is.False, "Maxing his chance does not reveal him.");
        }

        [Test]
        public void SettingsValidateTheBulkBuyerAndKeepTheirOwnCopy()
        {
            foreach (var broken in new Action<TradeSettings>[] {
                s => s.BulkBuyerChance = -.01f, s => s.BulkBuyerChance = 1.5f, s => s.BulkBuyerChance = float.NaN,
                s => s.BulkBuyerUpgradePrices = new[] { 500 },
                s => s.BulkBuyerUpgradeChances = new[] { 0f, .05f, .05f, .05f, .05f },
                s => s.BulkBuyerUpgradePrices = new[] { 500, 1000, -1, 3000, 5000 },
                s => s.BulkBuyerUpgradeChances = new[] { .5f, .5f, .05f, .05f, .05f },
                s => s.BulkBuyerMinDisplay = 0, s => s.BulkBuyerPriceRatio = 0, s => s.BulkBuyerPriceRatio = 1.2f })
            {
                var settings = Settings();
                broken(settings);
                Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(settings));
            }
            var defaults = new TradingSession();
            Assert.That((defaults.BulkBuyerSteps, defaults.BulkBuyerAtMaximum, defaults.BulkBuyerUpgradePrice), Is.EqualTo((0, true, 0)));
            Assert.That(defaults.BulkBuyerChance, Is.EqualTo(.05f).Within(1e-6f));
            Assert.That((defaults.BulkBuyerUnlocked, defaults.BulkBuyerMet), Is.EqualTo((false, false)), "Locked until the game loop sets the goal.");

            var tuned = Settings();
            var session = new TradingSession(tuned);
            tuned.BulkBuyerUpgradeChances[0] = .5f;
            tuned.BulkBuyerUpgradePrices[0] = 1;
            Assert.That(session.NextBulkBuyerChance, Is.EqualTo(.10f).Within(1e-4f), "Later edits to the settings do not leak in.");
            Assert.That(session.BulkBuyerUpgradePrice, Is.EqualTo(500));
        }
    }
}
