using UnityEngine;

namespace Hakaton.Loot
{
    /// <summary>
    /// How rare a find is. Rarity is presentation only: the colour of the glint over the item and of its pickup row
    /// (colours live in <see cref="LootCatalog"/>). The price is set per item.
    /// </summary>
    public enum LootRarity
    {
        [InspectorName("Обычный")] Common,
        [InspectorName("Необычный")] Uncommon,
        [InspectorName("Редкий")] Rare,
        [InspectorName("Легендарный")] Legendary
    }
}
