using NUnit.Framework;

namespace Hakaton.Character.Tests
{
    /// <summary>
    /// Board rule for timed card effects (Moon gravity, Lightness, wet): a repeat copy refreshes the duration, never
    /// adds it up, never multiplies the effect, and a shorter copy never cuts the time that is left.
    /// </summary>
    public sealed class TimedModifierTests
    {
        [Test]
        public void InactiveModifierGivesTheNeutralValue()
        {
            var modifier = new TimedModifier();
            Assert.That(modifier.IsActive(0), Is.False);
            Assert.That(modifier.Current(5, 1), Is.EqualTo(1));
            modifier.Apply(.25f, 12, 0);
            Assert.That(modifier.Current(11.9f, 1), Is.EqualTo(.25f));
            Assert.That(modifier.Current(12, 1), Is.EqualTo(1), "Ends after its duration.");
            Assert.That(modifier.Remaining(4), Is.EqualTo(8).Within(1e-4f));
        }

        [Test]
        public void LongerRepeatRefreshesWithoutAddingUp()
        {
            var modifier = new TimedModifier();
            modifier.Apply(.25f, 12, 0);
            modifier.Apply(.25f, 45, 5);
            Assert.That(modifier.Until, Is.EqualTo(50).Within(1e-4f), "5 + 45, not 12 + 45.");
        }

        [Test]
        public void ShorterRepeatNeverCutsTheRemainingTime()
        {
            var modifier = new TimedModifier();
            modifier.Apply(.25f, 45, 0);
            modifier.Apply(.25f, 12, 10);
            Assert.That(modifier.Until, Is.EqualTo(45).Within(1e-4f));
            modifier.Apply(.25f, 12, 40);
            Assert.That(modifier.Until, Is.EqualTo(52).Within(1e-4f), "Late in the effect a fresh copy extends it.");
        }

        [Test]
        public void RepeatReplacesTheValueInsteadOfMultiplying()
        {
            var modifier = new TimedModifier();
            modifier.Apply(.25f, 12, 0);
            modifier.Apply(.25f, 12, 1);
            Assert.That(modifier.Current(2, 1), Is.EqualTo(.25f), "Two Moon copies are still ×0.25, not ×0.0625.");
        }

        [Test]
        public void AfterExpiryAFreshApplyStartsFromNow()
        {
            var modifier = new TimedModifier();
            modifier.Apply(1.5f, 6, 0);
            modifier.Apply(1.5f, 6, 20);
            Assert.That(modifier.Until, Is.EqualTo(26).Within(1e-4f));
            modifier.Clear();
            Assert.That(modifier.IsActive(21), Is.False);
            Assert.That(modifier.Current(21, 1), Is.EqualTo(1));
        }
    }
}
