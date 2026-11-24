using System;
using NUnit.Framework;

namespace Hakaton.Trading.Tests
{
    /// <summary>
    /// The floor of an answer (<see cref="TradeSettings.MinAnswerSeconds"/>): every answer to a visitor costs at least that
    /// much of the shift, counted from the start of the line it answers. The shop's clock stands while a line types, so an
    /// answer with no <see cref="TradingSession.Tick"/> before it is an answer pressed while the line was still typing.
    /// </summary>
    public sealed class AnswerFloorTests
    {
        static readonly ItemDefinition Ware = new ItemDefinition("floor_ware", "Товар", "Трофеи", 100);
        const float Shift = 15, Floor = 2;

        static TradingSession Create(int seed = 5)
        {
            var session = new TradingSession(new TradeSettings { ShiftSeconds = Shift, MinAnswerSeconds = Floor }, seed);
            session.SeedInventory(Ware, 6);
            for (int slot = 0; slot < session.Slots.Count; slot++) Assert.That(session.PlaceItem(0, slot), Is.True);
            return session;
        }

        /// <summary>Every visitor asks the price and answers any price above one coin with his last word, 90.</summary>
        static void Staged(TradingSession session) => session.CustomerScript = () => new CustomerTerms
            { Name = "Тест", RequestedSlot = FirstOnDisplay(session), AsksPrice = true, MaxPrice = 200, Script = new ScriptedHaggle(1, 90, 1000) };

        static int FirstOnDisplay(TradingSession session)
        {
            for (int i = 0; i < session.Slots.Count; i++) if (session.Slots[i] != null) return i;
            return -1;
        }

        [Test]
        public void AnAnswerWhileTheLineTypesCostsExactlyTheFloor()
        {
            var session = Create();
            Staged(session);
            Assert.That(session.Open(), Is.True);
            var deal = session.CurrentDeal;
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True, "A price at once, as his line still types.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift - Floor));
            Assert.That(session.LastAnswerFloorCharge, Is.EqualTo(Floor));
            Assert.That(session.CurrentDeal, Is.SameAs(deal), "His counter: a new line to answer.");
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.True, "Taking the counter at once is an answer too.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift - 2 * Floor));
            Assert.That(session.AdvanceCustomer(), Is.True);
            Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True, "So is a refusal.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift - 3 * Floor));
        }

        [Test]
        public void AnAnswerThreeSecondsAfterTheLineCostsThoseThreeAndNothingMore()
        {
            var session = Create();
            Staged(session);
            Assert.That(session.Open(), Is.True);
            session.Tick(3);
            var deal = session.CurrentDeal;
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift - 3));
            Assert.That(session.LastAnswerFloorCharge, Is.Zero);
            // Half a second on his counter: the answer pays the other one and a half.
            session.Tick(.5f);
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.True);
            Assert.That(session.LastAnswerFloorCharge, Is.EqualTo(1.5f));
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift - 3 - Floor));
        }

        [Test]
        public void TwentyInstantAnswersEndAFifteenSecondShiftByTheEighthVisitor([Values(1, 2, 3, 4, 5)] int seed)
        {
            var session = Create(seed);
            var pick = new Random(seed);
            Assert.That(session.Open(), Is.True);
            int visitors = 1, answers = 0, visitorsAtZero = 0;
            while (session.IsOpen && answers < 20)
            {
                if (session.CurrentCustomer == null)
                {
                    Assert.That(session.AdvanceCustomer(), Is.True);
                    visitors++;
                }
                var deal = session.CurrentDeal;
                // A mix of instant answers: refusals, prices, taking the counter.
                int kind = pick.Next(3);
                bool answered = kind == 1 && deal.N.HasValue ? session.Accept(deal.Id, deal.Revision)
                    : kind == 2 && session.Offer(deal.Id, deal.Revision, pick.Next(5));
                if (!answered) Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
                answers++;
                if (visitorsAtZero == 0 && session.RemainingSeconds <= 0)
                {
                    visitorsAtZero = visitors;
                    Assert.That(answers, Is.EqualTo(8), "15 s at 2 s an answer: the eighth answer takes the last second.");
                }
            }
            Assert.That(session.IsOpen, Is.False, $"The shift is over after {answers} answers to {visitors} visitors.");
            Assert.That(session.RemainingSeconds, Is.Zero, "It ended by time, not by an empty display.");
            Assert.That(visitorsAtZero, Is.InRange(1, 8), "No ninth visitor.");
            Assert.That(visitors, Is.EqualTo(visitorsAtZero), "The visitor at zero was served to the end, and nobody came after.");
        }

        [Test]
        public void TheFloorNeverTakesTheClockBelowZeroAndAVisitorAtTheCounterIsStillServed()
        {
            var session = Create();
            Staged(session);
            Assert.That(session.Open(), Is.True);
            var deal = session.CurrentDeal;
            session.Tick(Shift - 1);
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True, "14 s spent: no floor to pay.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(1));
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.True, "At once: 2 s owed, 1 s left.");
            Assert.That(session.LastAnswerFloorCharge, Is.EqualTo(1));
            Assert.That(session.RemainingSeconds, Is.Zero);
            Assert.That((session.SalesCount, session.IsOpen), Is.EqualTo((1, false)), "Sold, then the shift ended.");

            // A visitor who came in before the time ran out is served to the end, floor or not.
            session = Create();
            Staged(session);
            Assert.That(session.Open(), Is.True);
            deal = session.CurrentDeal;
            session.Tick(Shift);
            Assert.That((session.RemainingSeconds, session.IsOpen), Is.EqualTo((0f, true)));
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True);
            Assert.That(session.CurrentDeal, Is.SameAs(deal), "He counters at zero.");
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.True, "An instant answer at zero.");
            Assert.That(session.RemainingSeconds, Is.Zero);
            Assert.That(session.LastAnswerFloorCharge, Is.Zero);
            Assert.That((session.SalesCount, session.IsOpen), Is.EqualTo((1, false)));
        }

        [Test]
        public void TheTutorialsTradeIsNeverCharged()
        {
            var session = Create();
            session.ClockPaused = true;
            Staged(session);
            Assert.That(session.Open(), Is.True);
            var deal = session.CurrentDeal;
            Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True);
            Assert.That(session.Accept(deal.Id, deal.Revision), Is.True);
            for (int i = 0; i < 10; i++)
            {
                Assert.That(session.AdvanceCustomer(), Is.True);
                Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            }
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift));
            // A line that began while the clock was paused has no floor even if the pause ends before the answer.
            Assert.That(session.AdvanceCustomer(), Is.True);
            session.ClockPaused = false;
            Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift));
            Assert.That(session.LastAnswerFloorCharge, Is.Zero);
        }

        [Test]
        public void TheBulkBuyersOfferHasTheFloorButNotHisExplanation()
        {
            var session = Create();
            session.ForceBulkBuyerNext = true;
            Assert.That(session.Open(), Is.True);
            var buyer = session.CurrentCustomer;
            Assert.That(buyer.IsBulkBuyer && buyer.FirstMeeting, Is.True);
            // Refused while he still explains himself (the shop has not shown his offer): no floor.
            Assert.That(session.Refuse(buyer.Id), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift));

            session.ForceBulkBuyerNext = true;
            Assert.That(session.AdvanceCustomer(), Is.True);
            buyer = session.CurrentCustomer;
            Assert.That(buyer.IsBulkBuyer && !buyer.FirstMeeting, Is.True, "Met: his offer is his first line.");
            Assert.That(session.OpenBulkOffer(buyer.Id), Is.True, "The shop shows his offer.");
            Assert.That(session.OpenBulkOffer(buyer.Id), Is.False, "Once a visit.");
            Assert.That(session.OpenBulkOffer(buyer.Id + 1), Is.False);
            Assert.That(session.AcceptBulkOffer(buyer.Id), Is.True, "Sold at once.");
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift - Floor));
            Assert.That(session.LastAnswerFloorCharge, Is.EqualTo(Floor));
        }

        [Test]
        public void StoryVisitorsAreReadForFree()
        {
            var session = Create();
            session.CustomerScript = () => new CustomerTerms { Name = "Незнакомец", Talk = new[] { "Раз.", "Два." } };
            Assert.That(session.Open(), Is.True);
            var talker = session.CurrentCustomer;
            Assert.That(talker.IsTalker, Is.True);
            Assert.That(session.Refuse(talker.Id), Is.False, "Nothing to refuse.");
            Assert.That(session.ReadOnTalker(talker.Id), Is.True);
            Assert.That(session.ReadOnTalker(talker.Id), Is.True);
            Assert.That(session.RemainingSeconds, Is.EqualTo(Shift));
        }

        [Test]
        public void TheFloorIsATuningThatCannotBeNegative()
        {
            Assert.That(new TradeSettings().MinAnswerSeconds, Is.Zero, "The bare session has no floor; the game sets it.");
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings { MinAnswerSeconds = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingSession(new TradeSettings { MinAnswerSeconds = float.NaN }));
        }
    }
}
