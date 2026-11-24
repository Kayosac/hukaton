using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Balance stage 4, «Цены четырёх карт»: the first purchase must be possible on 90–107 coins, so four stage-1 cards
    /// got cheaper. The face-down price follows from the full one: half, rounded up.
    /// </summary>
    public sealed class StarterCardPriceTests
    {
        [TestCase("heavy_strike", 110, 55), TestCase("fan", 120, 60), TestCase("heal", 120, 60), TestCase("merchant_nose", 140, 70)]
        public void FourCardsCostLessAndTheBlindPriceIsHalfRoundedUp(string id, int price, int blind)
        {
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            var card = config.Find(id);
            Assert.That(card, Is.Not.Null, id);
            Assert.That(card.purchasePrice, Is.EqualTo(price), id + " full price");
            var spec = card.ToSpec();
            Assert.That((spec.Price, spec.BlindPrice), Is.EqualTo((price, blind)), id + " full and blind price");
            Assert.That(spec.BlindPrice, Is.EqualTo((spec.Price + 1) / 2));
        }
    }
}
