using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>The arithmetic of the player's debuffs (board «Дебаффы»).</summary>
    public sealed class PlayerStatusRulesTests
    {
        [Test]
        public void ARepeatHitOnlyRefreshesTheTimer()
        {
            // Started at 0 for 6 s; hit again at 4 with 6 s: ends at 10, not 12 (no adding up).
            Assert.That(PlayerStatusRules.RefreshedUntil(6, true, 4, 6), Is.EqualTo(10).Within(1e-4));
            // A shorter repeat never cuts the time left.
            Assert.That(PlayerStatusRules.RefreshedUntil(10, true, 5, 2), Is.EqualTo(10).Within(1e-4));
            // Not on: starts from now.
            Assert.That(PlayerStatusRules.RefreshedUntil(-1, false, 3, 5), Is.EqualTo(8).Within(1e-4));
        }

        [Test]
        public void RotTakesAHeartPerHitAndAlwaysLeavesOne()
        {
            Assert.That(PlayerStatusRules.RotAfter(0, 2, 8), Is.EqualTo(2));
            Assert.That(PlayerStatusRules.RotAfter(2, 2, 8), Is.EqualTo(4));
            Assert.That(PlayerStatusRules.RotAfter(4, 2, 8), Is.EqualTo(6));
            Assert.That(PlayerStatusRules.RotAfter(6, 2, 8), Is.EqualTo(6), "One whole heart stays.");
        }

        [Test]
        public void FragileDoublesDamage()
        {
            Assert.That(PlayerStatusRules.FragileDamage(1, 2), Is.EqualTo(2));
            Assert.That(PlayerStatusRules.FragileDamage(3, 2), Is.EqualTo(6));
            Assert.That(PlayerStatusRules.FragileDamage(2, 1), Is.EqualTo(2));
            Assert.That(PlayerStatusRules.FragileDamage(0, 2), Is.EqualTo(0));
        }

        [Test]
        public void BlindFogHidesTheWorldBeyondTheVisibleDistance()
        {
            float density = PlayerStatusRules.BlindFogDensity(FogMode.ExponentialSquared, 6, .01f);
            float left = Mathf.Exp(-(density * 6) * (density * 6));
            Assert.That(left, Is.LessThan(.06f));
            float near = Mathf.Exp(-(density * 2) * (density * 2));
            Assert.That(near, Is.GreaterThan(.6f), "Close by is still seen.");
            Assert.That(PlayerStatusRules.BlindFogDensity(FogMode.ExponentialSquared, 6, 5), Is.EqualTo(5), "Never thinner than the scene's fog.");
        }

        [Test]
        public void CrookedThrowStaysWithinTheAngle()
        {
            Vector3 aim = Vector3.forward * 20;
            for (int i = 0; i <= 10; i++)
            {
                float roll = i / 10f;
                var crooked = PlayerStatusRules.Crook(aim, Vector3.up, Vector3.right, 14, roll, 1 - roll);
                Assert.That(Vector3.Angle(aim, crooked), Is.LessThanOrEqualTo(14 * 1.12f));
                Assert.That(crooked.magnitude, Is.EqualTo(20).Within(1e-3), "Only the direction changes.");
            }
            Assert.That(Vector3.Angle(aim, PlayerStatusRules.Crook(aim, Vector3.up, Vector3.right, 14, 0, .5f)), Is.EqualTo(14).Within(.01f));
        }

        [Test]
        public void LeakyPocketDropsACoinEveryStep()
        {
            float walked = 5.5f;
            Assert.That(PlayerStatusRules.Drops(ref walked, 2), Is.EqualTo(2));
            Assert.That(walked, Is.EqualTo(1.5f).Within(1e-4));
            walked = 1.9f;
            Assert.That(PlayerStatusRules.Drops(ref walked, 2), Is.Zero);
        }
    }
}
