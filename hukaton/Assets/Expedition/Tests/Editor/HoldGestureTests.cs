using NUnit.Framework;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Hold to confirm: R discards the hand after 0.5 s, LMB on «Пора домой» goes home after 0.8 s.</summary>
    public sealed class HoldGestureTests
    {
        const float Discard = .5f, Return = .8f, Frame = 1 / 60f;

        /// <summary>Holds the button for <paramref name="seconds"/> after the press, frame by frame; true if it completed.</summary>
        static bool Hold(HoldGesture hold, float seconds, float needed, bool canProgress = true)
        {
            bool done = hold.Tick(true, true, Frame, needed, canProgress);
            for (float t = 0; t + Frame * .5f < seconds && !done; t += Frame) done = hold.Tick(false, true, Frame, needed, canProgress);
            return done;
        }

        [Test]
        public void AShortPressDoesNothing()
        {
            var hold = new HoldGesture();
            Assert.That(Hold(hold, .3f, Discard), Is.False);
            Assert.That(hold.Active, Is.True);
            Assert.That(hold.Progress, Is.EqualTo(.6f).Within(.05f));
            Assert.That(hold.Tick(false, false, Frame, Discard), Is.False, "Let go before the end.");
            Assert.That(hold.Active, Is.False);
            Assert.That(hold.Progress, Is.Zero, "The ring is gone.");
        }

        [Test]
        public void HoldingTheWholeTimeCompletesOnce()
        {
            var hold = new HoldGesture();
            Assert.That(Hold(hold, Discard, Discard), Is.True);
            Assert.That(hold.Active, Is.False);
            for (int i = 0; i < 120; i++)
                Assert.That(hold.Tick(false, true, Frame, Discard), Is.False, "Still held after it fired: the next one needs a new press.");
            Assert.That(Hold(hold, Discard, Discard), Is.True, "A new press — a new hold.");
        }

        [Test]
        public void ProgressFillsFromEmptyToFull()
        {
            var hold = new HoldGesture();
            Assert.That(hold.Tick(true, true, Frame, Return), Is.False);
            Assert.That(hold.Progress, Is.Zero, "The press itself starts from empty.");
            Assert.That(hold.Tick(false, true, Return / 2, Return), Is.False);
            Assert.That(hold.Progress, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(hold.Tick(false, true, Return / 2, Return), Is.True);
        }

        [Test]
        public void AButtonDownBeforeTheHoldBecamePossibleNeverCounts()
        {
            var hold = new HoldGesture();
            // Held from the last throw: never a fresh press, however long.
            for (int i = 0; i < 200; i++) Assert.That(hold.Tick(false, true, Frame, Return), Is.False);
            Assert.That(hold.Active, Is.False);
            Assert.That(hold.Tick(false, false, Frame, Return), Is.False, "Let go…");
            Assert.That(Hold(hold, Return, Return), Is.True, "…and pressed again: now it counts.");
        }

        [Test]
        public void APressAndReleaseInOneFrameDoesNotStartAHold()
        {
            var hold = new HoldGesture();
            Assert.That(hold.Tick(true, false, Frame, Discard), Is.False);
            Assert.That(hold.Active, Is.False);
        }

        [Test]
        public void TheHoldWaitsWhileTheActionIsNotPossible()
        {
            var hold = new HoldGesture();
            // Pressed during the discard cooldown: nothing counts until it is over.
            Assert.That(Hold(hold, 1, Discard, false), Is.False);
            Assert.That(hold.Active, Is.True);
            Assert.That(hold.Progress, Is.Zero);
            bool done = false;
            for (float t = 0; t + Frame * .5f < Discard && !done; t += Frame) done = hold.Tick(false, true, Frame, Discard);
            Assert.That(done, Is.True, "Then the full hold time, still without a new press.");
        }

        [Test]
        public void CancelNeedsANewPress()
        {
            var hold = new HoldGesture();
            Assert.That(Hold(hold, .4f, Return), Is.False);
            hold.Cancel();
            Assert.That((hold.Active, hold.Progress), Is.EqualTo((false, 0f)));
            Assert.That(hold.Tick(false, true, 1, Return), Is.False, "Still down after the cancel: it does not count.");
            Assert.That(Hold(hold, Return, Return), Is.True, "Only a fresh press counts again.");
        }

        [Test]
        public void SettingsHoldTimesAreTheTunedOnes()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<ExpeditionSettings>("Assets/Game/Data/Expedition.asset");
            Assert.That(settings.discardHold, Is.EqualTo(Discard));
            Assert.That(settings.returnHold, Is.EqualTo(Return));
            Assert.That(settings.discardCooldown, Is.EqualTo(.65f));
        }
    }
}
