using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.SpecialItems
{
    /// <summary>Every special item the game knows, in the order the Q menu lays them out.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Special items/Catalog")]
    public sealed class SpecialItemCatalog : ScriptableObject
    {
        public SpecialItemDefinition[] items = new SpecialItemDefinition[0];

        public IReadOnlyList<SpecialItemDefinition> Items => items;

        public SpecialItemDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var item in items) if (item && item.id == id) return item;
            return null;
        }
    }
}
