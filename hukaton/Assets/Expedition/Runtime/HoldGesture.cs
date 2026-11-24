using System;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Hold to confirm (R discards the hand, LMB on «Пора домой» goes home): a fresh press starts it, keeping the button
    /// down for the given time completes it once, letting go earlier does nothing. A button that was already down before
    /// does not count — only a press passed to <see cref="Tick"/> starts a hold, and after a hold completes the next one
    /// needs a new press too. Plain C#: the thrower feeds it every frame, the HUD reads <see cref="Progress"/>.
    /// </summary>
    public sealed class HoldGesture
    {
        // Frame times add up with rounding: a hold this close to the end counts as complete.
        const float Tolerance = 1e-4f;

        /// <summary>The button went down with a fresh press and is still held; the hold has not completed yet.</summary>
        public bool Active { get; private set; }
        /// <summary>Seconds held so far (only while the action could go ahead).</summary>
        public float Elapsed { get; private set; }
        /// <summary>0 at the press, 1 when the hold completes; 0 when not active.</summary>
        public float Progress { get; private set; }

        /// <param name="pressedThisFrame">The button went down this frame (a fresh press that may start a hold).</param>
        /// <param name="isDown">The button is down now.</param>
        /// <param name="deltaTime">Seconds since the last call.</param>
        /// <param name="seconds">How long to hold.</param>
        /// <param name="canProgress">False while the action is not possible yet (a cooldown): the hold waits, it does not count.</param>
        /// <returns>True on the one call where the hold completes.</returns>
        public bool Tick(bool pressedThisFrame, bool isDown, float deltaTime, float seconds, bool canProgress = true)
        {
            if (!isDown) { Cancel(); return false; }
            if (pressedThisFrame)
            {
                // The press itself starts the count; the time before it in this frame is not held.
                Active = true;
                Elapsed = 0;
                Progress = 0;
                if (seconds > Tolerance || !canProgress) return false;
                Cancel();
                return true;
            }
            if (!Active || !canProgress) return false;
            Elapsed += Math.Max(0, deltaTime);
            Progress = seconds <= Tolerance ? 1 : Math.Min(1, Elapsed / seconds);
            if (Elapsed + Tolerance < seconds) return false;
            Cancel();
            return true;
        }

        public void Cancel()
        {
            Active = false;
            Elapsed = 0;
            Progress = 0;
        }
    }
}
