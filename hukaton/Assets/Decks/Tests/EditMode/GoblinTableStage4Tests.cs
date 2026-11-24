using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Random = System.Random;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Balance stage 4 (24.11.2026), the goblin's table: «по-свойски» until the shop's first goal (face up, half price,
    /// no dummy, nothing to reveal); the usual face-down trade after it, with dummies at 10%; the same table until the
    /// player comes back from an outing; the table and the read lines kept in the save; saves from before the stage.
    /// The goal itself is the game's: here the mode is passed to the call, as the game passes it.
    /// </summary>
    public sealed class GoblinTableStage4Tests
    {
        const double DummyChance = .1;
        const int IntroductionLines = 3, ExplanationLines = 4;

        static readonly CardSpec Dummy = new CardSpec("dummy", "Пустышка", 50, 1, true, null, dummy: true);
        static readonly CardSpec[] Shop =
        {
            new CardSpec("fire", "Огонь", 100, 1), new CardSpec("ice", "Лёд", 121, 1), new CardSpec("fog", "Туман", 120, 2),
            new CardSpec("rope", "Верёвка", 180, 3), new CardSpec("updraft", "Поток", 260, 4), new CardSpec("teleport", "Телепорт", 450, 5),
            Dummy
        };

        static DeckWorkshop Workshop(int coins = 100000) => new DeckWorkshop(new DeckRules(startingCoins: coins), Shop, 0);

        static int Dummies(GoblinOffer offer) => Enumerable.Range(0, offer.Count).Count(i => offer.CardAt(i) == Dummy.Id);

        static GoblinSave RoundTrip(GoblinSave save) => JsonUtility.FromJson<GoblinSave>(JsonUtility.ToJson(save));

        // ---------- before the goal: «по-свойски» ----------

        [Test]
        public void BeforeTheGoalTheTableIsFaceUpAtHalfPriceWithoutDummies()
        {
            var w = Workshop();
            for (int seed = 0; seed < 1000; seed++)
            {
                var visits = new GoblinVisits();
                Assert.That(visits.Call(Shop, new Random(seed), DummyChance, friendly: true), Is.True);
                var offer = visits.Offer;
                Assert.That((offer.Count, offer.Friendly, offer.Revealed), Is.EqualTo((10, true, true)), "Ten cards, face up from the start.");
                Assert.That(Dummies(offer), Is.Zero, $"Table {seed}: no dummy on a friendly table.");
                for (int i = 0; i < offer.Count; i++)
                    Assert.That(offer.PriceAt(i, w), Is.EqualTo(w.Spec(offer.CardAt(i)).BlindPrice), "Every card at its half price.");
                Assert.That(offer.RevealAll(), Is.False, "Nothing to reveal.");
            }
        }

        [Test]
        public void AFriendlyCardIsBoughtAtHalfPriceAndTheRestStayHalfPrice()
        {
            var w = Workshop(1000);
            var offer = GoblinOffer.Roll(Shop, new Random(7), DummyChance, friendly: true);
            var spec = w.Spec(offer.CardAt(0));
            Assert.That(offer.Buy(0, w, out var copy), Is.Null);
            Assert.That((copy.PaidPrice, w.Coins), Is.EqualTo((spec.BlindPrice, 1000 - spec.BlindPrice)));
            Assert.That(w.IsSelected(copy.Id), Is.True, "Still equipped at once while the deck has room.");
            Assert.That(offer.RevealAll(), Is.False);
            Assert.That(offer.PriceAt(1, w), Is.EqualTo(w.Spec(offer.CardAt(1)).BlindPrice));
        }

        // ---------- after the goal: the usual trade ----------

        [Test]
        public void TheFirstCallAfterTheGoalLaysANewFaceDownTableEvenWithoutAnOuting()
        {
            var w = Workshop();
            var visits = new GoblinVisits();
            visits.Call(Shop, new Random(1), DummyChance, friendly: true);
            var friendly = visits.Offer;
            friendly.Buy(0, w, out _);
            Assert.That(visits.NeedsNewTable(false), Is.True, "The goal is reached: the friendly table will not do.");
            Assert.That(visits.Call(Shop, new Random(2), DummyChance, friendly: false), Is.True);
            var usual = visits.Offer;
            Assert.That(usual, Is.Not.SameAs(friendly), "A new ten.");
            Assert.That((usual.Friendly, usual.Revealed), Is.EqualTo((false, false)), "Face down.");
            Assert.That(Enumerable.Range(0, usual.Count).Any(usual.IsSold), Is.False, "Nothing bought on it yet.");
            int real = Enumerable.Range(0, usual.Count).First(i => usual.CardAt(i) != Dummy.Id);
            Assert.That(usual.PriceAt(real, w), Is.EqualTo(w.Spec(usual.CardAt(real)).BlindPrice), "Blind: half price.");
            Assert.That(visits.Call(Shop, new Random(3), DummyChance, friendly: false), Is.True);
            Assert.That(visits.Offer, Is.SameAs(usual), "The next call brings the same usual table.");
        }

        [Test]
        public void RevealingTheUsualTableIsFreeAndRaisesThePricesToFull()
        {
            var w = Workshop(1000);
            var visits = new GoblinVisits();
            visits.Call(Shop, new Random(11), 0, friendly: false);
            var offer = visits.Offer;
            Assert.That(offer.RevealAll(), Is.True);
            Assert.That(w.Coins, Is.EqualTo(1000), "Free.");
            for (int i = 0; i < offer.Count; i++)
                Assert.That(offer.PriceAt(i, w), Is.EqualTo(w.Spec(offer.CardAt(i)).Price), "Full price once revealed.");
            Assert.That(offer.RevealAll(), Is.False);
        }

        [Test]
        public void OnUsualTablesAboutOnePlaceInTenHoldsADummy()
        {
            var random = new Random(20261124);
            var visits = new GoblinVisits();
            int dummies = 0, places = 0;
            for (int table = 0; table < 2000; table++)
            {
                // A new table each time: the player has been on an outing since the last one.
                if (table > 0) visits.OutingReturned();
                visits.Call(Shop, random, DummyChance, friendly: false);
                Assert.That(visits.Offer.Friendly, Is.False);
                dummies += Dummies(visits.Offer);
                places += visits.Offer.Count;
            }
            double share = dummies / (double)places;
            TestContext.WriteLine($"Dummies on 2000 usual tables: {dummies} of {places} places ({share:P2}).");
            Debug.Log($"[GoblinTableStage4Tests] dummy share {share:P2} ({dummies}/{places})");
            Assert.That(places, Is.EqualTo(20000));
            Assert.That(share, Is.InRange(.09, .11));
        }

        // ---------- the same table until an outing ----------

        [Test]
        public void TheSameTableComesBackUntilTheNextOuting([Values(false, true)] bool friendly)
        {
            var w = Workshop();
            var visits = new GoblinVisits();
            visits.Call(Shop, new Random(5), DummyChance, friendly);
            var offer = visits.Offer;
            var cards = offer.Save().cards;
            Assert.That(offer.Buy(2, w, out _), Is.Null);
            if (!friendly) offer.RevealAll();
            for (int pull = 0; pull < 5; pull++)
            {
                Assert.That(visits.CanCall, Is.True, "He may be called any time.");
                Assert.That(visits.Call(Shop, new Random(100 + pull), DummyChance, friendly), Is.True);
                Assert.That(visits.Offer, Is.SameAs(offer), "The same table, not a new ten.");
            }
            visits.ShiftClosed(true);
            visits.Call(Shop, new Random(200), DummyChance, friendly);
            Assert.That(visits.Offer, Is.SameAs(offer), "A shift alone does not change the table.");
            Assert.That(visits.Offer.Save().cards, Is.EqualTo(cards), "The same cards in the same places.");
            Assert.That((visits.Offer.IsSold(2), visits.Offer.IsSold(1), visits.Offer.Revealed), Is.EqualTo((true, false, true)),
                "The bought place stays empty, the revealed cards stay face up.");

            visits.OutingReturned();
            Assert.That(visits.NeedsNewTable(friendly), Is.True);
            visits.Call(Shop, new Random(300), DummyChance, friendly);
            Assert.That(visits.Offer, Is.Not.SameAs(offer), "Back from an outing: a new ten.");
            Assert.That(Enumerable.Range(0, GoblinOffer.Size).Any(visits.Offer.IsSold), Is.False);
            Assert.That(visits.Offer.Revealed, Is.EqualTo(friendly), "A usual table is face down again.");
            visits.Call(Shop, new Random(301), DummyChance, friendly);
            Assert.That(visits.NeedsNewTable(friendly), Is.False, "Only one new table per outing.");
        }

        [Test]
        public void AnEmptiedTableStaysEmptyUntilTheNextOuting()
        {
            var w = Workshop();
            var visits = new GoblinVisits();
            visits.Call(Shop, new Random(9), DummyChance, friendly: true);
            for (int i = 0; i < GoblinOffer.Size; i++) Assert.That(visits.Offer.Buy(i, w, out _), Is.Null);
            visits.Call(Shop, new Random(10), DummyChance, friendly: true);
            Assert.That(visits.Offer.AnyLeft, Is.False, "«Всё разобрали»: nothing new without an outing.");
        }

        // ---------- the save ----------

        [Test]
        public void TheTableItsModeAndTheReadLinesSurviveASave([Values(false, true)] bool friendly)
        {
            var w = Workshop();
            var visits = new GoblinVisits();
            visits.Call(Shop, new Random(21), DummyChance, friendly);
            visits.Offer.Buy(4, w, out _);
            if (!friendly) visits.Offer.RevealAll();
            for (int i = 0; i < 5; i++) visits.ReadIntroductionLine(IntroductionLines);
            visits.ReadExplanationLine(ExplanationLines);
            visits.ReadExplanationLine(ExplanationLines);
            var save = RoundTrip(visits.Save());
            Assert.That(save.version, Is.EqualTo(GoblinVisits.SaveVersion));

            var restored = new GoblinVisits();
            Assert.That(restored.TryRestore(save, id => w.Spec(id) != null), Is.True);
            var offer = restored.Offer;
            Assert.That(offer.Save().cards, Is.EqualTo(visits.Offer.Save().cards));
            Assert.That((offer.Friendly, offer.Revealed, offer.IsSold(4), offer.IsSold(3)), Is.EqualTo((friendly, true, true, false)));
            Assert.That((restored.Visited, restored.IntroductionRead, restored.ExplanationRead), Is.EqualTo((true, IntroductionLines, 2)));
            int slot = offer.IsSold(0) ? 1 : 0;
            var spec = w.Spec(offer.CardAt(slot));
            Assert.That(offer.PriceAt(slot, w), Is.EqualTo(friendly || spec.Dummy ? spec.BlindPrice : spec.Price));

            Assert.That(restored.Call(Shop, new Random(22), DummyChance, friendly), Is.True);
            Assert.That(restored.Offer, Is.SameAs(offer), "A loaded game shows the same table.");
        }

        [Test]
        public void AnOutingBeforeTheSaveStillBringsANewTableAfterTheLoad()
        {
            var w = Workshop();
            var visits = new GoblinVisits();
            visits.Call(Shop, new Random(31), DummyChance, friendly: false);
            visits.OutingReturned();
            var restored = new GoblinVisits();
            Assert.That(restored.TryRestore(RoundTrip(visits.Save()), id => w.Spec(id) != null), Is.True);
            Assert.That(restored.NeedsNewTable(false), Is.True);
            var before = restored.Offer;
            restored.Call(Shop, new Random(32), DummyChance, friendly: false);
            Assert.That(restored.Offer, Is.Not.SameAs(before));
        }

        // ---------- saves from before the stage ----------

        /// <summary>A goblin save as written before balance stage 4: no version, no mode, no explanation.</summary>
        static GoblinSave OldSave(bool visited, int introductionRead, bool revealed = false)
        {
            string offer = visited
                ? "{\"cards\":[\"fire\",\"ice\",\"fog\",\"fire\",\"rope\",\"dummy\",\"ice\",\"fire\",\"updraft\",\"fog\"]," +
                  "\"sold\":[false,true,false,false,false,false,false,false,false,false],\"revealed\":" + (revealed ? "true" : "false") + "}"
                : "{\"cards\":[],\"sold\":[],\"revealed\":false}";
            string json = "{\"visited\":" + (visited ? "true" : "false") + ",\"outingSince\":false,\"shiftSince\":false," +
                          "\"introductionRead\":" + introductionRead + ",\"offer\":" + offer + "}";
            var save = JsonUtility.FromJson<GoblinSave>(json);
            Assert.That((save.version, save.explanationRead, save.offer.friendly), Is.EqualTo((0, 0, false)), "Read as an old save.");
            return save;
        }

        static GoblinVisits Load(GoblinSave save, bool goalReached)
        {
            GoblinVisits.UpgradeSave(save, goalReached, IntroductionLines, ExplanationLines);
            var visits = new GoblinVisits();
            var w = Workshop();
            Assert.That(visits.TryRestore(save, id => w.Spec(id) != null), Is.True);
            return visits;
        }

        [Test]
        public void AnOldSaveOfAStrangerStartsWithTheIntroduction([Values(false, true)] bool goalReached)
        {
            var visits = Load(OldSave(false, 0), goalReached);
            Assert.That((visits.Visited, visits.Offer, visits.IntroductionRead, visits.ExplanationRead),
                Is.EqualTo((false, (GoblinOffer)null, 0, 0)), "The introduction first; the explanation too, once the table is usual.");
            visits.Call(Shop, new Random(1), DummyChance, friendly: !goalReached);
            Assert.That(visits.Offer.Friendly, Is.EqualTo(!goalReached), "His first table is in the game's mode.");
        }

        [Test]
        public void AnOldSaveOfAnAcquaintanceBeforeTheGoalTradesFriendlyAndHearsTheExplanationLater()
        {
            var old = OldSave(true, 4, revealed: true);
            var visits = Load(old, goalReached: false);
            Assert.That(old.version, Is.EqualTo(GoblinVisits.SaveVersion), "Upgraded in place.");
            Assert.That(visits.IntroductionRead, Is.EqualTo(IntroductionLines), "The old four lines read: the new three count as read.");
            Assert.That(visits.ExplanationRead, Is.Zero, "The explanation waits for the goal.");
            Assert.That(visits.Offer.Friendly, Is.False, "The old table is a usual one...");
            Assert.That(visits.NeedsNewTable(true), Is.True, "...so the next call replaces it with a friendly one.");
            visits.Call(Shop, new Random(2), DummyChance, friendly: true);
            Assert.That((visits.Offer.Friendly, visits.Offer.Revealed, Dummies(visits.Offer)), Is.EqualTo((true, true, 0)));
            visits.Call(Shop, new Random(3), DummyChance, friendly: false);
            Assert.That(visits.Offer.Friendly, Is.False, "After the goal: a usual table...");
            Assert.That(visits.ExplanationRead, Is.LessThan(ExplanationLines), "...and the explanation is still to be heard.");
        }

        [Test]
        public void AnOldSaveOfAnAcquaintanceAfterTheGoalKeepsItsTableAndSkipsTheExplanation()
        {
            var old = OldSave(true, 4);
            var visits = Load(old, goalReached: true);
            Assert.That((visits.IntroductionRead, visits.ExplanationRead), Is.EqualTo((IntroductionLines, ExplanationLines)),
                "He heard the old introduction, which told the usual rules.");
            var table = visits.Offer;
            Assert.That((table.Friendly, table.Revealed, table.IsSold(1)), Is.EqualTo((false, false, true)));
            Assert.That(visits.NeedsNewTable(false), Is.False);
            visits.Call(Shop, new Random(4), DummyChance, friendly: false);
            Assert.That(visits.Offer, Is.SameAs(table), "The same table as saved.");
        }

        [Test]
        public void AnOldPartlyReadIntroductionIsKeptAndACurrentSaveIsNotUpgradedAgain()
        {
            var partly = OldSave(true, 2);
            GoblinVisits.UpgradeSave(partly, false, IntroductionLines, ExplanationLines);
            Assert.That((partly.introductionRead, partly.explanationRead), Is.EqualTo((2, 0)));

            var visits = new GoblinVisits();
            visits.Call(Shop, new Random(5), DummyChance, friendly: false);
            visits.ReadExplanationLine(ExplanationLines);
            var current = RoundTrip(visits.Save());
            GoblinVisits.UpgradeSave(current, true, IntroductionLines, ExplanationLines);
            Assert.That((current.introductionRead, current.explanationRead), Is.EqualTo((0, 1)), "A current save is left as it is.");
        }
    }
}
