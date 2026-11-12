using System;
using System.Collections.Generic;

namespace Hakaton.Trading
{
    public enum CustomerMood
    {
        Neutral,
        Displeased,
        Angry,
        Happy
    }

    public enum CustomerOutcome
    {
        Purchased,
        Refused,
        WalkedAway,
        ShopClosed
    }

    /// <summary>The last completed visit, retained after its customer and deal are cleared.</summary>
    public sealed class CustomerDeparture
    {
        public long CustomerId { get; }
        public CustomerOutcome Outcome { get; }
        public int Sequence { get; }
        /// <summary>The visitor was the bulk buyer («Скупщик»): he takes a refusal calmly.</summary>
        public bool BulkBuyer { get; }

        internal CustomerDeparture(long customerId, CustomerOutcome outcome, int sequence, bool bulkBuyer = false)
        {
            CustomerId = customerId;
            Outcome = outcome;
            Sequence = sequence;
            BulkBuyer = bulkBuyer;
        }
    }

    [Serializable]
    public sealed class TradeSettings
    {
        public int SlotCount { get; set; } = 6;
        public float ShiftSeconds { get; set; } = 120f;
        public float MinOpeningRatio { get; set; } = .7f;
        public float MaxOpeningRatio { get; set; } = .9f;
        public float MinCeilingRatio { get; set; } = 1f;
        public float MaxCeilingRatio { get; set; } = 1.4f;
        public float MinOfferCeilingRatio { get; set; } = .95f;
        public float MaxOfferCeilingRatio { get; set; } = 1.2f;
        public float AsksPriceProbability { get; set; } = .5f;
        public bool UsePresetPatienceCosts { get; set; }
        public int MinPatience { get; set; } = 3;
        public int MaxPatience { get; set; } = 5;
        public float CounterStepRatio { get; set; } = .08f;
        /// <summary>Seconds each shift upgrade (bought on the PC, «Время смены») adds to the base shift, in order.</summary>
        public float[] ShiftUpgradeSeconds { get; set; } = Array.Empty<float>();
        /// <summary>Price of each shift upgrade: one per entry of <see cref="ShiftUpgradeSeconds"/>.</summary>
        public int[] ShiftUpgradePrices { get; set; } = Array.Empty<int>();
        /// <summary>
        /// Chance (0..1) that a generated visitor is the rare bulk buyer («Скупщик»), before upgrades. He comes only once
        /// the session unlocks him (<see cref="TradingSession.BulkBuyerUnlockRevenue"/>), at most once a shift.
        /// </summary>
        public float BulkBuyerChance { get; set; } = .05f;
        /// <summary>Chance each bulk buyer upgrade (bought on the PC, «Скупщик») adds, in order.</summary>
        public float[] BulkBuyerUpgradeChances { get; set; } = Array.Empty<float>();
        /// <summary>Price of each bulk buyer upgrade: one per entry of <see cref="BulkBuyerUpgradeChances"/>.</summary>
        public int[] BulkBuyerUpgradePrices { get; set; } = Array.Empty<int>();
        /// <summary>The bulk buyer comes for a display with at least this many items.</summary>
        public int BulkBuyerMinDisplay { get; set; } = 3;
        /// <summary>Share of each item's recommended price the bulk buyer pays (at least one coin an item).</summary>
        public float BulkBuyerPriceRatio { get; set; } = .75f;

        internal TradeSettings ValidatedCopy()
        {
            if (SlotCount < 1 || SlotCount > 64 || !Positive(ShiftSeconds)
                || !Positive(MinOpeningRatio) || MaxOpeningRatio < MinOpeningRatio
                || MaxOpeningRatio >= 1 || !Positive(MaxOpeningRatio)
                || !Positive(MinCeilingRatio) || !Positive(MaxCeilingRatio)
                || !Positive(MinOfferCeilingRatio) || !Positive(MaxOfferCeilingRatio)
                || MaxOfferCeilingRatio < MinOfferCeilingRatio
                || float.IsNaN(AsksPriceProbability) || AsksPriceProbability < 0 || AsksPriceProbability > 1
                || MaxCeilingRatio < MinCeilingRatio || MinPatience < 1
                || MaxPatience < MinPatience || MaxPatience == int.MaxValue
                || !Positive(CounterStepRatio))
                throw new ArgumentOutOfRangeException(nameof(TradeSettings), "Invalid trade tuning.");
            var upgradeSeconds = ShiftUpgradeSeconds ?? Array.Empty<float>();
            var upgradePrices = ShiftUpgradePrices ?? Array.Empty<int>();
            float longestShift = ShiftSeconds;
            foreach (float seconds in upgradeSeconds) longestShift = Positive(seconds) ? longestShift + seconds : float.NaN;
            if (upgradeSeconds.Length != upgradePrices.Length || !Positive(longestShift)
                || Array.Exists(upgradePrices, price => price < 0))
                throw new ArgumentOutOfRangeException(nameof(TradeSettings), "Invalid shift upgrades.");
            var buyerChances = BulkBuyerUpgradeChances ?? Array.Empty<float>();
            var buyerPrices = BulkBuyerUpgradePrices ?? Array.Empty<int>();
            float highestChance = BulkBuyerChance;
            foreach (float chance in buyerChances) highestChance = Positive(chance) ? highestChance + chance : float.NaN;
            if (float.IsNaN(BulkBuyerChance) || BulkBuyerChance < 0 || BulkBuyerChance > 1
                || float.IsNaN(highestChance) || highestChance > 1.0001f
                || buyerChances.Length != buyerPrices.Length || Array.Exists(buyerPrices, price => price < 0)
                || BulkBuyerMinDisplay < 1 || !Positive(BulkBuyerPriceRatio) || BulkBuyerPriceRatio > 1)
                throw new ArgumentOutOfRangeException(nameof(TradeSettings), "Invalid bulk buyer tuning.");
            var copy = (TradeSettings)MemberwiseClone();
            copy.ShiftUpgradeSeconds = (float[])upgradeSeconds.Clone();
            copy.ShiftUpgradePrices = (int[])upgradePrices.Clone();
            copy.BulkBuyerUpgradeChances = (float[])buyerChances.Clone();
            copy.BulkBuyerUpgradePrices = (int[])buyerPrices.Clone();
            return copy;
        }

        private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Persistent shop state. Coins belong to the shared game wallet, not this snapshot.</summary>
    [Serializable]
    public sealed class TradingSave
    {
        public int version = 1;
        public float remainingSeconds;
        public long grossRevenue, nextItemId, nextExpeditionId;
        public int salesCount;
        /// <summary>Shift upgrades bought; saves older than the upgrade load as 0 (the base shift).</summary>
        public int shiftLevel;
        /// <summary>Bulk buyer («Скупщик») chance upgrades bought; older saves load as 0 (the base chance).</summary>
        public int bulkBuyerLevel;
        /// <summary>The player has met the bulk buyer: he no longer explains himself, and the PC sells his upgrade.</summary>
        public bool bulkBuyerMet;
        /// <summary>The bulk buyer has already come during the current shift.</summary>
        public bool bulkBuyerThisShift;
        public List<TradingItemSave> items = new List<TradingItemSave>();
    }

    [Serializable]
    public sealed class TradingItemSave
    {
        public long instanceId;
        public string definitionId;
        // Inventory entries retain their list order; nonnegative values identify display slots.
        public int slot = -1;
    }

    [Serializable]
    public sealed class StockDelivery
    {
        public string definitionId;
        public int count;
        public StockDelivery(string definitionId, int count)
        { this.definitionId = definitionId; this.count = count; }
    }

    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Category { get; }
        public int RecommendedPrice { get; }

        public ItemDefinition(string id, string name, string category, int recommendedPrice)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
                throw new ArgumentException("An item needs an id, name and category.");
            if (recommendedPrice < 1 || recommendedPrice > 1000000)
                throw new ArgumentOutOfRangeException(nameof(recommendedPrice));
            Id = id; Name = name; Category = category; RecommendedPrice = recommendedPrice;
        }
    }

    public sealed class ItemInstance
    {
        public long Id { get; }
        public ItemDefinition Definition { get; }
        internal ItemInstance(long id, ItemDefinition definition) { Id = id; Definition = definition; }
    }

    // Optional authored visitor terms also let automated tests exercise exact conversations.
    public sealed class CustomerTerms
    {
        public string Name { get; set; } = "Посетитель";
        /// <summary>The display slot whose item the visitor asks for; it must hold an item when he arrives.</summary>
        public int RequestedSlot { get; set; } = -1;
        public bool AsksPrice { get; set; } = true;
        public float OpeningRatio { get; set; } = .8f;
        public int MaxPrice { get; set; } = 130;
        public int Patience { get; set; } = 4;
        /// <summary>Exact, staged answers instead of patience and chance (the tutorial's two lessons).</summary>
        public ScriptedHaggle Script { get; set; }
        /// <summary>
        /// The rare bulk buyer («Скупщик»): he names no item and never haggles, he offers to take the whole display at a
        /// discount. The item and haggle terms above are ignored for him.
        /// </summary>
        public bool BulkBuyer { get; set; }
    }

    /// <summary>
    /// A staged visitor (board, «Туториал», 06): prices up to <see cref="AcceptUpTo"/> sell at once; from
    /// <see cref="LeaveFrom"/> the visitor walks out; anything between gets <see cref="LastOffer"/> as the last word —
    /// accepting it sells, a new price after it ends the visit.
    /// </summary>
    public sealed class ScriptedHaggle
    {
        public int AcceptUpTo { get; }
        public int LastOffer { get; }
        public int LeaveFrom { get; }
        public ScriptedHaggle(int acceptUpTo, int lastOffer, int leaveFrom)
        {
            if (acceptUpTo < 1 || lastOffer < 1 || leaveFrom <= acceptUpTo) throw new ArgumentOutOfRangeException(nameof(acceptUpTo));
            AcceptUpTo = acceptUpTo;
            LastOffer = lastOffer;
            LeaveFrom = leaveFrom;
        }
    }

    public sealed class Customer
    {
        public long Id { get; }
        public string Name { get; }
        public int RequestedSlot { get; }
        public bool AsksPrice { get; }
        public int MaxPrice { get; }
        public int InitialPatience { get; }
        public ScriptedHaggle Script { get; }
        internal float OpeningRatio { get; }
        /// <summary>The bulk buyer («Скупщик»): no deal, one offer for the whole display.</summary>
        public bool IsBulkBuyer { get; }
        /// <summary>What the bulk buyer pays for the whole display, fixed when he walks in; 0 for other visitors.</summary>
        public int BulkPrice { get; internal set; }
        /// <summary>Items on the display when the bulk buyer walked in (all of them go); 0 for other visitors.</summary>
        public int BulkCount { get; internal set; }
        /// <summary>The bulk buyer meets the player for the first time and explains himself before the offer.</summary>
        public bool FirstMeeting { get; internal set; }
        /// <summary>
        /// The bulk buyer's speech in order, read on with a press: on a first meeting who he is and how he trades, then the
        /// offer; later only the offer. The last line is always the offer. Empty for other visitors.
        /// </summary>
        public IReadOnlyList<string> Lines { get; internal set; } = Array.Empty<string>();

        internal Customer(long id, CustomerTerms terms)
        {
            Id = id; Name = terms.Name;
            IsBulkBuyer = terms.BulkBuyer;
            RequestedSlot = terms.BulkBuyer ? -1 : terms.RequestedSlot; AsksPrice = terms.AsksPrice;
            MaxPrice = terms.MaxPrice; InitialPatience = terms.Patience; OpeningRatio = terms.OpeningRatio;
            Script = terms.BulkBuyer ? null : terms.Script;
        }
    }

    public readonly struct PriceOption
    {
        public int Percent { get; }
        public int Price { get; }
        public bool Enabled { get; }
        internal PriceOption(int percent, int price, bool enabled) { Percent = percent; Price = price; Enabled = enabled; }
    }

    public sealed class TradeDeal
    {
        private static readonly int[] Percentages = { 0, 5, 15, 25, 50 };
        public long Id { get; }
        public int Revision { get; internal set; }
        public ItemInstance Item { get; }
        public int SlotIndex { get; }
        public int R => Item.Definition.RecommendedPrice;
        public int B { get; }
        public int? N { get; internal set; }
        public int Round { get; internal set; }
        public bool IsLastOffer { get; internal set; }
        public CustomerMood Mood { get; internal set; } = CustomerMood.Neutral;
        public string Reaction { get; internal set; }
        public int Patience { get; internal set; }
        public int MaxPrice { get; }
        /// <summary>Prices up to this are accepted outright; drifts towards MaxPrice as the haggle goes on.</summary>
        internal float Comfort { get; set; }
        internal int LastAsk { get; set; } = -1;

        internal TradeDeal(long id, Customer customer, ItemInstance item, int slot)
        {
            Id = id; Revision = 1; Item = item; SlotIndex = slot;
            B = customer.AsksPrice ? R : Math.Max(1, Math.Min(R - 1, RoundMoney(R * customer.OpeningRatio)));
            N = customer.AsksPrice ? (int?)null : B;
            Patience = customer.InitialPatience; MaxPrice = customer.MaxPrice;
            // The visitor names the item himself; the name leads the line as is (no Russian case endings needed).
            Reaction = customer.AsksPrice ? $"{item.Definition.Name} — сколько стоит?"
                : $"{item.Definition.Name} — дам {B} монет. Договорились?";
            Comfort = customer.AsksPrice ? R : B;
        }

        public PriceOption[] GetPriceOptions()
        {
            var result = new PriceOption[Percentages.Length];
            int previous = -1;
            for (int i = 0; i < Percentages.Length; i++)
            {
                int price = RoundMoney(B * (100 + Percentages[i]) / 100.0);
                result[i] = new PriceOption(Percentages[i], price, price != previous && (!N.HasValue || price > N.Value));
                previous = price;
            }
            return result;
        }

        internal static int RoundMoney(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
