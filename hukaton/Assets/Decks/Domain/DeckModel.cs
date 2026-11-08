using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Economy;

namespace Hakaton.Decks
{
    public enum DeckKind
    {
        Exploration,
        Combat
    }

    public sealed class CardSpec
    {
        public readonly string Id, Name;
        public readonly DeckKind Kind;
        public readonly int Price, UpgradePrice, BasePower, PowerPerLevel;
        public CardSpec(string id, string name, DeckKind kind, int price, int upgradePrice, int power, int powerPerLevel)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Price = price;
            UpgradePrice = upgradePrice;
            BasePower = power;
            PowerPerLevel = powerPerLevel;
        }
    }

    public sealed class CardCopy
    {
        public int Id { get; }
        public CardSpec Spec { get; }
        public int Level { get; private set; }
        public bool IsReturn => Id == -1;
        public int Power => Spec.BasePower + (Level - 1) * Spec.PowerPerLevel;
        internal CardCopy(int id, CardSpec spec, int level = 1)
        {
            Id = id;
            Spec = spec;
            Level = level;
        }

        internal void Upgrade() { Level++; }
        internal CardCopy Snapshot() => new CardCopy(Id, Spec, Level);
        internal static CardCopy Return() => new CardCopy(
            -1, new CardSpec("return", "Пора домой", DeckKind.Exploration, 0, 0, 0, 0));
    }

    public sealed class DeckRules
    {
        public readonly int DeckLimit, InitialHand, MaxHand, MaxCardLevel, HandUpgradePrice, StartingCoins;
        public DeckRules(int deckLimit = 12, int initialHand = 3, int maxHand = 5, int maxCardLevel = 3,
            int handUpgradePrice = 60, int startingCoins = 240)
        {
            if (deckLimit < 1 || initialHand < 1 || maxHand < initialHand || maxHand > deckLimit || maxCardLevel < 1
                || handUpgradePrice < 0 || startingCoins < 0)
                throw new ArgumentOutOfRangeException(nameof(deckLimit));
            DeckLimit = deckLimit;
            InitialHand = initialHand;
            MaxHand = maxHand;
            MaxCardLevel = maxCardLevel;
            HandUpgradePrice = handUpgradePrice;
            StartingCoins = startingCoins;
        }
    }

    [Serializable]
    public sealed class CopySave
    {
        public int id, level;
        public string definition;
    }
    [Serializable]
    public sealed class LootSave
    {
        public string id, name;
        public int count, value;
    }
    [Serializable]
    public sealed class WorkshopSave
    {
        public int version = 1, coins, nextId, explorationHand, combatHand, journeys;
        public List<CopySave> copies = new List<CopySave>();
        public List<int> selected = new List<int>();
    }

    /// <summary>Collection and economy. Every equipped item is a particular owned copy.</summary>
    public sealed class DeckWorkshop
    {
        readonly List<CardCopy> owned = new List<CardCopy>();
        readonly HashSet<int> selected = new HashSet<int>();
        readonly Dictionary<string, CardSpec> catalog;
        readonly int[] handSizes;
        CoinWallet wallet;
        readonly bool ownsWallet;
        int nextId = 1;
        public event Action Changed;
        public DeckRules Rules { get; }
        public int Coins => wallet.Balance;
        public int Journeys { get; private set; }
        public IReadOnlyList<CardCopy> Owned => owned.AsReadOnly();
        public Expedition Current { get; private set; }
        public bool IsAway => Current != null && !Current.Completed;
        public DeckWorkshop(DeckRules rules, IEnumerable<CardSpec> specs, int copiesPerType = 3, CoinWallet wallet = null,
            IReadOnlyDictionary<string, int> initialCopies = null)
        {
            Rules = rules;
            catalog = specs.ToDictionary(s => s.Id);
            if (catalog.Count == 0
                || catalog.Values.Any(
                    s => string.IsNullOrWhiteSpace(s.Id) || s.Id == "return" || s.Price < 0 || s.UpgradePrice < 0))
                throw new ArgumentException("Invalid card catalogue");
            if (copiesPerType < 0) throw new ArgumentOutOfRangeException(nameof(copiesPerType));
            if (initialCopies != null && initialCopies.Any(entry => !catalog.ContainsKey(entry.Key) || entry.Value < 0))
                throw new ArgumentException("Invalid starting card copies", nameof(initialCopies));
            ownsWallet = wallet == null;
            this.wallet = wallet ?? new CoinWallet(rules.StartingCoins);
            handSizes = new[] { rules.InitialHand, rules.InitialHand };
            foreach (var spec in catalog.Values)
            {
                int count = initialCopies == null                                 ? copiesPerType
                    : initialCopies.TryGetValue(spec.Id, out var configuredCount) ? configuredCount
                                                                                  : 0;
                for (int i = 0; i < count; i++)
                {
                    var copy = new CardCopy(nextId++, spec);
                    owned.Add(copy);
                    if (Selected(spec.Kind).Count < rules.DeckLimit) selected.Add(copy.Id);
                }
            }
        }

        public CardCopy Find(int id) => owned.Find(c => c.Id == id);
        public bool IsSelected(int id) => selected.Contains(id);
        public List<CardCopy> Selected(
            DeckKind kind) => owned.Where(c => c.Spec.Kind == kind && selected.Contains(c.Id)).ToList();
        public int HandSize(DeckKind kind) => handSizes[(int)kind];
        public int HandPrice(DeckKind kind) => checked(Rules.HandUpgradePrice * (HandSize(kind) - Rules.InitialHand + 1));
        public int UpgradePrice(CardCopy copy) => checked(copy.Spec.UpgradePrice * copy.Level);
        public string Toggle(int id)
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            var copy = Find(id);
            if (copy == null) return "Экземпляр не найден.";
            if (selected.Remove(id))
            {
                Changed?.Invoke();
                return null;
            }
            if (Selected(copy.Spec.Kind).Count >= Rules.DeckLimit) return "Колода заполнена. Уберите одну карту.";
            selected.Add(id);
            Changed?.Invoke();
            return null;
        }

        public string Buy(string definition, out CardCopy copy)
        {
            copy = null;
            if (IsAway) return "Сначала вернитесь в лавку.";
            if (!catalog.TryGetValue(definition, out var spec)) return "Эта карта недоступна.";
            if (nextId == int.MaxValue) return "Коллекция заполнена.";
            if (!wallet.TrySpend(spec.Price)) return "Не хватает монет.";
            copy = new CardCopy(nextId++, spec);
            owned.Add(copy);
            Changed?.Invoke();
            return null;
        }

        public string Upgrade(int id)
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            var copy = Find(id);
            if (copy == null) return "Выберите экземпляр.";
            if (copy.Level >= Rules.MaxCardLevel) return "Экземпляр уже полностью улучшен.";
            int price = UpgradePrice(copy);
            if (!wallet.TrySpend(price)) return "Не хватает монет.";
            copy.Upgrade();
            Changed?.Invoke();
            return null;
        }

        public string ImproveHand(DeckKind kind)
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            if (HandSize(kind) >= Rules.MaxHand) return "Рука уже максимального размера.";
            int price = HandPrice(kind);
            if (!wallet.TrySpend(price)) return "Не хватает монет.";
            handSizes[(int)kind]++;
            Changed?.Invoke();
            return null;
        }

        public string CanDepart()
        {
            if (IsAway) return "Вылазка уже началась.";
            if (Selected(DeckKind.Exploration).Count == 0) return "Добавьте хотя бы одну карту исследования.";
            if (Selected(DeckKind.Combat).Count == 0) return "Добавьте хотя бы одну боевую карту.";
            return null;
        }

        public string Depart(int seed)
        {
            string error = CanDepart();
            if (error != null) return error;
            Current = new Expedition(Selected(DeckKind.Exploration), Selected(DeckKind.Combat), handSizes[0], handSizes[1], seed);
            Journeys++;
            Changed?.Invoke();
            return null;
        }

        public WorkshopSave Save() => new WorkshopSave { coins = Coins, nextId = nextId, explorationHand = handSizes[0],
            combatHand = handSizes[1], journeys = Journeys,
            copies = owned.Select(c => new CopySave { id = c.Id, definition = c.Spec.Id, level = c.Level }).ToList(),
            selected = owned.Where(c => selected.Contains(c.Id)).Select(c => c.Id).ToList() };
        /// <summary>Validates the full card snapshot before a scene-wide restore changes any subsystem.</summary>
        public bool CanRestoreProgress(WorkshopSave save)
        {
            if (IsAway || save == null || save.version != 1 || save.copies == null || save.selected == null || save.coins < 0
                || save.journeys < 0 || save.nextId < 1 || save.explorationHand < Rules.InitialHand
                || save.explorationHand > Rules.MaxHand || save.combatHand < Rules.InitialHand || save.combatHand > Rules.MaxHand)
                return false;
            var ids = new HashSet<int>();
            var restored = new List<CardCopy>();
            foreach (var c in save.copies)
            {
                if (c == null || c.definition == null || c.id < 1 || c.id >= save.nextId || !ids.Add(c.id) || c.level < 1
                    || c.level > Rules.MaxCardLevel || !catalog.TryGetValue(c.definition, out var spec))
                    return false;
                restored.Add(new CardCopy(c.id, spec, c.level));
            }
            var equipped = new HashSet<int>(save.selected);
            if (equipped.Count != save.selected.Count || !equipped.IsSubsetOf(ids)) return false;
            return restored.Count(c => equipped.Contains(c.Id) && c.Spec.Kind == DeckKind.Exploration) <= Rules.DeckLimit
                && restored.Count(c => equipped.Contains(c.Id) && c.Spec.Kind == DeckKind.Combat) <= Rules.DeckLimit;
        }

        /// <summary>Standalone saves own their wallet. Shared economies must restore their one wallet separately.</summary>
        public bool TryRestore(WorkshopSave save)
        {
            if (!ownsWallet || !CanRestoreProgress(save)) return false;
            ApplyProgress(save);
            wallet = new CoinWallet(save.coins);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Restores cards without replacing, crediting, or spending the shared wallet.</summary>
        public bool TryRestoreProgress(WorkshopSave save)
        {
            if (!CanRestoreProgress(save)) return false;
            ApplyProgress(save);
            Changed?.Invoke();
            return true;
        }

        void ApplyProgress(WorkshopSave save)
        {
            owned.Clear();
            owned.AddRange(save.copies.Select(c => new CardCopy(c.id, catalog[c.definition], c.level)));
            selected.Clear();
            selected.UnionWith(save.selected);
            nextId = save.nextId;
            Journeys = save.journeys;
            handSizes[0] = save.explorationHand;
            handSizes[1] = save.combatHand;
            Current = null;
        }
    }

    public sealed class ExpeditionDeck
    {
        readonly Queue<CardCopy> stock;
        readonly List<CardCopy> hand = new List<CardCopy>(), discard = new List<CardCopy>();
        public DeckKind Kind { get; }
        public int Capacity { get; }
        public int Total { get; }
        public int StockCount => stock.Count;
        public IReadOnlyList<CardCopy> Hand => hand.AsReadOnly();
        public IReadOnlyList<CardCopy> Discard => discard.AsReadOnly();
        public bool HasReturn => hand.Any(c => c.IsReturn);
        internal ExpeditionDeck(DeckKind kind, IEnumerable<CardCopy> cards, int capacity, int seed)
        {
            Kind = kind;
            Capacity = capacity;
            var order = cards.Select(c => c.Snapshot()).ToList();
            Total = order.Count;
            var random = new Random(seed);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var c = order[i];
                order[i] = order[j];
                order[j] = c;
            }
            stock = new Queue<CardCopy>(order);
            Fill();
        }

        void Fill()
        {
            while (hand.Count < Capacity && stock.Count > 0) hand.Add(stock.Dequeue());
            if (Kind == DeckKind.Exploration && stock.Count == 0 && hand.Count == 0) hand.Add(CardCopy.Return());
        }

        internal CardCopy Release(int id)
        {
            var card = hand.Find(c => c.Id == id && !c.IsReturn);
            if (card == null) return null;
            hand.Remove(card);
            discard.Add(card);
            Fill();
            return card;
        }

        internal List<CardCopy> DiscardHand()
        {
            var cards = hand.Where(c => !c.IsReturn).ToList();
            foreach (var c in cards)
            {
                hand.Remove(c);
                discard.Add(c);
            }
            Fill();
            return cards;
        }
    }

    /// <summary>No world effects are called by discarding. Only Release emits a played card.</summary>
    public sealed class Expedition
    {
        readonly ExpeditionDeck[] decks;
        public DeckKind ActiveKind { get; private set; }
        public ExpeditionDeck Active => decks[(int)ActiveKind];
        public bool Completed { get; private set; }
        public int PlayedCount { get; private set; }
        public int DiscardedCount { get; private set; }
        public event Action<CardCopy> CardReleased;
        public Expedition(
            IEnumerable<CardCopy> exploration, IEnumerable<CardCopy> combat, int explorationHand, int combatHand, int seed)
        {
            if (explorationHand < 1 || combatHand < 1) throw new ArgumentOutOfRangeException(nameof(explorationHand));
            // Independent RNG streams: combat composition cannot affect exploration's order.
            decks = new[] { new ExpeditionDeck(DeckKind.Exploration, exploration, explorationHand, seed),
                new ExpeditionDeck(DeckKind.Combat, combat, combatHand, unchecked(seed * 397 ^ 0x51ED270B)) };
        }

        public ExpeditionDeck Get(DeckKind kind) => decks[(int)kind];
        public void Switch(DeckKind kind)
        {
            if (!Completed) ActiveKind = kind;
        }

        public CardCopy Release(int id)
        {
            if (Completed) return null;
            var card = Active.Release(id);
            if (card == null) return null;
            PlayedCount++;
            CardReleased?.Invoke(card);
            return card;
        }

        public IReadOnlyList<CardCopy> DiscardHand()
        {
            if (Completed) return Array.Empty<CardCopy>();
            var cards = Active.DiscardHand();
            DiscardedCount += cards.Count;
            return cards;
        }

        public bool ReturnHome()
        {
            if (Completed || ActiveKind != DeckKind.Exploration || !Active.HasReturn) return false;
            Completed = true;
            return true;
        }

        public void Defeat() { Completed = true; }
    }
}
