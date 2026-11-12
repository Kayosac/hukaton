using System;
using System.Collections.Generic;

namespace Hakaton.Character
{
    /// <summary>
    /// «Выносливость» on the PC: every bought step adds the same seconds of sprint, which is one more pip in the arc under
    /// the crosshair. A pure model: the PC pays from the shared wallet, the game save keeps <see cref="Level"/>, and
    /// <see cref="PlayerStamina"/> resizes its budget whenever it <see cref="Changed"/>.
    /// </summary>
    public sealed class StaminaUpgrade
    {
        readonly int[] prices;

        /// <summary>Stamina before any step (the player prefab's capacity).</summary>
        public float BaseCapacity { get; }
        /// <summary>Stamina a second of sprint costs.</summary>
        public float DrainPerSecond { get; }
        /// <summary>Sprint seconds one step adds.</summary>
        public float SecondsPerStep { get; }
        /// <summary>Steps bought so far (0: the base stamina).</summary>
        public int Level { get; private set; }
        /// <summary>Steps there are to buy in total.</summary>
        public int Steps => prices.Length;
        public bool Maxed => Level >= Steps;
        /// <summary>Price of the next step; 0 once maxed.</summary>
        public int Price => PriceAt(Level);
        /// <summary>The stamina maximum now.</summary>
        public float Capacity => CapacityAt(Level);
        /// <summary>Seconds of sprint from full to empty now.</summary>
        public float Seconds => SecondsAt(Level);
        /// <summary>Seconds of sprint after the next step (the current ones once maxed).</summary>
        public float NextSeconds => SecondsAt(Math.Min(Level + 1, Steps));
        /// <summary>Stamina one step adds; also the stamina of one pip.</summary>
        public float StepCapacity => DrainPerSecond * SecondsPerStep;

        /// <summary>Raised when the level changes (a purchase, the cheat, a loaded save).</summary>
        public event Action Changed;

        public StaminaUpgrade(float baseCapacity, float drainPerSecond, float secondsPerStep, IReadOnlyList<int> prices)
        {
            if (!Positive(baseCapacity)) throw new ArgumentOutOfRangeException(nameof(baseCapacity));
            if (!Positive(drainPerSecond)) throw new ArgumentOutOfRangeException(nameof(drainPerSecond));
            if (!Positive(secondsPerStep)) throw new ArgumentOutOfRangeException(nameof(secondsPerStep));
            if (prices == null) throw new ArgumentNullException(nameof(prices));
            this.prices = new int[prices.Count];
            for (int i = 0; i < prices.Count; i++)
            {
                if (prices[i] < 0) throw new ArgumentOutOfRangeException(nameof(prices), "Prices cannot be negative.");
                this.prices[i] = prices[i];
            }
            BaseCapacity = baseCapacity;
            DrainPerSecond = drainPerSecond;
            SecondsPerStep = secondsPerStep;
        }

        /// <summary>Price of the step from <paramref name="level"/> to the next one; 0 past the last step.</summary>
        public int PriceAt(int level) => level >= 0 && level < Steps ? prices[level] : 0;
        public float CapacityAt(int level) => BaseCapacity + Clamp(level) * StepCapacity;
        public float SecondsAt(int level) => CapacityAt(level) / DrainPerSecond;

        /// <summary>Takes the next step (the caller has already paid for it). False once maxed.</summary>
        public bool Raise()
        {
            if (Maxed) return false;
            Level++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Debug/cheat helper: every step for free.</summary>
        public bool Maximize()
        {
            if (Maxed) return false;
            Level = Steps;
            Changed?.Invoke();
            return true;
        }

        /// <summary>A saved level, clamped to the steps there are now (prices may have been retuned since).</summary>
        public void Restore(int level)
        {
            level = Clamp(level);
            if (level == Level) return;
            Level = level;
            Changed?.Invoke();
        }

        int Clamp(int level) => Math.Max(0, Math.Min(level, Steps));
        static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
    }
}
