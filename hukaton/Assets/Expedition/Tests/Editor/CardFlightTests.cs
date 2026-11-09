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
            float t_wholePosition = 0, t_framePosition = 0;
            Vector3 wholePosition = Vector3.zero, wholeVelocity = new Vector3(1, 6, 14);
            CardFlight.Advance(ref wholePosition, ref wholeVelocity, ref t_wholePosition, new Vector2(.6f, .3f), settings, 1f);

            Vector3 framePosition = Vector3.zero, frameVelocity = new Vector3(1, 6, 14);
            for (int frame = 0; frame < 4; frame++) CardFlight.Advance(ref framePosition, ref frameVelocity, ref t_framePosition, new Vector2(.6f, .3f), settings, .25f);

            Assert.That(Vector3.Distance(wholePosition, framePosition), Is.LessThan(1e-4f),
                "The preview samples the path differently from the projectile; both must land in the same place.");
            Assert.That(Vector3.Distance(wholeVelocity, frameVelocity), Is.LessThan(1e-4f));
        }

        [Test]
        public void GravityPullsTheCardDownAsDesigned()
        {
            float t_position = 0;
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 5, 10);
            CardFlight.Advance(ref position, ref velocity, ref t_position, Vector2.zero, settings, 1f);
            float expected = 5 - .5f * settings.cardGravity;
            Assert.That(position.y, Is.EqualTo(expected).Within(.06f));
            Assert.That(velocity.y, Is.EqualTo(5 - settings.cardGravity).Within(1e-3f));
            Assert.That(position.z, Is.EqualTo(10).Within(1e-3f), "Without spin nothing slows the card horizontally.");
        }

        [Test]
        public void WithoutSpinTheCardStaysInItsThrowPlane()
        {
            float t_position = 0;
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 4, 12);
            CardFlight.Advance(ref position, ref velocity, ref t_position, Vector2.zero, settings, 1.5f);
            Assert.That(position.x, Is.EqualTo(0).Within(1e-5f));
        }

        [Test]
        public void SpinCurvesTheCardToEitherSideSymmetrically()
        {
            float t_right = 0, t_left = 0;
            Vector3 right = Vector3.zero, rightVelocity = new Vector3(0, 3, 12);
            Vector3 left = Vector3.zero, leftVelocity = new Vector3(0, 3, 12);
            CardFlight.Advance(ref right, ref rightVelocity, ref t_right, Vector2.right, settings, .8f);
            CardFlight.Advance(ref left, ref leftVelocity, ref t_left, Vector2.left, settings, .8f);
            Assert.That(right.x, Is.GreaterThan(.5f), "Positive spin curves to the thrower's right.");
            Assert.That(left.x, Is.EqualTo(-right.x).Within(1e-4f));
            Assert.That(left.y, Is.EqualTo(right.y).Within(1e-4f), "Sideways spin never changes the drop.");
        }

        [Test]
        public void UpSpinFloatsTheCardAndDownSpinDipsItWithoutSideDrift()
        {
            float t_still = 0, t_up = 0, t_down = 0;
            Vector3 still = Vector3.zero, stillVelocity = new Vector3(0, 3, 12);
            Vector3 up = Vector3.zero, upVelocity = new Vector3(0, 3, 12);
            Vector3 down = Vector3.zero, downVelocity = new Vector3(0, 3, 12);
            CardFlight.Advance(ref still, ref stillVelocity, ref t_still, Vector2.zero, settings, .8f);
            CardFlight.Advance(ref up, ref upVelocity, ref t_up, Vector2.up, settings, .8f);
            CardFlight.Advance(ref down, ref downVelocity, ref t_down, Vector2.down, settings, .8f);
            Assert.That(up.y, Is.GreaterThan(still.y + .5f));
            Assert.That(down.y, Is.LessThan(still.y - .5f));
            Assert.That(up.x, Is.EqualTo(0).Within(1e-5f));
            Assert.That(down.x, Is.EqualTo(0).Within(1e-5f));
        }

        [TestCase(0f, 0f, 18f)]
        [TestCase(0f, 8f, 16f)]
        [TestCase(0f, 18f, .01f)]
        public void FullUpSpinClimbsThenFallsFasterAndFaster(float x, float y, float z)
        {
            Vector3 position = Vector3.zero, velocity = new Vector3(x, y, z);
            float time = 0, previousFall = 0;
            bool fell = false;
            for (int second = 0; second < 6; second++)
            {
                CardFlight.Advance(ref position, ref velocity, ref time, Vector2.up, settings, 1f);
                if (velocity.y < -1f)
                {
                    if (fell) Assert.That(-velocity.y, Is.GreaterThan(previousFall + 5f), "Past the top the card falls with growing speed, never hangs.");
                    fell = true;
                    previousFall = -velocity.y;
                }
            }
            Assert.That(fell, "Up-spin can't keep a card climbing forever.");
            Assert.That(position.y, Is.LessThan(0), "It comes back down.");
        }

        [Test]
        public void UpSpinStillClimbsOffTheHand()
        {
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 0, 18);
            float time = 0;
            CardFlight.Advance(ref position, ref velocity, ref time, Vector2.up, settings, settings.liftHoldSeconds);
            Assert.That(velocity.y, Is.GreaterThan(0), "A flat throw with full up-spin rises at first.");
        }

        [Test]
        public void DiagonalSpinCurvesBothWays()
        {
            float t_position = 0, t_flat = 0;
            Vector3 position = Vector3.zero, velocity = new Vector3(0, 3, 12);
            Vector3 flat = Vector3.zero, flatVelocity = new Vector3(0, 3, 12);
            CardFlight.Advance(ref position, ref velocity, ref t_position, new Vector2(-.7f, .7f), settings, .8f);
            CardFlight.Advance(ref flat, ref flatVelocity, ref t_flat, Vector2.zero, settings, .8f);
            Assert.That(position.x, Is.LessThan(-.3f), "Left component curves left.");
            Assert.That(position.y, Is.GreaterThan(flat.y + .3f), "Up component lifts.");
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
