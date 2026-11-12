using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Gilding's arithmetic, apart from Unity objects (EditMode tests).</summary>
    public static class GildingRules
    {
        /// <summary>
        /// The rank a chest of <paramref name="rank"/> takes after <paramref name="steps"/> steps up: never above
        /// <paramref name="top"/> (the richest tier) and never below where it was.
        /// </summary>
        public static int NextRank(int rank, int steps, int top) => Mathf.Max(rank, Mathf.Min(top, rank + Mathf.Max(0, steps)));

        /// <summary>The chest's coin multiplier after gilding: the larger of what it had and what this level gives (no stacking).</summary>
        public static float CoinMultiplier(float current, float given) => Mathf.Max(Mathf.Max(1, current), given);

        /// <summary>Gilding changes something: a richer tier or more coins than the chest already pays.</summary>
        public static bool Changes(int rank, int next, float currentCoins, float givenCoins)
            => next > rank || CoinMultiplier(currentCoins, givenCoins) > Mathf.Max(1, currentCoins) + 1e-4f;
    }
}
