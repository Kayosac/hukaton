using System;
using System.Linq;
using Hakaton.Economy;
using NUnit.Framework;

namespace Hakaton.Trading.Tests
{
    public sealed class TradingProgressTests
    {
        static readonly ItemDefinition Find = new ItemDefinition("test_find", "Тестовая находка", "Находки", 20);
        static readonly ItemDefinition Tusk = new ItemDefinition("boar_tusk", "Кабаний клык", "Трофеи", 120);

        static TradingSession Create(CoinWallet wallet = null, TradeSettings settings = null)
        {
            var session = new TradingSession(settings, 17, wallet);
            session.SeedInventory(Find, 0);
            session.SeedInventory(Tusk, 0);
            return session;
        }

        static void Sell(TradingSession session, int slot)
        {
            session.Open();
            session.Refuse(session.CurrentCustomer.Id);
            session.AdvanceCustomer(new CustomerTerms { RequestedSlot = slot, AsksPrice = true, MaxPrice = 200 });
            var deal = session.CurrentDeal;
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True);
        }

        [Test]
        public void RevenueGoalCheatPreservesTheLiveDealAndEconomyAndPersists()
        {
            var wallet = new CoinWallet(75);
            var session = Create(wallet);
            session.SeedInventory(Tusk, 2);
            session.PlaceItem(0, 0);
            session.Open();
            var customer = session.CurrentCustomer;
            var deal = session.CurrentDeal;
            var stock = session.Slots[0];
            var inventory = session.Inventory.ToArray();
            float clock = session.RemainingSeconds;
            int changes = 0;
            session.Changed += () => changes++;

            Assert.That(session.CompleteRevenueGoal(404), Is.True);
            Assert.That(session.GrossRevenue, Is.EqualTo(404));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(session.Gold, Is.EqualTo(75));
            Assert.That(session.SalesCount, Is.Zero);
            Assert.That(session.SettlementSequence, Is.Zero);
            Assert.That(session.IsOpen, Is.True);
            Assert.That(session.CurrentCustomer, Is.SameAs(customer));
            Assert.That(session.CurrentDeal, Is.SameAs(deal));
            Assert.That(session.Slots[0], Is.SameAs(stock));
            Assert.That(session.Inventory, Is.EqualTo(inventory));
            Assert.That(session.RemainingSeconds, Is.EqualTo(clock));
            Assert.That(session.CompleteRevenueGoal(404), Is.False);
            Assert.That(session.CompleteRevenueGoal(300), Is.False);
            Assert.That(session.CompleteRevenueGoal(-1), Is.False);
            Assert.That(session.GrossRevenue, Is.EqualTo(404));
            Assert.That(changes, Is.EqualTo(1), "Repeated clicks never reduce progress or create extra changes.");

            var restored = Create(wallet);
            Assert.That(restored.TryRestore(session.Save()), Is.True);
            Assert.That(restored.GrossRevenue, Is.EqualTo(404));
            Assert.That(restored.Gold, Is.EqualTo(75));
            Assert.That(restored.SalesCount, Is.Zero);
        }

        /// <summary>Balance stage 10: a new game «after the tutorial» starts with revenue, but no sale and no coin.</summary>
        [Test]
        public void RevenueFromAwayAddsUpWithoutASaleOrCoinsAndPersists()
        {
            var wallet = new CoinWallet(100);
            var session = Create(wallet);
            int changes = 0;
            session.Changed += () => changes++;
            Assert.That(session.AddRevenue(100), Is.True);
            Assert.That(session.AddRevenue(0), Is.False);
            Assert.That(session.AddRevenue(-5), Is.False);
            Assert.That(session.GrossRevenue, Is.EqualTo(100));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(session.SalesCount, Is.Zero);
            Assert.That(session.SettlementSequence, Is.Zero);
            Assert.That(wallet.Balance, Is.EqualTo(100), "no coin credited");
            Assert.That(session.AddRevenue(4), Is.True);
            Assert.That(session.GrossRevenue, Is.EqualTo(104), "it adds up");
            var restored = Create(wallet);
            Assert.That(restored.TryRestore(session.Save()), Is.True);
            Assert.That(restored.GrossRevenue, Is.EqualTo(104));
        }

        [Test]
        public void RevenueCountsSalesAndSurvivesSpendingWithoutChangingSharedWalletOnRestore()
        {
            var wallet = new CoinWallet();
            var session = Create(wallet);
            session.SeedInventory(Tusk, 2);
            session.SeedInventory(Find);
            session.PlaceItem(0, 0); session.PlaceItem(0, 4);
            Sell(session, 0);
            session.Tick(19.5f); session.Close();
            Assert.That(wallet.TrySpend(100), Is.True);
            var save = session.Save();
            var restored = Create(wallet);
            Assert.That(restored.TryRestore(save), Is.True);
            Assert.That(restored.Gold, Is.EqualTo(20));
            Assert.That(restored.GrossRevenue, Is.EqualTo(120));
            Assert.That(restored.SalesCount, Is.EqualTo(1));
            Assert.That(restored.RemainingSeconds, Is.EqualTo(100.5f));
            Assert.That(restored.Slots[4].Id, Is.EqualTo(session.Slots[4].Id));
            Assert.That(restored.Inventory.Select(item => item.Id), Is.EqualTo(session.Inventory.Select(item => item.Id)));
            Assert.That(restored.IsOpen, Is.False);
            Assert.That(restored.CurrentDeal, Is.Null);
            restored.SeedInventory(Find);
            Assert.That(restored.Inventory.Last().Id, Is.GreaterThan(save.nextItemId));
        }

        [TestCase("duplicate id")]
        [TestCase("duplicate slot")]
        [TestCase("unknown definition")]
        [TestCase("negative revenue")]
        [TestCase("invalid clock")]
        public void InvalidSnapshotLeavesTheLiveSessionUntouched(string corruption)
        {
            var session = Create();
            session.SeedInventory(Find, 2);
            session.PlaceItem(0, 0); session.PlaceItem(0, 1);
            var first = session.Slots[0];
            var second = session.Slots[1];
            var save = session.Save();
            switch (corruption)
            {
                case "duplicate id": save.items[1].instanceId = save.items[0].instanceId; break;
                case "duplicate slot": save.items[1].slot = save.items[0].slot; break;
                case "unknown definition": save.items[0].definitionId = "missing"; break;
                case "negative revenue": save.grossRevenue = -1; break;
                case "invalid clock": save.remainingSeconds = float.NaN; break;
            }
            int revision = session.Revision;
            Assert.That(session.CanRestore(save), Is.False);
            Assert.That(session.TryRestore(save), Is.False);
            Assert.That(session.Slots[0], Is.SameAs(first));
            Assert.That(session.Slots[1], Is.SameAs(second));
            Assert.That(session.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void ExternalDeliveryRejectsTheWholeInvalidBatchAndCannotBeReplayed()
        {
            var session = Create();
            session.SeedInventory(Find); session.PlaceItem(0, 0);
            session.Open(); session.Tick(25); session.Close();
            long id = session.BeginExpedition();
            int before = session.TotalStockCount;
            Assert.That(session.CompleteExpedition(id, new[] { new StockDelivery(Find.Id, 2), new StockDelivery("unknown", 1) }), Is.False);
            Assert.That(session.PendingLootCount, Is.Zero);
            Assert.That(session.TotalStockCount, Is.EqualTo(before));
            Assert.That(session.ExpeditionId, Is.EqualTo(id));
            var delivery = new[] { new StockDelivery(Find.Id, 2), new StockDelivery(Tusk.Id, 1) };
            Assert.That(session.CompleteExpedition(id, delivery), Is.True);
            Assert.That(session.TotalStockCount, Is.EqualTo(before + 3));
            Assert.That(session.Inventory.Count(item => item.Definition.Id == Tusk.Id), Is.EqualTo(1));
            Assert.That(session.RemainingSeconds, Is.EqualTo(120));
            Assert.That(session.CompleteExpedition(id, delivery), Is.False);
            Assert.That(session.TotalStockCount, Is.EqualTo(before + 3));
            Assert.That(session.GrossRevenue, Is.Zero, "Delivery is not a sale.");
        }

        [Test]
        public void EmptyExternalReturnPreservesClockAndSaveNeverBanksPendingLoot()
        {
            var session = Create();
            session.SeedInventory(Find); session.PlaceItem(0, 0);
            session.Open(); session.Tick(45); session.Close();
            long id = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(id, Array.Empty<StockDelivery>()), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(75));
            long next = session.BeginExpedition();
            session.CollectLoot(next, Tusk);
            var save = session.Save();
            var restored = Create();
            Assert.That(restored.TryRestore(save), Is.True);
            Assert.That(restored.TotalStockCount, Is.EqualTo(1));
            Assert.That(restored.PendingLootCount, Is.Zero);
            Assert.That(restored.ExpeditionId, Is.Zero);
            Assert.That(restored.BeginExpedition(), Is.GreaterThan(next));
            Assert.That(restored.CompleteExpedition(next, new[] { new StockDelivery(Tusk.Id, 1) }), Is.False);
        }

        /// <summary>Opens the shop and lets the whole shift run out (the visitor at the counter is sent off by closing).</summary>
        static void Drain(TradingSession session)
        {
            Assert.That(session.Open(), Is.True);
            session.Tick(session.RemainingSeconds);
            session.Close();
            Assert.That(session.RemainingSeconds, Is.Zero);
        }

        const string HalfShiftMessage = "Вернулись без добычи. На складе есть товар — полсмены ваши.";

        [Test]
        public void EmptyReturnWithGoodsOnlyInStorageGivesHalfAShift()
        {
            var session = Create();
            session.SeedInventory(Find, 2); session.PlaceItem(0, 0);
            Drain(session);
            Assert.That(session.RemoveItem(0), Is.True);
            Assert.That((session.DisplayCount, session.Inventory.Count), Is.EqualTo((0, 2)), "Goods only in storage.");
            long id = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(id, Array.Empty<StockDelivery>()), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(session.ShiftLength / 2));
            Assert.That(session.LastReturn, Is.EqualTo(ShiftReturn.HalfShift));
            Assert.That(session.LastMessage, Is.EqualTo(HalfShiftMessage));
            Assert.That(session.PlaceItem(0, 0), Is.True);
            Assert.That(session.Open(), Is.True, "Goods in storage never leave the shop shut.");
        }

        [Test]
        public void EmptyReturnWithGoodsOnlyOnTheDisplayGivesHalfAShift()
        {
            var session = Create();
            session.SeedInventory(Find); session.PlaceItem(0, 0);
            Drain(session);
            Assert.That((session.DisplayCount, session.Inventory.Count), Is.EqualTo((1, 0)), "Goods only on the display.");
            long id = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(id), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(60), "Half of the 120 s shift.");
            Assert.That(session.LastReturn, Is.EqualTo(ShiftReturn.HalfShift));
            Assert.That(session.Open(), Is.True);
        }

        [Test]
        public void EmptyReturnWithNothingToSellLeavesTheClockAsItWas()
        {
            var session = Create();
            session.SeedInventory(Find); session.PlaceItem(0, 0);
            Assert.That(session.Open(), Is.True); session.Tick(100); session.Close();
            Sell(session, 0);
            Assert.That((session.TotalStockCount, session.IsOpen, session.RemainingSeconds), Is.EqualTo((0, false, 20f)));
            long id = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(id, Array.Empty<StockDelivery>()), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(20), "No goods: no half shift.");
            Assert.That(session.LastReturn, Is.EqualTo(ShiftReturn.Unchanged));
            Assert.That(session.LastMessage, Is.EqualTo("Вернулись без новой добычи. Остаток времени не изменился."));
        }

        [Test]
        public void EmptyReturnNeverShortensAShiftLongerThanHalf()
        {
            var session = Create();
            session.SeedInventory(Find, 3); session.PlaceItem(0, 0);
            Assert.That(session.Open(), Is.True); session.Tick(20); session.Close();
            long id = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(id), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(100));
            // Nothing was raised, so the message does not promise half a shift.
            Assert.That(session.LastReturn, Is.EqualTo(ShiftReturn.Unchanged));
            Assert.That(session.LastMessage, Is.EqualTo("Вернулись без новой добычи. Остаток времени не изменился."));

            // Exactly half left: not raised either, on every way back.
            Assert.That(session.Open(), Is.True); session.Tick(40); session.Close();
            id = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(id), Is.True);
            Assert.That((session.RemainingSeconds, session.LastReturn), Is.EqualTo((60f, ShiftReturn.Unchanged)));
            Assert.That(session.ReturnWithoutLoot(), Is.True);
            Assert.That((session.RemainingSeconds, session.LastReturn), Is.EqualTo((60f, ShiftReturn.Unchanged)));
            Assert.That(session.LastMessage, Is.EqualTo("Вернулись без новой добычи. Остаток времени не изменился."));

            // A second under half: raised, and now it says so.
            Assert.That(session.Open(), Is.True); session.Tick(1); session.Close();
            Assert.That(session.ReturnWithoutLoot(), Is.True);
            Assert.That((session.RemainingSeconds, session.LastReturn), Is.EqualTo((60f, ShiftReturn.HalfShift)));
            Assert.That(session.LastMessage, Is.EqualTo(HalfShiftMessage));
        }

        [Test]
        public void ReturnWithLootStillRefillsTheWholeShift()
        {
            var session = Create();
            session.SeedInventory(Find, 2); session.PlaceItem(0, 0);
            Drain(session);
            long id = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(id, new[] { new StockDelivery(Tusk.Id, 1) }), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(session.ShiftLength));
            Assert.That(session.LastReturn, Is.EqualTo(ShiftReturn.Restored));
            Assert.That(session.LastMessage, Is.EqualTo("Новая добыча доставлена. Время торговли восстановлено."));
        }

        [Test]
        public void TripOutsideAnOutingFollowsTheSameRuleAndACancelledOutingChangesNothing()
        {
            var session = Create();
            session.SeedInventory(Find, 2); session.PlaceItem(0, 0);
            Drain(session);
            // An outing the workshop refused never left: no trip, no half shift.
            long refused = session.BeginExpedition();
            Assert.That(session.CancelExpedition(refused), Is.True);
            Assert.That((session.ExpeditionId, session.RemainingSeconds), Is.EqualTo((0L, 0f)));
            Assert.That(session.CancelExpedition(refused), Is.False);
            Assert.That(session.CompleteExpedition(refused), Is.False);

            // The god's mountains: a trip with nothing to bring back, outside BeginExpedition.
            long away = session.BeginExpedition();
            Assert.That(session.ReturnWithoutLoot(), Is.False, "Not while an outing is under way.");
            Assert.That(session.CancelExpedition(away), Is.True);
            Assert.That(session.ReturnWithoutLoot(), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(60));
            Assert.That(session.LastReturn, Is.EqualTo(ShiftReturn.HalfShift));
            Assert.That(session.LastMessage, Is.EqualTo(HalfShiftMessage));
            Assert.That(session.Open(), Is.True);
            Assert.That(session.ReturnWithoutLoot(), Is.False, "Not while the shop is open.");
        }

        [Test]
        public void SharedWalletOverflowDoesNotAdvanceRevenueOrRemoveTheItem()
        {
            var session = Create(new CoinWallet(int.MaxValue));
            session.SeedInventory(Find); session.PlaceItem(0, 0);
            Assert.Throws<OverflowException>(() => Sell(session, 0));
            Assert.That(session.GrossRevenue, Is.Zero);
            Assert.That(session.SalesCount, Is.Zero);
            Assert.That(session.DisplayCount, Is.EqualTo(1));
        }

        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        public void PresetPatienceCostDependsOnTheChosenButton(int option, int expectedCost)
        {
            var session = Create(settings: new TradeSettings { UsePresetPatienceCosts = true });
            session.SeedInventory(Tusk); session.PlaceItem(0, 0); session.Open();
            session.Refuse(session.CurrentCustomer.Id);
            session.AdvanceCustomer(new CustomerTerms { RequestedSlot = 0, AsksPrice = false, OpeningRatio = .8f, MaxPrice = 96, Patience = 5 });
            var deal = session.CurrentDeal;
            Assert.That(session.Offer(deal.Id, deal.Revision, option), Is.True);
            Assert.That(deal.Patience, Is.EqualTo(5 - expectedCost));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AskingAndOfferingVisitorsUseTheirSeparateConfiguredCeilings(bool asks)
        {
            var session = new TradingSession(new TradeSettings {
                AsksPriceProbability = asks ? 1 : 0,
                MinCeilingRatio = 1.35f, MaxCeilingRatio = 1.35f,
                MinOfferCeilingRatio = .95f, MaxOfferCeilingRatio = .95f,
                MinOpeningRatio = .75f, MaxOpeningRatio = .75f
            });
            session.SeedInventory(new ItemDefinition("test", "Товар", "Трофеи", 100));
            session.PlaceItem(0, 0); session.Open();
            for (int i = 0; i < 30; i++)
            {
                Assert.That(session.CurrentCustomer.AsksPrice, Is.EqualTo(asks));
                Assert.That(session.CurrentCustomer.MaxPrice, Is.EqualTo(asks ? 135 : 95));
                session.Refuse(session.CurrentCustomer.Id); session.AdvanceCustomer();
            }
        }

        [Test]
        public void ChangeNotificationFollowsPersistentStockChangesButNotIdleClockTicks()
        {
            var session = Create();
            int changes = 0;
            session.Changed += () => changes++;
            session.SeedInventory(Find);
            session.PlaceItem(0, 0);
            Assert.That(changes, Is.EqualTo(2));
            session.Open();
            int beforeTick = changes;
            session.Tick(1);
            Assert.That(changes, Is.EqualTo(beforeTick));
            session.Close();
            Assert.That(changes, Is.GreaterThan(beforeTick));
        }
    }
}
