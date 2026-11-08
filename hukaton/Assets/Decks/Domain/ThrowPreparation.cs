using System;

namespace Hakaton.Decks
{
    /// <summary>A single release attempt. Charging controls range; it never changes card power.</summary>
    public sealed class ThrowPreparation
    {
        public bool Active { get; private set; }
        public float Charge { get; private set; }
        public float Spin { get; private set; }
        public bool GestureUsed { get; private set; }
        float gesture, quiet;
        int direction;
        public void Begin()
        {
            Cancel();
            Active = true;
        }

        public void Tick(float seconds, bool modifier, float horizontalDelta, float chargeSeconds, float gesturePixels)
        {
            if (!Active) return;
            if (!modifier)
            {
                if (direction != 0) GestureUsed = true;
                Charge = Math.Min(1, Charge + Math.Max(0, seconds) / Math.Max(.01f, chargeSeconds));
                return;
            }
            if (GestureUsed) return;
            if (Math.Abs(horizontalDelta) > 1)
            {
                int sign = Math.Sign(horizontalDelta);
                if (direction != 0 && sign != direction)
                {
                    GestureUsed = true;
                    return;
                }
                direction = sign;
                quiet = 0;
                gesture += horizontalDelta;
                Spin = Math.Max(-1, Math.Min(1, gesture / Math.Max(1, gesturePixels)));
                if (Math.Abs(Spin) >= 1) GestureUsed = true;
            }
            else if (direction != 0)
            {
                quiet += Math.Max(0, seconds);
                if (quiet >= .14f) GestureUsed = true;
            }
        }

        public void Cancel()
        {
            Active = false;
            Charge = Spin = gesture = quiet = 0;
            direction = 0;
            GestureUsed = false;
        }
    }
}
