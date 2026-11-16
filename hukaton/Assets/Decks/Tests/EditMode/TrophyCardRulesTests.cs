using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    /// <summary>Trophy cards (board «Пинок и трофейные карты»): chances, luck and which card comes.</summary>
    public sealed class TrophyCardRulesTests
    {
        static readonly (int, double)[] Bands = { (3, .08), (8, .2), (15, .4), (9999, .75) };

        static readonly CardSpec[] Shop =
        {
            new CardSpec("fire", "Огонь", 100, 1), new CardSpec("ice", "Лёд", 120, 1), new CardSpec("fog", "Туман", 120, 2),
            new CardSpec("rope", "Верёвка", 180, 3), new CardSpec("updraft", "Поток", 260, 4), new CardSpec("teleport", "Телепорт", 450, 5),
            new CardSpec("dummy", "Пустышка", 50, 1, true, null, dummy: true),
        };

        [TestCase(1, .08), TestCase(3, .08), TestCase(4, .2), TestCase(14, .4), TestCase(20, .75), TestCase(100000, .75)]
        public void TougherCreaturesLeaveCardsMoreOften(int health, double chance) =>
            Assert.AreEqual(chance, TrophyCardRules.CreatureChance(health, false, Bands), 1e-9);

        [Test]
        public void PeacefulAndTimidCreaturesLeaveNone() => Assert.AreEqual(0, TrophyCardRules.CreatureChance(20, true, Bands));

        [Test]
        public void NeverADummy()
        {
            var random = new Random(1);
            for (int i = 0; i < 2000; i++) Assert.IsFalse(TrophyCardRules.Pick(Shop, new HashSet<string>(), random).Dummy);
        }

        [Test]
        public void RanksFollowTheGoblinsWeights()
        {
            var random = new Random(2);
            var counts = new Dictionary<int, int>();
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                int rank = TrophyCardRules.Pick(Shop, new HashSet<string>(), random).Rank;
                counts[rank] = counts.TryGetValue(rank, out var c) ? c + 1 : 1;
            }
            Assert.AreEqual(.5, counts[1] / (double)n, .02);
            Assert.AreEqual(.3, counts[2] / (double)n, .02);
            Assert.AreEqual(.15, counts[3] / (double)n, .015);
        }

        [Test]
        public void CardsThePlayerLacksComeTwiceAsOften()
        {
            var random = new Random(3);
            var owned = new HashSet<string> { "fire" };
            int fire = 0, ice = 0;
            for (int i = 0; i < 20000; i++)
            {
                var spec = TrophyCardRules.Pick(Shop, owned, random);
                if (spec.Id == "fire") fire++;
                if (spec.Id == "ice") ice++;
            }
            Assert.AreEqual(2, ice / (double)fire, .15);
        }

        [Test]
        public void RichChestsGiveRareCards()
        {
            var random = new Random(4);
            for (int i = 0; i < 500; i++) Assert.GreaterOrEqual(TrophyCardRules.Pick(Shop, null, random, 2, 3).Rank, 3);
        }

        [Test]
        public void ACardIsSureAfterDryKills()
        {
            var luck = new TrophyLuck(4);
            luck.Begin(false);
            for (int i = 0; i < 4; i++) Assert.IsFalse(luck.Kill(0, .5));
            Assert.IsTrue(luck.Kill(0, .5));
            Assert.AreEqual(0, luck.Dry);
        }

        [Test]
        public void StartingOutTheFirstKillPays()
        {
            var luck = new TrophyLuck(4);
            luck.Begin(true);
            Assert.IsTrue(luck.Kill(0, .99));
            Assert.IsFalse(luck.Kill(0, .99));
            luck.Begin(false);
            Assert.IsFalse(luck.Kill(0, .99));
        }
    }
}
