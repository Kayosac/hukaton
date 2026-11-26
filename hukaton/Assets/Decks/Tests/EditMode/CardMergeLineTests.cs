using System.Linq;
using Hakaton.Decks.Unity;
using NUnit.Framework;
using UnityEditor;

namespace Hakaton.Decks.Tests
{
    /// <summary>
    /// Balance stage 9, «Окно слияния показывает цену»: before a merge the PC shows what it gives and takes, with the
    /// card's real numbers: the throws of the two copies and their main number → the throws and the number of the next
    /// level, in the card's own unit (damage, seconds, metres, a multiplier…).
    /// </summary>
    public sealed class CardMergeLineTests
    {
        static DeckPrototypeConfig Catalogue() => AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");

        static string Line(string id, int level) => DeckPrototype.MergeLine(Catalogue().Find(id), level, Catalogue().maximumCardLevel);

        [TestCase("strike", 1, "2 броска × 3 урона → 1 бросок × 5 урона")]
        [TestCase("strike", 2, "2 броска × 5 урона → 1 бросок × 8 урона")]
        [TestCase("ice", 1, "2 броска × 3 с → 1 бросок × 5 с")]
        [TestCase("ice", 2, "2 броска × 5 с → 1 бросок × 8 с")]
        [TestCase("branch_bridge", 1, "2 броска × 5 м → 1 бросок × 8 м")]
        [TestCase("sticky_hour", 1, "2 броска × медленнее в 2 раза → 1 бросок × медленнее в 3 раза")]
        [TestCase("boomerang", 1, "4 броска × 2 урона → 3 броска × 3 урона")]
        [TestCase("fan", 1, "2 броска × 3 карты → 1 бросок × 5 карт")]
        [TestCase("heal", 1, "2 броска × 1 серд. → 1 бросок × 2 серд.")]
        [TestCase("crows", 1, "2 броска × 1 урон/с → 1 бросок × 2 урона/с")]
        [TestCase("teleport", 1, "2 броска → 2 броска")]
        [TestCase("teleport", 2, "4 броска → 3 броска")]
        public void TheLineShowsTheThrowsAndTheNumberBeforeAndAfter(string id, int level, string expected)
        {
            Assert.That(Line(id, level), Is.EqualTo(expected));
        }

        [Test]
        public void EveryMergeableCardHasALineUntilTheLastLevel()
        {
            var config = Catalogue();
            foreach (var card in config.cards)
            {
                for (int level = 1; level < config.maximumCardLevel; level++)
                {
                    string line = DeckPrototype.MergeLine(card, level, config.maximumCardLevel);
                    Assert.That(line, Does.StartWith(DeckPrototype.Throws(card.uses.At(level) * 2)), card.id + " " + level);
                    Assert.That(line, Does.Contain(" → " + DeckPrototype.Throws(card.uses.At(level + 1))), card.id + " " + level);
                    UnityEngine.Debug.Log($"[Merge] {card.id} {level}: {line}");
                }
                Assert.That(DeckPrototype.MergeLine(card, config.maximumCardLevel, config.maximumCardLevel), Is.Null, card.id + " at the last level");
            }
            Assert.That(DeckPrototype.MergeLine(config.dummyCard, 1), Is.Null, "The dummy does not merge.");
        }

        [Test]
        public void ThrowsReadNaturally()
        {
            Assert.That(new[] { 1, 2, 4, 5, 11, 21, 22 }.Select(DeckPrototype.Throws),
                Is.EqualTo(new[] { "1 бросок", "2 броска", "4 броска", "5 бросков", "11 бросков", "21 бросок", "22 броска" }));
        }
    }
}
