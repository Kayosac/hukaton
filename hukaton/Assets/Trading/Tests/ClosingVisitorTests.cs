using System;
using NUnit.Framework;

namespace Hakaton.Trading.Tests
{
    /// <summary>
    /// The last visitor of a shift (the game loop's stranger): when the shop would close by itself — time up or the display
    /// sold out — the session asks once for one more visitor. A talker names no item, cannot be refused, is read on line by
    /// line; after his last line he goes and the shift ends.
    /// </summary>
    public sealed class ClosingVisitorTests
    {
        static readonly ItemDefinition Horn = new ItemDefinition("horn", "Рог", "Трофеи", 100);

        static readonly string[] Lines = { "Странно.", "А ты кто такой?", "..." };
        static readonly CustomerMood[] Moods = { CustomerMood.Neutral, CustomerMood.Neutral, CustomerMood.Angry };

        static CustomerTerms Stranger() => new CustomerTerms { Name = "Незнакомец", Talk = Lines, TalkMoods = Moods };

        int asked;

        TradingSession OpenShop(int onDisplay, Func<CustomerTerms> closing)
        {
            var session = new TradingSession(new TradeSettings { ShiftSeconds = 30 }, 5);
            session.SeedInventory(Horn, 4);
            for (int i = 0; i < onDisplay; i++) Assert.That(session.PlaceItem(0, i), Is.True);
            asked = 0;
            session.ClosingVisitor = () => { asked++; return closing(); };
            Assert.That(session.Open(), Is.True);
            return session;
        }

        static void SellCurrent(TradingSession session)
        {
            var deal = session.CurrentDeal;
            Assert.That(deal, Is.Not.Null);
            // R at once sells to a visitor who asked the price; otherwise his own offer is accepted.
            if (deal.N.HasValue) Assert.That(session.Accept(deal.Id, deal.Revision), Is.True);
            else Assert.That(session.Offer(deal.Id, deal.Revision, 0), Is.True);
            Assert.That(session.SalesCount, Is.EqualTo(1));
        }

        static Customer ReadToEnd(TradingSession session)
        {
            var talker = session.CurrentCustomer;
            Assert.That(talker.IsTalker, Is.True);
            Assert.That(session.CurrentDeal, Is.Null, "He names no item.");
            Assert.That(talker.RequestedSlot, Is.EqualTo(-1));
            Assert.That(session.Refuse(talker.Id), Is.False, "A conversation cannot be refused.");
            for (int i = 0; i < Lines.Length; i++)
            {
                Assert.That(talker.Line, Is.EqualTo(i));
                Assert.That(talker.LineMood, Is.EqualTo(Moods[i]));
                Assert.That(session.IsOpen, Is.True);
                Assert.That(session.ReadOnTalker(talker.Id), Is.True);
            }
            return talker;
        }

        [Test]
        public void TimeUpBringsTheStrangerThenTheShiftEnds()
        {
            var session = OpenShop(2, Stranger);
            session.Tick(29);
            Assert.That(session.CurrentCustomer, Is.Not.Null);
            var first = session.CurrentCustomer;
            Assert.That(session.Refuse(first.Id), Is.True);
            session.Tick(5);
            Assert.That(asked, Is.EqualTo(1), "Time is up and the counter is free: asked once.");
            Assert.That(session.IsOpen, Is.True, "The shop waits for him.");
            session.Tick(1);
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(session.AdvanceCustomer(), Is.True);
            var talker = ReadToEnd(session);
            Assert.That(session.CurrentCustomer, Is.Null);
            Assert.That(session.IsOpen, Is.False, "After his last line the shift is over.");
            Assert.That(session.LastDeparture.CustomerId, Is.EqualTo(talker.Id));
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.Talked));
            Assert.That(session.LastDeparture.Talker, Is.True);
            Assert.That(session.LastDeparture.Mood, Is.EqualTo(CustomerMood.Angry), "He goes with the face of his last line.");
            Assert.That(session.SalesCount, Is.Zero, "He buys nothing.");
            Assert.That(session.DisplayCount, Is.EqualTo(2));
        }

        [Test]
        public void SoldOutDisplayBringsHimAfterTheLastSale()
        {
            var session = OpenShop(1, Stranger);
            SellCurrent(session);
            Assert.That(session.DisplayCount, Is.Zero);
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(session.IsOpen, Is.True);
            Assert.That(session.AdvanceCustomer(), Is.True, "He comes even to an empty display.");
            ReadToEnd(session);
            Assert.That(session.IsOpen, Is.False);
        }

        [Test]
        public void NoTermsCloseAsUsualAndAskOnlyOnceAShift()
        {
            var session = OpenShop(2, () => null);
            session.Tick(29);
            Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            session.Tick(5);
            Assert.That(session.IsOpen, Is.False);
            Assert.That(asked, Is.EqualTo(1));
        }

        [Test]
        public void ManualCloseNeverAsks()
        {
            var session = OpenShop(2, Stranger);
            session.Close();
            Assert.That(asked, Is.Zero);
            Assert.That(session.IsOpen, Is.False);
        }

        [Test]
        public void ClosingWhileHeIsOnHisWayDropsHim()
        {
            var session = OpenShop(2, Stranger);
            session.Tick(29);
            Assert.That(session.Refuse(session.CurrentCustomer.Id), Is.True);
            session.Tick(5);
            Assert.That(session.IsOpen, Is.True);
            session.Close();
            Assert.That(session.IsOpen, Is.False);
            Assert.That(session.Open(), Is.False, "Time is up: the shift is over.");
        }
    }
}
