using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Balance stage 4, «Пул карт по этапам»: 12 cards until the god is met, 26 after him, all 39 after the first return
    /// from the endless world. The goblin, trophy cards and the phone offer only the current pool; owned copies stay.
    /// A rich chest's card of a guaranteed rank (III and up) comes from all 39, whatever the stage.
    /// </summary>
    public sealed class CardPoolTests
    {
        const string CataloguePath = "Assets/Game/Data/Cards.asset";

        static readonly string[] StageOne =
        {
            "strike", "fire", "ice", "heal", "lure", "fan", "heavy_strike", "impulse", "boomerang", "water_jet", "scarecrow",
            "merchant_nose",
        };

        static readonly string[] StageTwo =
        {
            "tar_puddle", "lightning", "rain_cloud", "rockfall", "bubble", "fog", "attraction", "marauder", "gilding", "crows",
            "powder_barrel", "sticky_hour", "stone_cube", "mushroom_trampoline",
        };

        static readonly string[] StageThree =
        {
            "branch_bridge", "lightness", "moon_gravity", "rope", "crooked_mirror", "draft", "updraft", "tree_awakening",
            "discord", "evil_eye", "swap", "teleport", "echo",
        };

        static readonly CardSpec[] Specs =
        {
            new CardSpec("a", "A", 100, 1), new CardSpec("b", "B", 100, 2, stage: 2), new CardSpec("c", "C", 100, 3, stage: 3),
            new CardSpec("dummy", "Пустышка", 50, 1, false, null, true, 3),
        };

        static DeckPrototypeConfig Catalogue() => AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>(CataloguePath);

        static string[] Upto(int stage) => stage == 1 ? StageOne
            : stage == 2 ? StageOne.Concat(StageTwo).ToArray() : StageOne.Concat(StageTwo).Concat(StageThree).ToArray();

        [TestCase(false, false, 1), TestCase(true, false, 2), TestCase(true, true, 3), TestCase(false, true, 3)]
        public void StageFollowsTheGodAndTheEndlessWorld(bool godMet, bool endlessVisited, int stage) =>
            Assert.That(CardPool.StageOf(godMet, endlessVisited), Is.EqualTo(stage));

        [Test]
        public void ACardIsOnSaleFromItsStageAndTheDummyAlways()
        {
            Assert.That(CardPool.ForStage(Specs, 1).Select(s => s.Id), Is.EqualTo(new[] { "a", "dummy" }));
            Assert.That(CardPool.ForStage(Specs, 2).Select(s => s.Id), Is.EqualTo(new[] { "a", "b", "dummy" }));
            Assert.That(CardPool.ForStage(Specs, 3).Select(s => s.Id), Is.EqualTo(new[] { "a", "b", "c", "dummy" }));
            Assert.That(CardPool.ForStage(Specs.Append(null), 3).Count(), Is.EqualTo(4), "Nulls are skipped.");
        }

        [Test]
        public void StageDefaultsToOneAndStaysInRange()
        {
            Assert.That(new CardSpec("x", "X", 10).Stage, Is.EqualTo(1));
            Assert.That(new CardSpec("x", "X", 10, stage: CardSpec.MaxStage).Stage, Is.EqualTo(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardSpec("x", "X", 10, stage: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CardSpec("x", "X", 10, stage: 4));
        }

        [Test]
        public void WithoutAGameTheWholePoolIsOnSale()
        {
            var workshop = new DeckWorkshop(new DeckRules(), Specs, 0);
            Assert.That(workshop.PoolStage, Is.EqualTo(CardSpec.MaxStage));
            Assert.That(CardPool.ForStage(workshop.Catalog, workshop.PoolStage).Count(), Is.EqualTo(Specs.Length));
        }

        [Test]
        public void TheListsOfTheStagesCoverTheCatalogueOnce()
        {
            var all = StageOne.Concat(StageTwo).Concat(StageThree).ToList();
            Assert.That((StageOne.Length, StageTwo.Length, StageThree.Length), Is.EqualTo((12, 14, 13)));
            Assert.That(all.Distinct().Count(), Is.EqualTo(39), "No card is in two stages.");
            var config = Catalogue();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.cards.Select(c => c.id), Is.EquivalentTo(all), "No card of Cards.asset is missing from a stage.");
            foreach (var card in config.cards)
            {
                int expected = StageOne.Contains(card.id) ? 1 : StageTwo.Contains(card.id) ? 2 : 3;
                Assert.That(card.stage, Is.EqualTo(expected), card.id + " stage");
                Assert.That(card.ToSpec().Stage, Is.EqualTo(expected), card.id + " spec stage");
            }
        }

        [TestCase(1, 12), TestCase(2, 26), TestCase(3, 39)]
        public void TheRealPoolGrowsTwelveTwentySixThirtyNine(int stage, int count)
        {
            var config = Catalogue();
            var workshop = config.CreateWorkshop(new Hakaton.Economy.CoinWallet(0));
            var pool = CardPool.ForStage(workshop.Catalog, stage).ToList();
            Assert.That(pool.Where(s => s.Dummy).Select(s => s.Id), Is.EqualTo(new[] { config.dummyCard.id }), "The dummy passes.");
            var cards = pool.Where(s => !s.Dummy).Select(s => s.Id).ToList();
            Assert.That(cards, Has.Count.EqualTo(count));
            Assert.That(cards, Is.EquivalentTo(Upto(stage)));
        }

        [TestCase(1), TestCase(2), TestCase(3)]
        public void TrophiesAndThePhoneOfferOnlyTheCurrentPool(int stage)
        {
            var config = Catalogue();
            var onSale = StagedCards.OnSale(config, stage);
            Assert.That(onSale.Select(c => c.id), Is.EquivalentTo(Upto(stage)));
            Assert.That(onSale, Has.No.Member(config.dummyCard));
            foreach (var card in config.storyCards.Concat(config.trophyCards)) Assert.That(onSale, Has.No.Member(card));
            // TrophyCards.Give picks among these for any trophy but a rich chest's (see the next test); Pick itself, asked for
            // rank 3+ in a pool without one, falls back to the pool's own ranks.
            var specs = onSale.Select(c => c.ToSpec()).ToList();
            var random = new Random(stage);
            var allowed = new HashSet<string>(Upto(stage));
            for (int i = 0; i < 2000; i++)
            {
                var pick = TrophyCardRules.Pick(specs, new HashSet<string> { "fire" }, random, 2, i % 2 == 0 ? 1 : 3);
                Assert.That(allowed, Does.Contain(pick.Id));
            }
        }

        [Test]
        public void ARichChestsCardComesFromAllCardsOtherTrophiesOnlyFromTheStagesPool()
        {
            const int richCardRank = 3; // TrophyCards.richChestCardRank
            Assert.That(TrophyCardRules.CatalogueStage(1, richCardRank), Is.EqualTo(CardSpec.MaxStage));
            Assert.That(TrophyCardRules.CatalogueStage(2, 1), Is.EqualTo(2));
            var config = Catalogue();
            // As TrophyCards.Give: the cards on sale at TrophyCardRules.CatalogueStage, then Pick with the same minimum rank.
            var rich = StagedCards.OnSale(config, TrophyCardRules.CatalogueStage(1, richCardRank)).Select(c => c.ToSpec()).ToList();
            var usual = StagedCards.OnSale(config, TrophyCardRules.CatalogueStage(1, 1)).Select(c => c.ToSpec()).ToList();
            Assert.That(rich.Select(s => s.Id), Is.EquivalentTo(Upto(3)), "A rich chest: all 39 cards.");
            Assert.That(usual.Select(s => s.Id), Is.EquivalentTo(StageOne), "Any other trophy: the stage's pool.");
            Assert.That(usual.Max(s => s.Rank), Is.LessThan(richCardRank), "Stage 1 holds no rank III.");
            var random = new Random(5);
            var owned = new HashSet<string> { "fire" };
            var richPicks = new HashSet<string>();
            for (int i = 0; i < 2000; i++)
            {
                var pick = TrophyCardRules.Pick(rich, owned, random, 2, richCardRank);
                Assert.That(pick.Rank, Is.GreaterThanOrEqualTo(richCardRank), pick.Id);
                Assert.That(StageOne, Has.No.Member(pick.Id), "Rank III+ lies beyond the stage 1 pool.");
                richPicks.Add(pick.Id);
                Assert.That(StageOne, Has.Member(TrophyCardRules.Pick(usual, owned, random, 2, 1).Id));
            }
            Assert.That(richPicks.Any(id => StageTwo.Contains(id)) && richPicks.Any(id => StageThree.Contains(id)), Is.True,
                "Rich chests at stage 1 give cards of stages 2 and 3 alike.");
        }

        [Test]
        public void OwnedCopiesStayAndTheCheatStillGivesEveryCard()
        {
            var config = Catalogue();
            var workshop = config.CreateWorkshop(new Hakaton.Economy.CoinWallet(0));
            workshop.PoolStage = 1;
            Assert.That(workshop.UnlockAllCards(out int added), Is.Null);
            Assert.That(added, Is.EqualTo(config.cards.Length - workshop.Owned.Count(c => c.Protected)));
            Assert.That(workshop.Owned.Select(c => c.Spec.Id).Distinct(), Is.EquivalentTo(config.cards.Select(c => c.id)),
                "«Все карты» ignores the stage.");
            Assert.That(workshop.Owned.Any(c => c.Spec.Stage == 3), Is.True, "Owned stage-3 copies stay at stage 1.");
        }
    }
}
