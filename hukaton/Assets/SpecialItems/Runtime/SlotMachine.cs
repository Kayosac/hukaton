using System;
using System.Collections.Generic;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// The phone's one-armed bandit, without presentation: an outcome is drawn from the chance table first, then the
    /// three reels are made to show it. Purely random: when the player stops the reels changes nothing.
    /// </summary>
    public static class SlotMachine
    {
        public const int Reels = 3;
        public const int SymbolCount = 7;

        /// <summary>
        /// The rare card on the reels: card, PENUS, card — not three cards (the usual card) and not three PENUS (the jackpot).
        /// A pair never shows two cards with a PENUS, in any order, so this reads only as the rare card.
        /// </summary>
        public static readonly PhoneSymbol[] RareCardReels = { PhoneSymbol.Card, PhoneSymbol.Penus, PhoneSymbol.Card };

        static readonly PhoneSymbol[] NotCardOrPenus = { PhoneSymbol.Coin, PhoneSymbol.Heart, PhoneSymbol.Lightning, PhoneSymbol.Goblin, PhoneSymbol.Skull };

        /// <summary>The symbol three of which bring the outcome (none for Nothing and Pair).</summary>
        public static PhoneSymbol? TripleOf(PhoneOutcome outcome)
        {
            switch (outcome)
            {
                case PhoneOutcome.Coins: return PhoneSymbol.Coin;
                case PhoneOutcome.Card: return PhoneSymbol.Card;
                case PhoneOutcome.Heal: return PhoneSymbol.Heart;
                case PhoneOutcome.Boost: return PhoneSymbol.Lightning;
                case PhoneOutcome.Call: return PhoneSymbol.Goblin;
                case PhoneOutcome.Curse: return PhoneSymbol.Skull;
                case PhoneOutcome.Debuff: return PhoneSymbol.Skull;
                case PhoneOutcome.Treasure: return PhoneSymbol.Coin;
                case PhoneOutcome.Jackpot: return PhoneSymbol.Penus;
                default: return null;
            }
        }

        /// <summary>The outcome reels showing <paramref name="outcome"/> read as: debuffs look like the curse (skulls), treasure like coins.</summary>
        public static PhoneOutcome Looks(PhoneOutcome outcome) => outcome == PhoneOutcome.Debuff ? PhoneOutcome.Curse
            : outcome == PhoneOutcome.Treasure ? PhoneOutcome.Coins : outcome;

        /// <summary>One outcome by the (normalised) chances of the table. An empty table brings nothing.</summary>
        public static PhoneOutcome Roll(IReadOnlyList<PhoneOutcomeChance> table, Random random)
        {
            if (table == null || random == null) return PhoneOutcome.Nothing;
            double total = 0;
            foreach (var entry in table) total += Math.Max(0, entry.percent);
            if (total <= 0) return PhoneOutcome.Nothing;
            double pick = random.NextDouble() * total;
            foreach (var entry in table)
            {
                double weight = Math.Max(0, entry.percent);
                if (weight <= 0) continue;
                if (pick < weight) return entry.outcome;
                pick -= weight;
            }
            for (int i = table.Count - 1; i >= 0; i--) if (table[i].percent > 0) return table[i].outcome;
            return PhoneOutcome.Nothing;
        }

        /// <summary>What the three reels show for <paramref name="outcome"/>.</summary>
        public static PhoneSymbol[] Show(PhoneOutcome outcome, Random random)
        {
            var reels = new PhoneSymbol[Reels];
            var triple = TripleOf(outcome);
            if (triple.HasValue)
            {
                for (int i = 0; i < Reels; i++) reels[i] = triple.Value;
                return reels;
            }
            if (outcome == PhoneOutcome.RareCard)
            {
                for (int i = 0; i < Reels; i++) reels[i] = RareCardReels[i];
                return reels;
            }
            if (outcome == PhoneOutcome.Pair)
            {
                var pair = (PhoneSymbol)random.Next(SymbolCount);
                var odd = (PhoneSymbol)((int)(pair + 1 + random.Next(SymbolCount - 1)) % SymbolCount);
                // Two cards and a PENUS would look like the rare card: the odd one is another symbol then.
                if (pair == PhoneSymbol.Card && odd == PhoneSymbol.Penus) odd = NotCardOrPenus[random.Next(NotCardOrPenus.Length)];
                int oddAt = random.Next(Reels);
                for (int i = 0; i < Reels; i++) reels[i] = i == oddAt ? odd : pair;
                return reels;
            }
            // Nothing: three different symbols.
            var bag = new List<PhoneSymbol>();
            for (int i = 0; i < SymbolCount; i++) bag.Add((PhoneSymbol)i);
            for (int i = 0; i < Reels; i++)
            {
                int at = random.Next(bag.Count);
                reels[i] = bag[at];
                bag.RemoveAt(at);
            }
            return reels;
        }

        /// <summary>The outcome three reels read as (the inverse of <see cref="Show"/>).</summary>
        public static PhoneOutcome Read(IReadOnlyList<PhoneSymbol> reels)
        {
            if (reels == null || reels.Count != Reels) return PhoneOutcome.Nothing;
            if (reels[0] == RareCardReels[0] && reels[1] == RareCardReels[1] && reels[2] == RareCardReels[2]) return PhoneOutcome.RareCard;
            if (reels[0] == reels[1] && reels[1] == reels[2])
                for (var outcome = PhoneOutcome.Coins; outcome <= PhoneOutcome.Jackpot; outcome++)
                    if (TripleOf(outcome) == reels[0]) return outcome;
            if (reels[0] == reels[1] || reels[1] == reels[2] || reels[0] == reels[2]) return PhoneOutcome.Pair;
            return PhoneOutcome.Nothing;
        }

        /// <summary>Average coins a spin brings back from coin prizes alone (for tuning and tests).</summary>
        public static double ExpectedCoins(PhoneSettings settings)
        {
            if (settings == null || settings.chances == null) return 0;
            double total = 0, coins = 0;
            foreach (var entry in settings.chances) total += Math.Max(0, entry.percent);
            if (total <= 0) return 0;
            foreach (var entry in settings.chances)
            {
                double chance = Math.Max(0, entry.percent) / total;
                if (entry.outcome == PhoneOutcome.Pair) coins += chance * settings.pairCoins;
                if (entry.outcome == PhoneOutcome.Coins) coins += chance * settings.coinsPrize;
                if (entry.outcome == PhoneOutcome.Jackpot) coins += chance * settings.jackpotCoins;
            }
            return coins;
        }
    }
}
