using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Hakaton.Economy;

namespace Hakaton.Trading
{
    /// <summary>Authoritative in-memory trade state. Rendering, input and wall-clock sampling live outside this class.</summary>
    public sealed class TradingSession
    {
        private readonly Random random;
        private readonly CoinWallet wallet;
        private readonly List<ItemInstance> inventory = new List<ItemInstance>();
        private readonly ItemInstance[] slots;
        private readonly List<ItemDefinition> catalog = new List<ItemDefinition>();
        private readonly List<ItemInstance> expeditionLoot = new List<ItemInstance>();
        private long nextItemId, nextCustomerId, nextDealId, nextExpeditionId;
        public TradeSettings Settings { get; }
        public bool IsOpen { get; private set; }
        public float RemainingSeconds { get; private set; }
        public int Gold => wallet.Balance;
        public IReadOnlyList<ItemInstance> Inventory { get; }
        public IReadOnlyList<ItemInstance> Slots { get; }
        public Customer CurrentCustomer { get; private set; }
        public TradeDeal CurrentDeal { get; private set; }
        public string LastMessage { get; private set; } = "Лавка закрыта. Разложите товары на витрине.";
        public int Revision { get; private set; }
        public int SalesCount { get; private set; }
        public int LastSalePrice { get; private set; }
        public ItemInstance LastSoldItem { get; private set; }
        public int SettlementSequence { get; private set; }
        public CustomerDeparture LastDeparture { get; private set; }
        public int DepartureSequence { get; private set; }
        public long ExpeditionId { get; private set; }
        public int PendingLootCount => expeditionLoot.Count;
        public int TotalStockCount => inventory.Count + DisplayCount;
        public int DisplayCount
        {
            get { int count = 0; foreach (var item in slots) if (item != null) count++; return count; }
        }

        public TradingSession(TradeSettings settings = null, int seed = 1, CoinWallet wallet = null)
        {
            Settings = (settings ?? new TradeSettings()).ValidatedCopy();
            this.wallet = wallet ?? new CoinWallet();
            random = new Random(seed);
            slots = new ItemInstance[Settings.SlotCount];
            Inventory = new ReadOnlyCollection<ItemInstance>(inventory);
            Slots = Array.AsReadOnly(slots);
            RemainingSeconds = Settings.ShiftSeconds;
        }

        public void SeedInventory(ItemDefinition definition, int count = 1)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            Register(definition);
            for (int i = 0; i < count; i++) inventory.Add(new ItemInstance(++nextItemId, definition));
            Revision++;
        }

        public bool PlaceItem(int inventoryIndex, int slotIndex)
        {
            if (IsOpen || ExpeditionId != 0 || !ValidSlot(slotIndex) || slots[slotIndex] != null
                || inventoryIndex < 0 || inventoryIndex >= inventory.Count) return false;
            slots[slotIndex] = inventory[inventoryIndex];
            inventory.RemoveAt(inventoryIndex);
            LastMessage = $"На витрине: {slots[slotIndex].Definition.Name}.";
            Revision++;
            return true;
        }

        public bool RemoveItem(int slotIndex)
        {
            if (IsOpen || ExpeditionId != 0 || !ValidSlot(slotIndex) || slots[slotIndex] == null) return false;
            inventory.Add(slots[slotIndex]); slots[slotIndex] = null;
            LastMessage = "Предмет возвращён в запас.";
            Revision++;
            return true;
        }

        public bool Open()
        {
            if (IsOpen || ExpeditionId != 0) return false;
            if (RemainingSeconds <= 0) { LastMessage = "Смена окончена. Вернитесь из вылазки с новой добычей."; return false; }
            if (DisplayCount == 0) { LastMessage = "Сначала выставьте хотя бы один предмет."; return false; }
            IsOpen = true; Revision++;
            AdvanceCustomer();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            RecordDeparture(CustomerOutcome.ShopClosed);
            IsOpen = false; CurrentCustomer = null; CurrentDeal = null;
            LastMessage = "Лавка закрыта. Непроданные товары и остаток времени сохранены.";
            Revision++;
        }

        public void Tick(float elapsedSeconds)
        {
            if (elapsedSeconds < 0 || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!IsOpen) return;
            RemainingSeconds = Math.Max(0, RemainingSeconds - elapsedSeconds);
            // Selecting an item for a category visitor is already part of the active conversation.
            if (RemainingSeconds <= 0 && CurrentCustomer == null) FinishShift("Время смены закончилось.");
        }

        public bool AdvanceCustomer(CustomerTerms terms = null)
        {
            if (!IsOpen || CurrentCustomer != null) return false;
            if (RemainingSeconds <= 0 || DisplayCount == 0)
            {
                FinishShift(RemainingSeconds <= 0 ? "Время смены закончилось." : "Витрина пуста. Лавка закрыта для выкладки.");
                return false;
            }
            var nextTerms = terms ?? GenerateCustomer();
            ValidateCustomerTerms(nextTerms);
            CurrentCustomer = new Customer(++nextCustomerId, nextTerms);
            LastMessage = CurrentCustomer.RequestedSlot >= 0
                ? "Покупатель выбрал предмет на витрине."
                : $"Ищу товар категории «{CurrentCustomer.RequestedCategory}». Что предложите?";
            Revision++;
            if (CurrentCustomer.RequestedSlot >= 0)
                SelectItem(CurrentCustomer.RequestedSlot, CurrentCustomer.Id);
            return true;
        }

        public bool SelectItem(int slotIndex, long customerId)
        {
            if (!IsOpen || CurrentCustomer == null || CurrentCustomer.Id != customerId || CurrentDeal != null
                || !ValidSlot(slotIndex) || slots[slotIndex] == null) return false;
            var customer = CurrentCustomer;
            if (customer.RequestedSlot >= 0 && customer.RequestedSlot != slotIndex) return false;
            if (customer.RequestedSlot < 0 && slots[slotIndex].Definition.Category != customer.RequestedCategory) return false;
            CurrentDeal = new TradeDeal(++nextDealId, customer, slots[slotIndex], slotIndex);
            LastMessage = CurrentDeal.Reaction;
            Revision++;
            return true;
        }

        public bool Offer(long dealId, int revision, int optionIndex)
        {
            if (!MatchesDeal(dealId, revision)) return false;
            var deal = CurrentDeal;
            var options = deal.GetPriceOptions();
            if (optionIndex < 0 || optionIndex >= options.Length || !options[optionIndex].Enabled) return false;
            int price = options[optionIndex].Price;
            deal.Round++; deal.Revision++; Revision++;
            if (deal.IsLastOffer)
            {
                EndWithoutSale("Хватит. Я ухожу.", CustomerOutcome.WalkedAway);
                return true;
            }
            // The first answer at R is guaranteed only for a visitor who asks the price.
            if (CurrentCustomer.AsksPrice && deal.Round == 1 && optionIndex == 0)
            {
                Settle(price); return true;
            }
            int willing = Math.Min(deal.MaxPrice, TradeDeal.RoundMoney(deal.R * (.94 + deal.Round * .055)));
            if (price <= willing) { Settle(price); return true; }

            int patienceCost = 1 + (price > deal.MaxPrice ? 1 : 0)
                + (price > deal.R * 1.35 ? 1 : 0);
            deal.Patience = Math.Max(0, deal.Patience - patienceCost);
            int previous = deal.N ?? Math.Min(deal.MaxPrice, deal.R);
            int increment = random.NextDouble() < .2 ? 0 : Math.Max(1, TradeDeal.RoundMoney(deal.R * Settings.CounterStepRatio));
            // Never offer the requested price as a counter: agreeing is a settlement.
            deal.N = Math.Max(1, Math.Min(price - 1, Math.Min(deal.MaxPrice, previous + increment)));
            deal.IsLastOffer = deal.Patience == 0;
            deal.Mood = deal.IsLastOffer || deal.Patience <= 1 ? CustomerMood.Angry : CustomerMood.Displeased;
            deal.Reaction = deal.IsLastOffer ? $"Последнее предложение: {deal.N}. Больше не дам."
                : deal.Patience <= 1 ? $"Не испытывай терпение. Дам {deal.N}."
                : $"Дороговато. Предлагаю {deal.N}.";
            LastMessage = deal.Reaction;
            return true;
        }

        public bool Accept(long dealId, int revision)
        {
            if (!MatchesDeal(dealId, revision) || !CurrentDeal.N.HasValue) return false;
            Settle(CurrentDeal.N.Value);
            return true;
        }

        public bool Refuse(long customerId)
        {
            if (!IsOpen || CurrentCustomer == null || CurrentCustomer.Id != customerId) return false;
            EndWithoutSale("Покупатель ушёл. Товар остался в лавке.", CustomerOutcome.Refused);
            return true;
        }

        public long BeginExpedition()
        {
            if (IsOpen || ExpeditionId != 0) return 0;
            ExpeditionId = ++nextExpeditionId;
            expeditionLoot.Clear();
            LastMessage = "Вылазка началась. Найдите новую добычу и вернитесь в лавку.";
            Revision++;
            return ExpeditionId;
        }

        public bool CollectLoot(long expeditionId, ItemDefinition definition)
        {
            if (definition == null || expeditionId == 0 || expeditionId != ExpeditionId) return false;
            Register(definition);
            expeditionLoot.Add(new ItemInstance(++nextItemId, definition));
            LastMessage = $"Новая добыча: {definition.Name}.";
            Revision++;
            return true;
        }

        public bool CompleteExpedition(long expeditionId)
        {
            if (IsOpen || expeditionId == 0 || expeditionId != ExpeditionId) return false;
            bool freshLoot = expeditionLoot.Count > 0;
            inventory.AddRange(expeditionLoot); expeditionLoot.Clear(); ExpeditionId = 0;
            if (freshLoot) RemainingSeconds = Settings.ShiftSeconds;
            LastMessage = freshLoot ? "Новая добыча доставлена. Время торговли восстановлено."
                : "Вернулись без новой добычи. Остаток времени не изменился.";
            Revision++;
            return true;
        }

        private bool MatchesDeal(long id, int revision) => IsOpen && CurrentDeal != null
            && CurrentDeal.Id == id && CurrentDeal.Revision == revision;

        private void Settle(int price)
        {
            var deal = CurrentDeal;
            // The displayed instance is the transaction's authority: one removal, one payment.
            if (deal == null || slots[deal.SlotIndex] != deal.Item) return;
            wallet.Credit(price);
            slots[deal.SlotIndex] = null;
            SalesCount++; SettlementSequence++;
            LastSalePrice = price; LastSoldItem = deal.Item;
            deal.Mood = CustomerMood.Happy;
            RecordDeparture(CustomerOutcome.Purchased);
            CurrentDeal = null; CurrentCustomer = null; Revision++;
            LastMessage = $"Продано: {deal.Item.Definition.Name} за {price} монет.";
            if (RemainingSeconds <= 0 || DisplayCount == 0)
                FinishShift(LastMessage + (RemainingSeconds <= 0 ? " Смена окончена." : " Витрина пуста."));
        }

        private void EndWithoutSale(string message, CustomerOutcome outcome)
        {
            RecordDeparture(outcome);
            CurrentDeal = null; CurrentCustomer = null; LastMessage = message; Revision++;
            if (RemainingSeconds <= 0) FinishShift(message + " Смена окончена.");
        }

        private void FinishShift(string message)
        {
            RecordDeparture(CustomerOutcome.ShopClosed);
            IsOpen = false; CurrentDeal = null; CurrentCustomer = null; LastMessage = message; Revision++;
        }

        private void RecordDeparture(CustomerOutcome outcome)
        {
            if (CurrentCustomer == null) return;
            LastDeparture = new CustomerDeparture(CurrentCustomer.Id, outcome, ++DepartureSequence);
        }

        private CustomerTerms GenerateCustomer()
        {
            var occupied = new List<int>();
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) occupied.Add(i);
            int selected = occupied[random.Next(occupied.Count)];
            bool categoryRequest = random.Next(3) == 0;
            string category = categoryRequest ? catalog[random.Next(catalog.Count)].Category : null;
            int referencePrice = slots[selected].Definition.RecommendedPrice;
            int minimumPrice = referencePrice;
            if (categoryRequest)
            {
                referencePrice = 1;
                minimumPrice = int.MaxValue;
                foreach (var definition in catalog)
                {
                    if (definition.Category != category) continue;
                    referencePrice = Math.Max(referencePrice, definition.RecommendedPrice);
                    minimumPrice = Math.Min(minimumPrice, definition.RecommendedPrice);
                }
            }
            // A one-coin item has no positive integer price below R. Such requests ask the price.
            bool asksPrice = random.Next(2) == 0 || minimumPrice <= 1;
            float ceilingRatio = Range(Settings.MinCeilingRatio, Settings.MaxCeilingRatio);
            float openingRatio = Range(Settings.MinOpeningRatio, Settings.MaxOpeningRatio);
            return new CustomerTerms
            {
                Name = new[] { "Охотник", "Собиратель", "Путник", "Сосед" }[random.Next(4)],
                RequestedCategory = category,
                RequestedSlot = categoryRequest ? -1 : selected,
                AsksPrice = asksPrice,
                OpeningRatio = openingRatio,
                MaxPrice = Math.Max(asksPrice ? referencePrice : Math.Max(1, TradeDeal.RoundMoney(referencePrice * openingRatio)), TradeDeal.RoundMoney(referencePrice * ceilingRatio)),
                Patience = random.Next(Settings.MinPatience, Settings.MaxPatience + 1)
            };
        }

        private void ValidateCustomerTerms(CustomerTerms terms)
        {
            if (terms.MaxPrice < 1 || terms.Patience < 1 || terms.OpeningRatio <= 0 || terms.OpeningRatio >= 1
                || float.IsNaN(terms.OpeningRatio) || string.IsNullOrWhiteSpace(terms.Name))
                throw new ArgumentException("Invalid customer terms.", nameof(terms));
            int maxR = 0;
            int minR = int.MaxValue;
            if (terms.RequestedSlot >= 0)
            {
                if (!ValidSlot(terms.RequestedSlot) || slots[terms.RequestedSlot] == null)
                    throw new ArgumentException("The requested slot must contain an item.", nameof(terms));
                maxR = slots[terms.RequestedSlot].Definition.RecommendedPrice;
                minR = maxR;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(terms.RequestedCategory))
                    throw new ArgumentException("A category request needs a category.", nameof(terms));
                foreach (var item in slots)
                    if (item != null && item.Definition.Category == terms.RequestedCategory)
                    {
                        maxR = Math.Max(maxR, item.Definition.RecommendedPrice);
                        minR = Math.Min(minR, item.Definition.RecommendedPrice);
                    }
            }
            if (!terms.AsksPrice && minR <= 1)
                throw new ArgumentException("One-coin goods require a price question; no lower positive opening exists.", nameof(terms));
            if (terms.AsksPrice && terms.MaxPrice < maxR)
                throw new ArgumentException("Price-asking visitors must be able to pay R.", nameof(terms));
            if (!terms.AsksPrice && maxR > 0 && terms.MaxPrice < Math.Max(1, TradeDeal.RoundMoney(maxR * terms.OpeningRatio)))
                throw new ArgumentException("The opening offer cannot exceed the budget.", nameof(terms));
        }

        private bool ValidSlot(int index) => index >= 0 && index < slots.Length;
        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        private void Register(ItemDefinition definition)
        {
            foreach (var known in catalog)
            {
                if (known.Id != definition.Id) continue;
                if (known.Name != definition.Name || known.Category != definition.Category || known.RecommendedPrice != definition.RecommendedPrice)
                    throw new ArgumentException("An item type must keep the same recommended price and metadata.");
                return;
            }
            catalog.Add(definition);
        }
    }
}
