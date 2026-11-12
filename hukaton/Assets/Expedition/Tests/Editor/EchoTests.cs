using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Rules of «Эхо»: what it remembers, what it may repeat, whose area it shows.</summary>
    public sealed class EchoTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            EchoMemory.Forget();
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            created.Clear();
        }

        T Make<T>() where T : ScriptableObject
        {
            var item = ScriptableObject.CreateInstance<T>();
            created.Add(item);
            return item;
        }

        CardDefinition Card(string id, ScriptableObject effect, CardAction action = CardAction.Strike)
        {
            var card = Make<CardDefinition>();
            card.id = id;
            card.action = action;
            card.effectAsset = effect;
            return card;
        }

        static readonly string[] Never = { "teleport", "echo", "return" };

        [Test]
        public void ItRepeatsOrdinaryCardsButNotTeleportEchoStoryOrNothing()
        {
            Assert.That(EchoRules.CanRepeat(Card("fog", Make<FogEffect>()), false, Never), Is.True);
            Assert.That(EchoRules.CanRepeat(null, false, Never), Is.False, "Nothing thrown yet.");
            Assert.That(EchoRules.CanRepeat(Card("teleport", Make<TeleportEffect>(), CardAction.Teleport), false, Never), Is.False);
            Assert.That(EchoRules.CanRepeat(Card("my_teleport", Make<TeleportEffect>()), false, null), Is.False, "Teleport by its effect, whatever the id.");
            Assert.That(EchoRules.CanRepeat(Card("echo", Make<EchoEffect>()), false, null), Is.False);
            Assert.That(EchoRules.CanRepeat(Card("note", Make<FogEffect>()), true, Never), Is.False, "Story cards.");
            Assert.That(EchoRules.CanRepeat(Card("return", Make<FogEffect>()), false, Never), Is.False, "«Пора домой».");
            Assert.That(EchoRules.CanRepeat(Card("blank", null), false, Never), Is.False, "No effect to repeat.");
        }

        [Test]
        public void ItRemembersTheLastThrowButNeverItself()
        {
            var fog = Card("fog", Make<FogEffect>());
            var echo = Card("echo", Make<EchoEffect>());
            var teleport = Card("teleport", Make<TeleportEffect>(), CardAction.Teleport);
            EchoMemory.Remember(null, fog, false);
            EchoMemory.Remember(null, echo, false);
            Assert.That(EchoMemory.Last, Is.SameAs(fog), "Throwing Эхо does not overwrite the card it repeats.");
            EchoMemory.Remember(null, teleport, false);
            Assert.That(EchoMemory.Last, Is.SameAs(teleport), "Teleport was the last card: Эхо fizzles, not reaching back to Fog.");
            Assert.That(EchoRules.CanRepeat(EchoMemory.Last, EchoMemory.LastIsStory, Never), Is.False);
            EchoMemory.Forget();
            Assert.That(EchoMemory.Last, Is.Null, "Forgotten at the end of the outing.");
        }

        [Test]
        public void ItsAreaIsTheRepeatedCardsAtTheEchosLevel()
        {
            var echo = Make<EchoEffect>();
            var echoCard = Card("echo", echo);
            var area = (ICardArea)echo;
            Assert.That(area.TryGetArea(echoCard, 1, null, out _), Is.False, "Nothing to repeat: no ring.");

            var lure = Make<LureEffect>();
            lure.radius = new LevelFloat(20, 40, 70);
            EchoMemory.Remember(null, Card("lure", lure), false);
            Assert.That(area.TryGetArea(echoCard, 2, null, out float radius), Is.True);
            Assert.That(radius, Is.EqualTo(40), "Lure at the level of the Эхо copy (II).");
            Assert.That(area.LeavesOutlinedZone, Is.True, "The lure's zone outlines itself.");

            EchoMemory.Remember(null, Card("draft", Make<DraftEffect>()), false);
            Assert.That(area.TryGetArea(echoCard, 2, null, out _), Is.False, "A card without an area: no ring.");
        }

        [Test]
        public void TheCardIsBound()
        {
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/echo.asset");
            var echo = card.effectAsset as EchoEffect;
            Assert.That(echo, Is.Not.Null, "echo.asset → Effects/Echo.asset");
            Assert.That(echo.impactVfx, Is.Not.Null, "The echo ripple.");
            Assert.That(echo.missSound, Is.Not.Null, "The fizzle when there is nothing to repeat.");
            Assert.That(echo.fizzleVfx, Is.Not.Null);
            Assert.That(echo.neverRepeat, Does.Contain("teleport").And.Contain("echo"));
            Assert.That(echo.TryGetStat(out _, out var levels, out _), Is.True);
            Assert.That((levels.i, levels.ii, levels.iii), Is.EqualTo((1f, 2f, 3f)));
        }
    }
}
