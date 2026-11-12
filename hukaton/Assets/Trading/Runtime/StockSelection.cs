using System.Collections.Generic;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// Which stock is in the player's hands. The hand holds a kind of goods, not one copy: placing keeps the
    /// same kind while copies remain, and the wheel steps between kinds in the order they first appear in stock.
    /// </summary>
    public static class StockSelection
    {
        /// <summary>
        /// The next item in hand after the one at <paramref name="placedIndex"/> (of kind <paramref name="placedId"/>)
        /// left <paramref name="inventory"/>: another copy of that kind, searching on from the same place with
        /// wrap-around; once the kind runs out, the first copy of the next kind (holding the button fills the shelf).
        /// </summary>
        public static int AfterPlacing(IReadOnlyList<ItemInstance> inventory, int placedIndex, string placedId)
        {
            int count = inventory.Count;
            if (count == 0) return 0;
            if (placedIndex < 0) placedIndex = 0;
            // The items after the placed one moved down a place: the search starts on the same index.
            for (int n = 0; n < count; n++)
            {
                int i = (placedIndex + n) % count;
                if (inventory[i].Definition.Id == placedId) return i;
            }
            // The last copy was the first of its kind, so the next kind is the next first appearance after it.
            for (int i = placedIndex; i < count; i++)
                if (IsFirstOfKind(inventory, i)) return i;
            return 0;
        }

        /// <summary>
        /// The first copy of the kind <paramref name="direction"/> steps away from the kind in hand,
        /// wrapping around; kinds are ordered by their first appearance in <paramref name="inventory"/>.
        /// </summary>
        public static int StepKind(IReadOnlyList<ItemInstance> inventory, int current, int direction)
        {
            int count = inventory.Count;
            if (count == 0) return 0;
            if (current < 0 || current >= count) current = current < 0 ? 0 : count - 1;
            if (direction == 0) return current;
            var firsts = new List<int>();
            int held = -1;
            string heldId = inventory[current].Definition.Id;
            for (int i = 0; i < count; i++)
            {
                if (!IsFirstOfKind(inventory, i)) continue;
                if (inventory[i].Definition.Id == heldId) held = firsts.Count;
                firsts.Add(i);
            }
            int step = direction > 0 ? 1 : -1;
            return firsts[(held + step + firsts.Count) % firsts.Count];
        }

        static bool IsFirstOfKind(IReadOnlyList<ItemInstance> inventory, int index)
        {
            string id = inventory[index].Definition.Id;
            for (int i = 0; i < index; i++)
                if (inventory[i].Definition.Id == id) return false;
            return true;
        }
    }
}
