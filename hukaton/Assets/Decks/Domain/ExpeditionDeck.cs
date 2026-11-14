using System;
using System.Collections.Generic;
using System.Linq;

namespace Hakaton.Decks
{
    /// <summary>
    /// One outing's deck: a shuffled draw pile, one hand and the spent pile. A card with uses left (Teleport II / III)
    /// is held while its throw is in the air, keeps its place in the hand, and comes back once the attempt settles.
    /// </summary>
    public sealed class ExpeditionDeck
    {
        readonly Queue<CardCopy> stock;
        readonly List<CardCopy> hand = new List<CardCopy>(), discard = new List<CardCopy>(), held = new List<CardCopy>();
        readonly Dictionary<int, int> heldSlots = new Dictionary<int, int>();
        /// <summary>Held cards that come back only by being caught (Бумеранг), not merely because they have uses left.</summary>
        readonly HashSet<int> catchOnly = new HashSet<int>();
        public int Capacity { get; }
        public int Total { get; }
        public int StockCount => stock.Count;
        public IReadOnlyList<CardCopy> Hand => hand.AsReadOnly();
        public IReadOnlyList<CardCopy> Discard => discard.AsReadOnly();
        /// <summary>Cards in the air that come back to the hand when their attempt settles.</summary>
        public IReadOnlyList<CardCopy> Held => held.AsReadOnly();
        public bool HasReturn => hand.Any(c => c.IsReturn);
        /// <summary>The cave prologue: spells come back after every throw, no Return card, R keeps the spells.</summary>
        public bool Prologue { get; }

        internal ExpeditionDeck(IEnumerable<CardCopy> cards, int capacity, int seed, bool prologue = false)
        {
            Capacity = capacity;
            Prologue = prologue;
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

        /// <summary>Draws into free places; once the pile, the hand and the held cards are gone, the Return card appears.</summary>
        void Fill()
        {
            while (hand.Count + held.Count < Capacity && stock.Count > 0) hand.Add(stock.Dequeue());
            if (!Prologue && stock.Count == 0 && hand.Count == 0 && held.Count == 0) hand.Add(CardCopy.Return());
        }

        /// <param name="mayComeBack">
        /// The throw itself may bring the card back (Бумеранг, caught on its way back): the card is held while it flies
        /// even with no uses left, so nothing is drawn into its place; <see cref="Catch"/> returns it to the hand,
        /// <see cref="Settle"/> without a catch sends it to the spent pile.
        /// </param>
        internal CardCopy Release(int id, bool mayComeBack = false)
        {
            var card = hand.Find(c => c.Id == id && !c.IsReturn);
            if (card == null) return null;
            int slot = hand.IndexOf(card);
            hand.Remove(card);
            // In the prologue a spell is never used up: it comes back once its throw is over.
            if (!(Prologue && card.Spec.Mergeable)) card.UsesLeft = Math.Max(0, card.UsesLeft - 1);
            if (card.UsesLeft > 0 || mayComeBack) { held.Add(card); heldSlots[card.Id] = slot; }
            else discard.Add(card);
            if (mayComeBack) catchOnly.Add(card.Id);
            Fill();
            return card;
        }

        /// <summary>
        /// The attempt of a held card is over: with uses left it comes back to the hand; a card that could only come
        /// back by being caught (no uses left) goes to the spent pile. False when it was not held (already back, or spent).
        /// </summary>
        internal bool Settle(int id)
        {
            var card = held.Find(c => c.Id == id);
            if (card == null) return false;
            // A card that comes back only by being caught and was not caught goes to the spent pile, uses or not.
            if (catchOnly.Remove(id) || card.UsesLeft <= 0)
            {
                held.Remove(card);
                heldSlots.Remove(id);
                discard.Add(card);
                Fill();
                return false;
            }
            BackToHand(card);
            return true;
        }

        /// <summary>
        /// A held card was caught on its way back (Бумеранг): with uses left the same copy returns to its place in the
        /// hand (the throw still spent its use: uses are the number of throws, so the returns are one fewer); caught on
        /// its last use it goes to the spent pile. False when it was not held or did not come back to the hand.
        /// </summary>
        internal bool Catch(int id)
        {
            var card = held.Find(c => c.Id == id);
            if (card == null) return false;
            catchOnly.Remove(id);
            if (card.UsesLeft <= 0)
            {
                held.Remove(card);
                heldSlots.Remove(id);
                discard.Add(card);
                Fill();
                return false;
            }
            BackToHand(card);
            return true;
        }

        void BackToHand(CardCopy card)
        {
            held.Remove(card);
            hand.RemoveAll(c => c.IsReturn);
            // Back to the place it left from, so the fan does not reshuffle under the player's fingers.
            int slot = heldSlots.TryGetValue(card.Id, out var at) ? at : hand.Count;
            heldSlots.Remove(card.Id);
            hand.Insert(Math.Min(slot, hand.Count), card);
        }

        /// <summary>Takes a copy out of the hand without playing it (a debug tool swapping its temporary card).</summary>
        internal bool Withdraw(int id)
        {
            var card = hand.Find(c => c.Id == id && !c.IsReturn);
            if (card == null) return false;
            hand.Remove(card);
            Fill();
            return true;
        }

        internal void AddToHand(CardCopy card)
        {
            hand.RemoveAll(c => c.IsReturn);
            hand.Add(card);
        }

        /// <summary>The end of the cave prologue: the hand is cleared and only the Return card is left in it.</summary>
        internal CardCopy GiveReturn()
        {
            foreach (var c in hand) if (!c.IsReturn) discard.Add(c);
            hand.Clear();
            held.Clear();
            heldSlots.Clear();
            var card = CardCopy.Return();
            hand.Add(card);
            return card;
        }

        /// <summary>
        /// Shuffles the order of the cards in the hand (the Shuffle debuff); nothing leaves or enters it. Always changes
        /// the order when there are two or more cards. False when there is nothing to shuffle.
        /// </summary>
        internal bool ShuffleHand(Random random)
        {
            if (hand.Count < 2 || random == null) return false;
            var before = hand.ToArray();
            for (int attempt = 0; attempt < 8; attempt++)
            {
                for (int i = hand.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (hand[i], hand[j]) = (hand[j], hand[i]);
                }
                for (int i = 0; i < hand.Count; i++) if (!ReferenceEquals(hand[i], before[i])) return true;
            }
            // Unlucky rolls: at least swap the first two.
            (hand[0], hand[1]) = (hand[1], hand[0]);
            return true;
        }

        internal List<CardCopy> DiscardHand()
        {
            // The prologue's R restores the spells: only the instruction cards (not mergeable) leave.
            var cards = hand.Where(c => !c.IsReturn && !(Prologue && c.Spec.Mergeable)).ToList();
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
        /// <summary>
        /// Dummies the deck shed as this outing began: never in it, gone from the collection for good. Only for the
        /// presentation (the hand flings them away as it is dealt).
        /// </summary>
        public IReadOnlyList<CardCopy> Dummies { get; internal set; } = Array.Empty<CardCopy>();
        public event Action<CardCopy> CardReleased;

        public Expedition(IEnumerable<CardCopy> cards, int handSize, int seed, bool prologue = false)
        {
            if (handSize < 1) throw new ArgumentOutOfRangeException(nameof(handSize));
            Deck = new ExpeditionDeck(cards, handSize, seed, prologue);
        }

        public bool Prologue => Deck.Prologue;

        /// <param name="mayComeBack">The throw may bring the card back by itself (Бумеранг): see <see cref="Catch"/>.</param>
        public CardCopy Release(int id, bool mayComeBack = false)
        {
            if (Completed) return null;
            var card = Deck.Release(id, mayComeBack);
            if (card == null) return null;
            PlayedCount++;
            CardReleased?.Invoke(card);
            return card;
        }

        /// <summary>The thrown card's attempt is over (hit, miss or gone): a card with uses left returns to the hand.</summary>
        public bool Settle(int id) => !Completed && Deck.Settle(id);

        /// <summary>
        /// A card that flew back was caught (Бумеранг): the same copy returns to the hand with the use its throw took.
        /// False when it was not in the air or the outing is over.
        /// </summary>
        public bool Catch(int id) => !Completed && Deck.Catch(id);

        /// <summary>
        /// Takes a temporary copy (<see cref="GrantToHand"/>) out of the hand without playing it: debug tools swap their
        /// card this way. Owned copies are refused.
        /// </summary>
        public bool Withdraw(int id) => !Completed && id <= -1000 && Deck.Withdraw(id);

        /// <summary>The Shuffle debuff: the hand's order is mixed up. False with fewer than two cards or after the end.</summary>
        public bool ShuffleHand(Random random) => !Completed && Deck.ShuffleHand(random);

        public IReadOnlyList<CardCopy> DiscardHand()
        {
            if (Completed) return Array.Empty<CardCopy>();
            var cards = Deck.DiscardHand();
            DiscardedCount += cards.Count;
            return cards;
        }

        int nextGrantedId = -1000;

        /// <summary>
        /// Adds a temporary copy (never owned or saved) to the hand, over the hand's limit: the cave prologue's cards,
        /// cheats, the phone's prize. <paramref name="uses"/> above 0 overrides the uses of its level (the phone's card is
        /// one-use); like every card in the hand, R discards it, and it is gone with the outing.
        /// </summary>
        public CardCopy GrantToHand(CardSpec spec, int level = 1, int uses = 0)
        {
            if (Completed || spec == null) return null;
            var card = new CardCopy(nextGrantedId--, spec, spec.Mergeable ? Math.Max(1, Math.Min(CardSpec.MaxLevel, level)) : 1);
            if (uses > 0) card.UsesLeft = uses;
            // A spell given in the prologue never runs out.
            if (Prologue && spec.Mergeable) card.UsesLeft = int.MaxValue;
            Deck.AddToHand(card);
            return card;
        }

        /// <summary>The goblin hands over the way home at the end of the cave: the hand becomes just the Return card.</summary>
        public CardCopy GrantReturn() => Completed ? null : Deck.GiveReturn();

        public bool ReturnHome()
        {
            if (Completed || !Deck.HasReturn) return false;
            Completed = true;
            return true;
        }

        public void Defeat() { Completed = true; }
    }
}
