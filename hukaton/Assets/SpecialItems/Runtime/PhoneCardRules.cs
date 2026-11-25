using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Decks;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// Which card the phone's bandit gives (balance stage 5). «Карта» (three cards): any card of the player's current pool
    /// stage (<see cref="CardPool.ForStage"/>), as the goblin and the trophies offer. «Редкая карта» (card, PENUS, card): a
    /// card of rank <see cref="RareMinimumRank"/> or higher from the whole catalogue, whatever the stage — the same way as a
    /// rich chest's guaranteed card (<see cref="TrophyCardRules.CatalogueStage"/>, <see cref="TrophyCardRules.Pick"/>).
    /// Dummies never come. Pure rules for tests; <c>PhoneTool</c> turns the spec back into its card.
    /// </summary>
    public static class PhoneCardRules
    {
        /// <summary>The rare card is at least this rank.</summary>
        public const int RareMinimumRank = 3;

        /// <summary>
        /// A card for <paramref name="outcome"/> (<see cref="PhoneOutcome.Card"/> or <see cref="PhoneOutcome.RareCard"/>)
        /// out of <paramref name="catalogue"/> (every card on sale, of every stage). Null when there is none to give.
        /// </summary>
        public static CardSpec Pick(PhoneOutcome outcome, IEnumerable<CardSpec> catalogue, int poolStage, ICollection<string> owned,
            Random random, double unownedWeight = 2)
        {
            if (catalogue == null || random == null) return null;
            if (outcome == PhoneOutcome.RareCard)
            {
                // As TrophyCards.Give for a rich chest: every stage, then a rank by the goblin's weights among III and up.
                var rare = CardPool.ForStage(catalogue, TrophyCardRules.CatalogueStage(poolStage, RareMinimumRank))
                    .Where(s => !s.Dummy && s.Rank >= RareMinimumRank).ToList();
                return rare.Count == 0 ? null : TrophyCardRules.Pick(rare, owned, random, unownedWeight, RareMinimumRank);
            }
            if (outcome != PhoneOutcome.Card) return null;
            var pool = CardPool.ForStage(catalogue, poolStage).Where(s => !s.Dummy).ToList();
            return pool.Count == 0 ? null : pool[random.Next(pool.Count)];
        }
    }
}
