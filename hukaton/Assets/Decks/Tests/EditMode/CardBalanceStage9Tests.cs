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
    }
}
