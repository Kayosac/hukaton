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
        public event Action Changed;
        /// <summary>Supplies the next visitor's terms (staged visitors); null terms fall back to a random visitor.</summary>
        public Func<CustomerTerms> CustomerScript { get; set; }
        /// <summary>The shift clock stands still (the tutorial's lessons take as long as they take).</summary>
        public bool ClockPaused { get; set; }
        public int SalesCount { get; private set; }
        public long GrossRevenue { get; private set; }
        public int LastSalePrice { get; private set; }
        /// <summary>The item of the last sale; after a bulk sale, the last of its items.</summary>
        public ItemInstance LastSoldItem { get; private set; }
        /// <summary>Every item of the last sale: one, or the whole display the bulk buyer took.</summary>
        public IReadOnlyList<ItemInstance> LastSoldItems { get; private set; } = Array.Empty<ItemInstance>();
        /// <summary>The last sale was the bulk buyer's: the whole display at once.</summary>
        public bool LastSaleBulk { get; private set; }
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

        // ---------- shift length («Время смены» on the PC) ----------

        /// <summary>Shift upgrades bought so far (0: the base shift).</summary>
        public int ShiftLevel { get; private set; }
        /// <summary>Shift upgrades there are to buy in total.</summary>
        public int ShiftSteps => Settings.ShiftUpgradeSeconds.Length;
        public bool ShiftAtMaximum => ShiftLevel >= ShiftSteps;
        /// <summary>A full shift now: the base length plus every bought upgrade.</summary>
        public float ShiftLength => ShiftLengthAt(ShiftLevel);
        /// <summary>A full shift after the next upgrade (the current length once maxed).</summary>
        public float NextShiftLength => ShiftLengthAt(Math.Min(ShiftLevel + 1, ShiftSteps));
        /// <summary>Price of the next upgrade; 0 once maxed.</summary>
        public int ShiftPrice => ShiftAtMaximum ? 0 : Settings.ShiftUpgradePrices[ShiftLevel];

        public float ShiftLengthAt(int level)
        {
            float seconds = Settings.ShiftSeconds;
            for (int i = 0; i < level && i < ShiftSteps; i++) seconds += Settings.ShiftUpgradeSeconds[i];
            return seconds;
        }

        /// <summary>
        /// Buys the next shift upgrade with the shared wallet (the PC), only between outings with the shop closed. Time
        /// still left in the current shift grows by the bought seconds; a finished shift waits for fresh loot as before.
        /// Returns an error to show, or null.
        /// </summary>
        public string ImproveShift()
        {
            if (ExpeditionId != 0) return "Сначала вернитесь в лавку.";
            if (IsOpen) return "Сначала закройте лавку.";
            if (ShiftAtMaximum) return "Смена уже максимальной длины.";
            if (!wallet.TrySpend(ShiftPrice)) return "Не хватает монет.";
            float added = Settings.ShiftUpgradeSeconds[ShiftLevel];
            ShiftLevel++;
            if (RemainingSeconds > 0) RemainingSeconds = Math.Min(ShiftLength, RemainingSeconds + added);
            Touch();
            return null;
        }

        /// <summary>Debug/cheat helper: every shift upgrade for free; time left in the shift grows the same way.</summary>
        public bool MaximizeShift()
        {
            if (ShiftAtMaximum) return false;
            float added = ShiftLengthAt(ShiftSteps) - ShiftLength;
            ShiftLevel = ShiftSteps;
            if (RemainingSeconds > 0) RemainingSeconds = Math.Min(ShiftLength, RemainingSeconds + added);
            Touch();
            return true;
        }

        // ---------- the bulk buyer («Скупщик») ----------

        /// <summary>
        /// Gross revenue from which the bulk buyer may come. The game loop sets its first revenue goal here; the default
        /// keeps him away (scenes without the loop, tests that do not ask for him).
        /// </summary>
        public long BulkBuyerUnlockRevenue { get; set; } = long.MaxValue;
        public bool BulkBuyerUnlocked => GrossRevenue >= BulkBuyerUnlockRevenue;
        /// <summary>The player has met him once: later visits skip the explanation, the PC sells his upgrade.</summary>
        public bool BulkBuyerMet { get; private set; }
        /// <summary>He has already come during this shift (a new shift starts with fresh loot from an outing).</summary>
        public bool BulkBuyerCameThisShift { get; private set; }
        /// <summary>Debug/cheat: the next generated visitor is the bulk buyer, whatever the chance, goal or shift.</summary>
        public bool ForceBulkBuyerNext { get; set; }
        /// <summary>Chance upgrades bought on the PC (0: the base chance).</summary>
        public int BulkBuyerLevel { get; private set; }
        public int BulkBuyerSteps => Settings.BulkBuyerUpgradeChances.Length;
        public bool BulkBuyerAtMaximum => BulkBuyerLevel >= BulkBuyerSteps;
        /// <summary>Chance (0..1) that a generated visitor is the bulk buyer now.</summary>
        public float BulkBuyerChance => BulkBuyerChanceAt(BulkBuyerLevel);
        /// <summary>The chance after the next upgrade (the current one once maxed).</summary>
        public float NextBulkBuyerChance => BulkBuyerChanceAt(Math.Min(BulkBuyerLevel + 1, BulkBuyerSteps));
        /// <summary>Price of the next chance upgrade; 0 once maxed.</summary>
        public int BulkBuyerUpgradePrice => BulkBuyerAtMaximum ? 0 : Settings.BulkBuyerUpgradePrices[BulkBuyerLevel];

        public float BulkBuyerChanceAt(int level)
        {
            float chance = Settings.BulkBuyerChance;
            for (int i = 0; i < level && i < BulkBuyerSteps; i++) chance += Settings.BulkBuyerUpgradeChances[i];
            return Math.Min(1f, chance);
        }

        /// <summary>Buys the next bulk buyer chance upgrade with the shared wallet (the PC). Returns an error to show, or null.</summary>
        public string ImproveBulkBuyer()
        {
            if (ExpeditionId != 0) return "Сначала вернитесь в лавку.";
            if (IsOpen) return "Сначала закройте лавку.";
            if (BulkBuyerAtMaximum) return "Скупщик и так заходит как можно чаще.";
            if (!wallet.TrySpend(BulkBuyerUpgradePrice)) return "Не хватает монет.";
            BulkBuyerLevel++;
            Touch();
            return null;
        }

        /// <summary>Debug/cheat helper: every bulk buyer chance upgrade for free.</summary>
        public bool MaximizeBulkBuyer()
        {
            if (BulkBuyerAtMaximum) return false;
            BulkBuyerLevel = BulkBuyerSteps;
            Touch();
            return true;
        }

        /// <summary>
        /// What the bulk buyer pays for the display as it stands: every item at <see cref="TradeSettings.BulkBuyerPriceRatio"/>
        /// of its recommended price, rounded half away from zero, at least one coin an item.
        /// </summary>
        public int BulkOfferForDisplay()
        {
            long total = 0;
            foreach (var item in slots)
                if (item != null)
                    total += Math.Max(1, TradeDeal.RoundMoney(item.Definition.RecommendedPrice * (double)Settings.BulkBuyerPriceRatio));
            return (int)Math.Min(int.MaxValue, total);
        }

        /// <summary>The bulk buyer takes the whole display for his offer. False if he is not the visitor at the counter.</summary>
        public bool AcceptBulkOffer(long customerId)
        {
            var customer = CurrentCustomer;
            if (!IsOpen || customer == null || customer.Id != customerId || !customer.IsBulkBuyer) return false;
            // The display cannot change while the shop is open; still, the offer is only good for the goods it was made for.
            if (DisplayCount != customer.BulkCount || BulkOfferForDisplay() != customer.BulkPrice) return false;
            int price = customer.BulkPrice, count = customer.BulkCount;
            long nextRevenue = checked(GrossRevenue + price);
            int nextSalesCount = checked(SalesCount + count);
            wallet.Credit(price);
            var sold = new List<ItemInstance>(count);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null) { sold.Add(slots[i]); slots[i] = null; }
            // Each item counts as a sale, as if sold one by one.
            GrossRevenue = nextRevenue; SalesCount = nextSalesCount; SettlementSequence++;
            LastSalePrice = price; LastSoldItem = sold[sold.Count - 1]; LastSoldItems = sold.AsReadOnly(); LastSaleBulk = true;
            BulkBuyerMet = true;
            RecordDeparture(CustomerOutcome.Purchased);
            CurrentDeal = null; CurrentCustomer = null; Touch();
            LastMessage = BulkSaleNotice(count, price);
            FinishShift(LastMessage + (RemainingSeconds <= 0 ? " Смена окончена." : " Витрина пуста."));
            return true;
        }

        /// <summary>«Скупщик забрал всё: 5 шт. за 210 монет.»</summary>
        public static string BulkSaleNotice(int count, int price) => $"Скупщик забрал всё: {count} шт. за {price} {CoinsAccusative(price)}.";

        /// <summary>"монет" after a count, nominative: 1 монета, 2 монеты, 5 монет.</summary>
        static string Coins(int n) => Plural(n, "монета", "монеты", "монет");
        /// <summary>"монет" after «за» + a count: за 1 монету, за 2 монеты, за 5 монет.</summary>
        static string CoinsAccusative(int n) => Plural(n, "монету", "монеты", "монет");

        static string Plural(int n, string one, string few, string many)
        {
            int last = Math.Abs(n) % 10, lastTwo = Math.Abs(n) % 100;
            return last == 1 && lastTwo != 11 ? one : last >= 2 && last <= 4 && (lastTwo < 12 || lastTwo > 14) ? few : many;
        }

        static readonly string[] BulkBuyerIntroLines =
        {
            "Я скупщик. Забираю всё, что лежит на витрине. Разом.",
            "Плачу три четверти цены. Зато сразу и за всё.",
            "Не торгуюсь. Да — да, нет — нет."
        };

        static string[] BulkBuyerLines(bool firstMeeting, int price)
        {
            string coins = $"{price} {Coins(price)}";
            if (!firstMeeting) return new[] { $"Снова я. Вся витрина — {coins}. По рукам?" };
            var lines = new string[BulkBuyerIntroLines.Length + 1];
            Array.Copy(BulkBuyerIntroLines, lines, BulkBuyerIntroLines.Length);
            lines[lines.Length - 1] = $"Вся витрина — {coins}. По рукам?";
            return lines;
        }

        /// <summary>
        /// Does the next generated visitor turn out to be the bulk buyer? Never with staged visitors or a paused clock (the
        /// tutorial); otherwise the cheat, or: unlocked, not yet this shift, enough goods on display, and the chance.
        /// </summary>
        bool BulkBuyerComes()
        {
            if (CustomerScript != null || ClockPaused || DisplayCount == 0) return false;
            if (ForceBulkBuyerNext) return true;
            if (!BulkBuyerUnlocked || BulkBuyerCameThisShift || DisplayCount < Settings.BulkBuyerMinDisplay) return false;
            float chance = BulkBuyerChance;
            return chance > 0 && random.NextDouble() < chance;
        }

        public TradingSession(TradeSettings settings = null, int seed = 1, CoinWallet wallet = null)
        {
            Settings = (settings ?? new TradeSettings()).ValidatedCopy();
            this.wallet = wallet ?? new CoinWallet();
            random = new Random(seed);
            slots = new ItemInstance[Settings.SlotCount];
            Inventory = new ReadOnlyCollection<ItemInstance>(inventory);
            Slots = Array.AsReadOnly(slots);
            RemainingSeconds = ShiftLength;
        }

        public void SeedInventory(ItemDefinition definition, int count = 1)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            Register(definition);
            for (int i = 0; i < count; i++) inventory.Add(new ItemInstance(++nextItemId, definition));
            Touch();
        }

        public bool PlaceItem(int inventoryIndex, int slotIndex)
        {
            if (IsOpen || ExpeditionId != 0 || !ValidSlot(slotIndex) || slots[slotIndex] != null
                || inventoryIndex < 0 || inventoryIndex >= inventory.Count) return false;
            slots[slotIndex] = inventory[inventoryIndex];
            inventory.RemoveAt(inventoryIndex);
            LastMessage = $"На витрине: {slots[slotIndex].Definition.Name}.";
            Touch();
            return true;
        }

        /// <summary>Debug/cheat helper: puts a new random catalogue item on every empty display slot.</summary>
        public int StockEmptySlotsRandomly(Random picker = null)
        {
            if (IsOpen || ExpeditionId != 0 || catalog.Count == 0) return 0;
            if (picker == null) picker = random;
            int added = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) continue;
                slots[i] = new ItemInstance(++nextItemId, catalog[picker.Next(catalog.Count)]);
                added++;
            }
            if (added == 0) return 0;
            LastMessage = $"На витрину добавлено товаров: {added}.";
            Touch();
            return added;
        }

        public bool RemoveItem(int slotIndex)
        {
            if (IsOpen || ExpeditionId != 0 || !ValidSlot(slotIndex) || slots[slotIndex] == null) return false;
            inventory.Add(slots[slotIndex]); slots[slotIndex] = null;
            LastMessage = "Предмет возвращён в запас.";
            Touch();
            return true;
        }

        public bool Open()
        {
            if (IsOpen || ExpeditionId != 0) return false;
            if (RemainingSeconds <= 0) { LastMessage = "Смена окончена. Вернитесь из вылазки с новой добычей."; return false; }
            if (DisplayCount == 0) { LastMessage = "Сначала выставьте хотя бы один предмет."; return false; }
            IsOpen = true; Touch();
            AdvanceCustomer();
            return true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            RecordDeparture(CustomerOutcome.ShopClosed);
            IsOpen = false; CurrentCustomer = null; CurrentDeal = null;
            LastMessage = "Лавка закрыта. Непроданные товары и остаток времени сохранены.";
            Touch();
        }

        public void Tick(float elapsedSeconds)
        {
            if (elapsedSeconds < 0 || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!IsOpen || ClockPaused) return;
            RemainingSeconds = Math.Max(0, RemainingSeconds - elapsedSeconds);
            // A visitor already at the counter is served to the end of the conversation.
            if (RemainingSeconds <= 0 && CurrentCustomer == null) FinishShift("Время смены закончилось.");
        }

        /// <summary>
        /// The next visitor walks in and names one item on the display himself: the deal over it starts at once
        /// (his first line asks its price or offers his starting price for it).
        /// </summary>
        public bool AdvanceCustomer(CustomerTerms terms = null)
        {
            if (!IsOpen || CurrentCustomer != null) return false;
            if (RemainingSeconds <= 0 || DisplayCount == 0)
            {
                FinishShift(RemainingSeconds <= 0 ? "Время смены закончилось." : "Витрина пуста. Лавка закрыта для выкладки.");
                return false;
            }
            var nextTerms = terms ?? CustomerScript?.Invoke();
            // Now and then (never in the tutorial) the visitor is the bulk buyer instead of a random one.
            if (nextTerms == null)
                nextTerms = BulkBuyerComes() ? new CustomerTerms { Name = "Скупщик", BulkBuyer = true } : GenerateCustomer();
            ValidateCustomerTerms(nextTerms);
            CurrentCustomer = new Customer(++nextCustomerId, nextTerms);
            if (CurrentCustomer.IsBulkBuyer)
            {
                // He offers for the whole display as it stands; the display cannot change until he leaves.
                ForceBulkBuyerNext = false;
                BulkBuyerCameThisShift = true;
                CurrentCustomer.BulkPrice = BulkOfferForDisplay();
                CurrentCustomer.BulkCount = DisplayCount;
                CurrentCustomer.FirstMeeting = !BulkBuyerMet;
                CurrentCustomer.Lines = BulkBuyerLines(CurrentCustomer.FirstMeeting, CurrentCustomer.BulkPrice);
                LastMessage = CurrentCustomer.Lines[0];
                Touch();
                return true;
            }
            int slot = CurrentCustomer.RequestedSlot;
            CurrentDeal = new TradeDeal(++nextDealId, CurrentCustomer, slots[slot], slot);
            LastMessage = CurrentDeal.Reaction;
            Touch();
            return true;
        }

        /// <summary>
        /// One round of haggling (board «торговля» 07–09, preset v0.1):
        /// each answer costs patience by its boldness (1/1/1/2/3); a demand the visitor has no patience left for
        /// makes them walk out; prices up to a drifting comfort level are accepted, prices between comfort and the
        /// visitor's fixed limit are accepted with falling probability, prices above the limit never; otherwise the
        /// visitor counters towards your ask — or stubbornly holds. Patience at zero means a last offer: accepting
        /// it sells, any further price answer ends the visit.
        /// </summary>
        public bool Offer(long dealId, int revision, int optionIndex)
        {
            if (!MatchesDeal(dealId, revision)) return false;
            var deal = CurrentDeal;
            var options = deal.GetPriceOptions();
            if (optionIndex < 0 || optionIndex >= options.Length || !options[optionIndex].Enabled) return false;
            int price = options[optionIndex].Price;
            deal.Round++; deal.Revision++;
            if (deal.IsLastOffer)
            {
                EndWithoutSale(Pick(LeaveLines), CustomerOutcome.WalkedAway);
                return true;
            }
            if (CurrentCustomer.Script != null)
            {
                Staged(deal, price, CurrentCustomer.Script);
                return true;
            }
            // The first answer at R is guaranteed only for a visitor who asks the price.
            if (CurrentCustomer.AsksPrice && deal.Round == 1 && optionIndex == 0)
            {
                Settle(price); return true;
            }

            int cost = Settings.UsePresetPatienceCosts
                ? (optionIndex == 4 ? 3 : optionIndex == 3 ? 2 : 1)
                : 1 + (price > deal.MaxPrice ? 1 : 0) + (price > deal.R * 1.35 ? 1 : 0);
            if (deal.LastAsk == price) cost++;
            deal.LastAsk = price;

            if (price <= deal.MaxPrice && WillAccept(deal, price)) { Settle(price); return true; }

            if (cost > deal.Patience)
            {
                deal.Patience = 0;
                deal.Mood = CustomerMood.Angry;
                EndWithoutSale(Pick(price > deal.MaxPrice ? InsultedLines : LeaveLines), CustomerOutcome.WalkedAway);
                return true;
            }
            deal.Patience -= cost;

            // Counter: move from the previous position towards what you asked (capped by the limit) — or hold.
            int previous = deal.N ?? Math.Max(1, TradeDeal.RoundMoney(Math.Min(deal.MaxPrice, deal.R) * .9));
            int target = Math.Min(price - 1, deal.MaxPrice);
            bool stubborn = random.NextDouble() < .2 || target <= previous;
            int counter = stubborn ? previous
                : previous + Math.Max(1, TradeDeal.RoundMoney((target - previous) * (.3 + random.NextDouble() * .35)));
            // Never offer the requested price as a counter: agreeing is a settlement.
            deal.N = Math.Max(1, Math.Min(price - 1, Math.Min(deal.MaxPrice, counter)));
            deal.Comfort += (deal.MaxPrice - deal.Comfort) * .3f;
            deal.IsLastOffer = deal.Patience == 0;
            float left = deal.Patience / (float)Math.Max(1, CurrentCustomer.InitialPatience);
            deal.Mood = deal.IsLastOffer || deal.Patience <= 1 ? CustomerMood.Angry : left <= .6f ? CustomerMood.Displeased : CustomerMood.Neutral;
            deal.Reaction = string.Format(deal.IsLastOffer ? Pick(LastOfferLines)
                : deal.Mood == CustomerMood.Angry ? Pick(AngryLines)
                : stubborn ? Pick(StubbornLines)
                : deal.Mood == CustomerMood.Displeased ? Pick(DispleasedLines) : Pick(CounterLines), deal.N);
            LastMessage = deal.Reaction;
            Touch();
            return true;
        }

        void Staged(TradeDeal deal, int price, ScriptedHaggle script)
        {
            if (price <= script.AcceptUpTo) { Settle(price); return; }
            if (price >= script.LeaveFrom)
            {
                deal.Mood = CustomerMood.Angry;
                EndWithoutSale(Pick(InsultedLines), CustomerOutcome.WalkedAway);
                return;
            }
            deal.N = Math.Min(price - 1, script.LastOffer);
            deal.IsLastOffer = true;
            deal.Mood = CustomerMood.Angry;
            deal.Reaction = string.Format(Pick(LastOfferLines), deal.N);
            LastMessage = deal.Reaction;
            Touch();
        }

        bool WillAccept(TradeDeal deal, int price)
        {
            if (price <= deal.Comfort) return true;
            float span = Math.Max(1, deal.MaxPrice - deal.Comfort);
            double u = (price - deal.Comfort) / span;
            double chance = (1 - Math.Pow(u, .7)) * .85;
            return random.NextDouble() < chance;
        }

        static readonly string[] CounterLines = { "Хм. Дам {0}.", "Дороговато. Предлагаю {0}.", "Давай так: {0}.", "За {0} возьму." };
        static readonly string[] StubbornLines = { "Нет. {0} — и точка.", "Не уступлю. {0}.", "{0}. Больше не проси." };
        static readonly string[] DispleasedLines = { "Ты меня грабишь. {0}, не больше.", "Ну и цены… {0}.", "Хватит торговаться. {0}." };
        static readonly string[] AngryLines = { "Не испытывай терпение. {0}.", "Ещё слово — и уйду. {0}.", "{0}, или я ухожу." };
        static readonly string[] LastOfferLines = { "Последнее предложение: {0}.", "{0}. Это последнее слово.", "Всё. {0} — и разойдёмся." };
        static readonly string[] LeaveLines = { "Хватит. Я ухожу.", "Да ну тебя!", "Найду в другом месте." };
        static readonly string[] InsultedLines = { "Ты шутишь?! Прощай.", "За такие деньги? Ни за что!", "Наглость! Ухожу." };
        string Pick(string[] lines) => lines[random.Next(lines.Length)];

        public bool Accept(long dealId, int revision)
        {
            if (!MatchesDeal(dealId, revision) || !CurrentDeal.N.HasValue) return false;
            Settle(CurrentDeal.N.Value);
            return true;
        }

        public bool Refuse(long customerId)
        {
            if (!IsOpen || CurrentCustomer == null || CurrentCustomer.Id != customerId) return false;
            if (CurrentCustomer.IsBulkBuyer)
            {
                // A refusal is an answer too: he has been met and leaves calmly, the goods stay where they are.
                BulkBuyerMet = true;
                EndWithoutSale("Скупщик ушёл. Товар остался на витрине.", CustomerOutcome.Refused);
                return true;
            }
            EndWithoutSale("Покупатель ушёл. Товар остался в лавке.", CustomerOutcome.Refused);
            return true;
        }

        public long BeginExpedition()
        {
            if (IsOpen || ExpeditionId != 0) return 0;
            ExpeditionId = ++nextExpeditionId;
            expeditionLoot.Clear();
            LastMessage = "Вылазка началась. Найдите новую добычу и вернитесь в лавку.";
            Touch();
            return ExpeditionId;
        }

        public bool CollectLoot(long expeditionId, ItemDefinition definition)
        {
            if (definition == null || expeditionId == 0 || expeditionId != ExpeditionId) return false;
            Register(definition);
            expeditionLoot.Add(new ItemInstance(++nextItemId, definition));
            LastMessage = $"Новая добыча: {definition.Name}.";
            Touch();
            return true;
        }

        public bool CompleteExpedition(long expeditionId)
        {
            if (IsOpen || expeditionId == 0 || expeditionId != ExpeditionId) return false;
            bool freshLoot = expeditionLoot.Count > 0;
            inventory.AddRange(expeditionLoot); expeditionLoot.Clear(); ExpeditionId = 0;
            // Fresh loot starts a new shift: the bulk buyer may come again.
            if (freshLoot) { RemainingSeconds = ShiftLength; BulkBuyerCameThisShift = false; }
            LastMessage = freshLoot ? "Новая добыча доставлена. Время торговли восстановлено."
                : "Вернулись без новой добычи. Остаток времени не изменился.";
            Touch();
            return true;
        }

        /// <summary>Validates the entire external delivery before changing inventory or the outing token.</summary>
        public bool CompleteExpedition(long expeditionId, IReadOnlyList<StockDelivery> delivery)
        {
            if (IsOpen || expeditionId == 0 || expeditionId != ExpeditionId || delivery == null) return false;
            var definitions = new List<ItemDefinition>();
            long total = expeditionLoot.Count;
            foreach (var entry in delivery)
            {
                if (entry == null || entry.count < 0) return false;
                var definition = FindDefinition(entry.definitionId);
                if (definition == null) return false;
                definitions.Add(definition);
                total += entry.count;
                if (total > int.MaxValue - inventory.Count || total > long.MaxValue - nextItemId) return false;
            }
            for (int i = 0; i < delivery.Count; i++)
                for (int n = 0; n < delivery[i].count; n++)
                    expeditionLoot.Add(new ItemInstance(++nextItemId, definitions[i]));
            return CompleteExpedition(expeditionId);
        }

        public ItemDefinition FindDefinition(string id) => catalog.Find(definition => definition.Id == id);

        /// <summary>Debug/cheat helper: completes a revenue target without inventing a sale or crediting the wallet.</summary>
        public bool CompleteRevenueGoal(long target)
        {
            if (target <= GrossRevenue) return false;
            GrossRevenue = target;
            Touch();
            return true;
        }

        public TradingSave Save()
        {
            var save = new TradingSave {
                remainingSeconds = RemainingSeconds, grossRevenue = GrossRevenue, salesCount = SalesCount,
                nextItemId = nextItemId, nextExpeditionId = nextExpeditionId, shiftLevel = ShiftLevel,
                bulkBuyerLevel = BulkBuyerLevel, bulkBuyerMet = BulkBuyerMet, bulkBuyerThisShift = BulkBuyerCameThisShift
            };
            foreach (var item in inventory)
                save.items.Add(new TradingItemSave { instanceId = item.Id, definitionId = item.Definition.Id });
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null)
                    save.items.Add(new TradingItemSave { instanceId = slots[i].Id, definitionId = slots[i].Definition.Id, slot = i });
            return save;
        }

        /// <summary>
        /// A save may hold more time than a shift lasts now (an older, longer shift; fewer upgrades in the settings):
        /// that is not damage, the restore clamps it. More bought upgrades than there are clamp to the maximum too.
        /// </summary>
        public bool CanRestore(TradingSave save)
        {
            if (save == null || save.version != 1 || save.items == null || save.salesCount < 0
                || save.grossRevenue < save.salesCount || save.nextItemId < 0 || save.nextItemId == long.MaxValue
                || save.nextExpeditionId < 0 || save.nextExpeditionId == long.MaxValue
                || float.IsNaN(save.remainingSeconds) || float.IsInfinity(save.remainingSeconds)
                || save.remainingSeconds < 0 || save.shiftLevel < 0 || save.bulkBuyerLevel < 0) return false;
            var ids = new HashSet<long>();
            var occupied = new HashSet<int>();
            foreach (var entry in save.items)
            {
                if (entry == null || entry.instanceId <= 0 || entry.instanceId > save.nextItemId
                    || !ids.Add(entry.instanceId) || FindDefinition(entry.definitionId) == null
                    || entry.slot < -1 || entry.slot >= slots.Length
                    || (entry.slot >= 0 && !occupied.Add(entry.slot))) return false;
            }
            return true;
        }

        /// <summary>Restores only at the shop boundary; transient conversations and unsaved expedition loot are excluded.</summary>
        public bool TryRestore(TradingSave save)
        {
            if (IsOpen || ExpeditionId != 0 || !CanRestore(save)) return false;
            var restoredInventory = new List<ItemInstance>();
            var restoredSlots = new ItemInstance[slots.Length];
            foreach (var entry in save.items)
            {
                var item = new ItemInstance(entry.instanceId, FindDefinition(entry.definitionId));
                if (entry.slot < 0) restoredInventory.Add(item); else restoredSlots[entry.slot] = item;
            }
            inventory.Clear(); inventory.AddRange(restoredInventory);
            Array.Copy(restoredSlots, slots, slots.Length);
            expeditionLoot.Clear();
            ShiftLevel = Math.Min(save.shiftLevel, ShiftSteps);
            RemainingSeconds = Math.Min(save.remainingSeconds, ShiftLength);
            BulkBuyerLevel = Math.Min(save.bulkBuyerLevel, BulkBuyerSteps);
            BulkBuyerMet = save.bulkBuyerMet; BulkBuyerCameThisShift = save.bulkBuyerThisShift;
            GrossRevenue = save.grossRevenue; SalesCount = save.salesCount;
            nextItemId = save.nextItemId; nextExpeditionId = save.nextExpeditionId;
            CurrentCustomer = null; CurrentDeal = null; LastSoldItem = null; LastDeparture = null;
            LastSoldItems = Array.Empty<ItemInstance>(); LastSaleBulk = false;
            SettlementSequence = DepartureSequence = LastSalePrice = 0;
            LastMessage = "Прогресс лавки восстановлен.";
            Touch();
            return true;
        }

        private void Touch() { Revision++; Changed?.Invoke(); }

        private bool MatchesDeal(long id, int revision) => IsOpen && CurrentDeal != null
            && CurrentDeal.Id == id && CurrentDeal.Revision == revision;

        private void Settle(int price)
        {
            var deal = CurrentDeal;
            // The displayed instance is the transaction's authority: one removal, one payment.
            if (deal == null || slots[deal.SlotIndex] != deal.Item) return;
            long nextRevenue = checked(GrossRevenue + price);
            int nextSalesCount = checked(SalesCount + 1);
            wallet.Credit(price);
            slots[deal.SlotIndex] = null;
            GrossRevenue = nextRevenue; SalesCount = nextSalesCount; SettlementSequence++;
            LastSalePrice = price; LastSoldItem = deal.Item; LastSoldItems = new[] { deal.Item }; LastSaleBulk = false;
            deal.Mood = CustomerMood.Happy;
            RecordDeparture(CustomerOutcome.Purchased);
            CurrentDeal = null; CurrentCustomer = null; Touch();
            LastMessage = $"Продано: {deal.Item.Definition.Name} за {price} монет.";
            if (RemainingSeconds <= 0 || DisplayCount == 0)
                FinishShift(LastMessage + (RemainingSeconds <= 0 ? " Смена окончена." : " Витрина пуста."));
        }

        private void EndWithoutSale(string message, CustomerOutcome outcome)
        {
            RecordDeparture(outcome);
            CurrentDeal = null; CurrentCustomer = null; LastMessage = message; Touch();
            if (RemainingSeconds <= 0) FinishShift(message + " Смена окончена.");
        }

        private void FinishShift(string message)
        {
            RecordDeparture(CustomerOutcome.ShopClosed);
            IsOpen = false; CurrentDeal = null; CurrentCustomer = null; LastMessage = message; Touch();
        }

        private void RecordDeparture(CustomerOutcome outcome)
        {
            if (CurrentCustomer == null) return;
            LastDeparture = new CustomerDeparture(CurrentCustomer.Id, outcome, ++DepartureSequence, CurrentCustomer.IsBulkBuyer);
        }

        private CustomerTerms GenerateCustomer()
        {
            var occupied = new List<int>();
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) occupied.Add(i);
            // Every visitor wants one particular item that is on the display.
            int selected = occupied[random.Next(occupied.Count)];
            int referencePrice = slots[selected].Definition.RecommendedPrice;
            // A one-coin item has no positive integer price below R. Such requests ask the price.
            bool asksPrice = random.NextDouble() < Settings.AsksPriceProbability || referencePrice <= 1;
            float ceilingRatio = asksPrice ? Range(Settings.MinCeilingRatio, Settings.MaxCeilingRatio)
                : Range(Settings.MinOfferCeilingRatio, Settings.MaxOfferCeilingRatio);
            float openingRatio = Range(Settings.MinOpeningRatio, Settings.MaxOpeningRatio);
            return new CustomerTerms
            {
                Name = new[] { "Охотник", "Собиратель", "Путник", "Сосед" }[random.Next(4)],
                RequestedSlot = selected,
                AsksPrice = asksPrice,
                OpeningRatio = openingRatio,
                MaxPrice = Math.Max(asksPrice ? referencePrice : Math.Max(1, TradeDeal.RoundMoney(referencePrice * openingRatio)), TradeDeal.RoundMoney(referencePrice * ceilingRatio)),
                Patience = random.Next(Settings.MinPatience, Settings.MaxPatience + 1)
            };
        }

        private void ValidateCustomerTerms(CustomerTerms terms)
        {
            if (terms.BulkBuyer)
            {
                // He names no item and never haggles: only a name and something on the display to buy.
                if (string.IsNullOrWhiteSpace(terms.Name) || DisplayCount == 0)
                    throw new ArgumentException("The bulk buyer needs a name and goods on the display.", nameof(terms));
                return;
            }
            if (terms.MaxPrice < 1 || terms.Patience < 1 || terms.OpeningRatio <= 0 || terms.OpeningRatio >= 1
                || float.IsNaN(terms.OpeningRatio) || string.IsNullOrWhiteSpace(terms.Name))
                throw new ArgumentException("Invalid customer terms.", nameof(terms));
            // The visitor names one item himself: it must be on the display.
            if (!ValidSlot(terms.RequestedSlot) || slots[terms.RequestedSlot] == null)
                throw new ArgumentException("A visitor must ask for an item that is on the display.", nameof(terms));
            int r = slots[terms.RequestedSlot].Definition.RecommendedPrice;
            if (!terms.AsksPrice && r <= 1)
                throw new ArgumentException("One-coin goods require a price question; no lower positive opening exists.", nameof(terms));
            if (terms.AsksPrice && terms.MaxPrice < r)
                throw new ArgumentException("Price-asking visitors must be able to pay R.", nameof(terms));
            if (!terms.AsksPrice && terms.MaxPrice < Math.Max(1, TradeDeal.RoundMoney(r * terms.OpeningRatio)))
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
