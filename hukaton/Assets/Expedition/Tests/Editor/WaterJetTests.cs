using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Правила «Струи»: числа по уровням, отброс, мокрота, тушение, радиус всплеска.</summary>
    public sealed class WaterJetTests
    {
        static CardDefinition Card()
        {
            var config = AssetDatabase.LoadAssetAtPath<DeckPrototypeConfig>("Assets/Game/Data/Cards.asset");
            var card = config.Find("water_jet");
            Assert.That(card, Is.Not.Null, "Струя есть в каталоге.");
            return card;
        }

        static WaterJetEffect Effect()
        {
            var effect = Card().effectAsset as WaterJetEffect;
            Assert.That(effect, Is.Not.Null);
            return effect;
        }

        [Test]
        public void CardHasTheTablesRankPriceAndUses()
        {
            var card = Card();
            Assert.That(card.action, Is.EqualTo(CardAction.WaterJet));
            Assert.That((int)CardAction.WaterJet, Is.EqualTo(36));
            Assert.That((card.rank, card.purchasePrice), Is.EqualTo((2, 130)));
            Assert.That((card.uses.i, card.uses.ii, card.uses.iii), Is.EqualTo((1, 1, 1)));
            Assert.That(card.artwork, Is.Not.Null);
        }

        [Test]
        public void DamageAndKnockbackGrowWithTheLevel()
        {
            var effect = Effect();
            Assert.That((effect.damage.At(1), effect.damage.At(2), effect.damage.At(3)), Is.EqualTo((2, 3, 5)));
            Assert.That((effect.knockback.At(1), effect.knockback.At(2), effect.knockback.At(3)), Is.EqualTo((4f, 5f, 6f)));
            Assert.That(effect.wetSeconds, Is.EqualTo(30));
            Assert.That(effect.TryGetStat(out var label, out var values, out _), Is.True);
            Assert.That(label, Is.EqualTo("Урон"));
            Assert.That((values.i, values.ii, values.iii), Is.EqualTo((2f, 3f, 5f)), "ПК показывает урон.");
        }

        [Test]
        public void KnockbackFollowsTheThrowAndLiftsALittle()
        {
            var push = WaterJetRules.Push(new Vector3(3, -6, 4), 5, .2f);
            Assert.That(Vector3.Distance(push, new Vector3(3, 1, 4)), Is.LessThan(1e-4f), "Горизонталь броска × 5 и 20 % вверх.");
            push = WaterJetRules.Push(Vector3.down, 4, 0);
            Assert.That(Vector3.Distance(push, Vector3.forward * 4), Is.LessThan(1e-4f), "Отвесный бросок: вперёд по миру.");
        }

        [Test]
        public void DrenchMakesTheBodyWetAndRefreshesTheTime()
        {
            var body = new GameObject("body");
            try
            {
                Assert.That(WetStatus.IsWet(body.transform), Is.False);
                Assert.That(WaterJetRules.Drench(body.transform, null, 30, null), Is.False, "Сухое и не горящее: тушить нечего.");
                var wet = body.GetComponent<WetStatus>();
                Assert.That(wet, Is.Not.Null);
                Assert.That(wet.Wet, Is.True);
                Assert.That(wet.Remaining, Is.EqualTo(30).Within(.01f));
                WaterJetRules.Drench(body.transform, null, 30, null);
                Assert.That(wet.Remaining, Is.EqualTo(30).Within(.01f), "Повторно — срок обновляется, не складывается.");
                Assert.That(body.GetComponents<WetStatus>().Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(body); }
        }

        [Test]
        public void DrenchPutsTheFireOut()
        {
            var body = new GameObject("burning");
            try
            {
                bool burning = true;
                bool Douse()
                {
                    bool was = burning;
                    burning = false;
                    return was;
                }
                Assert.That(WaterJetRules.Drench(body.transform, null, 30, Douse), Is.True, "Горел — погас.");
                Assert.That(burning, Is.False);
                Assert.That(WetStatus.IsWet(body.transform), Is.True, "И стал мокрым.");
                Assert.That(WaterJetRules.Drench(body.transform, null, 30, Douse), Is.False, "Уже не горит.");
            }
            finally { Object.DestroyImmediate(body); }
        }

        [Test]
        public void SplashReachesOnlyTheCloseOnes()
        {
            Assert.That(WaterJetRules.InSplash(Vector3.zero, 1, new Vector3(.8f, 0, 0), new Vector3(.8f, .6f, 0)), Is.True);
            Assert.That(WaterJetRules.InSplash(Vector3.zero, 1, new Vector3(0, -.9f, 0), new Vector3(0, 1.5f, 0)), Is.True, "Ноги чуть ниже точки.");
            Assert.That(WaterJetRules.InSplash(Vector3.zero, 1, new Vector3(1.5f, 0, 0), new Vector3(1.5f, .6f, 0)), Is.False);
            Assert.That(Effect().splashRadius, Is.EqualTo(1));
        }

        [Test]
        public void SplashRingShowsWhenAimingAwayFromCreatures()
        {
            ICardArea area = Effect();
            Assert.That(area.TryGetArea(Card(), 1, null, out float radius), Is.True);
            Assert.That(radius, Is.EqualTo(1));
            Assert.That(area.LeavesOutlinedZone, Is.False);
        }
    }
}
