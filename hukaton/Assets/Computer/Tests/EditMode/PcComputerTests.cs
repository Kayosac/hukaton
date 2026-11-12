using System.Linq;
using Hakaton.Decks;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Computer.Tests
{
    public sealed class PcSortTests
    {
        static DeckWorkshop Workshop()
        {
            var specs = new[] { new CardSpec("rope", "Верёвка", 180, 3), new CardSpec("fire", "Огонь", 100, 1), new CardSpec("ice", "Лёд", 120, 2) };
            var w = new DeckWorkshop(new DeckRules(startingCoins: 5000), specs, 0);
            foreach (var id in new[] { "fire", "rope", "ice", "fire", "fire" }) Assert.That(w.Buy(id, out _), Is.Null);
            return w;
        }

        static string[] Names(DeckWorkshop w, PcSortMode mode) =>
            PcSort.Apply(w.Owned, mode, w.IsSelected).Select(c => c.Spec.Name + c.Level).ToArray();

        [Test]
        public void TheButtonCyclesThroughEveryModeAndBack()
        {
            var mode = PcSortMode.Acquired;
            for (int i = 0; i < PcSort.Cycle.Length; i++) mode = PcSort.Next(mode);
            Assert.That(mode, Is.EqualTo(PcSortMode.Acquired));
            Assert.That(PcSort.Cycle.Distinct().Count(), Is.EqualTo(System.Enum.GetValues(typeof(PcSortMode)).Length));
        }

        [Test]
        public void ModesOrderTheCollection()
        {
            var w = Workshop();
            var fire = w.Owned.Where(c => c.Spec.Id == "fire").ToList();
            Assert.That(w.Merge(fire[1].Id, fire[2].Id, out _), Is.Null);
            Assert.That(w.Toggle(w.Owned.First(c => c.Spec.Id == "ice").Id), Is.Null);

            Assert.That(Names(w, PcSortMode.Acquired), Is.EqualTo(new[] { "Огонь1", "Верёвка1", "Лёд1", "Огонь2" }));
            Assert.That(Names(w, PcSortMode.Name), Is.EqualTo(new[] { "Верёвка1", "Лёд1", "Огонь2", "Огонь1" }));
            Assert.That(Names(w, PcSortMode.Level), Is.EqualTo(new[] { "Огонь2", "Верёвка1", "Лёд1", "Огонь1" }));
            Assert.That(Names(w, PcSortMode.DeckFirst), Is.EqualTo(new[] { "Лёд1", "Огонь1", "Верёвка1", "Огонь2" }));
            Assert.That(Names(w, PcSortMode.Rank), Is.EqualTo(new[] { "Верёвка1", "Лёд1", "Огонь2", "Огонь1" }));
        }
    }

    public sealed class PcUpgradeTrackTests
    {
        [Test]
        public void HandAndDeckAreSegmentedTracks()
        {
            var rules = new DeckRules(deckLimit: 12, initialHand: 3, maxHand: 5, handUpgradePrice: 200, startingCoins: 900,
                deckSteps: new[] { 16, 20 }, deckStepPrices: new[] { 150, 250 });
            var w = new DeckWorkshop(rules, new[] { new CardSpec("fire", "Огонь", 100) }, 0);
            var tracks = PcUpgradeTrack.For(w);
            var hand = tracks[0];
            var deck = tracks[1];

            Assert.That((hand.Step, hand.Steps, hand.Price, hand.Values), Is.EqualTo((0, 2, 200, "3 » 4")));
            Assert.That((deck.Step, deck.Steps, deck.Price, deck.Values), Is.EqualTo((0, 2, 150, "12 » 16")));

            Assert.That(hand.Buy(), Is.Null);
            Assert.That((hand.Step, hand.Price, hand.Bought), Is.EqualTo((1, 400, "В руке теперь 4 карты.")));
            Assert.That(deck.Buy(), Is.Null);
            Assert.That(deck.Buy(), Is.Null);
            Assert.That(deck.Maxed && deck.Step == 2 && deck.Values == "20 карт", Is.True);
            Assert.That(w.Coins, Is.EqualTo(900 - 200 - 150 - 250));
            // Not enough coins: the step is not bought.
            Assert.That(hand.Buy(), Is.Not.Null);
            Assert.That(hand.Step, Is.EqualTo(1));
        }

        [TestCase(1, "1 карта"), TestCase(4, "4 карты"), TestCase(5, "5 карт"), TestCase(12, "12 карт"), TestCase(22, "22 карты")]
        public void CardCountsReadNaturally(int n, string expected) => Assert.That(PcUpgradeTrack.Cards(n), Is.EqualTo(expected));

        /// <summary>Playtest: «Время смены» on the PC, paid from the wallet the cards and the shop share.</summary>
        [Test]
        public void ShiftIsTheThirdTrackAndSpendsTheSharedWallet()
        {
            var wallet = new Hakaton.Economy.CoinWallet(500);
            var w = new DeckWorkshop(new DeckRules(deckSteps: new[] { 16 }, deckStepPrices: new[] { 150 }),
                new[] { new CardSpec("fire", "Огонь", 100) }, 0, wallet);
            var shop = new Hakaton.Trading.TradingSession(new Hakaton.Trading.TradeSettings {
                ShiftSeconds = 30, ShiftUpgradeSeconds = new[] { 10f, 10f }, ShiftUpgradePrices = new[] { 150, 250 } }, 1, wallet);
            Assert.That(PcUpgradeTrack.For(w).Count, Is.EqualTo(2), "Without the shop only the deck workshop's tracks.");
            Assert.That(PcUpgradeTrack.For(w, new Hakaton.Trading.TradingSession()).Count, Is.EqualTo(2), "A shop without upgrades adds no track.");

            var tracks = PcUpgradeTrack.For(w, shop);
            Assert.That(tracks.Count, Is.EqualTo(3));
            var shift = tracks[2];
            Assert.That((shift.Title, shift.Step, shift.Steps, shift.Price, shift.Values), Is.EqualTo(("Время смены", 0, 2, 150, "30 » 40 с")));
            Assert.That(shift.Buy(), Is.Null);
            Assert.That((shift.Step, shift.Price, shift.Values, shift.Bought), Is.EqualTo((1, 250, "40 » 50 с", "Смена длится 40 с.")));
            Assert.That((wallet.Balance, w.Coins, shop.Gold), Is.EqualTo((350, 350, 350)));
            Assert.That(shift.Buy(), Is.Null);
            Assert.That((shift.Maxed, shift.Values, wallet.Balance), Is.EqualTo((true, "50 с", 100)));
            Assert.That(shift.Buy(), Is.Not.Null, "Nothing left to buy.");
            Assert.That(wallet.Balance, Is.EqualTo(100));
        }

        /// <summary>Playtest: «Скупщик» raises the bulk buyer's chance, and shows only once he has been met (a surprise until then).</summary>
        [Test]
        public void BulkBuyerTrackAppearsOnceHeHasBeenMet()
        {
            var wallet = new Hakaton.Economy.CoinWallet(12000);
            var w = new DeckWorkshop(new DeckRules(deckSteps: new[] { 16 }, deckStepPrices: new[] { 150 }),
                new[] { new CardSpec("fire", "Огонь", 100) }, 0, wallet);
            var shop = new Hakaton.Trading.TradingSession(new Hakaton.Trading.TradeSettings {
                ShiftUpgradeSeconds = new[] { 10f }, ShiftUpgradePrices = new[] { 150 },
                BulkBuyerChance = .05f, BulkBuyerUpgradeChances = new[] { .05f, .05f, .05f, .05f, .05f },
                BulkBuyerUpgradePrices = new[] { 500, 1000, 2000, 3000, 5000 } }, 1, wallet);
            shop.SeedInventory(new Hakaton.Trading.ItemDefinition("horn", "Рог", "Трофеи", 100), 3);
            for (int i = 0; i < 3; i++) Assert.That(shop.PlaceItem(0, i), Is.True);
            Assert.That(PcUpgradeTrack.For(w, shop).Select(t => t.Title), Has.No.Member("Скупщик"), "Not before he has come.");

            shop.ForceBulkBuyerNext = true;
            Assert.That(shop.Open(), Is.True);
            Assert.That(shop.Refuse(shop.CurrentCustomer.Id), Is.True);
            shop.Close();
            var tracks = PcUpgradeTrack.For(w, shop);
            Assert.That(tracks.Select(t => t.Title), Is.EqualTo(new[] { "Размер руки", "Размер колоды", "Время смены", "Скупщик" }));
            var buyer = tracks[3];
            Assert.That((buyer.Step, buyer.Steps, buyer.Price, buyer.Values), Is.EqualTo((0, 5, 500, "5% » 10%")));
            Assert.That(buyer.Buy(), Is.Null);
            Assert.That((buyer.Step, buyer.Price, buyer.Values, buyer.Bought), Is.EqualTo((1, 1000, "10% » 15%", "Скупщик заходит чаще: 10%.")));
            for (int i = 0; i < 4; i++) Assert.That(buyer.Buy(), Is.Null);
            Assert.That((buyer.Maxed, buyer.Values, wallet.Balance), Is.EqualTo((true, "30%", 500)));
            Assert.That(buyer.Buy(), Is.Not.Null, "Nothing left to buy.");
        }
    }

    public sealed class CrtScreenMathTests
    {
        [Test]
        public void TheCentreOfTheGlassIsTheCentreOfThePicture()
        {
            var shape = CrtShape.Default;
            var uv = CrtScreenMath.ContentUV(new Vector2(.5f, .5f), shape);
            Assert.That(Vector2.Distance(uv, new Vector2(.5f, .5f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void TheBulgePushesTheEdgesOutward()
        {
            var flat = CrtShape.Default; flat.curve = 0;
            var bulged = CrtShape.Default;
            var glass = new Vector2(.9f, .5f);
            Assert.That(CrtScreenMath.ContentUV(glass, bulged).x, Is.GreaterThan(CrtScreenMath.ContentUV(glass, flat).x));
            Assert.That(CrtScreenMath.InsidePicture(new Vector2(.5f, .5f), bulged), Is.True);
            Assert.That(CrtScreenMath.InsidePicture(new Vector2(.001f, .999f), bulged), Is.False, "rounded corner");
        }

        [Test]
        public void InverseBilinearFindsPointsOnACrookedQuad()
        {
            Vector2 bl = new Vector2(0, 0), br = new Vector2(.44f, -.02f), tr = new Vector2(.45f, .28f), tl = new Vector2(.01f, .3f);
            foreach (var uv in new[] { new Vector2(.2f, .7f), new Vector2(.9f, .1f), new Vector2(.5f, .5f) })
            {
                var p = Vector2.Lerp(Vector2.Lerp(bl, br, uv.x), Vector2.Lerp(tl, tr, uv.x), uv.y);
                Assert.That(CrtScreenMath.InverseBilinear(p, bl, br, tr, tl, out var found), Is.True);
                Assert.That(Vector2.Distance(found, uv), Is.LessThan(1e-4f));
            }
        }
    }

    public sealed class CrtSupersampleTests
    {
        [Test]
        public void TheCanvasIsRenderedAtLeastAsTallAsThePictureOnScreen()
        {
            Assert.That(CrtScreen.SupersampleFor(480, 480, 4), Is.EqualTo(1));
            Assert.That(CrtScreen.SupersampleFor(994, 480, 4), Is.EqualTo(3)); // 1080p
            Assert.That(CrtScreen.SupersampleFor(1325, 480, 4), Is.EqualTo(3)); // 1440p
            Assert.That(CrtScreen.SupersampleFor(1987, 480, 4), Is.EqualTo(4)); // 4K, capped
            Assert.That(CrtScreen.SupersampleFor(300, 480, 4), Is.EqualTo(1));
        }
    }
}
