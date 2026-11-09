using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// The board's extended catalogue (09.11.2026): 22 ordinary card types, the 16 new ones bought in the market,
    /// Teleport without an upgrade.
    /// </summary>
    public sealed class CardCatalogueTests
    {
        const string CataloguePath = "Assets/Game/Data/Cards.asset";

        /// <summary>Board table «расширенный каталог»: purchase / upgrade price.</summary>
        static readonly Dictionary<string, (string name, int price, int upgrade)> NewCards = new Dictionary<string, (string, int, int)>
        {
            { "lure", ("Манок", 80, 60) }, { "tar_puddle", ("Лужа смолы", 100, 80) }, { "attraction", ("Притяжение", 120, 100) },
            { "fog", ("Туман", 120, 100) }, { "rain_cloud", ("Облако дождя", 140, 100) }, { "lightning", ("Разряд молнии", 150, 180) },
            { "stone_cube", ("Каменный куб", 180, 140) }, { "branch_bridge", ("Ветвь-мост", 180, 140) },
            { "lightness", ("Лёгкость", 180, 140) }, { "mushroom_trampoline", ("Гриб-батут", 160, 120) },
            { "moon_gravity", ("Лунная гравитация", 180, 140) }, { "rope", ("Верёвка", 180, 140) },
            { "updraft", ("Восходящий поток", 260, 180) }, { "tree_awakening", ("Оживление дерева", 300, 180) },
            { "swap", ("Подмена", 360, 240) }, { "teleport", ("Телепорт", 450, 0) },
        };

        static DeckPrototypeConfig Catalogue() => AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>(CataloguePath);

        [Test]
        public void CatalogueHasTwentyTwoCompleteCardTypes()
        {
            var config = Catalogue();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.cards.Length, Is.EqualTo(22), "3 starting + 3 earlier + 16 new card types.");
            Assert.That(config.cards.Select(c => c.id).Distinct().Count(), Is.EqualTo(22), "Card ids are unique.");
            Assert.That(config.cards.Select(c => (int)c.action).Distinct().Count(), Is.EqualTo(22), "Each card has its own action.");
            foreach (var card in config.cards)
            {
                Assert.That(card.artwork, Is.Not.Null, card.id + " has artwork");
                Assert.That(card.effectAsset, Is.Not.Null, card.id + " has an effect asset");
                Assert.That(string.IsNullOrWhiteSpace(card.displayName), Is.False, card.id + " has a name");
                Assert.That(string.IsNullOrWhiteSpace(card.description), Is.False, card.id + " has a description");
            }
        }

        [Test]
        public void NewCardsUseTheBoardsNamesAndPricesAndStartWithNoCopies()
        {
            var config = Catalogue();
            foreach (var entry in NewCards)
            {
                var card = config.Find(entry.Key);
                Assert.That(card, Is.Not.Null, entry.Key);
                Assert.That(card.displayName, Is.EqualTo(entry.Value.name));
                Assert.That(card.purchasePrice, Is.EqualTo(entry.Value.price), entry.Key + " purchase");
                Assert.That(card.upgradePrice, Is.EqualTo(entry.Value.upgrade), entry.Key + " upgrade");
                Assert.That(card.upgradable, Is.EqualTo(entry.Key != "teleport"), entry.Key + " upgradable");
                var start = config.initialCopies.SingleOrDefault(c => c.definitionId == entry.Key);
                Assert.That(start, Is.Not.Null, entry.Key + " is listed in the starting collection");
                Assert.That(start.count, Is.Zero, entry.Key + " is bought in the market");
                Assert.That(start.inDeck, Is.Zero);
            }
            Assert.That(config.cards.Where(c => !NewCards.ContainsKey(c.id)).All(c => c.upgradable), Is.True,
                "The earlier cards keep their upgrade.");
        }

        [Test]
        public void WorkshopFromTheCatalogueSellsEveryTypeAndKeepsTheStartingCollection()
        {
            var config = Catalogue();
            var workshop = config.CreateWorkshop(new Hakaton.Economy.CoinWallet(10000));
            Assert.That(workshop.Owned.Count, Is.EqualTo(15), "Board: the starting collection is still 15 copies.");
            Assert.That(workshop.Owned.Any(c => NewCards.ContainsKey(c.Spec.Id)), Is.False);
            foreach (var id in NewCards.Keys)
            {
                Assert.That(workshop.Buy(id, out var copy), Is.Null, id + " can be bought");
                Assert.That(copy.Level, Is.EqualTo(1));
            }
            Assert.That(workshop.Coins, Is.EqualTo(10000 - NewCards.Values.Sum(v => v.price)));
        }
    }
}
