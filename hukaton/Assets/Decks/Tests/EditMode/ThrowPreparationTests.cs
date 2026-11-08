using Hakaton.Decks;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    public sealed class ThrowPreparationTests
    {
        const float ChargeSeconds = .65f, Gesture = 200;

        static ThrowPreparation Started()
        {
            var preparation = new ThrowPreparation();
            preparation.Begin();
            return preparation;
        }

        [Test]
        public void HoldingChargesToFullAndNoFurther()
        {
            var p = Started();
            p.Tick(ChargeSeconds / 2, false, 0, 0, ChargeSeconds, Gesture);
            Assert.That(p.Charge, Is.EqualTo(.5f).Within(1e-4f));
            p.Tick(ChargeSeconds, false, 0, 0, ChargeSeconds, Gesture);
            Assert.That(p.Charge, Is.EqualTo(1));
        }

        [Test]
        public void MouseMotionWithoutTheModifierNeverSpins()
        {
            var p = Started();
            p.Tick(.1f, false, 300, -150, ChargeSeconds, Gesture);
            Assert.That(p.SpinAmount, Is.Zero);
        }

        [Test]
        public void TheModifierPausesTheChargeAndSteersSpinInAnyDirection()
        {
            var p = Started();
            p.Tick(.2f, false, 0, 0, ChargeSeconds, Gesture);
            float charge = p.Charge;
            p.Tick(.2f, true, 60, 80, ChargeSeconds, Gesture);
            Assert.That(p.Charge, Is.EqualTo(charge), "Charge pauses while the spin is steered.");
            Assert.That(p.Steering, Is.True);
            Assert.That(p.SpinX, Is.EqualTo(.3f).Within(1e-4f));
            Assert.That(p.SpinY, Is.EqualTo(.4f).Within(1e-4f));
            p.Tick(.02f, true, -120, -40, ChargeSeconds, Gesture);
            Assert.That(p.SpinX, Is.EqualTo(-.3f).Within(1e-4f), "Steering continues; it is not a one-shot gesture.");
            Assert.That(p.SpinY, Is.EqualTo(.2f).Within(1e-4f));
        }

        [Test]
        public void SpinIsClampedToFullStrengthKeepingItsDirection()
        {
            var p = Started();
            p.Tick(.02f, true, 600, 600, ChargeSeconds, Gesture);
            Assert.That(p.SpinAmount, Is.EqualTo(1).Within(1e-4f));
            Assert.That(p.SpinX, Is.EqualTo(p.SpinY).Within(1e-4f));
        }

        [Test]
        public void ReleasingTheModifierKeepsTheSpinAndResumesCharging()
        {
            var p = Started();
            p.Tick(.02f, true, 0, -100, ChargeSeconds, Gesture);
            p.Tick(.1f, false, 500, 500, ChargeSeconds, Gesture);
            Assert.That(p.Steering, Is.False);
            Assert.That(p.SpinY, Is.EqualTo(-.5f).Within(1e-4f));
            Assert.That(p.SpinX, Is.Zero);
            Assert.That(p.Charge, Is.GreaterThan(0));
        }

        [Test]
        public void CancelAndBeginStartClean()
        {
            var p = Started();
            p.Tick(.3f, false, 0, 0, ChargeSeconds, Gesture);
            p.Tick(.02f, true, 100, 0, ChargeSeconds, Gesture);
            p.Cancel();
            Assert.That(p.Active, Is.False);
            p.Begin();
            Assert.That(p.Charge, Is.Zero);
            Assert.That(p.SpinAmount, Is.Zero);
        }
    }
}
