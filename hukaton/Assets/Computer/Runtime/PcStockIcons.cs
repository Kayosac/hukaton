using System;
using System.Collections.Generic;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>
    /// Pictures for «Склад»: an item with its own <see cref="LootItem.icon"/> shows it; a 3D-only item (the curios, the
    /// forest finds) shows a pixel icon baked from its shelf model, listed here by item id
    /// (Assets/Computer/Art/Stock, baker: output/pc-computer/bake_stock_icons.cs).
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

        [Tooltip("Запечённые значки предметов без своего рисунка: id предмета → спрайт.")]
        public List<Entry> baked = new List<Entry>();

        public Sprite Baked(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var entry in baked) if (entry != null && entry.id == id) return entry.icon;
            return null;
        }

        /// <summary>The item's own drawing, else its baked icon, else null.</summary>
        public Sprite For(LootItem item)
        {
            if (!item) return null;
            return item.icon ? item.icon : Baked(item.id);
        }
    }
}
