using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Hapay
{
    /// <summary>What хаPAY needs to know about the player's goods (storage and display together) and their prices.</summary>
    public interface IHapayStock
    {
        /// <summary>Pieces of the item in storage (not on the display).</summary>
        int Stored(string item);
        /// <summary>Pieces of the item on the display.</summary>
        int Displayed(string item);
        /// <summary>The item's recommended price (0: unknown item).</summary>
        int Price(string item);
        /// <summary>0 common … 3 legendary.</summary>
        int Rarity(string item);
        /// <summary>Every item id the player has, storage and display, each once.</summary>
        IEnumerable<string> Stocked();
    }

    /// <summary>A lot on the site: a collector's request and its time.</summary>
    public sealed class HapayLot
    {
        public HapayCollection Collection { get; }
        /// <summary>Own wants (the first lot, made from what the player had); otherwise the collection's.</summary>
        public IReadOnlyList<HapayWant> Wants { get; }
        public float Seconds { get; }
        public float SecondsLeft { get; internal set; }
        public bool Expired => SecondsLeft <= 0;

        internal HapayLot(HapayCollection collection, IReadOnlyList<HapayWant> wants, float seconds, float left)
        {
            Collection = collection;
            Wants = wants ?? collection.wants;
            Seconds = seconds;
            SecondsLeft = left;
        }

        public int Pieces
        {
            get
            {
                int n = 0;
                foreach (var want in Wants) if (want != null) n += Math.Max(1, want.count);
                return n;
            }
        }
    }

    [Serializable]
    public sealed class HapayLotSave
    {
        public string collection;
        public float seconds, left;
        /// <summary>Only for the first lot (its wants were made from the player's goods).</summary>
        public List<HapayWant> wants;
    }

    /// <summary>The хаPAY part of the game save. Null in saves made before хаPAY.</summary>
    [Serializable]
    public sealed class HapaySave
    {
        public List<HapayLotSave> lots = new List<HapayLotSave>();
        public List<string> recent = new List<string>();
        public bool firstLotDone;
        public int sold;
        public long earned;
    }

    /// <summary>
    /// The site's state: the lots on it and their clocks (they run while the game runs, the caller decides when), the lots
    /// that left lately (not shown again for a while), the first lot made from what the player has, and the sales. Pure
    /// model, no Unity objects but the pool's data; the game save keeps it (<see cref="Save"/> / <see cref="Restore"/>).
    /// </summary>
    public sealed class HapayMarket
    {
        readonly HapayPool pool;
        readonly System.Random random;
        readonly List<HapayLot> lots = new List<HapayLot>();
        readonly List<string> recent = new List<string>();

        public HapayMarket(HapayPool pool, int seed = 0)
        {
            this.pool = pool;
            random = seed == 0 ? new System.Random() : new System.Random(seed);
        }

        public HapayPool Pool => pool;
        public IReadOnlyList<HapayLot> Lots => lots;
        /// <summary>The first lot (made from the player's goods) is still to come.</summary>
        public bool FirstLotDone { get; private set; }
        public int Sold { get; private set; }
        public long Earned { get; private set; }
        /// <summary>A lot came, went, or was sold (anything the save and the site must follow).</summary>
        public event Action Changed;

        /// <summary>Tops the site up to its number of lots.</summary>
        public void Fill()
        {
            bool changed = false;
            while (lots.Count < pool.lotsOnSite)
            {
                var next = Pick();
                if (next == null) break;
                lots.Add(NewLot(next, null));
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Clocks run; a lot whose time is up leaves for a new one.</summary>
        public void Tick(float seconds)
        {
            if (seconds <= 0) return;
            bool changed = false;
            for (int i = 0; i < lots.Count; i++)
            {
                var lot = lots[i];
                lot.SecondsLeft = Mathf.Max(0, lot.SecondsLeft - seconds);
                if (!lot.Expired) continue;
                Leave(lot);
                var next = Pick();
                if (next != null) lots[i] = NewLot(next, null);
                else { lots.RemoveAt(i); i--; }
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>What the collector pays: the sum of recommended prices × the multiplier, rounded.</summary>
        public int PriceOf(HapayLot lot, IHapayStock stock)
        {
            if (lot == null || stock == null) return 0;
            long sum = 0;
            int pieces = 0;
            float rarity = 0;
            foreach (var want in lot.Wants)
            {
                if (want == null) continue;
                int count = Math.Max(1, want.count);
                sum += (long)stock.Price(want.item) * count;
                pieces += count;
                rarity += stock.Rarity(want.item) * count;
            }
            if (pieces == 0) return 0;
            // Rounded to 4 places so 1.3 + 0.08 + 0.15 is 1.53, not 1.5299999 (float steps).
            double multiplier = Math.Round((double)Multiplier(pieces, rarity / pieces), 4);
            int step = Math.Max(1, pool.roundTo);
            long price = (long)Math.Round(Math.Round(sum * multiplier, 4) / step, MidpointRounding.AwayFromZero) * step;
            return (int)Math.Min(int.MaxValue, Math.Max(step, price));
        }

        public float Multiplier(int pieces, float averageRarity) =>
            Mathf.Min(pool.maxMultiplier, pool.baseMultiplier + pool.perPiece * Math.Max(0, pieces - 2) + pool.perRarity * Mathf.Max(0, averageRarity));

        /// <summary>How many of the want the player has (storage and display).</summary>
        public static int Have(HapayWant want, IHapayStock stock) => stock.Stored(want.item) + stock.Displayed(want.item);

        /// <summary>Everything the lot wants is there.</summary>
        public static bool Complete(HapayLot lot, IHapayStock stock)
        {
            if (lot == null || stock == null) return false;
            foreach (var want in Merge(lot.Wants))
                if (Have(want, stock) < want.count) return false;
            return true;
        }

        /// <summary>The same item asked twice counts as one want of both counts.</summary>
        public static List<HapayWant> Merge(IEnumerable<HapayWant> wants)
        {
            var merged = new List<HapayWant>();
            foreach (var want in wants)
            {
                if (want == null || string.IsNullOrEmpty(want.item)) continue;
                var same = merged.Find(w => w.item == want.item);
                if (same != null) same.count += Math.Max(1, want.count);
                else merged.Add(new HapayWant(want.item, Math.Max(1, want.count)));
            }
            return merged;
        }

        /// <summary>The lot was sold (the caller has taken the goods and paid): it leaves for a new one.</summary>
        public void MarkSold(HapayLot lot, int price)
        {
            int index = lots.IndexOf(lot);
            if (index < 0) return;
            Sold++;
            Earned += Math.Max(0, price);
            Leave(lot);
            var next = Pick();
            if (next != null) lots[index] = NewLot(next, null);
            else lots.RemoveAt(index);
            Changed?.Invoke();
        }

        /// <summary>
        /// The first lot after the account is bought, made from what the player has (up to a few kinds, one piece each), so
        /// the first sale can be made at once. Takes the first place on the site. False: nothing to make it from yet (or done).
        /// </summary>
        public bool MakeFirstLot(IHapayStock stock)
        {
            if (FirstLotDone || stock == null || pool.firstLot == null) return false;
            var kinds = new List<string>();
            foreach (var id in stock.Stocked()) if (!string.IsNullOrEmpty(id) && stock.Price(id) > 0 && !kinds.Contains(id)) kinds.Add(id);
            if (kinds.Count == 0) return false;
            // The dearest few: a first sale worth seeing.
            kinds.Sort((a, b) => stock.Price(b).CompareTo(stock.Price(a)));
            var wants = new List<HapayWant>();
            for (int i = 0; i < kinds.Count && i < Math.Max(1, pool.firstLotMaxKinds); i++) wants.Add(new HapayWant(kinds[i], 1));
            var lot = new HapayLot(pool.firstLot, wants, pool.longestMinutes * 60, pool.longestMinutes * 60);
            if (lots.Count == 0) lots.Add(lot);
            else { Leave(lots[0]); lots[0] = lot; }
            FirstLotDone = true;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Cheats: every lot leaves for a new one.</summary>
        public void Renew()
        {
            foreach (var lot in lots) Leave(lot);
            lots.Clear();
            Fill();
            Changed?.Invoke();
        }

        void Leave(HapayLot lot)
        {
            if (lot?.Collection == null || lot.Collection == pool.firstLot) return;
            recent.Remove(lot.Collection.id);
            recent.Add(lot.Collection.id);
            while (recent.Count > pool.noRepeat) recent.RemoveAt(0);
        }

        HapayLot NewLot(HapayCollection collection, IReadOnlyList<HapayWant> wants)
        {
            float seconds = pool.SecondsFor(collection.Pieces);
            return new HapayLot(collection, wants, seconds, seconds);
        }

        /// <summary>A collection not on the site and not among the recent ones (if all are, the oldest recent one).</summary>
        HapayCollection Pick()
        {
            var free = new List<HapayCollection>();
            foreach (var c in pool.collections)
                if (c != null && !string.IsNullOrEmpty(c.id) && c.wants.Count > 0 && !OnSite(c) && !recent.Contains(c.id)) free.Add(c);
            if (free.Count > 0) return free[random.Next(free.Count)];
            foreach (var id in recent)
            {
                var c = pool.Find(id);
                if (c != null && !OnSite(c)) return c;
            }
            return null;
        }

        bool OnSite(HapayCollection collection)
        {
            foreach (var lot in lots) if (lot.Collection == collection) return true;
            return false;
        }

        public HapaySave Save()
        {
            var save = new HapaySave { firstLotDone = FirstLotDone, sold = Sold, earned = Earned, recent = new List<string>(recent) };
            foreach (var lot in lots)
                save.lots.Add(new HapayLotSave
                {
                    collection = lot.Collection.id, seconds = lot.Seconds, left = lot.SecondsLeft,
                    wants = lot.Collection == pool.firstLot ? new List<HapayWant>(lot.Wants) : null
                });
            return save;
        }

        /// <summary>A saved state; lots of collections the pool no longer has are dropped, the site is topped up. Null: a fresh site.</summary>
        public void Restore(HapaySave save)
        {
            lots.Clear();
            recent.Clear();
            FirstLotDone = false;
            Sold = 0;
            Earned = 0;
            if (save != null)
            {
                FirstLotDone = save.firstLotDone;
                Sold = Math.Max(0, save.sold);
                Earned = Math.Max(0, save.earned);
                foreach (var id in save.recent ?? new List<string>()) if (pool.Find(id) != null && !recent.Contains(id)) recent.Add(id);
                foreach (var entry in save.lots ?? new List<HapayLotSave>())
                {
                    var collection = entry != null ? pool.Find(entry.collection) : null;
                    if (collection == null || OnSite(collection) || lots.Count >= pool.lotsOnSite) continue;
                    bool first = collection == pool.firstLot;
                    if (first && (entry.wants == null || entry.wants.Count == 0)) continue;
                    float seconds = entry.seconds > 0 && !float.IsInfinity(entry.seconds) ? entry.seconds : pool.SecondsFor(collection.Pieces);
                    float left = float.IsNaN(entry.left) ? 0 : Mathf.Clamp(entry.left, 0, seconds);
                    if (left <= 0) continue;
                    lots.Add(new HapayLot(collection, first ? new List<HapayWant>(entry.wants) : null, seconds, left));
                }
            }
            Fill();
            Changed?.Invoke();
        }
    }
}
