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
