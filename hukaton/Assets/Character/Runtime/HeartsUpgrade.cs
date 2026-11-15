using System;
using System.Collections.Generic;

namespace Hakaton.Character
{
    /// <summary>
    /// «Сердца» on the PC: every bought step adds one whole heart to the outing maximum. A pure model like
    /// <see cref="StaminaUpgrade"/>: the PC pays from the shared wallet, the game save keeps <see cref="Level"/>, and the
    /// player's vitals use <see cref="HalfHearts"/> as their maximum from the next outing (or at once in the shop).
    /// </summary>
    public sealed class HeartsUpgrade
    {
        public const int HalfHeartsPerStep = 2;
        readonly int[] prices;

        /// <summary>Half hearts before any step (the expedition settings' maximum).</summary>
        public int BaseHalfHearts { get; }
        /// <summary>Steps bought so far (0: the base hearts).</summary>
        public int Level { get; private set; }
        /// <summary>Steps there are to buy in total.</summary>
        public int Steps => prices.Length;
        public bool Maxed => Level >= Steps;
        /// <summary>Price of the next step; 0 once maxed.</summary>
        public int Price => PriceAt(Level);
        /// <summary>The outing maximum now, in half hearts.</summary>
        public int HalfHearts => HalfHeartsAt(Level);
        /// <summary>Whole hearts now.</summary>
        public int Hearts => HalfHearts / 2;
        /// <summary>Whole hearts after the next step (the current ones once maxed).</summary>
        public int NextHearts => HalfHeartsAt(Math.Min(Level + 1, Steps)) / 2;
        /// <summary>The most hearts there can ever be (every step bought): the HUD needs this many slots.</summary>
        public int MaximumHearts => HalfHeartsAt(Steps) / 2;

        /// <summary>Raised when the level changes (a purchase, the cheat, a loaded save).</summary>
        public event Action Changed;

        public HeartsUpgrade(int baseHalfHearts, IReadOnlyList<int> prices)
        {
            if (baseHalfHearts < 2) throw new ArgumentOutOfRangeException(nameof(baseHalfHearts));
            if (prices == null) throw new ArgumentNullException(nameof(prices));
            this.prices = new int[prices.Count];
            for (int i = 0; i < prices.Count; i++)
            {
                if (prices[i] < 0) throw new ArgumentOutOfRangeException(nameof(prices), "Prices cannot be negative.");
                this.prices[i] = prices[i];
            }
            BaseHalfHearts = baseHalfHearts;
        }

        /// <summary>Price of the step from <paramref name="level"/> to the next one; 0 past the last step.</summary>
        public int PriceAt(int level) => level >= 0 && level < Steps ? prices[level] : 0;
        public int HalfHeartsAt(int level) => BaseHalfHearts + Clamp(level) * HalfHeartsPerStep;

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
    }
}
