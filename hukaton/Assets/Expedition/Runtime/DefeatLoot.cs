using System;
using System.Collections.Generic;
using Hakaton.Decks;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// What a defeat leaves the player (pure rules, no scene): the bag counts as one heap of single items, ceil(N × (1 − loss))
    /// of them come home and which ones is drawn at random, each copy with the same chance and none twice. Coins round down,
    /// as before. The world passes a random seeded by the outing, so the same outing keeps the same things.
    /// </summary>
    public static class DefeatLoot
    {
        /// <summary>How many of <paramref name="total"/> items come home after a defeat: ceil(total × (1 − loss)).</summary>
        public static int KeptCount(int total, float loss)
        {
            if (total <= 0) return 0;
            double share = 1 - Math.Min(1, Math.Max(0, (double)loss));
            // A hair below the exact product: 10 × 0.7 must stay 7, not become 8 through a rounding error.
            int kept = (int)Math.Ceiling(total * share - 1e-6);
            return Math.Min(total, Math.Max(0, kept));
        }

        /// <summary>Coins kept after a defeat: floor(coins × (1 − loss)), as before.</summary>
        public static int KeptCoins(int coins, float loss)
        {
            if (coins <= 0) return 0;
            double share = 1 - Math.Min(1, Math.Max(0, (double)loss));
            return Math.Min(coins, (int)Math.Floor(coins * share + 1e-6));
        }

        /// <summary>
        /// Draws the items that come home: the result holds, for each bag entry, how many of its copies are kept (the
        /// same order as <paramref name="counts"/>). Exactly <see cref="KeptCount"/> single copies are drawn, each copy of
        /// the whole bag equally likely and none drawn twice.
        /// </summary>
        public static int[] Keep(IReadOnlyList<int> counts, float loss, Random random)
        {
            if (counts == null) throw new ArgumentNullException(nameof(counts));
            if (random == null) throw new ArgumentNullException(nameof(random));
            var kept = new int[counts.Count];
            // Every single copy in the bag, by the entry it belongs to.
            var units = new List<int>();
            for (int i = 0; i < counts.Count; i++)
                for (int c = 0; c < counts[i]; c++) units.Add(i);
            int keep = KeptCount(units.Count, loss);
            // A partial Fisher–Yates shuffle: the first <keep> places get distinct copies, each equally likely.
            for (int i = 0; i < keep; i++)
            {
                int j = i + random.Next(units.Count - i);
                (units[i], units[j]) = (units[j], units[i]);
                kept[units[i]]++;
            }
            return kept;
        }

        /// <summary>The bag after a defeat: the kept copies of each entry, entries with none dropped.</summary>
        public static List<LootSave> Keep(IReadOnlyList<LootSave> bag, float loss, Random random)
        {
            var counts = new int[bag.Count];
            for (int i = 0; i < bag.Count; i++) counts[i] = Math.Max(0, bag[i].count);
            var kept = Keep(counts, loss, random);
            var retained = new List<LootSave>();
            for (int i = 0; i < bag.Count; i++)
                if (kept[i] > 0) retained.Add(new LootSave { id = bag[i].id, name = bag[i].name, count = kept[i], value = bag[i].value });
            return retained;
        }

        /// <summary>The random a defeat draws its kept items from: the outing's seed, salted so it is not the deck's shuffle.</summary>
        public static Random RandomFor(int outingSeed) => new Random(unchecked(outingSeed * 7919 + 104729));
    }
}
