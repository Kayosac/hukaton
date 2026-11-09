using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Decks.Tests
{
    /// <summary>Cards without an upgrade (Teleport) and the board's single upgrade step «база → ↑».</summary>
    public sealed class UpgradableCardTests
    {
        static readonly CardSpec[] Specs = { new CardSpec("rope", "Верёвка", 180, 140, 1, 0),
            new CardSpec("teleport", "Телепорт", 450, 0, 1, 0, upgradable: false) };

        static DeckWorkshop Workshop() => new DeckWorkshop(new DeckRules(maxCardLevel: 2, startingCoins: 2000), Specs, 0);

        [Test]
        public void CardWithoutUpgradeKeepsItsCopiesAtLevelOneAndTheCoins()
        {
            var w = Workshop();
            Assert.That(w.Buy("teleport", out var copy), Is.Null);
            int coins = w.Coins;
            Assert.That(w.CanUpgrade(copy), Is.False);
            Assert.That(w.Upgrade(copy.Id), Is.EqualTo("Эту карту нельзя улучшить."));
            Assert.That(copy.Level, Is.EqualTo(1));
            Assert.That(copy.Upgraded, Is.False);
            Assert.That(w.Coins, Is.EqualTo(coins), "A refused upgrade costs nothing.");
        }

        [Test]
        public void OrdinaryCardHasOneUpgradeStep()
        {
            var w = Workshop();
            w.Buy("rope", out var copy);
            Assert.That(copy.Upgraded, Is.False);
            Assert.That(w.CanUpgrade(copy), Is.True);
            Assert.That(w.Upgrade(copy.Id), Is.Null);
            Assert.That(copy.Upgraded, Is.True, "Level 2 uses the upgraded numbers.");
            Assert.That(w.CanUpgrade(copy), Is.False);
            Assert.That(w.Upgrade(copy.Id), Is.Not.Null, "No step beyond the board's one upgrade.");
        }

        [Test]
        public void SpecsAreUpgradableUnlessSaidOtherwise()
        {
            Assert.That(new CardSpec("strike", "Удар", 120, 100, 3, 1).Upgradable, Is.True);
            var definition = ScriptableObject.CreateInstance<CardDefinition>();
            try
            {
                definition.id = "teleport";
                Assert.That(definition.ToSpec().Upgradable, Is.True, "Upgradable by default.");
                definition.upgradable = false;
                Assert.That(definition.ToSpec().Upgradable, Is.False, "The flag reaches the workshop model.");
            }
            finally { Object.DestroyImmediate(definition); }
        }

        [Test]
        public void UpgradedTeleportCopyFromAnOldSaveCanStillBeLoaded()
        {
            // Saves are validated against the level limit only: a card that loses its upgrade never breaks a save.
            var w = Workshop();
            w.Buy("teleport", out _);
            var save = w.Save();
            save.copies[0].level = 2;
            Assert.That(w.CanRestoreProgress(save), Is.True);
        }
    }
}
