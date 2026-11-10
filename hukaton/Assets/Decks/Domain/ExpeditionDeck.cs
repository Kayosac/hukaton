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

        internal CardCopy Release(int id)
        {
            var card = hand.Find(c => c.Id == id && !c.IsReturn);
            if (card == null) return null;
            hand.Remove(card);
            // In the prologue a spell is never used up: it comes back once its throw is over.
            if (!(Prologue && card.Spec.Mergeable)) card.UsesLeft = Math.Max(0, card.UsesLeft - 1);
            if (card.UsesLeft > 0) held.Add(card);
            else discard.Add(card);
            Fill();
            return card;
        }

        /// <summary>Brings a held card back to the hand. False when it was not held (already back, or spent).</summary>
        internal bool Settle(int id)
        {
            var card = held.Find(c => c.Id == id);
            if (card == null) return false;
            held.Remove(card);
            hand.RemoveAll(c => c.IsReturn);
            hand.Add(card);
            return true;
        }

        internal void AddToHand(CardCopy card)
        {
            hand.RemoveAll(c => c.IsReturn);
            hand.Add(card);
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
        public event Action<CardCopy> CardReleased;

        public Expedition(IEnumerable<CardCopy> cards, int handSize, int seed, bool prologue = false)
        {
            if (handSize < 1) throw new ArgumentOutOfRangeException(nameof(handSize));
            Deck = new ExpeditionDeck(cards, handSize, seed, prologue);
        }

        public bool Prologue => Deck.Prologue;

        public CardCopy Release(int id)
        {
            if (Completed) return null;
            var card = Deck.Release(id);
            if (card == null) return null;
            PlayedCount++;
            CardReleased?.Invoke(card);
            return card;
        }

        /// <summary>The thrown card's attempt is over (hit, miss or gone): a card with uses left returns to the hand.</summary>
        public bool Settle(int id) => !Completed && Deck.Settle(id);

        public IReadOnlyList<CardCopy> DiscardHand()
        {
            if (Completed) return Array.Empty<CardCopy>();
            var cards = Deck.DiscardHand();
            DiscardedCount += cards.Count;
            return cards;
        }

        int nextGrantedId = -1000;

        /// <summary>Adds a temporary copy (never owned or saved) to the hand: the cave prologue's cards, cheats.</summary>
        public CardCopy GrantToHand(CardSpec spec, int level = 1)
        {
            if (Completed || spec == null) return null;
            var card = new CardCopy(nextGrantedId--, spec, spec.Mergeable ? Math.Max(1, Math.Min(CardSpec.MaxLevel, level)) : 1);
            // A spell given in the prologue never runs out.
            if (Prologue && spec.Mergeable) card.UsesLeft = int.MaxValue;
            Deck.AddToHand(card);
            return card;
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
