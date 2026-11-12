using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Мародёр»: the mark's time, the loot rolls, and the card's numbers.</summary>
    public sealed class MarauderTests
    {
        [Test]
        public void ARenewedMarkLastsFromTheLaterEnd()
        {
            Assert.That(MarauderRules.Expiry(0, 100, 10), Is.EqualTo(110), "A fresh mark: now + its duration.");
            Assert.That(MarauderRules.Expiry(110, 105, 10), Is.EqualTo(115), "Renewed: from the new hit.");
            Assert.That(MarauderRules.Expiry(130, 105, 10), Is.EqualTo(130), "A shorter copy never cuts a longer mark short.");
        }

        [Test]
        public void RollsNeverGoDown()
        {
            Assert.That(MarauderRules.Rolls(1, 2), Is.EqualTo(2));
            Assert.That(MarauderRules.Rolls(3, 2), Is.EqualTo(3), "A level I copy on a level III mark keeps three rolls.");
            Assert.That(MarauderRules.Rolls(0, 0), Is.EqualTo(1));
        }

        [Test]
        public void OnlyAMarkedDeathRollsMore()
        {
            Assert.That(MarauderRules.DiedMarked(109.9f, 110), Is.True);
            Assert.That(MarauderRules.DiedMarked(110, 110), Is.False, "The mark ran out first.");
            Assert.That(MarauderRules.LootRolls(true, 3), Is.EqualTo(3));
            Assert.That(MarauderRules.LootRolls(false, 3), Is.EqualTo(1), "Unmarked: the usual single roll.");
        }

        [Test]
        public void TheCardIsWiredWithTheBoardNumbers()
        {
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/marauder.asset");
            var effect = card.effectAsset as MarauderEffect;
            Assert.That(effect, Is.Not.Null, "marauder.asset → Effects/Marauder.asset");
            Assert.That((effect.duration.i, effect.duration.ii, effect.duration.iii), Is.EqualTo((10f, 15f, 20f)));
            Assert.That((effect.lootRolls.i, effect.lootRolls.ii, effect.lootRolls.iii), Is.EqualTo((2, 2, 3)));
            Assert.That(effect.markPrefab, Is.Not.Null);
            Assert.That(effect.markPrefab.sack, Is.Not.Null, "The mark has its drawing.");
            Assert.That(effect.markPrefab.sack.sprite, Is.Not.Null);
            Assert.That(effect.markPrefab.payoutBurst, Is.Not.Null);
            Assert.That(effect.impactVfx, Is.Not.Null);
            Assert.That(effect.impactSound, Is.Not.Null);
            Assert.That(effect.missSound, Is.Not.Null);
            Assert.That(effect.TryGetStat(out var label, out var values, out var unit), Is.True);
            Assert.That((label, values.i, values.iii, unit), Is.EqualTo(("Метка", 10f, 20f, " с")));
            Assert.That((card.purchasePrice, card.rank), Is.EqualTo((160, 3)), "Price and rank are the coordinator's.");
        }
    }
}
