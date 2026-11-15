using System;
using System.Collections.Generic;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>
    /// Pictures for «Склад»: one 32 px pixel icon per catalogue item, all baked by one pipeline so the list looks of a
    /// piece — from the shelf model for 3D items, from <see cref="LootItem.icon"/> for drawings (same fit, premultiplied
    /// averaging, hard alpha, 1 px dark contour). Listed here by item id (Assets/Computer/Art/Stock, baker:
    /// output/pc-computer/bake_stock_icons.cs). The item's own drawing is only a fallback for an item not baked yet.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/ПК/Значки склада", fileName = "PcStockIcons")]
    public sealed class PcStockIcons : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public Sprite icon;
        }

        [Tooltip("Запечённые значки склада для всех предметов каталога: id предмета → спрайт.")]
        public List<Entry> baked = new List<Entry>();

        public Sprite Baked(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var entry in baked) if (entry != null && entry.id == id) return entry.icon;
            return null;
        }

        /// <summary>The baked icon, else (not baked yet) the item's own drawing, else null.</summary>
        public Sprite For(LootItem item)
        {
            if (!item) return null;
            var baked = Baked(item.id);
            return baked ? baked : item.icon;
        }
    }
}
