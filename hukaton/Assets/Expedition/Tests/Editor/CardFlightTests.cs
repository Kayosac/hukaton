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
            CardFlight.Advance(ref wholePosition, ref wholeVelocity, .6f, settings, 1f);

            Vector3 framePosition = Vector3.zero, frameVelocity = new Vector3(1, 6, 14);
            for (int frame = 0; frame < 4; frame++) CardFlight.Advance(ref framePosition, ref frameVelocity, .6f, settings, .25f);

            Assert.That(Vector3.Distance(wholePosition, framePosition), Is.LessThan(1e-4f),
                "The preview samples the path differently from the projectile; both must land in the same place.");
            Assert.That(Vector3.Distance(wholeVelocity, frameVelocity), Is.LessThan(1e-4f));
        }

        [Test]
        public void GravityPullsTheCardDownAsDesigned()
        {
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 5, 10);
            CardFlight.Advance(ref position, ref velocity, 0, settings, 1f);
            float expected = 5 - .5f * settings.cardGravity;
            Assert.That(position.y, Is.EqualTo(expected).Within(.06f));
            Assert.That(velocity.y, Is.EqualTo(5 - settings.cardGravity).Within(1e-3f));
            Assert.That(position.z, Is.EqualTo(10).Within(1e-3f), "Without spin nothing slows the card horizontally.");
        }

        [Test]
        public void WithoutSpinTheCardStaysInItsThrowPlane()
        {
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 4, 12);
            CardFlight.Advance(ref position, ref velocity, 0, settings, 1.5f);
            Assert.That(position.x, Is.EqualTo(0).Within(1e-5f));
        }

        [Test]
        public void SpinCurvesTheCardToEitherSideSymmetrically()
        {
            Vector3 right = Vector3.zero, rightVelocity = new Vector3(0, 3, 12);
            Vector3 left = Vector3.zero, leftVelocity = new Vector3(0, 3, 12);
            CardFlight.Advance(ref right, ref rightVelocity, 1, settings, .8f);
            CardFlight.Advance(ref left, ref leftVelocity, -1, settings, .8f);
            Assert.That(right.x, Is.GreaterThan(.5f), "Positive spin curves to the thrower's right.");
            Assert.That(left.x, Is.EqualTo(-right.x).Within(1e-4f));
            Assert.That(left.y, Is.EqualTo(right.y).Within(1e-4f), "Spin never changes the drop.");
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
