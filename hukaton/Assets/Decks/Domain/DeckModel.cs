using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Economy;

namespace Hakaton.Decks
{
    /// <summary>A card type. Cards are not split into combat and exploration: what a card is for follows from its effect.</summary>
    public sealed class CardSpec
    {
        public readonly string Id, Name;
        public readonly int Price, UpgradePrice, BasePower, PowerPerLevel;
        public CardSpec(string id, string name, int price, int upgradePrice, int power, int powerPerLevel)
        {
            Id = id;
            Name = name;
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
        internal static CardCopy Return() => new CardCopy(-1, new CardSpec("return", "Пора домой", 0, 0, 0, 0));
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
    /// <summary>Version 2: one deck and one hand. Version 1 (exploration and combat decks) is still read.</summary>
    [Serializable]
    public sealed class WorkshopSave
    {
        public const int CurrentVersion = 2;
        public int version = CurrentVersion, coins, nextId, hand, journeys;
        /// <summary>Version 1 only: the hands of the former exploration and combat decks.</summary>
        public int explorationHand, combatHand;
        public List<CopySave> copies = new List<CopySave>();
        public List<int> selected = new List<int>();
    }

    /// <summary>Collection and economy. Every card in the deck is a particular owned copy; one deck, one hand.</summary>
    public sealed class DeckWorkshop
    {
        readonly List<CardCopy> owned = new List<CardCopy>();
        readonly HashSet<int> selected = new HashSet<int>();
        readonly Dictionary<string, CardSpec> catalog;
        int handSize;
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
        /// <param name="initialDeck">Copies of each type that start in the deck. Without it the deck takes the
        /// collection in order up to the limit.</param>
        public DeckWorkshop(DeckRules rules, IEnumerable<CardSpec> specs, int copiesPerType = 3, CoinWallet wallet = null,
            IReadOnlyDictionary<string, int> initialCopies = null, IReadOnlyDictionary<string, int> initialDeck = null)
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
            if (initialDeck != null && initialDeck.Any(entry => !catalog.ContainsKey(entry.Key) || entry.Value < 0))
                throw new ArgumentException("Invalid starting deck", nameof(initialDeck));
            ownsWallet = wallet == null;
            this.wallet = wallet ?? new CoinWallet(rules.StartingCoins);
            handSize = rules.InitialHand;
            foreach (var spec in catalog.Values)
            {
                int count = initialCopies == null                                 ? copiesPerType
                    : initialCopies.TryGetValue(spec.Id, out var configuredCount) ? configuredCount
                                                                                  : 0;
                int inDeck = initialDeck == null ? count : initialDeck.TryGetValue(spec.Id, out var deckCount) ? deckCount : 0;
                for (int i = 0; i < count; i++)
                {
                    var copy = new CardCopy(nextId++, spec);
                    owned.Add(copy);
                    if (i < inDeck && selected.Count < rules.DeckLimit) selected.Add(copy.Id);
                }
            }
        }

        public CardCopy Find(int id) => owned.Find(c => c.Id == id);
        public bool IsSelected(int id) => selected.Contains(id);
        public List<CardCopy> Selected() => owned.Where(c => selected.Contains(c.Id)).ToList();
        public int HandSize => handSize;
        public int HandPrice => checked(Rules.HandUpgradePrice * (handSize - Rules.InitialHand + 1));
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
            if (selected.Count >= Rules.DeckLimit) return "Колода заполнена. Уберите одну карту.";
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

        public string ImproveHand()
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            if (handSize >= Rules.MaxHand) return "Рука уже максимального размера.";
            if (!wallet.TrySpend(HandPrice)) return "Не хватает монет.";
            handSize++;
            Changed?.Invoke();
            return null;
        }

        public string CanDepart()
        {
            if (IsAway) return "Вылазка уже началась.";
            if (selected.Count == 0) return "Добавьте в колоду хотя бы одну карту.";
            return null;
        }

        public string Depart(int seed)
        {
            string error = CanDepart();
            if (error != null) return error;
            Current = new Expedition(Selected(), handSize, seed);
            Journeys++;
            Changed?.Invoke();
            return null;
        }

        public WorkshopSave Save() => new WorkshopSave { coins = Coins, nextId = nextId, hand = handSize, journeys = Journeys,
            copies = owned.Select(c => new CopySave { id = c.Id, definition = c.Spec.Id, level = c.Level }).ToList(),
            selected = owned.Where(c => selected.Contains(c.Id)).Select(c => c.Id).ToList() };

        /// <summary>
        /// Validates a snapshot and brings an old one up to date: a version 1 save had two decks and two hands,
        /// so it keeps the larger (paid) hand and the first copies of its selection that fit the one deck.
        /// </summary>
        bool Read(WorkshopSave save, out List<CardCopy> copies, out List<int> equipped, out int hand)
        {
            copies = null;
            equipped = null;
            hand = 0;
            if (save == null || save.version < 1 || save.version > WorkshopSave.CurrentVersion || save.copies == null
                || save.selected == null || save.coins < 0 || save.journeys < 0 || save.nextId < 1)
                return false;
            bool legacy = save.version == 1;
            hand = legacy ? Math.Max(save.explorationHand, save.combatHand) : save.hand;
            if (hand < Rules.InitialHand || hand > Rules.MaxHand
                || legacy && (save.explorationHand < Rules.InitialHand || save.combatHand < Rules.InitialHand))
                return false;
            var ids = new HashSet<int>();
            copies = new List<CardCopy>();
            foreach (var c in save.copies)
            {
                if (c == null || c.definition == null || c.id < 1 || c.id >= save.nextId || !ids.Add(c.id) || c.level < 1
                    || c.level > Rules.MaxCardLevel || !catalog.TryGetValue(c.definition, out var spec))
                    return false;
                copies.Add(new CardCopy(c.id, spec, c.level));
            }
            var unique = new HashSet<int>(save.selected);
            if (unique.Count != save.selected.Count || !unique.IsSubsetOf(ids)) return false;
            if (!legacy && save.selected.Count > Rules.DeckLimit) return false;
            equipped = save.selected.Take(Rules.DeckLimit).ToList();
            return true;
        }

        /// <summary>Validates the full card snapshot before a scene-wide restore changes any subsystem.</summary>
        public bool CanRestoreProgress(WorkshopSave save) => !IsAway && Read(save, out _, out _, out _);

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
            Read(save, out var copies, out var equipped, out var hand);
            owned.Clear();
            owned.AddRange(copies);
            selected.Clear();
            selected.UnionWith(equipped);
            nextId = save.nextId;
            Journeys = save.journeys;
            handSize = hand;
            Current = null;
        }
    }

    /// <summary>One outing's deck: a shuffled draw pile, one hand and the spent pile.</summary>
    public sealed class ExpeditionDeck
    {
        readonly Queue<CardCopy> stock;
        readonly List<CardCopy> hand = new List<CardCopy>(), discard = new List<CardCopy>();
        public int Capacity { get; }
        public int Total { get; }
        public int StockCount => stock.Count;
        public IReadOnlyList<CardCopy> Hand => hand.AsReadOnly();
        public IReadOnlyList<CardCopy> Discard => discard.AsReadOnly();
        public bool HasReturn => hand.Any(c => c.IsReturn);
        internal ExpeditionDeck(IEnumerable<CardCopy> cards, int capacity, int seed)
        {
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

        /// <summary>Draws into free places; once the pile and every ordinary card in hand are gone, the Return card appears.</summary>
        void Fill()
        {
            while (hand.Count < Capacity && stock.Count > 0) hand.Add(stock.Dequeue());
            if (stock.Count == 0 && hand.Count == 0) hand.Add(CardCopy.Return());
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
        public ExpeditionDeck Deck { get; }
        public bool Completed { get; private set; }
        public int PlayedCount { get; private set; }
        public int DiscardedCount { get; private set; }
        public event Action<CardCopy> CardReleased;
        public Expedition(IEnumerable<CardCopy> cards, int handSize, int seed)
        {
            if (handSize < 1) throw new ArgumentOutOfRangeException(nameof(handSize));
            Deck = new ExpeditionDeck(cards, handSize, seed);
        }

        public CardCopy Release(int id)
        {
            if (Completed) return null;
            var card = Deck.Release(id);
            if (card == null) return null;
            PlayedCount++;
            CardReleased?.Invoke(card);
            return card;
        }

        public IReadOnlyList<CardCopy> DiscardHand()
        {
            if (Completed) return Array.Empty<CardCopy>();
            var cards = Deck.DiscardHand();
            DiscardedCount += cards.Count;
            return cards;
        }

        public bool ReturnHome()
        {
            if (Completed || !Deck.HasReturn) return false;
            Completed = true;
            return true;
        }

        public void Defeat() { Completed = true; }
    }
}
