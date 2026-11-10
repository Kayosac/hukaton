using System;
using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Decks
{
    /// <summary>
    /// A card type. Cards are not split into combat and exploration: what a card is for follows from its effect.
    /// Board, «Карты — мердж I / II / III»: a copy has level I, II or III; two equal copies merge into the next level.
    /// </summary>
    public sealed class CardSpec
    {
        public const int MaxLevel = 3;
        public readonly string Id, Name;
        /// <summary>Full price of one copy at the goblin («цена копии»).</summary>
        public readonly int Price;
        /// <summary>Rarity rank 1–5 (the goblin rolls a rank first). Not the copy's level.</summary>
        public readonly int Rank;
        /// <summary>False keeps every copy at level I.</summary>
        public readonly bool Mergeable;
        readonly int[] uses;

        /// <param name="usesPerLevel">Uses of one copy per outing at I / II / III. One for ordinary cards; Teleport 1 / 2 / 3.</param>
        public CardSpec(string id, string name, int price, int rank = 1, bool mergeable = true, IReadOnlyList<int> usesPerLevel = null)
        {
            if (price < 0 || rank < 1 || rank > 5) throw new ArgumentOutOfRangeException(nameof(price));
            Id = id;
            Name = name;
            Price = price;
            Rank = rank;
            Mergeable = mergeable;
            uses = usesPerLevel == null ? new[] { 1, 1, 1 } : usesPerLevel.ToArray();
            if (uses.Length != MaxLevel || uses.Any(u => u < 1)) throw new ArgumentException("One use count per level", nameof(usesPerLevel));
        }

        public int UsesAt(int level) => uses[Math.Max(1, Math.Min(MaxLevel, level)) - 1];

        /// <summary>The goblin's price for a face-down card: half, rounded up to a whole coin.</summary>
        public int BlindPrice => (Price + 1) / 2;
    }

    public sealed class CardCopy
    {
        public int Id { get; }
        public CardSpec Spec { get; }
        /// <summary>1, 2 or 3 (I / II / III).</summary>
        public int Level { get; private set; }
        /// <summary>What was actually paid for this copy (blind purchases are half). Burning costs 75% of it.</summary>
        public int PaidPrice { get; private set; }
        /// <summary>The three starting copies from the cave: the brazier refuses them.</summary>
        public bool Protected { get; private set; }
        /// <summary>Uses left in the current outing (outing snapshots only).</summary>
        public int UsesLeft { get; internal set; }
        public bool IsReturn => Id == -1;
        public string LevelNumeral => Numeral(Level);

        internal CardCopy(int id, CardSpec spec, int level = 1, int paidPrice = 0, bool isProtected = false)
        {
            Id = id;
            Spec = spec;
            Level = level;
            PaidPrice = paidPrice;
            Protected = isProtected;
            UsesLeft = spec.UsesAt(level);
        }

        /// <summary>Merging absorbs the other copy: its price and protection move into this one.</summary>
        internal void Absorb(CardCopy other)
        {
            Level++;
            PaidPrice = checked(PaidPrice + other.PaidPrice);
            Protected |= other.Protected;
            UsesLeft = Spec.UsesAt(Level);
        }

        internal CardCopy Snapshot() => new CardCopy(Id, Spec, Level, PaidPrice, Protected);
        internal static CardCopy Return() => new CardCopy(-1, new CardSpec("return", "Пора домой", 0));

        public static string Numeral(int level) => level >= 3 ? "III" : level == 2 ? "II" : "I";
    }

    /// <summary>Rules of the collection and the deck. The deck limit grows on the PC in steps; the collection holds twice the deck.</summary>
    public sealed class DeckRules
    {
        public const int CollectionFactor = 2;
        public readonly int InitialHand, MaxHand, MaxCardLevel, HandUpgradePrice, StartingCoins;
        readonly int[] deckLimits, deckPrices;

        /// <param name="deckLimit">Deck limit at the start.</param>
        /// <param name="deckSteps">Further deck limits bought on the PC, in order (each above the previous).</param>
        /// <param name="deckStepPrices">Price of each further step.</param>
        public DeckRules(int deckLimit = 12, int initialHand = 3, int maxHand = 5, int maxCardLevel = CardSpec.MaxLevel,
            int handUpgradePrice = 60, int startingCoins = 240, IReadOnlyList<int> deckSteps = null, IReadOnlyList<int> deckStepPrices = null)
        {
            var steps = new List<int> { deckLimit };
            if (deckSteps != null) steps.AddRange(deckSteps);
            var prices = deckStepPrices?.ToArray() ?? Array.Empty<int>();
            if (deckLimit < 1 || initialHand < 1 || maxHand < initialHand || maxHand > deckLimit || maxCardLevel < 1
                || maxCardLevel > CardSpec.MaxLevel || handUpgradePrice < 0 || startingCoins < 0
                || prices.Length != steps.Count - 1 || prices.Any(p => p < 0)
                || steps.Zip(steps.Skip(1), (a, b) => b > a).Any(up => !up))
                throw new ArgumentOutOfRangeException(nameof(deckLimit));
            InitialHand = initialHand;
            MaxHand = maxHand;
            MaxCardLevel = maxCardLevel;
            HandUpgradePrice = handUpgradePrice;
            StartingCoins = startingCoins;
            deckLimits = steps.ToArray();
            deckPrices = prices;
        }

        public int DeckSteps => deckLimits.Length;
        public int DeckLimitAt(int step) => deckLimits[Math.Max(0, Math.Min(deckLimits.Length - 1, step))];
        /// <summary>Price of the step that leads to <paramref name="step"/> (0 for the starting step).</summary>
        public int DeckStepPrice(int step) => step <= 0 || step >= deckLimits.Length ? 0 : deckPrices[step - 1];
        /// <summary>Backwards-compatible: the starting deck limit.</summary>
        public int DeckLimit => deckLimits[0];
    }

    /// <summary>Copies a new game starts with.</summary>
    public readonly struct StartingCopies
    {
        public readonly string Id;
        public readonly int Count, InDeck;
        public readonly bool Protected;
        public StartingCopies(string id, int count, int inDeck, bool isProtected = false)
        {
            Id = id;
            Count = count;
            InDeck = inDeck;
            Protected = isProtected;
        }
    }

    [Serializable]
    public sealed class CopySave
    {
        public int id, level, paid;
        public string definition;
        public bool locked;
    }

    [Serializable]
    public sealed class LootSave
    {
        public string id, name;
        public int count, value;
    }

    /// <summary>
    /// Version 3: copies carry their paid price and protection, the deck limit step is stored. Version 2 (one deck,
    /// upgrade by coins) and version 1 (exploration and combat decks) are still read.
    /// </summary>
    [Serializable]
    public sealed class WorkshopSave
    {
        public const int CurrentVersion = 3;
        public int version = CurrentVersion, coins, nextId, hand, journeys, deckStep;
        /// <summary>Version 1 only: the hands of the former exploration and combat decks.</summary>
        public int explorationHand, combatHand;
        public List<CopySave> copies = new List<CopySave>();
        public List<int> selected = new List<int>();
    }
}
