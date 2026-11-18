using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Loot
{
    /// <summary>One row of a drop table: the item, its chance per roll and how many copies drop.</summary>
    [Serializable]
    public sealed class LootDrop
    {
        public LootItem item;
        [Tooltip("Шанс выпасть за один бросок, от 0 до 1 (0.25 = каждый четвёртый раз).")]
        [Range(0, 1)] public float chance = 1;
        [Tooltip("Сколько копий выпадает, если строка сработала: от и до включительно.")]
        [Min(1)] public int min = 1;
        [Min(1)] public int max = 1;

        public int MinCount => Mathf.Max(1, min);
        public int MaxCount => Mathf.Max(MinCount, max);
    }

    /// <summary>
    /// What a creature (or anything else) leaves behind. Every row is rolled on its own, so several items can drop
    /// at once. With <see cref="atLeastOne"/>, <see cref="minimumDrops"/> copies are picked with the chances as weights
    /// when ordinary rolls produced fewer. Copies may repeat; distinct item kinds are not required.
    /// </summary>
    [Serializable]
    public sealed class LootTable
    {
        [Tooltip("Если обычные броски дали меньше minimumDrops — добрать копии (строки с большим шансом выпадают чаще).")]
        public bool atLeastOne = true;
        [Tooltip("Минимум копий при включённом atLeastOne. 1 сохраняет прежнее поведение; копии могут повторяться.")]
        [Min(1)] public int minimumDrops = 1;
        public LootDrop[] entries = new LootDrop[0];

        public bool IsEmpty
        {
            get
            {
                if (entries != null) foreach (var entry in entries) if (entry != null && entry.item) return false;
                return true;
            }
        }

        /// <summary>Rolls the table with Unity's random and appends one item per dropped copy.</summary>
        public void Roll(List<LootItem> into) => Roll(() => UnityEngine.Random.value, into);

        /// <summary>Rolls the table; <paramref name="next"/> returns uniform values in [0, 1].</summary>
        public void Roll(Func<float> next, List<LootItem> into)
        {
            if (entries == null || into == null || next == null) return;
            int before = into.Count;
            foreach (var entry in entries)
            {
                if (entry == null || !entry.item || entry.chance <= 0) continue;
                if (entry.chance < 1 && next() >= entry.chance) continue;
                Add(entry, next, into);
            }
            int required = before + Mathf.Max(1, minimumDrops);
            if (!atLeastOne || into.Count >= required) return;
            float total = 0;
            int valid = 0;
            foreach (var entry in entries)
                if (entry != null && entry.item) { total += Mathf.Max(0, entry.chance); valid++; }
            if (valid == 0) return;
            while (into.Count < required)
            {
                float pick = next() * (total > 0 ? total : valid);
                LootDrop chosen = null;
                foreach (var entry in entries)
                {
                    if (entry == null || !entry.item) continue;
                    chosen = entry;
                    pick -= total > 0 ? Mathf.Max(0, entry.chance) : 1;
                    if (pick < 0) break;
                }
                Add(chosen, next, into);
            }
        }

        static void Add(LootDrop entry, Func<float> next, List<LootItem> into)
        {
            int spread = entry.MaxCount - entry.MinCount;
            int count = entry.MinCount + (spread > 0 ? Mathf.Min(spread, (int)(next() * (spread + 1))) : 0);
            for (int i = 0; i < count; i++) into.Add(entry.item);
        }
    }
}
