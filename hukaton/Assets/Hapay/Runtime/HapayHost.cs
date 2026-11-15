using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Hapay
{
    /// <summary>
    /// The scene's хаPAY (board «Спецпредметы»): the pool, the site's state (<see cref="Market"/>) and the hooks the game
    /// loop fills in — the player's goods (<see cref="Stock"/>), the sale itself (<see cref="SellGoods"/>), item names and
    /// pictures. The game loop opens it once the account is bought (<see cref="Open"/>) and ticks it while the game runs;
    /// the PC shows the site.
    /// </summary>
    public sealed class HapayHost : MonoBehaviour
    {
        public HapayPool pool;

        HapayMarket market;
        public HapayMarket Market => market ??= new HapayMarket(pool);
        /// <summary>The account is the player's: the site is on the PC and its clocks run.</summary>
        public bool Open { get; set; }
        /// <summary>Clocks stand (the helper is explaining the site).</summary>
        public bool Frozen { get; set; }
        public IHapayStock Stock { get; set; }
        /// <summary>Takes the wanted goods (storage first, then the display), pays the price into the shop's revenue. False: refused.</summary>
        public Func<IReadOnlyList<HapayWant>, int, bool> SellGoods { get; set; }
        public Func<string, string> ItemName { get; set; }
        public Func<string, Sprite> ItemIcon { get; set; }
        /// <summary>A lot was sold: the lot and the price.</summary>
        public event Action<HapayLot, int> Sold;

        /// <summary>Clocks run for this much game time (the game loop calls it while the game is not paused or in a menu).</summary>
        public void Tick(float seconds)
        {
            if (!Open || Frozen || !pool) return;
            Market.Stock = Stock;
            Market.Fill();
            Market.Tick(seconds);
        }

        public int PriceOf(HapayLot lot) => Market.PriceOf(lot, Stock);
        public bool Complete(HapayLot lot) => HapayMarket.Complete(lot, Stock);

        /// <summary>Sells the lot if everything is there. False: something is missing, or the shop refused.</summary>
        public bool Sell(HapayLot lot)
        {
            if (!Open || lot == null || Stock == null || SellGoods == null || !Complete(lot)) return false;
            int price = PriceOf(lot);
            if (price <= 0 || !SellGoods(HapayMarket.Merge(lot.Wants), price)) return false;
            Market.Stock = Stock;
            Market.MarkSold(lot, price);
            Sold?.Invoke(lot, price);
            return true;
        }

        /// <summary>The first lot, from what the player has, if it is still to come and there is something to make it from.</summary>
        public bool TryFirstLot() => Open && pool && Stock != null && Market.MakeFirstLot(Stock);

        public string NameOf(string item) => ItemName?.Invoke(item) ?? item;
        public Sprite IconOf(string item) => ItemIcon?.Invoke(item);
    }
}
