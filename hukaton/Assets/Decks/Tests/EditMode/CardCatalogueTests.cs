using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Board, «Карты — мердж I / II / III · 22 + Пора домой» (09.11.2026): 22 ordinary cards with a copy price, a rarity
    /// rank and three levels; a new game starts with one Fire, one Ice and one Strike from the cave.
    /// </summary>
    public sealed class CardCatalogueTests
    {
        const string CataloguePath = "Assets/Game/Data/Cards.asset";

        /// <summary>Copy price and rank. Strike, Heavy strike and Heal are «обычные» without a rank on the board: rank 1 here.</summary>
        static readonly Dictionary<string, (int price, int rank)> Board = new Dictionary<string, (int, int)>
        {
            { "impulse", (120, 1) }, { "ice", (120, 1) }, { "fire", (100, 1) }, { "lure", (80, 1) }, { "tar_puddle", (100, 1) },
            { "attraction", (120, 2) }, { "fog", (120, 2) }, { "rain_cloud", (140, 2) }, { "lightning", (150, 2) },
            { "stone_cube", (180, 3) }, { "branch_bridge", (180, 3) }, { "lightness", (180, 3) },
            { "mushroom_trampoline", (160, 3) }, { "moon_gravity", (180, 3) }, { "rope", (180, 3) },
            { "updraft", (260, 4) }, { "tree_awakening", (300, 4) }, { "swap", (360, 5) }, { "teleport", (450, 5) },
            { "strike", (120, 1) }, { "heavy_strike", (140, 1) }, { "heal", (140, 1) },
        };

        /// <summary>The main I / II / III number of each card as the PC shows it.</summary>
        static readonly Dictionary<string, (float, float, float)> Levels = new Dictionary<string, (float, float, float)>
        {
            { "impulse", (8, 11, 14) }, { "ice", (2, 4, 7) }, { "fire", (1, 2, 4) }, { "lure", (20, 40, 70) },
            { "tar_puddle", (2, 3, 4) }, { "attraction", (6, 9, 12) }, { "fog", (8, 14, 24) }, { "rain_cloud", (3, 4.5f, 6) },
            { "lightning", (3, 5, 8) }, { "stone_cube", (1.1f, 1.5f, 2) }, { "branch_bridge", (5, 8, 12) },
            { "lightness", (6, 12, 20) }, { "mushroom_trampoline", (80, 90, 100) }, { "moon_gravity", (12, 25, 45) },
            { "rope", (5, 7.5f, 10) }, { "updraft", (6, 10, 15) }, { "tree_awakening", (12, 20, 32) }, { "swap", (12, 20, 30) },
            { "teleport", (1, 2, 3) }, { "strike", (3, 5, 8) }, { "heavy_strike", (10, 14, 18) }, { "heal", (1, 2, 3) },
        };

        static DeckPrototypeConfig Catalogue() => AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>(CataloguePath);

        static LevelFloat MainNumber(CardDefinition card)
        {
            if (card.effectAsset is ICardStatLine line && line.TryGetStat(out _, out var values, out _)) return values;
            if (card.uses.iii > 1) return card.uses;
            switch (card.action)
            {
                case CardAction.Impulse:
                case CardAction.HeavyStrike: return card.impulse;
                case CardAction.Heal: return new LevelFloat(card.power.i / 2f, card.power.ii / 2f, card.power.iii / 2f);
                default: return card.power;
            }
        }

        [Test]
        public void EveryCatalogueCardIsComplete()
        {
            var config = Catalogue();
            Assert.That(config, Is.Not.Null);
            // The board's 22 and every card added since (the catalogue keeps growing).
            Assert.That(config.cards.Length, Is.GreaterThanOrEqualTo(22));
            Assert.That(config.cards.Select(c => c.id).Distinct().Count(), Is.EqualTo(config.cards.Length), "Card ids are unique.");
            Assert.That(config.cards.Select(c => (int)c.action).Distinct().Count(), Is.EqualTo(config.cards.Length), "Each card has its own action.");
            foreach (var card in config.cards)
            {
                Assert.That(card.artwork, Is.Not.Null, card.id + " has artwork");
                Assert.That(card.effectAsset, Is.Not.Null, card.id + " has an effect asset");
                Assert.That(string.IsNullOrWhiteSpace(card.displayName), Is.False, card.id + " has a name");
                Assert.That(string.IsNullOrWhiteSpace(card.description), Is.False, card.id + " has a description");
                Assert.That(card.upgradable, Is.True, card.id + " merges I → II → III");
            }
        }

        [Test]
        public void EveryCardHasTheBoardsPriceRankAndLevels()
        {
            var config = Catalogue();
            foreach (var entry in Board)
            {
                var card = config.Find(entry.Key);
                Assert.That(card, Is.Not.Null, entry.Key);
                Assert.That((card.purchasePrice, card.rank), Is.EqualTo(entry.Value), entry.Key + " price and rank");
                var n = MainNumber(card);
                var expected = Levels[entry.Key];
                Assert.That(new[] { n.i, n.ii, n.iii }, Is.EqualTo(new[] { expected.Item1, expected.Item2, expected.Item3 }).Within(.001f),
                    entry.Key + " I / II / III");
                Assert.That(card.uses.ToArray(), Is.EqualTo(entry.Key == "teleport" ? new[] { 1, 2, 3 } : new[] { 1, 1, 1 }), entry.Key + " uses");
            }
            Assert.That(config.Find("heavy_strike").power.ToArray(), Is.EqualTo(new[] { 2, 3, 4 }), "Heavy strike damage.");
        }

        /// <summary>Playtest 12.11.2026: the goblin's dummy («Пустышка») lives apart from the ordinary cards, costs 25 and does nothing.</summary>
        [Test]
        public void TheDummyStandsApartFromTheOrdinaryCards()
        {
            var config = Catalogue();
            var dummy = config.dummyCard;
            Assert.That(dummy, Is.Not.Null, "Cards.asset assigns the dummy card.");
            Assert.That(config.cards, Has.No.Member(dummy));
            Assert.That(config.cards.Select(c => c.id), Has.No.Member(dummy.id));
            Assert.That(config.Find(dummy.id), Is.SameAs(dummy), "Saves and views resolve it.");
            Assert.That((dummy.dummy, dummy.upgradable, dummy.purchasePrice, dummy.rank), Is.EqualTo((true, false, 50, 1)));
            Assert.That(dummy.artwork, Is.Not.Null, "Its own face (Decks/Art/blank.png).");
            Assert.That(dummy.effectAsset, Is.Not.Null, "A no-op effect, should it ever be thrown.");
            Assert.That(string.IsNullOrWhiteSpace(dummy.displayName) || string.IsNullOrWhiteSpace(dummy.description), Is.False);
            Assert.That(config.dummyChance, Is.EqualTo(.1f).Within(.0001f));
            var workshop = config.CreateWorkshop(new Hakaton.Economy.CoinWallet(0));
            var spec = workshop.Spec(dummy.id);
            Assert.That(spec, Is.Not.Null, "In the workshop's catalogue, so a save holding it loads.");
            Assert.That((spec.Dummy, spec.Mergeable, spec.BlindPrice), Is.EqualTo((true, false, 25)));
            Assert.That(workshop.Catalog.Count(s => !s.Dummy), Is.EqualTo(config.cards.Length), "The dummy is the only extra in the catalogue.");
            Assert.That(workshop.Owned.Any(c => c.Spec.Dummy), Is.False);
        }

        [Test]
        public void NewGameStartsWithTheCavesThreeProtectedCards()
        {
            var config = Catalogue();
            var workshop = config.CreateWorkshop(new Hakaton.Economy.CoinWallet(0));
            Assert.That(workshop.Owned.Select(c => c.Spec.Id), Is.EquivalentTo(new[] { "fire", "ice", "strike" }));
            Assert.That(workshop.Owned.All(c => c.Protected), Is.True);
            Assert.That(workshop.Selected(), Has.Count.EqualTo(3));
            Assert.That((workshop.DeckLimit, workshop.CollectionLimit, workshop.HandSize, workshop.HandPrice), Is.EqualTo((12, 24, 3, 200)));
            Assert.That(config.startingCoins, Is.Zero);
        }

        [Test]
        public void DeckLimitGrowsInStepsToFifty()
        {
            var config = Catalogue();
            var limits = config.deckLimitSteps.Select(s => s.limit).ToArray();
            Assert.That(limits.Last(), Is.EqualTo(50));
            Assert.That(limits.Zip(limits.Skip(1), (a, b) => b > a).All(up => up), Is.True);
            Assert.That(config.deckLimitSteps.All(s => s.price > 0), Is.True);
        }

        /// <summary>Playtest: the hand grows from 3 to 10 cards, every step 200 dearer (200, 400 … 1400).</summary>
        [Test]
        public void HandGrowsToTenCardsAtRisingPrices()
        {
            var config = Catalogue();
            Assert.That((config.initialHand, config.maximumHand, config.handUpgradePrice), Is.EqualTo((3, 10, 200)));
            var workshop = config.CreateWorkshop(new Hakaton.Economy.CoinWallet(10000));
            var prices = new List<int>();
            while (workshop.HandSize < workshop.Rules.MaxHand)
            {
                prices.Add(workshop.HandPrice);
                Assert.That(workshop.ImproveHand(), Is.Null);
            }
            Assert.That(prices, Is.EqualTo(new[] { 200, 400, 600, 800, 1000, 1200, 1400 }));
            Assert.That((workshop.HandSize, workshop.Coins), Is.EqualTo((10, 10000 - 5600)));
            Assert.That(workshop.ImproveHand(), Is.Not.Null, "Ten is the maximum.");
        }
    }
}
