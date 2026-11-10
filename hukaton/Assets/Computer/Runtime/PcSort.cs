using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;

namespace Hakaton.Computer
{
    public enum PcSortMode { Acquired, Name, Level, DeckFirst, Rank }

    /// <summary>The card window's sorting; the button cycles through the modes in this order.</summary>
    public static class PcSort
    {
        public static readonly PcSortMode[] Cycle = { PcSortMode.Acquired, PcSortMode.Name, PcSortMode.Level, PcSortMode.DeckFirst, PcSortMode.Rank };

        public static PcSortMode Next(PcSortMode mode) => Cycle[(Array.IndexOf(Cycle, mode) + 1) % Cycle.Length];

        public static string Label(PcSortMode mode)
        {
            switch (mode)
            {
                case PcSortMode.Name: return "по имени";
                case PcSortMode.Level: return "по уровню";
                case PcSortMode.DeckFirst: return "сначала колода";
                case PcSortMode.Rank: return "по редкости";
                default: return "по порядку";
            }
        }

        /// <summary>Stable: equal cards keep the order they were acquired in.</summary>
        public static List<CardCopy> Apply(IReadOnlyList<CardCopy> owned, PcSortMode mode, Func<int, bool> inDeck)
        {
            var order = owned.Select((copy, index) => (copy, index));
            switch (mode)
            {
                case PcSortMode.Name:
                    order = order.OrderBy(x => x.copy.Spec.Name, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(x => x.copy.Level).ThenBy(x => x.index);
                    break;
                case PcSortMode.Level:
                    order = order.OrderByDescending(x => x.copy.Level).ThenBy(x => x.copy.Spec.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.index);
                    break;
                case PcSortMode.DeckFirst:
                    order = order.OrderBy(x => inDeck(x.copy.Id) ? 0 : 1).ThenBy(x => x.index);
                    break;
                case PcSortMode.Rank:
                    order = order.OrderByDescending(x => x.copy.Spec.Rank).ThenBy(x => x.copy.Spec.Name, StringComparer.CurrentCultureIgnoreCase)
                                 .ThenByDescending(x => x.copy.Level).ThenBy(x => x.index);
                    break;
                default:
                    order = order.OrderBy(x => x.index);
                    break;
            }
            return order.Select(x => x.copy).ToList();
        }
    }
}
