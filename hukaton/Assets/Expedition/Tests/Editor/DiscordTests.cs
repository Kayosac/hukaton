using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using Hakaton.Expeditions.Events;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Раздор»: who joins the free-for-all, who fights whom, when it ends, and the card's numbers.</summary>
    public sealed class DiscordTests
    {
        [Test]
        public void LiveOrdinaryBeastsJoinTheDeadAndTheBusyDoNot()
        {
            Assert.That(DiscordRules.Decide(true, false, null), Is.EqualTo(DiscordRules.Entry.Join));
            Assert.That(DiscordRules.Decide(false, false, null), Is.EqualTo(DiscordRules.Entry.Skip), "The dead stay out.");
            Assert.That(DiscordRules.Decide(true, true, null), Is.EqualTo(DiscordRules.Entry.Skip), "Already in another «Раздор».");
        }

        [Test]
        public void BeastBrawlBeastsAreStartedLeftInTheirFightOrJoinOnceItIsOver()
        {
            Assert.That(DiscordRules.Decide(true, false, BeastBrawl.Phase.NotStarted), Is.EqualTo(DiscordRules.Entry.StartEvent), "Calm packs: the card sets them on each other.");
            Assert.That(DiscordRules.Decide(true, true, BeastBrawl.Phase.NotStarted), Is.EqualTo(DiscordRules.Entry.StartEvent), "Posturing packs: the card starts their fight.");
            Assert.That(DiscordRules.Decide(true, true, BeastBrawl.Phase.Fighting), Is.EqualTo(DiscordRules.Entry.Skip), "The event's own referee runs a fight under way.");
            Assert.That(DiscordRules.Decide(true, false, BeastBrawl.Phase.Fighting), Is.EqualTo(DiscordRules.Entry.Skip), "A beast gone after the player stays with the event.");
            Assert.That(DiscordRules.Decide(true, false, BeastBrawl.Phase.Over), Is.EqualTo(DiscordRules.Entry.Join), "Survivors of a finished event are ordinary beasts.");
            Assert.That(DiscordRules.Decide(false, false, BeastBrawl.Phase.NotStarted), Is.EqualTo(DiscordRules.Entry.Skip));
        }

        [Test]
        public void ItTakesTwoToFight()
        {
            Assert.That(DiscordRules.Gathers(0), Is.False);
            Assert.That(DiscordRules.Gathers(1), Is.False, "One beast only bristles.");
            Assert.That(DiscordRules.Gathers(2), Is.True);
            Assert.That(DiscordRules.Gathers(7), Is.True);
        }

        [Test]
        public void TheFightEndsWithOneLeftOrAfterTheMaximum()
        {
            Assert.That(DiscordRules.Over(3, 10, 45), Is.False);
            Assert.That(DiscordRules.Over(2, 44.9f, 45), Is.False);
            Assert.That(DiscordRules.Over(1, 3, 45), Is.True, "One left: it has won.");
            Assert.That(DiscordRules.Over(0, 3, 45), Is.True, "The last two fell together.");
            Assert.That(DiscordRules.Over(4, 45, 45), Is.True, "Called off after 45 s.");
        }

        [Test]
        public void TheRadiusIsMeasuredOnTheGround()
        {
            Assert.That(DiscordRules.Within(Vector3.zero, new Vector3(3, 0, 4), 5), Is.True, "Right on the edge.");
            Assert.That(DiscordRules.Within(Vector3.zero, new Vector3(3, 0, 4.1f), 5), Is.False);
            Assert.That(DiscordRules.Within(Vector3.zero, new Vector3(1, 6, 1), 5), Is.True, "A flyer above the spot counts.");
        }

        [Test]
        public void EachGoesForTheNearestOtherFighter()
        {
            var positions = new List<Vector3> { Vector3.zero, new Vector3(5, 0, 0), new Vector3(0, 3, 2), new Vector3(-1, 0, 0) };
            var fighting = new List<bool> { true, true, true, false };
            Assert.That(DiscordRules.Nearest(positions, fighting, 0), Is.EqualTo(2), "Flat distance; the one out of the fight is no rival.");
            Assert.That(DiscordRules.Nearest(positions, fighting, 1), Is.EqualTo(0), "5 m flat beats 5.4 m.");
            fighting[2] = false;
            Assert.That(DiscordRules.Nearest(positions, fighting, 0), Is.EqualTo(1), "Its rival fell: the next nearest.");
            fighting[1] = false;
            Assert.That(DiscordRules.Nearest(positions, fighting, 0), Is.EqualTo(-1), "Nobody left to fight.");
        }

        [Test]
        public void ARivalOnlyChangesForAClearlyNearerOne()
        {
            Assert.That(DiscordRules.KeepsRival(3, 2, 1.5f), Is.True);
            Assert.That(DiscordRules.KeepsRival(3, 1.4f, 1.5f), Is.False);
        }

        [Test]
        public void TheCardIsWiredWithTheBoardNumbers()
        {
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/discord.asset");
            var effect = card.effectAsset as DiscordEffect;
            Assert.That(effect, Is.Not.Null, "discord.asset → Effects/Discord.asset");
            Assert.That((effect.radius.i, effect.radius.ii, effect.radius.iii), Is.EqualTo((5f, 10f, 15f)));
            Assert.That(effect.maximumSeconds, Is.EqualTo(45));
            Assert.That(effect.bristleSeconds, Is.EqualTo(2).Within(.5f));
            Assert.That(effect.brawlPrefab, Is.Not.Null);
            Assert.That(effect.impactVfx, Is.Not.Null);
            Assert.That(effect.impactSound, Is.Not.Null);
            Assert.That(effect.missSound, Is.Not.Null);
            var area = (ICardArea)effect;
            for (int level = 1; level <= 3; level++)
            {
                Assert.That(area.TryGetArea(card, level, null, out float r), Is.True);
                Assert.That(r, Is.EqualTo(level * 5f));
            }
            Assert.That(area.LeavesOutlinedZone, Is.False, "The landing flashes the radius.");
            Assert.That(effect.TryGetStat(out var label, out var values, out var unit), Is.True);
            Assert.That((label, values.iii, unit), Is.EqualTo(("Радиус", 15f, " м")));
            Assert.That((card.purchasePrice, card.rank), Is.EqualTo((300, 4)), "Price and rank are the user's (KayoSpace card table).");
        }
    }
}
