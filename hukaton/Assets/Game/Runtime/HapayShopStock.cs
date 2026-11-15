using System.Collections.Generic;
using Hakaton.Hapay;
using Hakaton.Loot;
using Hakaton.Trading;

namespace Hakaton.Game
{
    /// <summary>хаPAY's view of the shop's goods: storage and display from the trading session, prices and rarity from the loot catalogue.</summary>
    sealed class HapayShopStock : IHapayStock
    {
        readonly TradingSession session;
        readonly LootCatalog catalog;

        public HapayShopStock(TradingSession session, LootCatalog catalog)
        {
            this.session = session;
            this.catalog = catalog;
        }

        public int Stored(string item) => session.StoredCount(item);
        public int Displayed(string item) => session.DisplayedCount(item);

        public int Price(string item)
        {
            var loot = catalog ? catalog.Find(item) : null;
            if (loot) return loot.price;
            var definition = session.FindDefinition(item);
            return definition != null ? definition.RecommendedPrice : 0;
        }

        public int Rarity(string item)
        {
            var loot = catalog ? catalog.Find(item) : null;
            return loot ? (int)loot.rarity : 0;
        }

        public IEnumerable<string> Stocked()
        {
            var seen = new HashSet<string>();
            foreach (var piece in session.Inventory) if (seen.Add(piece.Definition.Id)) yield return piece.Definition.Id;
            foreach (var piece in session.Slots) if (piece != null && seen.Add(piece.Definition.Id)) yield return piece.Definition.Id;
        }
    }
}
