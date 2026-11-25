using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.SpecialItems.Tests
{
    /// <summary>
    /// Balance stage 5, «Телефон: новая таблица исходов» and «Редкая карта»: the phone is above all a machine of temporary
    /// cards (a card in 20% of spins, a rare card of rank III+ in 2%), a pair only returns the bet, three coins pay 100,
    /// the 1% jackpot of 1200 stays. Checked on the saved asset the game reads.
    /// </summary>
    public sealed class PhoneBalanceTests
    {
        const string SettingsPath = "Assets/SpecialItems/Data/Phone settings.asset";
        const string CataloguePath = "Assets/Game/Data/Cards.asset";

        static readonly Dictionary<PhoneOutcome, float> Table = new Dictionary<PhoneOutcome, float>
        {
            { PhoneOutcome.Nothing, 30 }, { PhoneOutcome.Pair, 22 }, { PhoneOutcome.Coins, 5 }, { PhoneOutcome.Card, 20 },
            { PhoneOutcome.RareCard, 2 }, { PhoneOutcome.Heal, 4 }, { PhoneOutcome.Boost, 4 }, { PhoneOutcome.Call, 2 },
            { PhoneOutcome.Debuff, 5 }, { PhoneOutcome.Curse, 1.5f }, { PhoneOutcome.Treasure, 3.5f }, { PhoneOutcome.Jackpot, 1 },
        };

        static PhoneSettings Saved() => AssetDatabase.LoadAssetAtPath<PhoneSettings>(SettingsPath);

        [Test]
        public void TheSavedTableAndPrizesAreStageFives()
        {
            var settings = Saved();
            Assert.That(settings, Is.Not.Null);
            var saved = settings.chances.ToDictionary(c => c.outcome, c => c.percent);
            Assert.That(saved, Is.EquivalentTo(Table), "Every outcome once, with the stage-5 percent.");
            Assert.That((settings.bet, settings.baseSpins, settings.pairCoins, settings.coinsPrize, settings.jackpotCoins),
                Is.EqualTo((10, 10, 10, 100, 1200)), "Bet 10, a pair returns it, three coins 100, the jackpot unchanged.");
            Assert.That((settings.SpinsAt(1), settings.SpinsAt(2), settings.SpinsAt(3)), Is.EqualTo((15, 20, 30)), "The battery is untouched.");
            var defaults = ScriptableObject.CreateInstance<PhoneSettings>();
            Assert.That(defaults.chances.ToDictionary(c => c.outcome, c => c.percent), Is.EquivalentTo(Table), "Defaults match the asset.");
            Assert.That((defaults.pairCoins, defaults.coinsPrize, defaults.jackpotCoins), Is.EqualTo((10, 100, 1200)));
            Object.DestroyImmediate(defaults);
        }

        [Test]
        public void TwoHundredThousandSpinsFollowTheTable()
        {
            var settings = Saved();
            const int spins = 200000;
            var random = new System.Random(20261126);
            var counts = new Dictionary<PhoneOutcome, int>();
            long coins = 0;
            for (int i = 0; i < spins; i++)
            {
                var outcome = SlotMachine.Roll(settings.chances, random);
                counts[outcome] = counts.TryGetValue(outcome, out var c) ? c + 1 : 1;
                if (outcome == PhoneOutcome.Pair) coins += settings.pairCoins;
                if (outcome == PhoneOutcome.Coins) coins += settings.coinsPrize;
                if (outcome == PhoneOutcome.Jackpot) coins += settings.jackpotCoins;
            }
            var report = string.Join(", ", Table.Keys.Select(o => o + " " + (100.0 * counts.GetValueOrDefault(o) / spins).ToString("0.00")));
            Debug.Log("[PhoneBalance] shares, %: " + report);
            foreach (var pair in Table)
                Assert.That(100.0 * counts.GetValueOrDefault(pair.Key) / spins, Is.EqualTo(pair.Value).Within(.5), pair.Key + " — " + report);
            double money = coins / (double)spins;
            double cardsPerTen = 10.0 * (counts.GetValueOrDefault(PhoneOutcome.Card) + counts.GetValueOrDefault(PhoneOutcome.RareCard)) / spins;
            Debug.Log($"[PhoneBalance] money back per spin {money:0.00}, cards per ten spins {cardsPerTen:0.000}");
            Assert.That(money, Is.InRange(18.5, 20), "Coins back per 10-coin spin.");
            Assert.That(cardsPerTen, Is.InRange(2.0, 2.4), "Cards (usual and rare) per ten spins.");
        }

        [Test]
        public void TheRareCardShowsCardPenusCardAndAPairNeverLooksLikeIt()
        {
            var random = new System.Random(11);
            Assert.That(SlotMachine.Show(PhoneOutcome.RareCard, random), Is.EqualTo(new[] { PhoneSymbol.Card, PhoneSymbol.Penus, PhoneSymbol.Card }));
            Assert.That(SlotMachine.Read(SlotMachine.RareCardReels), Is.EqualTo(PhoneOutcome.RareCard));
            Assert.That(SlotMachine.Read(new[] { PhoneSymbol.Card, PhoneSymbol.Card, PhoneSymbol.Card }), Is.EqualTo(PhoneOutcome.Card));
            Assert.That(SlotMachine.Read(new[] { PhoneSymbol.Penus, PhoneSymbol.Penus, PhoneSymbol.Penus }), Is.EqualTo(PhoneOutcome.Jackpot));
            for (int i = 0; i < 20000; i++)
            {
                var reels = SlotMachine.Show(PhoneOutcome.Pair, random);
                bool looksRare = reels.Count(s => s == PhoneSymbol.Card) == 2 && reels.Contains(PhoneSymbol.Penus);
                Assert.That(looksRare, Is.False, string.Join(" ", reels));
                Assert.That(SlotMachine.Read(reels), Is.EqualTo(PhoneOutcome.Pair));
            }
        }

        [TestCase(1), TestCase(2), TestCase(3)]
        public void TheRareCardIsRankThreeOrMoreFromAllCardsTheUsualOneOnlyFromTheStagesPool(int stage)
        {
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>(CataloguePath);
            Assert.That(config, Is.Not.Null);
            // As PhoneTool.GrantCard: every card on sale of every stage, narrowed by the rules.
            var catalogue = StagedCards.OnSale(config, CardSpec.MaxStage).Select(c => c.ToSpec()).ToList();
            Assert.That(catalogue, Has.Count.EqualTo(39), "The whole catalogue.");
            var pool = new HashSet<string>(StagedCards.OnSale(config, stage).Select(c => c.id));
            var rareAllowed = new HashSet<string>(catalogue.Where(s => s.Rank >= PhoneCardRules.RareMinimumRank).Select(s => s.Id));
            Assert.That(rareAllowed, Is.Not.Empty);
            var random = new System.Random(stage);
            var owned = new HashSet<string> { "fire" };
            var rareSeen = new HashSet<string>();
            for (int i = 0; i < 4000; i++)
            {
                var rare = PhoneCardRules.Pick(PhoneOutcome.RareCard, catalogue, stage, owned, random);
                Assert.That(rare, Is.Not.Null);
                Assert.That(rare.Rank, Is.GreaterThanOrEqualTo(3), rare.Id);
                Assert.That(rare.Dummy, Is.False);
                rareSeen.Add(rare.Id);
                var usual = PhoneCardRules.Pick(PhoneOutcome.Card, catalogue, stage, owned, random);
                Assert.That(pool, Does.Contain(usual.Id), "The usual card stays inside the stage's pool.");
            }
            Assert.That(rareSeen, Is.SubsetOf(rareAllowed));
            Assert.That(rareSeen.Count, Is.GreaterThan(1), "Not one card over and over.");
            if (stage == 1)
                Assert.That(rareSeen.Any(id => !pool.Contains(id)), Is.True, "On the first stage the rare card comes from beyond its pool.");
            Assert.That(PhoneCardRules.Pick(PhoneOutcome.Nothing, catalogue, stage, owned, random), Is.Null);
        }
    }

    /// <summary>Balance stage 5, «Плеер»: after the god the player costs 300 (was 500); the other special items keep their prices.</summary>
    public sealed class SpecialItemPricesTests
    {
        [TestCase("Player", "player", 300), TestCase("Phone", "phone", 1000), TestCase("Account", "hapay_account", 1500),
         TestCase("Compass", "ant_compass", 800)]
        public void ASpecialItemCosts(string asset, string id, int price)
        {
            var item = AssetDatabase.LoadAssetAtPath<SpecialItemDefinition>("Assets/SpecialItems/Data/" + asset + ".asset");
            Assert.That(item, Is.Not.Null, asset);
            Assert.That((item.id, item.price), Is.EqualTo((id, price)));
        }
    }
}
