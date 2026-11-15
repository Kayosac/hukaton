using System;
using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Computer
{
    public enum PcStockSortMode { Count, Name, Category, Rarity, Price }

    /// <summary>One kind of goods in the shop: how many lie in the storeroom and how many stand on the display.</summary>
    public sealed class PcStockLine
    {
        public string Id;
        public string Name;
        public string Category;
        /// <summary><see cref="Hakaton.Loot.LootRarity"/> as a number (0 — обычный … 3 — легендарный).</summary>
        public int Rarity;
        public int Price;
        /// <summary>In the storeroom (<c>TradingSession.Inventory</c>), not yet on the display.</summary>
        public int Stored;
        /// <summary>On the display (<c>TradingSession.Slots</c>).</summary>
        public int Shelved;
        public int Total => Stored + Shelved;
    }

    /// <summary>The goods one kind of item stands for (name, category, rarity, price); looked up by id.</summary>
    public struct PcStockInfo
    {
        public string Name, Category;
        public int Rarity, Price;
    }

    /// <summary>
    /// «Склад»: counting the shop's goods by kind and sorting the list. Pure, no Unity: the window feeds it the ids of
    /// the storeroom's and the display's items.
    /// </summary>
    public static class PcStock
    {
        public static readonly PcStockSortMode[] Cycle =
            { PcStockSortMode.Count, PcStockSortMode.Name, PcStockSortMode.Category, PcStockSortMode.Rarity, PcStockSortMode.Price };

        public static PcStockSortMode Next(PcStockSortMode mode) => Cycle[(Array.IndexOf(Cycle, mode) + 1) % Cycle.Length];

        public static string Label(PcStockSortMode mode)
        {
            switch (mode)
            {
                case PcStockSortMode.Name: return "по имени";
                case PcStockSortMode.Category: return "по категории";
                case PcStockSortMode.Rarity: return "по редкости";
                case PcStockSortMode.Price: return "по цене";
                default: return "по количеству";
            }
        }

        static readonly string[] RarityLabels = { "обычный", "необычный", "редкий", "легендарный" };

        public static string RarityLabel(int rarity) => rarity >= 0 && rarity < RarityLabels.Length ? RarityLabels[rarity] : "";

        /// <summary>
        /// One line per kind that has at least one copy in the storeroom or on the display. Empty display slots
        /// (null or empty ids) are skipped. Lines come in the order the kinds were first met (storeroom first).
        /// </summary>
        public static List<PcStockLine> Count(IEnumerable<string> stored, IEnumerable<string> shelved, Func<string, PcStockInfo> info)
        {
            var lines = new List<PcStockLine>();
            var byId = new Dictionary<string, PcStockLine>(StringComparer.Ordinal);
            PcStockLine Line(string id)
            {
                if (byId.TryGetValue(id, out var line)) return line;
                var i = info != null ? info(id) : default;
                line = new PcStockLine
                {
                    Id = id, Name = string.IsNullOrEmpty(i.Name) ? id : i.Name, Category = i.Category ?? "",
                    Rarity = i.Rarity, Price = i.Price
                };
                byId.Add(id, line);
                lines.Add(line);
                return line;
            }
            if (stored != null) foreach (var id in stored) if (!string.IsNullOrEmpty(id)) Line(id).Stored++;
            if (shelved != null) foreach (var id in shelved) if (!string.IsNullOrEmpty(id)) Line(id).Shelved++;
            return lines;
        }

        /// <summary>Stable; ties fall back to the name, then to the order the kinds were met.</summary>
        public static List<PcStockLine> Sort(IReadOnlyList<PcStockLine> lines, PcStockSortMode mode)
        {
            var items = lines.Select((line, index) => (line, index));
            var byName = StringComparer.CurrentCultureIgnoreCase;
            IOrderedEnumerable<(PcStockLine line, int index)> order;
            switch (mode)
            {
                case PcStockSortMode.Name:
                    order = items.OrderBy(x => x.line.Name, byName);
                    break;
                case PcStockSortMode.Category:
                    order = items.OrderBy(x => x.line.Category, byName).ThenBy(x => x.line.Name, byName);
                    break;
                case PcStockSortMode.Rarity:
                    order = items.OrderByDescending(x => x.line.Rarity).ThenBy(x => x.line.Name, byName);
                    break;
                case PcStockSortMode.Price:
                    order = items.OrderByDescending(x => x.line.Price).ThenBy(x => x.line.Name, byName);
                    break;
                default:
                    order = items.OrderByDescending(x => x.line.Stored).ThenByDescending(x => x.line.Shelved).ThenBy(x => x.line.Name, byName);
                    break;
            }
            return order.ThenBy(x => x.index).Select(x => x.line).ToList();
        }

        /// <summary>«3 находки», «1 находка», «5 находок».</summary>
        public static string Finds(int n)
        {
            int m10 = n % 10, m100 = n % 100;
            string word = m10 == 1 && m100 != 11 ? "находка" : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "находки" : "находок";
            return n + " " + word;
        }
    }
}
