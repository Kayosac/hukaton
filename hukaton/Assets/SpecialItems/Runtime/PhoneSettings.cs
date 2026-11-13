using System;
using UnityEngine;

namespace Hakaton.SpecialItems
{
    /// <summary>What a spin of the phone's one-armed bandit can bring.</summary>
    public enum PhoneOutcome
    {
        /// <summary>No two symbols alike.</summary>
        Nothing,
        /// <summary>Two alike: the bet comes back.</summary>
        Pair,
        /// <summary>Three coins.</summary>
        Coins,
        /// <summary>Three cards: a random one-use card straight into the hand.</summary>
        Card,
        /// <summary>Three hearts: full health.</summary>
        Heal,
        /// <summary>Three lightnings: a short boost (endless stamina or a fast run).</summary>
        Boost,
        /// <summary>Three goblins: the goblin calls and says something.</summary>
        Call,
        /// <summary>Three skulls: the hearts drop to one at once.</summary>
        Curse,
        /// <summary>Three PENUS: the jackpot.</summary>
        Jackpot
    }

    /// <summary>The reel symbols in the order of the drawn reel strip (top to bottom).</summary>
    public enum PhoneSymbol { Coin, Card, Heart, Lightning, Goblin, Skull, Penus }

    [Serializable]
    public struct PhoneOutcomeChance
    {
        public PhoneOutcome outcome;
        [Tooltip("Chance of this outcome per spin, percent. The table is normalised, so it need not add up to exactly 100.")]
        [Min(0)] public float percent;
        public PhoneOutcomeChance(PhoneOutcome outcome, float percent) { this.outcome = outcome; this.percent = percent; }
    }

    [Serializable]
    public struct PhoneBatteryStep
    {
        [Tooltip("Spins per outing after this step.")]
        [Min(1)] public int spins;
        [Min(0)] public int price;
        public PhoneBatteryStep(int spins, int price) { this.spins = spins; this.price = price; }
    }

    /// <summary>
    /// The phone's numbers (board «Спецпредметы», frame «Телефон»): the bet, the battery and its PC steps, the chances of
    /// every outcome and what each brings. Tuned here, read by the phone on outings and by the PC's «Прокачка».
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Special items/Phone settings")]
    public sealed class PhoneSettings : ScriptableObject
    {
        [Header("Bet and battery")]
        [Tooltip("Coins a spin costs: from the outing's purse first, then from the shop wallet.")]
        [Min(0)] public int bet = 10;
        [Tooltip("Spins per outing before any battery step. The battery is full at the start of every outing.")]
        [Min(1)] public int baseSpins = 10;
        [Tooltip("«Батарея телефона» on the PC, in order.")]
        public PhoneBatteryStep[] batterySteps =
        {
            new PhoneBatteryStep(15, 1000), new PhoneBatteryStep(20, 2000), new PhoneBatteryStep(30, 4000)
        };

        [Header("Chances")]
        public PhoneOutcomeChance[] chances =
        {
            new PhoneOutcomeChance(PhoneOutcome.Nothing, 45),
            new PhoneOutcomeChance(PhoneOutcome.Pair, 25),
            new PhoneOutcomeChance(PhoneOutcome.Coins, 8),
            new PhoneOutcomeChance(PhoneOutcome.Card, 7),
            new PhoneOutcomeChance(PhoneOutcome.Heal, 4),
            new PhoneOutcomeChance(PhoneOutcome.Boost, 4),
            new PhoneOutcomeChance(PhoneOutcome.Call, 3),
            new PhoneOutcomeChance(PhoneOutcome.Curse, 3.5f),
            new PhoneOutcomeChance(PhoneOutcome.Jackpot, .5f)
        };

        [Header("Prizes")]
        [Min(0)] public int pairCoins = 10;
        [Min(0)] public int coinsPrize = 60;
        [Min(0)] public int jackpotCoins = 500;
        [Tooltip("Boost: seconds of endless stamina or of the fast run (one of the two, at random).")]
        [Min(1)] public float boostSeconds = 30;
        [Min(1)] public float fastRunMultiplier = 1.4f;
        [Tooltip("Curse: the hearts drop to this many halves at once (2 = one heart). Never kills.")]
        [Min(1)] public int curseKeepHalfHearts = 2;

        [Header("Timing")]
        [Tooltip("The reels stop by themselves this long after the lever if Tab is not pressed again.")]
        [Min(.2f)] public float autoStopSeconds = 3;
        [Tooltip("Seconds between the reels stopping, left to right.")]
        [Min(0)] public float reelStopStep = .15f;
        [Tooltip("How long a money win (and the jackpot) stays on the screen.")]
        [Min(.2f)] public float winSeconds = 1.6f;
        [Tooltip("How long the goblin's call lasts at least (it waits for his line to be typed).")]
        [Min(.5f)] public float callSeconds = 4;

        [Header("The goblin's call")]
        [TextArea] public string[] callLines =
        {
            "Алло, партнёр? Просто проверяю, живой ли. Живой. Ладно.",
            "Если увидишь сундук, который дышит, — не открывай. Или открывай. Мне-то что.",
            "Глубокую воду лучше заморозить. Это бесплатный совет. Первый и последний.",
            "Ты опять крутишь автомат? Я тоже. Мы с тобой похожи.",
            "Карты бросай, а не держи. Они от этого не дорожают.",
            "Слышал, в лесу кто-то разговаривает. Это был не я.",
            "Звоню сказать, что звонок платный. Шучу. Или нет."
        };

        /// <summary>Spins per outing with <paramref name="level"/> battery steps bought.</summary>
        public int SpinsAt(int level)
        {
            if (batterySteps == null || level <= 0) return Mathf.Max(1, baseSpins);
            int index = Mathf.Min(level, batterySteps.Length) - 1;
            return Mathf.Max(1, batterySteps[index].spins);
        }

        public int BatterySteps => batterySteps == null ? 0 : batterySteps.Length;

        /// <summary>Price of the step after <paramref name="level"/>; 0 once maxed.</summary>
        public int BatteryPriceAt(int level) => batterySteps != null && level >= 0 && level < batterySteps.Length ? batterySteps[level].price : 0;
    }
}
