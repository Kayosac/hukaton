using System;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.Trading.Tests
{
    public sealed class TradingSessionTests
    {
        private static readonly ItemDefinition Horn = new ItemDefinition("horn", "Рог", "Трофеи", 100);
        private static readonly ItemDefinition Herb = new ItemDefinition("herb", "Трава", "Растения", 40);

        private static TradingSession Stocked(float seconds = 120, bool presetCosts = false, int seed = 42)
        {
            var session = new TradingSession(new TradeSettings { ShiftSeconds = seconds, UsePresetPatienceCosts = presetCosts }, seed);
            session.SeedInventory(Horn, 6);
            session.SeedInventory(Herb, 2);
            for (int i = 0; i < 6; i++) Assert.That(session.PlaceItem(0, i), Is.True);
            return session;
        }

        private static TradeDeal Visitor(TradingSession session, bool asks = true, int ceiling = 130,
            int patience = 4, int slot = 0)
        {
            if (!session.IsOpen) Assert.That(session.Open(), Is.True);
            if (session.CurrentCustomer != null) Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            Assert.That(session.AdvanceCustomer(new CustomerTerms
            {
                RequestedSlot = slot, AsksPrice = asks, OpeningRatio = .8f,
                MaxPrice = ceiling, Patience = patience
            }), Is.True);
            return session.CurrentDeal;
        }

        [Test]
        public void PlacementMovesUniqueInstanceAndRejectsOccupiedSlots()
        {
            var session = new TradingSession();
            session.SeedInventory(Horn, 2);
            long id = session.Inventory[0].Id;
            Assert.That(session.PlaceItem(0, 0), Is.True);
            Assert.That(session.Slots[0].Id, Is.EqualTo(id));
            Assert.That(session.PlaceItem(0, 0), Is.False);
            Assert.That(session.Inventory.Count, Is.EqualTo(1));
            Assert.That(session.RemoveItem(0), Is.True);
            Assert.That(session.Inventory.Select(i => i.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(session.TotalStockCount, Is.EqualTo(2));
        }

        [Test]
        public void RandomStockFillsOnlyEmptySlotsFromCatalogue()
        {
            var session = new TradingSession();
            session.SeedInventory(Horn, 1);
            session.PlaceItem(0, 2);
            long kept = session.Slots[2].Id;
            Assert.That(session.StockEmptySlotsRandomly(new System.Random(3)), Is.EqualTo(session.Slots.Count - 1));
            Assert.That(session.Slots.All(slot => slot != null), Is.True);
            Assert.That(session.Slots[2].Id, Is.EqualTo(kept));
            Assert.That(session.Slots.Select(slot => slot.Id).Distinct().Count(), Is.EqualTo(session.Slots.Count));
            Assert.That(session.StockEmptySlotsRandomly(), Is.Zero);
        }

        [Test]
        public void ClosedShopDoesNotSpendTimeAndReopeningDoesNotRefill()
        {
            var session = Stocked();
            session.Tick(9);
            Assert.That(session.RemainingSeconds, Is.EqualTo(120));
            session.Open(); session.Tick(17); session.Close(); session.Tick(50); session.Open();
            Assert.That(session.RemainingSeconds, Is.EqualTo(103));
        }

        [Test]
        public void OpenShopPreventsRestockingAndRemovingItems()
        {
            var session = Stocked();
            session.Open();
            Assert.That(session.RemoveItem(0), Is.False);
            Assert.That(session.PlaceItem(0, 0), Is.False);
            Assert.That(session.TotalStockCount, Is.EqualTo(8));
        }

        [Test]
        public void AskingPriceFirstBaseAnswerSellsExactlyOneAtRecommendedPrice()
        {
            var session = Stocked();
            var deal = Visitor(session);
            Assert.That(deal.N, Is.Null);
            Assert.That(deal.GetPriceOptions().Select(o => o.Price), Is.EqualTo(new[] { 100, 105, 115, 125, 150 }));
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True);
            Assert.That(session.Gold, Is.EqualTo(100));
            Assert.That(session.SalesCount, Is.EqualTo(1));
            Assert.That(session.TotalStockCount, Is.EqualTo(7));
            Assert.That(session.Slots[0], Is.Null);
            Assert.That(session.LastSoldItem.Id, Is.EqualTo(deal.Item.Id));
            Assert.That(session.LastSalePrice, Is.EqualTo(100));
            Assert.That(session.SettlementSequence, Is.EqualTo(1));
            Assert.That(session.CurrentCustomer, Is.Null);
        }

        [Test]
        public void LowOpeningUsesImmutableBaseRatherThanRecommendationOrNewCounter()
        {
            var session = Stocked();
            var deal = Visitor(session, false);
            Assert.That(deal.R, Is.EqualTo(100));
            Assert.That(deal.B, Is.EqualTo(80));
            Assert.That(deal.N, Is.EqualTo(80));
            Assert.That(deal.GetPriceOptions().Select(o => o.Price), Is.EqualTo(new[] { 80, 84, 92, 100, 120 }));
            Assert.That(deal.GetPriceOptions()[0].Enabled, Is.False);
            session.Offer(deal.Id, deal.Revision, 4);
            Assert.That(session.CurrentDeal, Is.SameAs(deal));
            Assert.That(deal.B, Is.EqualTo(80));
            Assert.That(deal.GetPriceOptions()[4].Price, Is.EqualTo(120));
            foreach (var option in deal.GetPriceOptions())
                if (option.Price <= deal.N) Assert.That(option.Enabled, Is.False);
        }

        [Test]
        public void AcceptingCounterCompletesWithoutAnotherRoundAndRejectsReplay()
        {
            var session = Stocked();
            var deal = Visitor(session, false);
            long id = deal.Id; int revision = deal.Revision;
            Assert.That(session.Accept(id, revision), Is.True);
            Assert.That(session.Accept(id, revision), Is.False);
            Assert.That(session.Offer(id, revision, 4), Is.False);
            Assert.That(session.Gold, Is.EqualTo(80));
            Assert.That(session.TotalStockCount, Is.EqualTo(7));
            Assert.That(deal.Round, Is.Zero);
        }

        [Test]
        public void StaleRoundResponseCannotChangeDealAgain()
        {
            var session = Stocked();
            var deal = Visitor(session, false);
            int revision = deal.Revision;
            Assert.That(session.Offer(deal.Id, revision, 4), Is.True);
            int round = deal.Round; int? offer = deal.N;
            Assert.That(session.Offer(deal.Id, revision, 4), Is.False);
            Assert.That(session.Accept(deal.Id, revision), Is.False);
            Assert.That(deal.Round, Is.EqualTo(round));
            Assert.That(deal.N, Is.EqualTo(offer));
        }

        [Test]
        public void PreviousDealCommandCannotSellNextCustomersItem()
        {
            var session = Stocked();
            var old = Visitor(session, false);
            session.Accept(old.Id, old.Revision);
            var next = Visitor(session, false, slot: 1);
            Assert.That(next.Id, Is.Not.EqualTo(old.Id));
            Assert.That(session.Accept(old.Id, old.Revision), Is.False);
            Assert.That(session.CurrentDeal, Is.SameAs(next));
            Assert.That(session.SalesCount, Is.EqualTo(1));
        }

        [Test]
        public void NoOfferMeansAcceptUnavailableAndDisabledPriceCannotStartRound()
        {
            var session = Stocked();
            var deal = Visitor(session);
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.False);
            session.Refuse(session.CurrentCustomer.Id);
            deal = Visitor(session, false);
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.False);
            Assert.That(deal.Round, Is.Zero);
            Assert.That(session.Gold, Is.Zero);
        }

        [Test]
        public void RefusalAndManualClosePreserveGoodsAndRevenue()
        {
            var session = Stocked();
            Visitor(session);
            long customerId = session.CurrentCustomer.Id;
            Assert.That(session.Refuse(customerId), Is.True);
            Assert.That(session.Refuse(customerId), Is.False);
            Visitor(session, false);
            session.Tick(10); session.Close();
            Assert.That(session.CurrentDeal, Is.Null);
            Assert.That(session.TotalStockCount, Is.EqualTo(8));
            Assert.That(session.Gold, Is.Zero);
            Assert.That(session.RemainingSeconds, Is.EqualTo(110));
        }

        [Test]
        public void ZeroTimerAllowsCurrentDealToFinishButBlocksNextVisitor()
        {
            var session = Stocked(1);
            var deal = Visitor(session, false);
            session.Tick(10);
            Assert.That(session.IsOpen, Is.True);
            Assert.That(session.CurrentDeal, Is.SameAs(deal));
            Assert.That(session.RemainingSeconds, Is.Zero);
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.True);
            Assert.That(session.Gold, Is.EqualTo(80));
            Assert.That(session.IsOpen, Is.False);
            Assert.That(session.AdvanceCustomer(), Is.False);
            Assert.That(session.Open(), Is.False);
        }

        [Test]
        public void ZeroTimerDoesNotRefillPatienceOrProtectFromWalkout()
        {
            var session = Stocked(1);
            var deal = Visitor(session, ceiling: 100, patience: 1);
            session.Tick(2);
            // A demand the visitor has no patience left for ends the visit; at zero time the shop then closes.
            Assert.That(session.Offer(deal.Id, deal.Revision, 4), Is.True);
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.WalkedAway));
            Assert.That(session.IsOpen, Is.False);
            Assert.That(session.TotalStockCount, Is.EqualTo(8));
            Assert.That(session.Gold, Is.Zero);
        }

        [Test]
        public void LastOfferRemainsAcceptableEvenWithExhaustedPatience()
        {
            var session = Stocked();
            // +5% over a 100 ceiling costs 2 (bold over the limit) — exactly the visitor's patience.
            var deal = Visitor(session, ceiling: 100, patience: 2);
            session.Offer(deal.Id, deal.Revision, 1);
            Assert.That(deal.IsLastOffer, Is.True);
            Assert.That(deal.Patience, Is.Zero);
            int price = deal.N.Value;
            Assert.That(price, Is.LessThanOrEqualTo(100));
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.True);
            Assert.That(session.Gold, Is.EqualTo(price));
        }

        [Test]
        public void EmptyDisplayClosesEvenIfInventoryHasStock()
        {
            var session = new TradingSession();
            session.SeedInventory(Horn, 2); session.PlaceItem(0, 0);
            var deal = Visitor(session);
            session.Offer(deal.Id, deal.Revision, 0);
            Assert.That(session.IsOpen, Is.False);
            Assert.That(session.DisplayCount, Is.Zero);
            Assert.That(session.Inventory.Count, Is.EqualTo(1));
            Assert.That(session.Open(), Is.False);
            session.PlaceItem(0, 0);
            Assert.That(session.Open(), Is.True);
        }

        [Test]
        public void VisitorMustAskForAnItemOnDisplay()
        {
            var session = new TradingSession();
            session.SeedInventory(Horn, 2); session.PlaceItem(0, 0);
            session.Open(); session.Refuse(session.CurrentCustomer.Id);
            // No item named, an empty slot, a slot outside the display: such a visitor never arrives.
            Assert.Throws<ArgumentException>(() => session.AdvanceCustomer(new CustomerTerms { MaxPrice = 130 }));
            Assert.Throws<ArgumentException>(() => session.AdvanceCustomer(new CustomerTerms { RequestedSlot = 3, MaxPrice = 130 }));
            Assert.Throws<ArgumentException>(() => session.AdvanceCustomer(new CustomerTerms { RequestedSlot = 6, MaxPrice = 130 }));
            Assert.That(session.CurrentCustomer, Is.Null);
            Assert.That(session.CurrentDeal, Is.Null);
            Assert.That(session.TotalStockCount, Is.EqualTo(2));
        }

        [Test]
        public void EmptyReturnAndReplayedCompletionDoNotResetShift()
        {
            var session = Stocked();
            session.Open(); session.Tick(30); session.Close();
            long first = session.BeginExpedition();
            Assert.That(first, Is.GreaterThan(0));
            Assert.That(session.Open(), Is.False);
            Assert.That(session.CompleteExpedition(first), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(90));
            Assert.That(session.CollectLoot(first, Herb), Is.False);
            Assert.That(session.CompleteExpedition(first), Is.False);
            Assert.That(session.RemainingSeconds, Is.EqualTo(90));
        }

        [Test]
        public void FreshLootRefillsOnceAndOldExpeditionCannotRefillAgain()
        {
            var session = Stocked();
            session.Open(); session.Tick(30); session.Close();
            long first = session.BeginExpedition();
            Assert.That(session.BeginExpedition(), Is.Zero);
            Assert.That(session.CollectLoot(first, Herb), Is.True);
            Assert.That(session.PendingLootCount, Is.EqualTo(1));
            Assert.That(session.CompleteExpedition(first), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(120));
            Assert.That(session.TotalStockCount, Is.EqualTo(9));
            session.Open(); session.Tick(20); session.Close();
            long second = session.BeginExpedition();
            Assert.That(session.CompleteExpedition(first), Is.False);
            Assert.That(session.CollectLoot(first, Herb), Is.False);
            Assert.That(session.CompleteExpedition(second), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(100));
        }

        [Test]
        public void RoundedDuplicateButtonsAreDisabledRatherThanRepeatingSamePrice()
        {
            var session = new TradingSession();
            session.SeedInventory(new ItemDefinition("cheap", "Камешек", "Находки", 1));
            session.PlaceItem(0, 0);
            var deal = Visitor(session, ceiling: 2);
            Assert.That(deal.GetPriceOptions().Select(o => o.Price), Is.EqualTo(new[] { 1, 1, 1, 1, 2 }));
            Assert.That(deal.GetPriceOptions().Select(o => o.Enabled), Is.EqualTo(new[] { true, false, false, false, true }));
        }

        [Test]
        public void CustomerCeilingIsFixedAndAllCountersRemainWithinBudget()
        {
            var session = Stocked();
            var deal = Visitor(session, false, ceiling: 101, patience: 8);
            while (session.CurrentDeal != null && !deal.IsLastOffer)
            {
                session.Offer(deal.Id, deal.Revision, 4);
                Assert.That(deal.MaxPrice, Is.EqualTo(101));
                Assert.That(deal.N.Value, Is.LessThanOrEqualTo(101));
                Assert.That(deal.B, Is.EqualTo(80));
            }
            Assert.That(session.Gold, Is.Zero);
        }

        [Test]
        public void HigherDemandsSpendMorePatienceWithoutArtificialTimeCharge()
        {
            var session = Stocked();
            var deal = Visitor(session, ceiling: 110, patience: 5);
            session.Offer(deal.Id, deal.Revision, 4);
            Assert.That(deal.Patience, Is.EqualTo(2));
            Assert.That(session.RemainingSeconds, Is.EqualTo(120));
        }

        [Test]
        public void ItemTypeCannotChangeRecommendedPriceAcrossInstances()
        {
            var session = Stocked();
            Assert.Throws<ArgumentException>(() => session.SeedInventory(new ItemDefinition("horn", "Рог", "Трофеи", 1)));
            Assert.That(session.TotalStockCount, Is.EqualTo(8));
        }

        [Test]
        public void InvalidClockAndConfigurationAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings { ShiftSeconds = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings { MaxOpeningRatio = 1 }));
            var session = Stocked();
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(-1));
        }

        [Test]
        public void LowCeilingTuningCannotGenerateAnUnaffordableOpening()
        {
            var session = new TradingSession(new TradeSettings { MinCeilingRatio = .5f, MaxCeilingRatio = .5f });
            session.SeedInventory(Horn); session.PlaceItem(0, 0); session.Open();
            for (int i = 0; i < 50; i++)
            {
                var customer = session.CurrentCustomer;
                Assert.That(customer.MaxPrice, Is.GreaterThanOrEqualTo(session.CurrentDeal.N ?? session.CurrentDeal.R));
                session.Refuse(customer.Id); session.AdvanceCustomer();
            }
        }

        [Test]
        public void OneCoinGoodsAlwaysStartWithPriceQuestion()
        {
            var session = new TradingSession();
            session.SeedInventory(Horn);
            session.SeedInventory(new ItemDefinition("chip", "Обломок", "Трофеи", 1));
            session.PlaceItem(0, 0); session.PlaceItem(0, 1); session.Open();
            for (int i = 0; i < 100; i++)
            {
                var customer = session.CurrentCustomer;
                if (session.CurrentDeal.R == 1)
                {
                    Assert.That(customer.AsksPrice, Is.True);
                    Assert.That(session.CurrentDeal.N, Is.Null);
                }
                session.Refuse(customer.Id); session.AdvanceCustomer();
            }
        }

        [Test]
        public void RandomVisitorsPreserveStockAndBudgetInLongSession()
        {
            var session = Stocked(1000);
            for (int visit = 0; visit < 150; visit++)
            {
                if (!session.IsOpen) session.Open();
                if (session.CurrentCustomer == null) session.AdvanceCustomer();
                var customer = session.CurrentCustomer;
                var deal = session.CurrentDeal;
                Assert.That(deal, Is.Not.Null);
                Assert.That(deal.MaxPrice, Is.GreaterThanOrEqualTo(deal.N ?? deal.R));
                Assert.That(deal.B, Is.LessThanOrEqualTo(deal.R));
                session.Refuse(customer.Id);
                session.Tick(.1f);
            }
            Assert.That(session.TotalStockCount, Is.EqualTo(8));
            Assert.That(session.Gold, Is.Zero);
        }

        [Test]
        public void NewDealStartsNeutralAndCounterShowsDispleasureWithoutDeparture()
        {
            var session = Stocked();
            var deal = Visitor(session, ceiling: 110, patience: 4);
            var priorDeparture = session.LastDeparture;
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Neutral));
            Assert.That(session.Offer(deal.Id, deal.Revision, 3), Is.True);
            Assert.That(deal.Patience, Is.EqualTo(2));
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Displeased));
            Assert.That(session.LastDeparture, Is.SameAs(priorDeparture));
            Assert.That(session.CurrentDeal, Is.SameAs(deal));
        }

        [Test]
        public void LowPatienceAndFinalCounterAreAngryBeforeCustomerLeaves()
        {
            var session = Stocked(presetCosts: true);
            var deal = Visitor(session, ceiling: 100, patience: 3);
            long customerId = session.CurrentCustomer.Id;
            int departures = session.DepartureSequence;
            session.Offer(deal.Id, deal.Revision, 3);
            Assert.That(deal.Patience, Is.EqualTo(1));
            Assert.That(deal.IsLastOffer, Is.False);
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Angry));
            session.Offer(deal.Id, deal.Revision, 1);
            Assert.That(deal.IsLastOffer, Is.True);
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Angry));
            Assert.That(session.CurrentCustomer.Id, Is.EqualTo(customerId));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures));
        }

        [Test]
        public void SuccessfulOfferRecordsPurchasedAndHappyDealExactlyOnce()
        {
            var session = Stocked();
            var deal = Visitor(session);
            long customerId = session.CurrentCustomer.Id;
            int departures = session.DepartureSequence;
            int revision = deal.Revision;
            session.Offer(deal.Id, revision, 0);
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Happy));
            Assert.That(session.LastDeparture.CustomerId, Is.EqualTo(customerId));
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Purchased));
            Assert.That(session.LastDeparture.Sequence, Is.EqualTo(departures + 1));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
            var departure = session.LastDeparture;
            Assert.That(session.Offer(deal.Id, revision, 0), Is.False);
            Assert.That(session.Accept(deal.Id, revision), Is.False);
            Assert.That(session.Refuse(customerId), Is.False);
            Assert.That(session.LastDeparture, Is.SameAs(departure));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
        }

        [Test]
        public void AcceptedFinalOfferRecordsPurchaseRatherThanWalkout()
        {
            var session = Stocked();
            var deal = Visitor(session, ceiling: 100, patience: 2);
            long customerId = session.CurrentCustomer.Id;
            int departures = session.DepartureSequence;
            session.Offer(deal.Id, deal.Revision, 1);
            Assert.That(deal.IsLastOffer, Is.True);
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Angry));
            session.Accept(deal.Id, deal.Revision);
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Happy));
            Assert.That(session.LastDeparture.CustomerId, Is.EqualTo(customerId));
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Purchased));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
        }

        [Test]
        public void ExplicitRefusalRecordsRefusedAndPreservesPreviousDepartureSnapshot()
        {
            var session = Stocked();
            Visitor(session);
            long firstId = session.CurrentCustomer.Id;
            session.Refuse(firstId);
            var refused = session.LastDeparture;
            Assert.That(refused.CustomerId, Is.EqualTo(firstId));
            Assert.That(refused.Outcome, Is.EqualTo(CustomerOutcome.Refused));
            session.AdvanceCustomer();
            Assert.That(session.LastDeparture, Is.SameAs(refused));
            long nextId = session.CurrentCustomer.Id;
            session.Refuse(nextId);
            Assert.That(session.LastDeparture.CustomerId, Is.EqualTo(nextId));
            Assert.That(session.LastDeparture.Sequence, Is.EqualTo(refused.Sequence + 1));
            Assert.That(refused.CustomerId, Is.EqualTo(firstId));
            Assert.That(refused.Outcome, Is.EqualTo(CustomerOutcome.Refused));
        }

        [Test]
        public void PushingFinalOfferRecordsWalkedAwayOnlyOnceAtExpiredShift()
        {
            var session = Stocked(1);
            var deal = Visitor(session, ceiling: 100, patience: 1);
            long customerId = session.CurrentCustomer.Id;
            int departures = session.DepartureSequence;
            session.Tick(2);
            session.Offer(deal.Id, deal.Revision, 4);
            session.Offer(deal.Id, deal.Revision, 4);
            Assert.That(session.LastDeparture.CustomerId, Is.EqualTo(customerId));
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.WalkedAway));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
            Assert.That(session.IsOpen, Is.False);
            Assert.That(session.TotalStockCount, Is.EqualTo(8));
            session.Close(); session.Tick(2); session.AdvanceCustomer();
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.WalkedAway));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
        }

        [Test]
        public void ClosingMidConversationRecordsShopClosedOnce()
        {
            var session = Stocked();
            Visitor(session);
            long customerId = session.CurrentCustomer.Id;
            int departures = session.DepartureSequence;
            session.Close();
            Assert.That(session.LastDeparture.CustomerId, Is.EqualTo(customerId));
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.ShopClosed));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
            session.Close();
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
        }

        [Test]
        public void NoCustomerMeansNoDepartureAndClosingAfterSaleDoesNotOverwritePurchase()
        {
            var session = Stocked();
            session.Close(); session.Tick(1);
            Assert.That(session.LastDeparture, Is.Null);
            Assert.That(session.DepartureSequence, Is.Zero);
            var deal = Visitor(session, false);
            session.Accept(deal.Id, deal.Revision);
            var departure = session.LastDeparture;
            session.Close();
            Assert.That(session.LastDeparture, Is.SameAs(departure));
            Assert.That(session.DepartureSequence, Is.EqualTo(departure.Sequence));
            Assert.That(departure.Outcome, Is.EqualTo(CustomerOutcome.Purchased));
        }

        [Test]
        public void LastItemSaleAndExpiredShiftDoNotOverwritePurchasedDeparture()
        {
            var session = new TradingSession(new TradeSettings { ShiftSeconds = 1 });
            session.SeedInventory(Horn); session.PlaceItem(0, 0);
            var deal = Visitor(session);
            long customerId = session.CurrentCustomer.Id;
            int departures = session.DepartureSequence;
            session.Tick(2);
            session.Offer(deal.Id, deal.Revision, 0);
            Assert.That(session.IsOpen, Is.False);
            Assert.That(session.DisplayCount, Is.Zero);
            Assert.That(session.LastDeparture.CustomerId, Is.EqualTo(customerId));
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Purchased));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures + 1));
        }

        [Test]
        public void StaleCounterCommandsCannotChangeMoodOrCreateDeparture()
        {
            var session = Stocked();
            var deal = Visitor(session, ceiling: 110);
            int revision = deal.Revision;
            int departures = session.DepartureSequence;
            session.Offer(deal.Id, revision, 3);
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Displeased));
            Assert.That(session.Offer(deal.Id, revision, 4), Is.False);
            Assert.That(session.Accept(deal.Id, revision), Is.False);
            Assert.That(deal.Mood, Is.EqualTo(CustomerMood.Displeased));
            Assert.That(session.DepartureSequence, Is.EqualTo(departures));
        }
            [Test]
        public void GreedBeyondRemainingPatienceMakesTheVisitorWalkOut()
        {
            var session = Stocked(presetCosts: true);
            var deal = Visitor(session, ceiling: 130, patience: 2);
            Assert.That(session.Offer(deal.Id, deal.Revision, 4), Is.True);
            Assert.That(session.CurrentCustomer, Is.Null);
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.WalkedAway));
            Assert.That(session.TotalStockCount, Is.EqualTo(8));
            Assert.That(session.Gold, Is.Zero);
        }

        [Test]
        public void PricesAboveTheVisitorsLimitAreNeverAccepted()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var session = Stocked(presetCosts: true, seed: seed);
                var deal = Visitor(session, ceiling: 100, patience: 5);
                while (session.CurrentDeal == deal && !deal.IsLastOffer)
                {
                    int option = deal.GetPriceOptions().ToList().FindIndex(o => o.Enabled && o.Price > 100);
                    if (option < 0) break;
                    session.Offer(deal.Id, deal.Revision, option);
                }
                Assert.That(session.Gold, Is.Zero, "seed " + seed);
                Assert.That(session.SalesCount, Is.Zero, "seed " + seed);
            }
        }

        [Test]
        public void HigherAsksDrawHigherOrEqualCounters()
        {
            int Counter(int option, int seed)
            {
                var session = Stocked(presetCosts: true, seed: seed);
                var deal = Visitor(session, ceiling: 300, patience: 5);
                // Same seed, same rolls: if both asks drew a counter, the bolder ask must not draw a lower one.
                session.Offer(deal.Id, deal.Revision, option);
                return session.CurrentDeal == deal ? deal.N ?? -1 : -1;
            }
            // Several seeds, so the comparison never depends on one particular sequence of rolls.
            int compared = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                int modest = Counter(2, seed), bold = Counter(4, seed);
                if (modest <= 0 || bold <= 0) continue;
                compared++;
                Assert.That(bold, Is.GreaterThanOrEqualTo(modest), "seed " + seed);
            }
            Assert.That(compared, Is.GreaterThan(0), "no seed drew a counter for both asks");
        }

        [Test]
        public void EveryGeneratedVisitorNamesAnItemOnDisplayAndBargainsAtOnce()
        {
            var session = new TradingSession(new TradeSettings(), 3);
            session.SeedInventory(Horn, 2);
            session.SeedInventory(Herb, 3);
            // Two horns and one herb on display, gaps between them; the rest of the herbs stay in stock.
            Assert.That(session.PlaceItem(0, 1), Is.True);
            Assert.That(session.PlaceItem(0, 3), Is.True);
            Assert.That(session.PlaceItem(0, 4), Is.True);
            Assert.That(session.Open(), Is.True);
            bool asked = false, offered = false;
            for (int visit = 0; visit < 60; visit++)
            {
                var customer = session.CurrentCustomer;
                var deal = session.CurrentDeal;
                Assert.That(customer, Is.Not.Null);
                Assert.That(deal, Is.Not.Null, "the deal starts as the visitor arrives");
                Assert.That(session.Slots[customer.RequestedSlot], Is.Not.Null);
                Assert.That(deal.SlotIndex, Is.EqualTo(customer.RequestedSlot));
                Assert.That(deal.Item, Is.SameAs(session.Slots[deal.SlotIndex]));
                // The visitor's first line names the item he wants.
                Assert.That(deal.Reaction.StartsWith(deal.Item.Definition.Name + " — "), Is.True, deal.Reaction);
                Assert.That(session.LastMessage, Is.EqualTo(deal.Reaction));
                if (customer.AsksPrice) { asked = true; Assert.That(deal.N, Is.Null); }
                else { offered = true; Assert.That(deal.Reaction.Contains(deal.B.ToString()), Is.True, deal.Reaction); }
                session.Refuse(customer.Id);
                Assert.That(session.AdvanceCustomer(), Is.True);
            }
            Assert.That(asked && offered, Is.True, "both kinds of opening lines occur");
            Assert.That(session.TotalStockCount, Is.EqualTo(5));
        }
    }
}
