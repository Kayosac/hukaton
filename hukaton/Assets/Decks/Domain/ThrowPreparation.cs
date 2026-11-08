using System;

namespace Hakaton.Decks
{
    /// <summary>
    /// A single release attempt. Charging controls range; it never changes card power.
    /// While the modifier is held the charge pauses and mouse motion steers the spin like a small stick:
    /// any direction (x — right, y — up), clamped to full strength. Releasing the modifier keeps the spin;
    /// holding it again keeps steering from there.
    /// </summary>
    public sealed class ThrowPreparation
    {
        public bool Active { get; private set; }
        public float Charge { get; private set; }

        /// <summary>Sideways spin, −1 (left) … 1 (right).</summary>
        public float SpinX { get; private set; }

        /// <summary>Vertical spin, −1 (dips) … 1 (floats).</summary>
        public float SpinY { get; private set; }

        /// <summary>Strength of the spin, 0 … 1.</summary>
        public float SpinAmount => (float)Math.Sqrt(SpinX * SpinX + SpinY * SpinY);

        /// <summary>True while the modifier is held this preparation (the player is steering the spin).</summary>
        public bool Steering { get; private set; }

        public void Begin()
        {
            Cancel();
            Active = true;
        }

        /// <param name="deltaX">Mouse motion this frame, pixels, right positive.</param>
        /// <param name="deltaY">Mouse motion this frame, pixels, up positive.</param>
        /// <param name="gesturePixels">Mouse travel for a full-strength spin.</param>
        public void Tick(float seconds, bool modifier, float deltaX, float deltaY, float chargeSeconds, float gesturePixels)
        {
            if (!Active) return;
            Steering = modifier;
            if (!modifier)
            {
                Charge = Math.Min(1, Charge + Math.Max(0, seconds) / Math.Max(.01f, chargeSeconds));
                return;
            }
            float scale = 1f / Math.Max(1, gesturePixels);
            SpinX += deltaX * scale;
            SpinY += deltaY * scale;
            float amount = SpinAmount;
            if (amount > 1)
            {
                SpinX /= amount;
                SpinY /= amount;
            }
        }

        public void Cancel()
        {
            Active = Steering = false;
            Charge = SpinX = SpinY = 0;
        }
    }
}
