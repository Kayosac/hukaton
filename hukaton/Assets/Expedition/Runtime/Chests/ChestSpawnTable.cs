using System;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Which chest turns up at a spot: the chance there is one at all, then a weighted pick of the tier.
    /// The Old Apiary's chest sites and each ring of the endless world have one (Game/Data/Chests).
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Chest spawn table")]
    public sealed class ChestSpawnTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public TreasureChest prefab;
            [Tooltip("Relative weight: twice the weight, twice as often.")]
            [Min(0)] public float weight;
        }

        [Tooltip("Chance that a spot has a chest at all, 0..1.")]
        [Range(0, 1)] public float chance = .5f;
        public Entry[] entries = new Entry[0];

        /// <summary>The chest for a uniform roll in [0, 1); null when nothing has weight.</summary>
        public TreasureChest Pick(double roll)
        {
            float total = 0;
            foreach (var e in entries) if (e.prefab && e.weight > 0) total += e.weight;
            if (total <= 0) return null;
            double pick = Math.Max(0, Math.Min(.999999, roll)) * total;
            TreasureChest last = null;
            foreach (var e in entries)
            {
                if (!e.prefab || e.weight <= 0) continue;
                last = e.prefab;
                pick -= e.weight;
                if (pick < 0) return e.prefab;
            }
            return last;
        }

        /// <summary>Rolls the chance with <paramref name="presence"/> and the tier with <paramref name="tierRoll"/>.</summary>
        public TreasureChest Roll(double presence, double tierRoll) => presence < chance ? Pick(tierRoll) : null;

        /// <summary>Share of spots that get this prefab (for the Inspector and tests).</summary>
        public float Probability(TreasureChest prefab)
        {
            float total = 0, mine = 0;
            foreach (var e in entries)
            {
                if (!e.prefab || e.weight <= 0) continue;
                total += e.weight;
                if (e.prefab == prefab) mine += e.weight;
            }
            return total > 0 ? chance * mine / total : 0;
        }
    }
}
