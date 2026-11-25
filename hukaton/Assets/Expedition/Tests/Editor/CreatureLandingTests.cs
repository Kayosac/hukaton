using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// A creature's own lunge or charge coming down on the ground is a landing, not a crash (it once took 2 HP off a
    /// pumpkin rat whose pounce met a bump in uneven ground edge-on at 9 m/s); what it runs into at body height still counts.
    /// </summary>
    public sealed class CreatureLandingTests
    {
        [Test]
        public void TheGroundUnderItsFeetIsALanding()
        {
            Assert.That(ExpeditionEffectRules.LandsOnItsFeet(10.05f, 10f), Is.True, "at the soles");
            Assert.That(ExpeditionEffectRules.LandsOnItsFeet(10.25f, 10f), Is.True, "a bump just over the soles");
            Assert.That(ExpeditionEffectRules.LandsOnItsFeet(9.9f, 10f), Is.True, "below them");
        }

        [Test]
        public void WhatItRunsIntoHigherUpIsNot()
        {
            Assert.That(ExpeditionEffectRules.LandsOnItsFeet(10.3f, 10f), Is.False);
            Assert.That(ExpeditionEffectRules.LandsOnItsFeet(10.8f, 10f), Is.False, "a rock at chest height");
        }

        [Test]
        public void TheCrashScaleIsUnchanged()
        {
            Assert.That(ExpeditionEffectRules.ImpactDamage(5.9f, 6, 3), Is.EqualTo(0));
            Assert.That(ExpeditionEffectRules.ImpactDamage(6f, 6, 3), Is.EqualTo(1));
            Assert.That(ExpeditionEffectRules.ImpactDamage(9.2f, 6, 3), Is.EqualTo(2));
        }
    }
}
