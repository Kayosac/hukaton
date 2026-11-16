using NUnit.Framework;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>The kick is for control, not for killing (board «Пинок и трофейные карты»).</summary>
    public sealed class KickRulesTests
    {
        [Test]
        public void KickTakesOnePointLikeAnyPhysicalHit() => Assert.AreEqual(1, KickRules.Damage);

        [TestCase(1, .6f), TestCase(5, .6f), TestCase(6, .3f), TestCase(13, .3f), TestCase(14, 0f), TestCase(20, 0f)]
        public void OnlySmallCreaturesAreStunned(int health, float seconds) =>
            Assert.AreEqual(seconds, KickRules.StaggerSeconds(health, KickResponse.Normal), 1e-5);

        [Test]
        public void HeavyCreaturesBarelyMove()
        {
            Assert.AreEqual(1, KickRules.ShoveShare(4, KickResponse.Normal), 1e-5);
            Assert.Less(KickRules.ShoveShare(9, KickResponse.Normal), 1);
            Assert.Less(KickRules.ShoveShare(16, KickResponse.Normal), KickRules.ShoveShare(9, KickResponse.Normal));
        }

        [Test]
        public void RootedCreaturesAreHurtButNeitherShovedNorStunned()
        {
            Assert.IsTrue(KickRules.Lands(KickResponse.Anchored, true, false));
            Assert.AreEqual(0, KickRules.ShoveShare(3, KickResponse.Anchored));
            Assert.AreEqual(0, KickRules.StaggerSeconds(3, KickResponse.Anchored));
        }

        [Test]
        public void TheLegGoesThroughSwarmsGhostsTheBurrowedAndTheDead()
        {
            Assert.IsFalse(KickRules.Lands(KickResponse.Immune, true, false));
            Assert.IsFalse(KickRules.Lands(KickResponse.Normal, true, true));
            Assert.IsFalse(KickRules.Lands(KickResponse.Normal, false, false));
            Assert.IsTrue(KickRules.Lands(KickResponse.Normal, true, false));
        }
    }
}
