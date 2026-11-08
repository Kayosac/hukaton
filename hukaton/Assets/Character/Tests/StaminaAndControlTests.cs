using System;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Character.Tests
{
    public sealed class StaminaAndControlTests
    {
        static StaminaState Full() => new StaminaState(100, 20, 25, 1, .25f);

        [Test]
        public void RunningDepletesWithoutNegativeStaminaAndRequiresRecoveryThreshold()
        {
            var state = Full();
            state.Tick(6, true);
            Assert.That(state.Value, Is.Zero);
            Assert.That(state.Exhausted, Is.True);
            Assert.That(state.CanSprint, Is.False);
            Assert.That(state.TrySpend(1), Is.False);
            state.Tick(1.5f, false);
            Assert.That(state.Value, Is.EqualTo(12.5f).Within(.001f));
            Assert.That(state.CanSprint, Is.False);
            state.Tick(.5f, false);
            Assert.That(state.Value, Is.EqualTo(25).Within(.001f));
            Assert.That(state.CanSprint, Is.True);
        }

        [Test]
        public void JumpCostIsAtomicAndFailedJumpDoesNotResetRecoveryDelay()
        {
            var state = Full();
            Assert.That(state.TrySpend(95), Is.True);
            state.Tick(.8f, false);
            Assert.That(state.TrySpend(12), Is.False);
            Assert.That(state.Value, Is.EqualTo(5));
            state.Tick(.4f, false);
            Assert.That(state.Value, Is.EqualTo(10).Within(.001f));
        }

        [Test]
        public void RecoveryUsesOnlyTimeAfterDelayAndNeverExceedsCapacity()
        {
            var state = Full();
            state.TrySpend(50);
            state.Tick(.5f, false);
            Assert.That(state.Value, Is.EqualTo(50));
            state.Tick(.7f, false);
            Assert.That(state.Value, Is.EqualTo(55).Within(.001f));
            state.Tick(100, false);
            Assert.That(state.Value, Is.EqualTo(100));
        }

        [Test]
        public void SameElapsedTimeProducesSameBudgetAcrossFrameRates()
        {
            var slow = Full(); var fast = Full();
            for (int i = 0; i < 60; i++) slow.Tick(1f / 30, true);
            for (int i = 0; i < 240; i++) fast.Tick(1f / 120, true);
            Assert.That(slow.Value, Is.EqualTo(fast.Value).Within(.005f));
            for (int i = 0; i < 90; i++) slow.Tick(1f / 30, false);
            for (int i = 0; i < 360; i++) fast.Tick(1f / 120, false);
            Assert.That(slow.Value, Is.EqualTo(fast.Value).Within(.005f));
        }

        [Test]
        public void InvalidTimeCannotCreateOrRemoveStamina()
        {
            var state = Full();
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Tick(float.NaN, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Tick(-1, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.TrySpend(-10));
            Assert.That(state.Value, Is.EqualTo(100));
        }

        [Test]
        public void IndependentModalOwnersCannotReleaseOneAnother()
        {
            var previousLock = Cursor.lockState;
            bool previousVisible = Cursor.visible;
            var player = new GameObject("Control test player");
            var first = new GameObject("First modal owner");
            var second = new GameObject("Second modal owner");
            try
            {
                var gate = player.AddComponent<PlayerControlGate>();
                gate.SetBlocked(first, true);
                gate.SetBlocked(first, true);
                gate.SetBlocked(second, true);
                gate.SetBlocked(first, false);
                Assert.That(gate.HasModalOwner, Is.True);
                Assert.That(gate.GameplayActive, Is.False);
                gate.SetBlocked(first, false);
                Assert.That(gate.HasModalOwner, Is.True);
                gate.SetBlocked(second, false);
                Assert.That(gate.HasModalOwner, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                Cursor.lockState = previousLock;
                Cursor.visible = previousVisible;
            }
        }
    }
}
