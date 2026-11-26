using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Balance stage 9, «Карты»: every card costs what it is worth. The numbers of the cards the stage changed, read from
    /// the assets (an effect's fields by name, so this assembly needs no reference to the expedition's).
    /// </summary>
    public sealed class CardBalanceStage9Tests
    {
        static DeckPrototypeConfig Catalogue() => AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");

        static CardDefinition Card(string id)
        {
            var card = Catalogue().Find(id);
            Assert.That(card, Is.Not.Null, id);
            return card;
        }

        /// <summary>A LevelFloat / LevelInt field of the card's effect asset at I / II / III.</summary>
        static float[] EffectLevels(string id, string field)
        {
            var effect = Card(id).effectAsset;
            Assert.That(effect, Is.Not.Null, id + " effect");
            var property = new SerializedObject(effect).FindProperty(field);
            Assert.That(property, Is.Not.Null, id + "." + field);
            float Read(string level)
            {
                var p = property.FindPropertyRelative(level);
                return p.propertyType == SerializedPropertyType.Integer ? p.intValue : p.floatValue;
            }
            return new[] { Read("i"), Read("ii"), Read("iii") };
        }

        [Test]
        public void MarauderCostsMoreAndGildingPaysLess()
        {
            Assert.That((Card("marauder").purchasePrice, Card("marauder").rank), Is.EqualTo((220, 3)), "Marauder 160 → 220.");
            Assert.That(EffectLevels("gilding", "coinMultiplier"), Is.EqualTo(new[] { 1f, 1.25f, 1.5f }), "Gilding ×1 / 1,5 / 2 → ×1 / 1,25 / 1,5.");
            Assert.That(Card("gilding").purchasePrice, Is.EqualTo(200), "Gilding's price stays.");
        }

        [Test]
        public void TheBubbleHoldsLongerAtTheFirstLevels()
        {
            Assert.That(EffectLevels("bubble", "duration"), Is.EqualTo(new[] { 6f, 10f, 16f }), "Bubble 4 / 8 / 16 → 6 / 10 / 16 s.");
            Assert.That(Card("bubble").purchasePrice, Is.EqualTo(140), "Its price stays.");
        }

        /// <summary>
        /// Crooked mirror rank 3 → 4 at 280 (was 400), Sticky hour rank 4 → 3 at 200; their stages stay (3 and 2), the
        /// number of cards per rank too (9 / 10 / 12 / 5 / 3). What hangs on the rank: the goblin's table (stage 2 now has
        /// no rank IV card: its share goes to the ranks present), a rich chest's card and the phone's rare card (rank III and
        /// up from all 39 — both cards stay there).
        /// </summary>
        [Test]
        public void CrookedMirrorAndStickyHourSwapRanks()
        {
            var mirror = Card("crooked_mirror");
            var hour = Card("sticky_hour");
            Assert.That((mirror.purchasePrice, mirror.rank, mirror.stage), Is.EqualTo((280, 4, 3)), "Crooked mirror.");
            Assert.That((hour.purchasePrice, hour.rank, hour.stage), Is.EqualTo((200, 3, 2)), "Sticky hour.");
            Assert.That(mirror.ToSpec().BlindPrice, Is.EqualTo(140), "No blind price of 200 in rank III any more.");

            var config = Catalogue();
            var perRank = Enumerable.Range(1, 5).Select(r => config.cards.Count(c => c.rank == r)).ToArray();
            Assert.That(perRank, Is.EqualTo(new[] { 9, 10, 12, 5, 3 }), "Cards per rank.");

            // A rich chest's card and the phone's rare card: rank III and up out of every stage (TrophyCardRules.CatalogueStage).
            var rare = StagedCards.OnSale(config, TrophyCardRules.CatalogueStage(1, 3)).Select(c => c.ToSpec()).Where(s => s.Rank >= 3).ToList();
            Assert.That(rare.Select(s => s.Id), Does.Contain("crooked_mirror").And.Contain("sticky_hour"));
            var random = new Random(9);
            var picked = new HashSet<string>();
            for (int i = 0; i < 4000; i++) picked.Add(TrophyCardRules.Pick(rare, new HashSet<string>(), random, 2, 3).Id);
            Assert.That(picked, Does.Contain("crooked_mirror").And.Contain("sticky_hour"), "Both still come from a rich chest and the phone.");

            // The goblin's table by pool stage: stage 2 — ranks I–III only, Sticky hour among rank III; stage 3 — the mirror at IV.
            var workshop = config.CreateWorkshop(new Hakaton.Economy.CoinWallet(0));
            foreach (int stage in new[] { 2, 3 })
            {
                var pool = CardPool.ForStage(workshop.Catalog, stage).Where(s => !s.Dummy).ToList();
                var specs = pool.ToDictionary(s => s.Id);
                var seen = new HashSet<string>();
                var ranks = new HashSet<int>();
                for (int table = 0; table < 2000; table++)
                {
                    var offer = GoblinOffer.Roll(pool, random);
                    for (int slot = 0; slot < offer.Count; slot++)
                    {
                        seen.Add(offer.CardAt(slot));
                        ranks.Add(specs[offer.CardAt(slot)].Rank);
                    }
                }
                Assert.That(seen, Does.Contain("sticky_hour"), "stage " + stage);
                if (stage == 2)
                {
                    Assert.That(ranks, Is.EquivalentTo(new[] { 1, 2, 3 }), "Stage 2 offers ranks I–III.");
                    Assert.That(seen, Does.Not.Contain("crooked_mirror"), "The mirror waits for stage 3.");
                }
                else
                {
                    Assert.That(seen, Does.Contain("crooked_mirror"));
                    Assert.That(specs["crooked_mirror"].Rank, Is.EqualTo(4));
                }
            }
        }
    }
}
