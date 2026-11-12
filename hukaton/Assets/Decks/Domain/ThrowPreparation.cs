using System;

namespace Hakaton.Decks
{
    /// <summary>A single release attempt. Charging controls range; it never changes card power.</summary>
    public sealed class ThrowPreparation
    {
        public bool Active { get; private set; }
        public float Charge { get; private set; }

        public void Begin()
        {
            Cancel();
            Active = true;
        }

        public void Tick(float seconds, float chargeSeconds)
        {
            if (!Active) return;
            Charge = Math.Min(1, Charge + Math.Max(0, seconds) / Math.Max(.01f, chargeSeconds));
        }

        public void Cancel()
        {
            Active = false;
            Charge = 0;
        }
    }
}
