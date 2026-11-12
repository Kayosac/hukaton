using Hakaton.Decks;
using NUnit.Framework;

namespace Hakaton.Decks.Tests
{
    public sealed class ThrowPreparationTests
    {
        const float ChargeSeconds = .65f;

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
            p.Tick(ChargeSeconds / 2, ChargeSeconds);
            Assert.That(p.Charge, Is.EqualTo(.5f).Within(1e-4f));
            p.Tick(ChargeSeconds, ChargeSeconds);
            Assert.That(p.Charge, Is.EqualTo(1));
        }

        [Test]
        public void NothingChargesBeforeBeginOrAfterCancel()
        {
            var p = new ThrowPreparation();
            p.Tick(ChargeSeconds, ChargeSeconds);
            Assert.That(p.Active, Is.False);
            Assert.That(p.Charge, Is.Zero);
            p.Begin();
            p.Cancel();
            p.Tick(ChargeSeconds, ChargeSeconds);
            Assert.That(p.Charge, Is.Zero);
        }

        [Test]
        public void CancelAndBeginStartClean()
        {
            var p = Started();
            p.Tick(.3f, ChargeSeconds);
            p.Cancel();
            Assert.That(p.Active, Is.False);
            p.Begin();
            Assert.That(p.Active, Is.True);
            Assert.That(p.Charge, Is.Zero);
        }
    }
}
