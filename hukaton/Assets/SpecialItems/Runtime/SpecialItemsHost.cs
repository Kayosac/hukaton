using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// The scene's special items: the catalogue, the phone's numbers and what the player has (<see cref="Inventory"/>).
    /// The game loop tells it which unlocks are open (<see cref="UnlockCheck"/>) and saves the inventory; the goblin
    /// dealer sells from it, the PC improves the phone's battery, the outing's Q takes the items out.
    /// </summary>
    public sealed class SpecialItemsHost : MonoBehaviour
    {
        public SpecialItemCatalog catalog;
        public PhoneSettings phone;

        public SpecialItemInventory Inventory { get; } = new SpecialItemInventory();
        /// <summary>Is this unlock open now? Set by the game loop (the first goal: the shop's revenue). Unset: only «Always».</summary>
        public Func<SpecialItemUnlock, bool> UnlockCheck { get; set; }
        /// <summary>Raised when a purchase happens (the dealer, a cheat): the save and the Q menu follow.</summary>
        public event Action<SpecialItemDefinition> Bought;

        public bool IsUnlocked(SpecialItemDefinition item)
        {
            if (!item) return false;
            if (item.unlock == SpecialItemUnlock.Always) return true;
            return UnlockCheck != null && UnlockCheck(item.unlock);
        }

        /// <summary>Lies on the dealer's table: open and not bought yet.</summary>
        public bool OnSale(SpecialItemDefinition item) => item && IsUnlocked(item) && !Inventory.Owns(item.id);

        public bool Owns(SpecialItemDefinition item) => item && Inventory.Owns(item.id);

        public SpecialItemDefinition Find(string id) => catalog ? catalog.Find(id) : null;

        /// <summary>The bought items in catalogue order (the Q menu's order).</summary>
        public List<SpecialItemDefinition> OwnedItems()
        {
            var list = new List<SpecialItemDefinition>();
            if (!catalog) return list;
            foreach (var item in catalog.items) if (item && Inventory.Owns(item.id)) list.Add(item);
            return list;
        }

        /// <summary>The item is the player's (the caller has taken the money).</summary>
        public bool Grant(SpecialItemDefinition item)
        {
            if (!item || !Inventory.Add(item.id)) return false;
            Bought?.Invoke(item);
            return true;
        }

        /// <summary>A saved state (null: nothing bought yet).</summary>
        public void Restore(SpecialItemsSave save) => Inventory.Restore(save, id => Find(id) != null);
    }
}
