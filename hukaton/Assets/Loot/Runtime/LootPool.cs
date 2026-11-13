using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Loot
{
    /// <summary>
    /// Items a find can turn out to be, with weights. A find with a pool (<c>WorldLoot.pool</c>) picks one of them:
    /// on a ready-made location anew every outing, in the endless world once per chunk spot (the world seed decides,
    /// so a reloaded chunk shows the same item). Create → Hakaton → Лут → Пул находок.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Лут/Пул находок", fileName = "new_pool", order = 2)]
    public sealed class LootPool : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public LootItem item;
            [Tooltip("Вес: 2 выпадает вдвое чаще, чем 1. 0 — не выпадает.")]
            [Min(0)] public float weight = 1;
        }

        [Tooltip("Что может оказаться на месте находки.")]
        public List<Entry> entries = new List<Entry>();

        float Total
        {
            get
            {
                float sum = 0;
                foreach (var e in entries) if (e != null && e.item && e.weight > 0) sum += e.weight;
                return sum;
            }
        }

        public bool IsEmpty => Total <= 0;

        /// <summary>Weighted pick; <paramref name="roll"/> is uniform in [0, 1). Null when the pool is empty.</summary>
        public LootItem Pick(double roll)
        {
            float total = Total;
            if (total <= 0) return null;
            double target = Math.Max(0, Math.Min(roll, .999999)) * total;
            LootItem last = null;
            foreach (var e in entries)
            {
                if (e == null || !e.item || e.weight <= 0) continue;
                last = e.item;
                target -= e.weight;
                if (target < 0) return e.item;
            }
            return last;
        }

        public LootItem Pick(System.Random random) => random == null ? Pick(UnityEngine.Random.value) : Pick(random.NextDouble());

        /// <summary>The chance of every item, for tooling and tests.</summary>
        public IEnumerable<(LootItem item, float chance)> Chances()
        {
            float total = Total;
            if (total <= 0) yield break;
            foreach (var e in entries)
                if (e != null && e.item && e.weight > 0) yield return (e.item, e.weight / total);
        }
    }
}
