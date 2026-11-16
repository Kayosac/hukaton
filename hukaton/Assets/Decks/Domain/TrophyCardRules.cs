using System;
using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Decks
{
    /// <summary>
    /// Trophy cards (board «Пинок и трофейные карты»): now and then a creature, a chest, a mimic or a broken crate leaves a
    /// card that flies straight into the hand — for this outing only, one throw, never in the collection. Which card: one
    /// of the goblin's (no dummy, no story or Return card), its rank by his weights, a card the player does not own yet
    /// twice as likely (a taste of what the goblin sells). Pure rules for tests; the numbers live in the settings asset.
    /// </summary>
    public static class TrophyCardRules
    {
        /// <summary>
        /// Chance a slain creature leaves a card by its toughness: <paramref name="bands"/> are (up to this much health,
        /// chance) in ascending order; above the last band the last chance holds. Peaceful and timid creatures leave none.
        /// </summary>
        public static double CreatureChance(int maximumHealth, bool harmless, IReadOnlyList<(int upToHealth, double chance)> bands)
        {
            if (harmless || bands == null || bands.Count == 0) return 0;
            foreach (var (upTo, chance) in bands)
                if (maximumHealth <= upTo) return Clamp01(chance);
            return Clamp01(bands[bands.Count - 1].chance);
        }

        /// <summary>
        /// A card from <paramref name="catalogue"/>: a rank by the goblin's weights among the ranks present (at least
        /// <paramref name="minimumRank"/>), then a card of that rank, those not in <paramref name="owned"/> weighing
        /// <paramref name="unownedWeight"/> times as much. Dummies never come. Null for an empty catalogue.
        /// </summary>
        public static CardSpec Pick(IEnumerable<CardSpec> catalogue, ICollection<string> owned, Random random, double unownedWeight = 2,
            int minimumRank = 1)
        {
            var specs = catalogue.Where(s => s != null && !s.Dummy).ToList();
            var byRank = specs.Where(s => s.Rank >= minimumRank).GroupBy(s => s.Rank).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id, StringComparer.Ordinal).ToList());
            if (byRank.Count == 0) byRank = specs.GroupBy(s => s.Rank).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id, StringComparer.Ordinal).ToList());
            if (byRank.Count == 0) return null;
            int total = 0;
            foreach (var rank in byRank.Keys) total += Weight(rank);
            int roll = random.Next(Math.Max(1, total));
            int chosen = byRank.Keys.Min();
            foreach (var rank in byRank.Keys.OrderBy(r => r))
            {
                roll -= Weight(rank);
                if (roll < 0) { chosen = rank; break; }
            }
            var options = byRank[chosen];
            double sum = 0;
            foreach (var spec in options) sum += owned != null && owned.Contains(spec.Id) ? 1 : unownedWeight;
            double pick = random.NextDouble() * sum;
            foreach (var spec in options)
            {
                pick -= owned != null && owned.Contains(spec.Id) ? 1 : unownedWeight;
                if (pick < 0) return spec;
            }
            return options[options.Count - 1];
        }

        static int Weight(int rank) => rank >= 1 && rank <= GoblinOffer.RankWeights.Length ? GoblinOffer.RankWeights[rank - 1] : 1;

        static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }

    /// <summary>
    /// Luck of one outing's kills: a card is sure after <see cref="Streak"/> kills in a row without one, and on the first
    /// kill of an outing while the player is still starting out (few cards).
    /// </summary>
    public sealed class TrophyLuck
    {
        /// <summary>Kills in a row without a card that make the next one certain (0: never certain).</summary>
        public int Streak { get; }
        public int Dry { get; private set; }
        public int Kills { get; private set; }
        public bool Starting { get; private set; }

        public TrophyLuck(int streak) { Streak = Math.Max(0, streak); }

        /// <summary>A new outing: <paramref name="starting"/> — the player has few cards, the first kill pays for sure.</summary>
        public void Begin(bool starting)
        {
            Dry = 0;
            Kills = 0;
            Starting = starting;
        }

        /// <summary>A creature that can leave a card was killed: true when it does (<paramref name="roll"/> is in [0, 1)).</summary>
        public bool Kill(double chance, double roll)
        {
            Kills++;
            bool sure = Starting && Kills == 1 || Streak > 0 && Dry >= Streak;
            bool card = sure || roll < chance;
            Dry = card ? 0 : Dry + 1;
            return card;
        }
    }
}
