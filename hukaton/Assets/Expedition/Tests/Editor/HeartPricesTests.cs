using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Balance stage 5, «Первое сердце»: the PC's «Сердца» cost 250 / 700 / 1300 / 2200 (the first was 300). The game's
    /// settings asset must not override the default.
    /// </summary>
    public sealed class HeartPricesTests
    {
        [Test]
        public void TheFirstHeartCostsTwoHundredFifty()
        {
            var defaults = ScriptableObject.CreateInstance<ExpeditionSettings>();
            Assert.That(defaults.heartUpgradePrices, Is.EqualTo(new[] { 250, 700, 1300, 2200 }));
            Object.DestroyImmediate(defaults);
            var game = AssetDatabase.LoadAssetAtPath<ExpeditionSettings>("Assets/Game/Data/Expedition.asset");
            Assert.That(game, Is.Not.Null);
            Assert.That(game.heartUpgradePrices, Is.EqualTo(new[] { 250, 700, 1300, 2200 }), "Game/Data/Expedition.asset");
        }
    }
}
