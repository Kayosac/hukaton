using System;

namespace Hakaton.Character
{
    /// <summary>Frame-rate independent stamina budget, isolated for balance tests.</summary>
    public sealed class StaminaState
    {
        readonly float capacity, drain, recovery, delay, recoveryThreshold;
        float sinceSpend;
        public float Value { get; private set; }
        public float Normalized => Value / capacity;
        public bool Exhausted { get; private set; }
        public bool CanSprint => !Exhausted && Value > 0;

        public StaminaState(float capacity, float drain, float recovery, float delay, float recoveryThreshold)
        {
            if (!Finite(capacity) || capacity <= 0 || !Finite(drain) || drain < 0
                || !Finite(recovery) || recovery < 0 || !Finite(delay) || delay < 0
                || !Finite(recoveryThreshold) || recoveryThreshold <= 0 || recoveryThreshold > 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Invalid stamina tuning.");
            this.capacity = capacity; this.drain = drain; this.recovery = recovery;
            this.delay = delay; this.recoveryThreshold = recoveryThreshold;
            Value = capacity;
            sinceSpend = delay;
        }

        public bool TrySpend(float amount)
        {
            if (!Finite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (Exhausted || Value < amount) return false;
            Value = Math.Max(0, Value - amount);
            sinceSpend = 0;
            if (Value <= 0) Exhausted = true;
            return true;
        }

        public void Tick(float deltaTime, bool sprinting)
        {
            if (!Finite(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (sprinting && CanSprint)
            {
                Value = Math.Max(0, Value - drain * deltaTime);
                sinceSpend = 0;
                if (Value <= 0) Exhausted = true;
                return;
            }
            // Apply recovery only to the part of this frame after the delay elapsed.
            float previous = sinceSpend;
            sinceSpend += deltaTime;
            float recoveryTime = Math.Max(0, sinceSpend - delay) - Math.Max(0, previous - delay);
            Value = Math.Min(capacity, Value + recoveryTime * recovery);
            if (Exhausted && Value >= capacity * recoveryThreshold) Exhausted = false;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
