using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>«Тягучий час»: the slow clock, the zone's shape, slowed projectiles, the card's numbers and assets.</summary>
    public sealed class StickyHourTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void NTimesSlowerIsAClockOfOneNth()
        {
            Assert.That(StickyHourRules.TimeScale(2), Is.EqualTo(.5f));
            Assert.That(StickyHourRules.TimeScale(3), Is.EqualTo(1 / 3f).Within(1e-6f));
            Assert.That(StickyHourRules.TimeScale(4), Is.EqualTo(.25f));
            Assert.That(StickyHourRules.TimeScale(.5f), Is.EqualTo(1), "never speeds anything up");
        }

        [Test]
        public void AttackTimersStretchByTheSlowdown()
        {
            // A wolf's wind-up of 0.7 s, strike 0.45 s and recovery 1.1 s inside a level III zone.
            float scale = StickyHourRules.TimeScale(4);
            Assert.That(StickyHourRules.SlowedSeconds(.7f, scale), Is.EqualTo(2.8f).Within(1e-5f));
            Assert.That(StickyHourRules.SlowedSeconds(.45f + 1.1f, scale), Is.EqualTo(6.2f).Within(1e-5f));
        }

        [Test]
        public void ZoneIsAnUprightCylinder()
        {
            var centre = new Vector3(10, 2, -3);
            Assert.That(StickyHourRules.Inside(centre + new Vector3(3.9f, 0, 0), centre, 4, 4.5f, .6f), Is.True);
            Assert.That(StickyHourRules.Inside(centre + new Vector3(3, 0, 3), centre, 4, 4.5f, .6f), Is.False, "4.24 m away");
            Assert.That(StickyHourRules.Inside(centre + new Vector3(4.2f, 0, 0), centre, 4, 4.5f, .6f, .3f), Is.True, "a body touching the edge counts");
            Assert.That(StickyHourRules.Inside(centre + Vector3.up * 4, centre, 4, 4.5f, .6f), Is.True, "a flyer above the haze");
            Assert.That(StickyHourRules.Inside(centre + Vector3.up * 5, centre, 4, 4.5f, .6f), Is.False);
            Assert.That(StickyHourRules.Inside(centre + Vector3.down * .5f, centre, 4, 4.5f, .6f), Is.True, "uneven ground below");
            Assert.That(StickyHourRules.Inside(centre + Vector3.down * 1, centre, 4, 4.5f, .6f), Is.False);
        }

        [Test]
        public void SlowedProjectileFollowsTheSamePathNTimesSlower()
        {
            Vector3 from = new Vector3(0, 1.5f, 0), to = new Vector3(10, 1, 4);
            EnemyProjectileFlight.Launch(from, to, 9, 1.2f, out var velocity, out float gravity);
            float dt = .02f, scale = StickyHourRules.TimeScale(3);
            Vector3 normal = from, normalVelocity = velocity, slow = from, slowVelocity = velocity;
            for (int i = 0; i < 20; i++) EnemyProjectileFlight.Advance(ref normal, ref normalVelocity, gravity, dt);
            for (int i = 0; i < 60; i++) EnemyProjectileFlight.Advance(ref slow, ref slowVelocity, gravity, dt * scale);
            Assert.That(Vector3.Distance(normal, slow), Is.LessThan(1e-3f), "3 times the steps reach the same point");
            Assert.That(Vector3.Distance(normalVelocity, slowVelocity), Is.LessThan(1e-3f));
        }

        [Test]
        public void CardNumbersAndAssetsAreWired()
        {
            var effect = AssetDatabase.LoadAssetAtPath<StickyHourEffect>("Assets/Game/Data/Effects/Sticky hour.asset");
            Assert.That(effect, Is.Not.Null);
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/sticky_hour.asset");
            Assert.That(card.effectAsset, Is.SameAs(effect));
            Assert.That((effect.slowdown.i, effect.slowdown.ii, effect.slowdown.iii), Is.EqualTo((2f, 3f, 4f)));
            Assert.That(effect.radius, Is.EqualTo(4));
            Assert.That(effect.duration, Is.EqualTo(10));
            Assert.That(effect.TryGetStat(out string label, out var values, out string unit), Is.True);
            Assert.That((label, values.iii, unit), Is.EqualTo(("Медленнее в", 4f, " раза")));
            var area = (ICardArea)effect;
            Assert.That(area.TryGetArea(card, 3, null, out float radius), Is.True);
            Assert.That(radius, Is.EqualTo(4));
            Assert.That(area.LeavesOutlinedZone, Is.True);
            Assert.That(effect.impactVfx, Is.Not.Null);
            Assert.That(effect.impactSound, Is.Not.Null);
            var zone = effect.zonePrefab;
            Assert.That(zone, Is.Not.Null);
            Assert.That(zone.dome, Is.Not.Null);
            Assert.That(zone.domeRenderer, Is.Not.Null);
            Assert.That(zone.sigil, Is.Not.Null);
            Assert.That(zone.motes, Is.Not.Empty);
            Assert.That(zone.GetComponentsInChildren<Collider>(true), Is.Empty, "no invisible colliders: the zone works by script");
        }

        [Test]
        public void ZoneOutlinesItsRadiusAndContainsByItsCylinder()
        {
            var effect = AssetDatabase.LoadAssetAtPath<StickyHourEffect>("Assets/Game/Data/Effects/Sticky hour.asset");
            Assume.That(effect && effect.zonePrefab, Is.True);
            var zone = Object.Instantiate(effect.zonePrefab, new Vector3(5, 0, 5), Quaternion.identity);
            created.Add(zone.gameObject);
            zone.Setup(10, 4, .25f);
            Assert.That(zone.TimeScale, Is.EqualTo(.25f));
            Assert.That(zone.Contains(new Vector3(8.5f, 1, 5)), Is.True);
            Assert.That(zone.Contains(new Vector3(9.5f, 1, 5)), Is.False);
            Assert.That(zone.TryGetOutline(out _, out _), Is.False, "outlined only once the world spawned it");
        }
    }
}
