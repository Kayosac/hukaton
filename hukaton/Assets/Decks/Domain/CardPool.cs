using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Decks
{
    /// <summary>
    /// Pool stages: which cards new offers (the goblin's table, trophy cards, the phone's prize) may hold. A card is on
    /// sale from its <see cref="CardSpec.Stage"/>: 1 from the start, 2 after the god is met, 3 after the first return
    /// from the endless world. Cards the player already owns stay at any stage; the filter only limits new offers.
    /// </summary>
    public static class CardPool
    {
        /// <summary>Cards of <paramref name="catalogue"/> on sale at <paramref name="stage"/>; the dummy always passes.</summary>
        public static IEnumerable<CardSpec> ForStage(IEnumerable<CardSpec> catalogue, int stage)
            => catalogue.Where(s => s != null && (s.Dummy || s.Stage <= stage));

        /// <summary>The player's stage by progress: 3 after the endless world, 2 after the god, otherwise 1.</summary>
        public static int StageOf(bool godMet, bool endlessVisited) => endlessVisited ? 3 : godMet ? 2 : 1;
    }
}
