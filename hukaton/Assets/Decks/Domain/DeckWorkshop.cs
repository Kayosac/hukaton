using System;
using System.Collections.Generic;
using System.Linq;
using Hakaton.Economy;

namespace Hakaton.Decks
{
    /// <summary>
    /// Collection and economy. Every card in the deck is a particular owned copy; one deck, one hand.
    /// Board: copies merge for free on the PC (I + I → II, II + II → III); the collection holds at most twice the deck
    /// limit; the brazier burns a copy for 75% of what was paid for it; the three starting copies cannot burn.
    /// </summary>
    public sealed class DeckWorkshop
    {
        /// <summary>Burning costs this share of the copy's purchase price, rounded up.</summary>
        public const int BurnPercent = 75;

        readonly List<CardCopy> owned = new List<CardCopy>();
        readonly HashSet<int> selected = new HashSet<int>();
        readonly Dictionary<string, CardSpec> catalog;
        int handSize, deckStep;
        CoinWallet wallet;
        readonly bool ownsWallet;
        int nextId = 1;
        public event Action Changed;
        public DeckRules Rules { get; }
        public int Coins => wallet.Balance;
        public CoinWallet Wallet => wallet;
        public int Journeys { get; private set; }
        public IReadOnlyList<CardCopy> Owned => owned.AsReadOnly();
        public IEnumerable<CardSpec> Catalog => catalog.Values;
        public Expedition Current { get; private set; }
        public bool IsAway => Current != null && !Current.Completed;

        public int DeckLimit => Rules.DeckLimitAt(deckStep);
        public int CollectionLimit => DeckRules.CollectionFactor * DeckLimit;
        public bool CollectionFull => owned.Count >= CollectionLimit;
        public bool DeckAtMaximum => deckStep >= Rules.DeckSteps - 1;
        public int DeckLimitPrice => DeckAtMaximum ? 0 : Rules.DeckStepPrice(deckStep + 1);
        public int NextDeckLimit => Rules.DeckLimitAt(deckStep + 1);

        /// <param name="initialCopies">Copies of each type to start with. Without it every type starts with
        /// <paramref name="copiesPerType"/> copies.</param>
        /// <param name="initialDeck">Copies of each type that start in the deck. Without it the deck takes the
        /// collection in order up to the limit.</param>
        public DeckWorkshop(DeckRules rules, IEnumerable<CardSpec> specs, int copiesPerType = 3, CoinWallet wallet = null,
            IReadOnlyDictionary<string, int> initialCopies = null, IReadOnlyDictionary<string, int> initialDeck = null)
            : this(rules, specs, wallet, Starting(specs, copiesPerType, initialCopies, initialDeck)) { }

        public DeckWorkshop(DeckRules rules, IEnumerable<CardSpec> specs, CoinWallet wallet, IEnumerable<StartingCopies> start)
        {
            Rules = rules;
            catalog = specs.ToDictionary(s => s.Id);
            if (catalog.Count == 0 || catalog.Values.Any(s => string.IsNullOrWhiteSpace(s.Id) || s.Id == "return"))
                throw new ArgumentException("Invalid card catalogue");
            var startList = (start ?? Enumerable.Empty<StartingCopies>()).ToList();
            if (startList.Any(s => s.Id == null || !catalog.ContainsKey(s.Id) || s.Count < 0 || s.InDeck < 0))
                throw new ArgumentException("Invalid starting card copies", nameof(start));
            ownsWallet = wallet == null;
            this.wallet = wallet ?? new CoinWallet(rules.StartingCoins);
            handSize = rules.InitialHand;
            foreach (var entry in startList)
            {
                var spec = catalog[entry.Id];
                for (int i = 0; i < entry.Count; i++)
                {
                    // Starting copies count as bought at full price (the burn price follows from it).
                    var copy = new CardCopy(nextId++, spec, 1, spec.Price, entry.Protected);
                    owned.Add(copy);
                    if (i < entry.InDeck && selected.Count < DeckLimit) selected.Add(copy.Id);
                }
            }
        }

        static IEnumerable<StartingCopies> Starting(IEnumerable<CardSpec> specs, int copiesPerType,
            IReadOnlyDictionary<string, int> initialCopies, IReadOnlyDictionary<string, int> initialDeck)
        {
            if (copiesPerType < 0) throw new ArgumentOutOfRangeException(nameof(copiesPerType));
            var list = specs.ToList();
            if (initialCopies != null && initialCopies.Any(e => list.All(s => s.Id != e.Key) || e.Value < 0))
                throw new ArgumentException("Invalid starting card copies", nameof(initialCopies));
            if (initialDeck != null && initialDeck.Any(e => list.All(s => s.Id != e.Key) || e.Value < 0))
                throw new ArgumentException("Invalid starting deck", nameof(initialDeck));
            foreach (var spec in list)
            {
                int count = initialCopies == null ? copiesPerType : initialCopies.TryGetValue(spec.Id, out var c) ? c : 0;
                int inDeck = initialDeck == null ? count : initialDeck.TryGetValue(spec.Id, out var d) ? d : 0;
                yield return new StartingCopies(spec.Id, count, inDeck);
            }
        }

        public CardSpec Spec(string id) => id != null && catalog.TryGetValue(id, out var spec) ? spec : null;
        public CardCopy Find(int id) => owned.Find(c => c.Id == id);
        public bool IsSelected(int id) => selected.Contains(id);
        public List<CardCopy> Selected() => owned.Where(c => selected.Contains(c.Id)).ToList();
        public int HandSize => handSize;
        public int HandPrice => checked(Rules.HandUpgradePrice * (handSize - Rules.InitialHand + 1));

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
            if (selected.Count >= DeckLimit) return "Колода заполнена. Уберите одну карту.";
            selected.Add(id);
            Changed?.Invoke();
            return null;
        }

        // ---------- getting and losing copies ----------

        /// <summary>Buys a copy at its full price (debug and tests; the goblin sells through <see cref="Purchase"/>).</summary>
        public string Buy(string definition, out CardCopy copy) => Purchase(definition, Spec(definition)?.Price ?? 0, out copy);

        /// <summary>Adds a copy paid at <paramref name="price"/>. Nothing changes when there is no room or no money.</summary>
        public string Purchase(string definition, int price, out CardCopy copy)
        {
            copy = null;
            if (IsAway) return "Сначала вернитесь в лавку.";
            var spec = Spec(definition);
            if (spec == null) return "Эта карта недоступна.";
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
            if (CollectionFull || nextId == int.MaxValue) return "В коллекции нет места.";
            if (!wallet.TrySpend(price)) return "Не хватает монет.";
            copy = new CardCopy(nextId++, spec, 1, price);
            owned.Add(copy);
            Changed?.Invoke();
            return null;
        }

        public int BurnPrice(CardCopy copy) => copy == null ? 0 : (int)Math.Ceiling(copy.PaidPrice * (BurnPercent / 100.0));

        public string CanBurn(CardCopy copy)
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            if (copy == null || !owned.Contains(copy)) return "Экземпляр не найден.";
            if (copy.Protected) return "Эту карту не сжечь.";
            if (Coins < BurnPrice(copy)) return "Не хватает монет.";
            return null;
        }

        /// <summary>Destroys the copy for <see cref="BurnPrice"/>; a copy in the deck leaves it.</summary>
        public string Burn(int id)
        {
            var copy = Find(id);
            string error = CanBurn(copy);
            if (error != null) return error;
            if (!wallet.TrySpend(BurnPrice(copy))) return "Не хватает монет.";
            owned.Remove(copy);
            selected.Remove(id);
            Changed?.Invoke();
            return null;
        }

        // ---------- merging ----------

        /// <summary>Copies that can merge with <paramref name="copy"/>: same card, same level, below the maximum.</summary>
        public IEnumerable<CardCopy> MergePartners(CardCopy copy) => owned.Where(other => CanMerge(copy, other) == null);

        public string CanMerge(CardCopy a, CardCopy b)
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            if (a == null || b == null || !owned.Contains(a) || !owned.Contains(b)) return "Выберите две копии.";
            if (a.Id == b.Id) return "Нужны две разные копии.";
            if (a.Spec.Id != b.Spec.Id) return "Объединяются только одинаковые карты.";
            if (!a.Spec.Mergeable) return "Эту карту нельзя объединить.";
            if (a.Level != b.Level) return "Нужны копии одного уровня.";
            if (a.Level >= Rules.MaxCardLevel) return "Уровень III — максимум.";
            return null;
        }

        /// <summary>
        /// Two equal copies become one of the next level, for free. The first copy keeps its number; the result stays
        /// in the deck if either copy was there.
        /// </summary>
        public string Merge(int first, int second, out CardCopy result)
        {
            var a = Find(first);
            var b = Find(second);
            result = null;
            string error = CanMerge(a, b);
            if (error != null) return error;
            bool inDeck = selected.Remove(a.Id) | selected.Remove(b.Id);
            owned.Remove(b);
            a.Absorb(b);
            if (inDeck) selected.Add(a.Id);
            result = a;
            Changed?.Invoke();
            return null;
        }

        // ---------- capacities ----------

        public string ImproveHand()
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            if (handSize >= Rules.MaxHand) return "Рука уже максимального размера.";
            if (!wallet.TrySpend(HandPrice)) return "Не хватает монет.";
            handSize++;
            Changed?.Invoke();
            return null;
        }

        public string ImproveDeckLimit()
        {
            if (IsAway) return "Сначала вернитесь в лавку.";
            if (DeckAtMaximum) return "Колода уже максимального размера.";
            if (!wallet.TrySpend(DeckLimitPrice)) return "Не хватает монет.";
            deckStep++;
            Changed?.Invoke();
            return null;
        }

        // ---------- outings ----------

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

        /// <summary>
        /// The cave prologue: an outing without the deck — the tutorial puts its cards into the hand itself. Spells come
        /// back after every throw and no Return card appears.
        /// </summary>
        public string DepartPrologue(int handSize = 3)
        {
            if (IsAway) return "Вылазка уже началась.";
            Current = new Expedition(Enumerable.Empty<CardCopy>(), handSize, 0, true);
            Changed?.Invoke();
            return null;
        }

        // ---------- saving ----------

        public WorkshopSave Save() => new WorkshopSave { coins = Coins, nextId = nextId, hand = handSize, journeys = Journeys,
            deckStep = deckStep,
            copies = owned.Select(c => new CopySave { id = c.Id, definition = c.Spec.Id, level = c.Level, paid = c.PaidPrice,
                locked = c.Protected }).ToList(),
            selected = owned.Where(c => selected.Contains(c.Id)).Select(c => c.Id).ToList() };

        /// <summary>
        /// Validates a snapshot and brings an old one up to date. Version 1 had two decks and two hands: it keeps the
        /// larger (paid) hand and trims the selection to the one deck's limit. Versions 1–2 knew no paid prices: the
        /// copies count as bought at full price.
        /// </summary>
        bool Read(WorkshopSave save, out List<CardCopy> copies, out List<int> equipped, out int hand, out int step)
        {
            copies = null;
            equipped = null;
            hand = 0;
            step = 0;
            if (save == null || save.version < 1 || save.version > WorkshopSave.CurrentVersion || save.copies == null
                || save.selected == null || save.coins < 0 || save.journeys < 0 || save.nextId < 1)
                return false;
            bool legacy = save.version == 1;
            bool priced = save.version >= 3;
            hand = legacy ? Math.Max(save.explorationHand, save.combatHand) : save.hand;
            step = priced ? save.deckStep : 0;
            if (hand < Rules.InitialHand || hand > Rules.MaxHand || step < 0 || step >= Rules.DeckSteps
                || legacy && (save.explorationHand < Rules.InitialHand || save.combatHand < Rules.InitialHand))
                return false;
            var ids = new HashSet<int>();
            copies = new List<CardCopy>();
            foreach (var c in save.copies)
            {
                if (c == null || c.definition == null || c.id < 1 || c.id >= save.nextId || !ids.Add(c.id) || c.level < 1
                    || c.level > Rules.MaxCardLevel || c.paid < 0 || !catalog.TryGetValue(c.definition, out var spec))
                    return false;
                int level = spec.Mergeable ? c.level : 1;
                copies.Add(new CardCopy(c.id, spec, level, priced ? c.paid : spec.Price, priced && c.locked));
            }
            var unique = new HashSet<int>(save.selected);
            if (unique.Count != save.selected.Count || !unique.IsSubsetOf(ids)) return false;
            int limit = Rules.DeckLimitAt(step);
            if (!legacy && save.selected.Count > limit) return false;
            equipped = Trim(save.selected, copies.ToDictionary(c => c.Id), limit);
            return true;
        }

        /// <summary>Drops copies from the most numerous type first, so every type that was equipped stays in the deck.</summary>
        static List<int> Trim(List<int> selection, Dictionary<int, CardCopy> copies, int limit)
        {
            var kept = new List<int>(selection);
            while (kept.Count > limit)
            {
                var largest = kept.GroupBy(id => copies[id].Spec.Id).OrderByDescending(g => g.Count()).First();
                kept.Remove(largest.Last());
            }
            return kept;
        }

        /// <summary>Validates the full card snapshot before a scene-wide restore changes any subsystem.</summary>
        public bool CanRestoreProgress(WorkshopSave save) => !IsAway && Read(save, out _, out _, out _, out _);

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
            Read(save, out var copies, out var equipped, out var hand, out var step);
            owned.Clear();
            owned.AddRange(copies);
            selected.Clear();
            selected.UnionWith(equipped);
            nextId = save.nextId;
            Journeys = save.journeys;
            handSize = hand;
            deckStep = step;
            Current = null;
        }
    }
}
