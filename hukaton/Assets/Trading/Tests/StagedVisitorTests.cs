using NUnit.Framework;

namespace Hakaton.Trading.Tests
{
    /// <summary>Board, «Туториал», 06 «Точные ответы покупателей»: the two lessons answer exactly, without chance.</summary>
    public sealed class StagedVisitorTests
    {
        static readonly ItemDefinition Cap = new ItemDefinition("hare_foot", "Грибная шляпка", "Трофеи", 60);
        static readonly ItemDefinition Fang = new ItemDefinition("boar_tusk", "Клык гоблин-пса", "Трофеи", 120);

        public static CustomerTerms CapLesson(int slot) => new CustomerTerms
            { RequestedSlot = slot, AsksPrice = true, MaxPrice = 69, Script = new ScriptedHaggle(69, 66, 90) };
        public static CustomerTerms FangLesson(int slot) => new CustomerTerms
            { RequestedSlot = slot, AsksPrice = false, OpeningRatio = .75f, MaxPrice = 104, Script = new ScriptedHaggle(104, 108, 135) };

        static TradingSession Shop(out TradeDeal deal, CustomerTerms terms, int seed = 1)
        {
            var session = new TradingSession(new TradeSettings(), seed);
            session.SeedInventory(Cap);
            session.SeedInventory(Fang);
            session.PlaceItem(0, 0);
            session.PlaceItem(0, 1);
            session.CustomerScript = () => terms;
            Assert.That(session.Open(), Is.True);
            deal = session.CurrentDeal;
            return session;
        }

        [TestCase(0, 60)] [TestCase(1, 63)] [TestCase(2, 69)]
        public void CapSellsUpToFifteenPercent(int option, int price)
        {
            for (int seed = 1; seed < 6; seed++)
            {
                var session = Shop(out var deal, CapLesson(0), seed);
                Assert.That(deal.GetPriceOptions()[option].Price, Is.EqualTo(price));
                session.Offer(deal.Id, deal.Revision, option);
                Assert.That(session.LastSalePrice, Is.EqualTo(price), "seed " + seed);
            }
        }

        [Test]
        public void CapAtTwentyFiveGetsTheLastWordSixtySix()
        {
            var session = Shop(out var deal, CapLesson(0));
            Assert.That(deal.GetPriceOptions()[3].Price, Is.EqualTo(75));
            session.Offer(deal.Id, deal.Revision, 3);
            Assert.That((deal.N, deal.IsLastOffer, session.SalesCount), Is.EqualTo(((int?)66, true, 0)));
            session.Accept(deal.Id, deal.Revision);
            Assert.That(session.LastSalePrice, Is.EqualTo(66));
        }

        [Test]
        public void NewPriceAfterTheLastWordOrFiftyPercentEndsTheVisit()
        {
            var session = Shop(out var deal, CapLesson(0));
            session.Offer(deal.Id, deal.Revision, 3);
            session.Offer(deal.Id, deal.Revision, 4);
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.WalkedAway));
            Assert.That(session.Slots[0], Is.Not.Null, "The item stays.");
            session = Shop(out deal, CapLesson(0));
            Assert.That(deal.GetPriceOptions()[4].Price, Is.EqualTo(90));
            session.Offer(deal.Id, deal.Revision, 4);
            Assert.That(session.LastDeparture.Outcome, Is.EqualTo(CustomerOutcome.WalkedAway));
        }

        [Test]
        public void FangOffersNinetyAndAnswersOnItsOwnScale()
        {
            var session = Shop(out var deal, FangLesson(1));
            var options = deal.GetPriceOptions();
            Assert.That(deal.N, Is.EqualTo(90), "He names his price.");
            Assert.That(new[] { options[1].Price, options[2].Price, options[3].Price, options[4].Price }, Is.EqualTo(new[] { 95, 104, 113, 135 }));
            Assert.That(options[0].Enabled, Is.False, "Prices up to N are not offered; agreeing is «Продать за N».");
            session.Offer(deal.Id, deal.Revision, 3);
            Assert.That(deal.N, Is.EqualTo(108));
            session.Accept(deal.Id, deal.Revision);
            Assert.That(session.LastSalePrice, Is.EqualTo(108));

            session = Shop(out deal, FangLesson(1));
            session.Accept(deal.Id, deal.Revision);
            Assert.That(session.LastSalePrice, Is.EqualTo(90));
            session = Shop(out deal, FangLesson(1));
            session.Offer(deal.Id, deal.Revision, 2);
            Assert.That(session.LastSalePrice, Is.EqualTo(104));
        }

        [Test]
        public void PausedClockDoesNotRun()
        {
            var session = Shop(out _, CapLesson(0));
            float before = session.RemainingSeconds;
            session.ClockPaused = true;
            session.Tick(30);
            Assert.That(session.RemainingSeconds, Is.EqualTo(before));
        }
    }
}
