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

        internal CustomerDeparture(long customerId, CustomerOutcome outcome, int sequence)
        {
            CustomerId = customerId;
            Outcome = outcome;
            Sequence = sequence;
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
            return (TradeSettings)MemberwiseClone();
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
        public string RequestedCategory { get; set; }
        public int RequestedSlot { get; set; } = -1;
        public bool AsksPrice { get; set; } = true;
        public float OpeningRatio { get; set; } = .8f;
        public int MaxPrice { get; set; } = 130;
        public int Patience { get; set; } = 4;
    }

    public sealed class Customer
    {
        public long Id { get; }
        public string Name { get; }
        public string RequestedCategory { get; }
        public int RequestedSlot { get; }
        public bool AsksPrice { get; }
        public int MaxPrice { get; }
        public int InitialPatience { get; }
        internal float OpeningRatio { get; }

        internal Customer(long id, CustomerTerms terms)
        {
            Id = id; Name = terms.Name; RequestedCategory = terms.RequestedCategory;
            RequestedSlot = terms.RequestedSlot; AsksPrice = terms.AsksPrice;
            MaxPrice = terms.MaxPrice; InitialPatience = terms.Patience; OpeningRatio = terms.OpeningRatio;
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

        internal TradeDeal(long id, Customer customer, ItemInstance item, int slot)
        {
            Id = id; Revision = 1; Item = item; SlotIndex = slot;
            B = customer.AsksPrice ? R : Math.Max(1, Math.Min(R - 1, RoundMoney(R * customer.OpeningRatio)));
            N = customer.AsksPrice ? (int?)null : B;
            Patience = customer.InitialPatience; MaxPrice = customer.MaxPrice;
            Reaction = customer.AsksPrice ? "Сколько стоит?" : $"Дам {B} монет. Договорились?";
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
