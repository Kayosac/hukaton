using System.Linq;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Decks.Tests
{
    /// <summary>Board, «мердж I / II / III»: two equal copies of one level become one of the next, for free, on the PC.</summary>
    public sealed class CardMergeTests
    {
        static readonly CardSpec[] Specs =
        {
            new CardSpec("rope", "Верёвка", 180, 3), new CardSpec("fire", "Огонь", 100),
            new CardSpec("teleport", "Телепорт", 450, 5, true, new[] { 1, 2, 3 }), new CardSpec("relic", "Реликвия", 50, 1, false)
        };

        static DeckWorkshop Workshop(int coins = 5000) => new DeckWorkshop(new DeckRules(startingCoins: coins), Specs, 0);

        static CardCopy Bought(DeckWorkshop w, string id, int price = -1)
        {
            Assert.That(w.Purchase(id, price < 0 ? w.Spec(id).Price : price, out var copy), Is.Null);
            return copy;
        }

        [Test]
        public void TwoEqualCopiesBecomeOneOfTheNextLevelForFree()
        {
            var w = Workshop();
            var a = Bought(w, "rope");
            var b = Bought(w, "rope");
            int coins = w.Coins;
            Assert.That(w.Merge(a.Id, b.Id, out var result), Is.Null);
            Assert.That(result, Is.SameAs(a), "The first copy keeps its number.");
            Assert.That(result.Level, Is.EqualTo(2));
            Assert.That(w.Owned, Has.Count.EqualTo(1));
            Assert.That(w.Find(b.Id), Is.Null);
            Assert.That(w.Coins, Is.EqualTo(coins), "Merging is free.");
        }

        [Test]
        public void LevelThreeNeedsFourBaseCopiesAndIsTheMaximum()
        {
            var w = Workshop();
            var c = Enumerable.Range(0, 5).Select(_ => Bought(w, "rope")).ToArray();
            w.Merge(c[0].Id, c[1].Id, out _);
            Assert.That(w.Merge(c[0].Id, c[2].Id, out _), Is.Not.Null, "II + I does not merge.");
            w.Merge(c[2].Id, c[3].Id, out _);
            Assert.That(w.Merge(c[0].Id, c[2].Id, out var third), Is.Null);
            Assert.That(third.Level, Is.EqualTo(3));
            Assert.That(w.MergePartners(third), Is.Empty);
            Assert.That(w.Owned.Select(o => o.Level), Is.EquivalentTo(new[] { 3, 1 }));
        }

        [Test]
        public void OnlyTheSameCardMergesAndNotWithItself()
        {
            var w = Workshop();
            var rope = Bought(w, "rope");
            var fire = Bought(w, "fire");
            Assert.That(w.Merge(rope.Id, fire.Id, out _), Is.Not.Null);
            Assert.That(w.Merge(rope.Id, rope.Id, out _), Is.Not.Null);
            var relics = new[] { Bought(w, "relic"), Bought(w, "relic") };
            Assert.That(w.Merge(relics[0].Id, relics[1].Id, out _), Is.Not.Null, "A card marked not mergeable stays at I.");
            Assert.That(w.Owned, Has.Count.EqualTo(4));
        }

        [Test]
        public void MergedCopyStaysInTheDeckAndFreesThePartnersPlace()
        {
            var w = Workshop();
            var a = Bought(w, "rope");
            var b = Bought(w, "rope");
            w.Toggle(a.Id);
            w.Toggle(b.Id);
            Assert.That(w.Selected(), Has.Count.EqualTo(2));
            w.Merge(b.Id, a.Id, out var result);
            Assert.That(w.Selected(), Is.EqualTo(new[] { result }));
        }

        [Test]
        public void MergedCopyKeepsWhatWasPaidAndTheProtection()
        {
            var w = new DeckWorkshop(new DeckRules(startingCoins: 1000), Specs, null,
                new[] { new StartingCopies("fire", 1, 1, true) });
            var start = w.Owned[0];
            var bought = Bought(w, "fire", 50);
            w.Merge(bought.Id, start.Id, out var result);
            Assert.That(result.PaidPrice, Is.EqualTo(150), "100 for the starting copy + 50 paid blind.");
            Assert.That(result.Protected, Is.True, "A starting copy never burns, merged or not.");
        }

        [Test]
        public void TeleportGetsMoreUsesPerLevel()
        {
            var w = Workshop();
            var t = Enumerable.Range(0, 2).Select(_ => Bought(w, "teleport")).ToArray();
            Assert.That(t[0].UsesLeft, Is.EqualTo(1));
            w.Merge(t[0].Id, t[1].Id, out var merged);
            Assert.That(merged.Level, Is.EqualTo(2));
            Assert.That(merged.UsesLeft, Is.EqualTo(2));
            Assert.That(w.Spec("teleport").UsesAt(3), Is.EqualTo(3));
        }

        [Test]
        public void MergedLevelsAndPricesSurviveASave()
        {
            var w = Workshop();
            var a = Bought(w, "rope", 90);
            w.Merge(a.Id, Bought(w, "rope").Id, out _);
            var restored = Workshop(0);
            Assert.That(restored.TryRestoreProgress(w.Save()), Is.True);
            var copy = restored.Owned.Single();
            Assert.That((copy.Level, copy.PaidPrice), Is.EqualTo((2, 270)));
        }

        [Test]
        public void DefinitionCarriesRankUsesAndMergeFlagToTheSpec()
        {
            var definition = ScriptableObject.CreateInstance<CardDefinition>();
            try
            {
                definition.id = "teleport";
                definition.rank = 5;
                definition.uses = new LevelInt(1, 2, 3);
                var spec = definition.ToSpec();
                Assert.That((spec.Rank, spec.Mergeable, spec.UsesAt(1), spec.UsesAt(3)), Is.EqualTo((5, true, 1, 3)));
                definition.upgradable = false;
                Assert.That(definition.ToSpec().Mergeable, Is.False);
            }
            finally { Object.DestroyImmediate(definition); }
        }

        [Test]
        public void OldSaveWithAnUpgradedCardOfNoLevelsLoadsAtLevelOne()
        {
            var w = Workshop();
            Bought(w, "relic");
            var save = w.Save();
            save.version = 2;
            save.copies[0].level = 2;
            var restored = Workshop(0);
            Assert.That(restored.TryRestoreProgress(save), Is.True);
            Assert.That(restored.Owned[0].Level, Is.EqualTo(1));
            Assert.That(restored.Owned[0].PaidPrice, Is.EqualTo(50), "Old saves knew no paid price: the full price counts.");
        }
    }
}
