using UnityEngine;

namespace Hakaton.Character
{
    /// <summary>
    /// A timed value — a multiplier or a limit put on by a card (Moon gravity, Lightness, tar slowdown).
    /// The board's rule for repeated copies: applying again while active refreshes the timer to the LONGER of what
    /// is left and the new duration. Durations never add up, values never multiply, a shorter copy never cuts the
    /// remaining time. The latest value replaces the previous one. Time is passed in, so the rule is testable.
    /// </summary>
    public struct TimedModifier
    {
        /// <summary>The value set by the latest <see cref="Apply"/>.</summary>
        public float Value { get; private set; }
        /// <summary>Time (same clock as the <c>now</c> arguments) when the modifier ends.</summary>
        public float Until { get; private set; }

        public bool IsActive(float now) => now < Until;

        /// <summary>The value while active, otherwise <paramref name="neutral"/> (1 for a multiplier, ∞ for a limit).</summary>
        public float Current(float now, float neutral) => IsActive(now) ? Value : neutral;

        public float Remaining(float now) => Mathf.Max(0, Until - now);

        /// <summary>Starts or refreshes the modifier: ends at max(current end, now + seconds).</summary>
        public void Apply(float value, float seconds, float now)
        {
            Value = value;
            Until = Mathf.Max(IsActive(now) ? Until : now, now + Mathf.Max(0, seconds));
        }

        public void Clear()
        {
            Value = 0;
            Until = float.NegativeInfinity;
        }
    }
}
