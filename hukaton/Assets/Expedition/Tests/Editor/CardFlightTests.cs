using Hakaton.Expeditions;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>The flight model shared by the projectile and the aiming preview.</summary>
    public sealed class CardFlightTests
    {
        ExpeditionSettings settings;

        [SetUp]
        public void CreateSettings() => settings = ScriptableObject.CreateInstance<ExpeditionSettings>();

        [TearDown]
        public void DestroySettings() => Object.DestroyImmediate(settings);

        [Test]
        public void SplittingTheSameTimeIntoFramesGivesTheSamePath()
        {
            Vector3 wholePosition = Vector3.zero, wholeVelocity = new Vector3(1, 6, 14);
            CardFlight.Advance(ref wholePosition, ref wholeVelocity, settings, 1f);

            Vector3 framePosition = Vector3.zero, frameVelocity = new Vector3(1, 6, 14);
            for (int frame = 0; frame < 4; frame++) CardFlight.Advance(ref framePosition, ref frameVelocity, settings, .25f);

            Assert.That(Vector3.Distance(wholePosition, framePosition), Is.LessThan(1e-4f),
                "The preview samples the path differently from the projectile; both must land in the same place.");
            Assert.That(Vector3.Distance(wholeVelocity, frameVelocity), Is.LessThan(1e-4f));
        }

        [Test]
        public void GravityPullsTheCardDownAsDesigned()
        {
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 5, 10);
            CardFlight.Advance(ref position, ref velocity, settings, 1f);
            float expected = 5 - .5f * settings.cardGravity;
            Assert.That(position.y, Is.EqualTo(expected).Within(.06f));
            Assert.That(velocity.y, Is.EqualTo(5 - settings.cardGravity).Within(1e-3f));
            Assert.That(position.z, Is.EqualTo(10).Within(1e-3f), "Nothing slows the card horizontally.");
        }

        [Test]
        public void TheCardStaysInItsThrowPlane()
        {
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 4, 12);
            CardFlight.Advance(ref position, ref velocity, settings, 1.5f);
            Assert.That(position.x, Is.EqualTo(0).Within(1e-5f));
        }

        [Test]
        public void CardsNeverCollideWithThePlayerOrOtherCards()
        {
            int mask = CardFlight.CollisionMask;
            Assert.That(mask & (1 << 2), Is.Zero, "The player rig lives on Ignore Raycast.");
            int cards = LayerMask.NameToLayer("Cards");
            Assume.That(cards, Is.GreaterThanOrEqualTo(0), "The project defines a Cards layer.");
            Assert.That(mask & (1 << cards), Is.Zero);
            Assert.That(mask & 1, Is.Not.Zero, "Default geometry still stops cards.");
        }
    }
}
