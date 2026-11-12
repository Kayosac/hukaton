using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Plain rules of «Мародёр» (<see cref="MarauderEffect"/>, <see cref="MarauderMark"/>), for EditMode tests.</summary>
    public static class MarauderRules
    {
        /// <summary>A new hit renews the mark: it ends at the later of when it would end and now + the new duration.</summary>
        public static float Expiry(float currentExpiry, float now, float seconds) => Mathf.Max(currentExpiry, now + Mathf.Max(0, seconds));

        /// <summary>A second copy never lowers the number of rolls a mark gives.</summary>
        public static int Rolls(int current, int fresh) => Mathf.Max(1, Mathf.Max(current, fresh));

        /// <summary>Whether a creature that died at <paramref name="deathTime"/> was still marked.</summary>
        public static bool DiedMarked(float deathTime, float expiry) => deathTime < expiry;

        /// <summary>Rolls of the drop table on death: the mark's while it holds, otherwise the usual one.</summary>
        public static int LootRolls(bool marked, int markRolls) => marked ? Mathf.Max(1, markRolls) : 1;
    }
}
