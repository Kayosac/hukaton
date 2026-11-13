using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Creatures' strike contact (<see cref="WorldCreature.StrikeConnects"/>): small and big beasts both reach a target
    /// standing right at them, neither reaches one far away or behind. The Pumpkin rat (size 3.3) used to whiff at
    /// point blank: its snout was already past the target.
    /// </summary>
    public sealed class CreatureStrikeContactTests
    {
        static readonly Vector3 Forward = Vector3.forward;

        static bool Strikes(float size, float capsuleRadius, Vector3 victimFeet, float contactRange = 1.6f)
        {
            Vector3 body = Vector3.zero;
            Vector3 snout = body + (Forward * .65f + Vector3.up * .6f) * size;
            return WorldCreature.StrikeConnects(snout, body, capsuleRadius * size, size, Forward, victimFeet, victimFeet + Vector3.up * .9f, contactRange);
        }

        [Test]
        public void SmallBeastHitsUpCloseButNotFarOrBehind()
        {
            Assert.That(Strikes(.6f, .35f, new Vector3(0, 0, 1.2f)), Is.True);
            Assert.That(Strikes(.6f, .35f, new Vector3(.3f, 0, .7f)), Is.True, "Right against it.");
            Assert.That(Strikes(.6f, .35f, new Vector3(0, 0, 3)), Is.False);
            Assert.That(Strikes(.6f, .35f, new Vector3(0, 0, -1)), Is.False, "Behind it.");
        }

        [Test]
        public void BigBeastHitsUpCloseWhereItsSnoutIsAlreadyPastTheTarget()
        {
            // Pumpkin rat: size 3.3, its capsule pushes the target to ~1.6 m from its feet; the snout is 2.1 m ahead.
            Assert.That(Strikes(3.3f, .35f, new Vector3(0, 0, 1.6f)), Is.True);
            Assert.That(Strikes(3.3f, .35f, new Vector3(.8f, 0, 2.2f)), Is.True);
            Assert.That(Strikes(3.3f, .35f, new Vector3(0, 0, 3.6f)), Is.True, "At its snout.");
            Assert.That(Strikes(3.3f, .35f, new Vector3(0, 0, 6)), Is.False, "Far ahead.");
            Assert.That(Strikes(3.3f, .35f, new Vector3(0, 0, -1.6f)), Is.False, "Behind it.");
            Assert.That(Strikes(3.3f, .35f, new Vector3(0, 6, 1.6f)), Is.False, "Far above it.");
        }

        [Test]
        public void TheSnoutRuleIsUnchangedForOrdinaryBeasts()
        {
            // A goblin dog-sized beast at the edge of its old reach still hits; just beyond it, still misses.
            Assert.That(Strikes(1.2f, .35f, new Vector3(0, 0, 2.1f)), Is.True);
            Assert.That(Strikes(1.2f, .35f, new Vector3(0, 0, 2.6f)), Is.False);
        }
    }
}
