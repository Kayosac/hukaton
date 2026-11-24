using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Decks.Unity
{
    /// <summary>
    /// The config's cards that new random gifts may be at a pool stage (<see cref="CardPool.ForStage"/>): the trophy
    /// cards of <c>TrophyCards</c> and the phone's prize. Only the ordinary cards with artwork; never the dummy, the
    /// story cards or the dungeon's own trophy cards. The goblin filters his catalogue the same way.
    /// </summary>
    public static class StagedCards
    {
        public static List<CardDefinition> OnSale(DeckPrototypeConfig config, int stage)
        {
            var result = new List<CardDefinition>();
            if (!config || config.cards == null) return result;
            var definitions = new Dictionary<string, CardDefinition>();
            var specs = new List<CardSpec>();
            foreach (var card in config.cards)
                if (card && card != config.dummyCard && !card.dummy && card.artwork && !definitions.ContainsKey(card.id))
                {
                    definitions[card.id] = card;
                    specs.Add(card.ToSpec());
                }
            result.AddRange(CardPool.ForStage(specs, stage).Select(s => definitions[s.Id]));
            return result;
        }
    }
}
