using System;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.EnemyDebug
{
    /// <summary>
    /// One debug card, granted only to this isolated prologue session: an ordinary Strike to begin with. The existing
    /// deck returns it after every attempt. <see cref="nextCardKey"/> swaps it for the next card of the session's
    /// catalogue (the isolated session's <c>cards</c>), so any card added there can be thrown for real from the hand.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class EnemyDebugStrikeCard : MonoBehaviour, IThrowRules
    {
        public ExpeditionWorld world;
        [Tooltip("The card in the hand when the scene starts (Strike).")]
        public CardDefinition definition;
        [Range(1, 3)] public int level = 1;
        [Tooltip("Swaps the card in the hand for the next card of the isolated session's catalogue.")]
        public Key nextCardKey = Key.T;

        public CardCopy Card { get; private set; }
        /// <summary>The definition of the card in the hand now.</summary>
        public CardDefinition Current { get; private set; }

        void Start()
        {
            if (!world || !world.thrower || !world.hand || !definition || !(definition.effectAsset is StrikeEffect)
                || world.workshop.Model.Current == null || !world.workshop.Model.Current.Prologue)
            {
                Debug.LogError("The debug Strike needs its saved world, thrower, hand, Strike definition and isolated prologue session.", this);
                enabled = false;
                return;
            }
            world.thrower.rules = this;
            Grant(definition);
            world.thrower.ResetSelection();
            world.thrower.SelectCopy(Card.Id);
            world.hand.Deal();
        }

        void Grant(CardDefinition card)
        {
            Current = card;
            Card = world.workshop.Model.Current.GrantToHand(card.ToSpec(), level);
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null || nextCardKey == Key.None || !keys[nextCardKey].wasPressedThisFrame) return;
            if (!world.Running || (world.control && (!world.control.GameplayActive || world.control.CapturedThisFrame))) return;
            Next();
        }

        /// <summary>
        /// Swaps the card in the hand for the next throwable card of the catalogue. Only while the card is in the hand
        /// (not in the air). Returns the card now in the hand.
        /// </summary>
        public CardCopy Next()
        {
            var trip = world.workshop.Model.Current;
            var cards = world.workshop.config ? world.workshop.config.cards : null;
            if (trip == null || Card == null || cards == null || cards.Length < 2) return Card;
            bool inHand = false;
            foreach (var card in trip.Deck.Hand) if (card.Id == Card.Id) inHand = true;
            if (!inHand) return Card;
            int at = Array.IndexOf(cards, Current);
            for (int step = 1; step <= cards.Length; step++)
            {
                var next = cards[((at < 0 ? 0 : at) + step) % cards.Length];
                if (!next || next == Current || !(next.effectAsset is CardEffect)) continue;
                world.thrower.Cancel();
                if (!trip.Withdraw(Card.Id)) return Card;
                Grant(next);
                world.thrower.SelectCopy(Card.Id);
                world.Announce("Отладка: " + next.displayName, 1.5f);
                break;
            }
            return Card;
        }

        public bool CanSelect(CardCopy card) => card != null && card.Id == Card?.Id;
        public bool CanThrow(CardCopy card) => CanSelect(card);
        // R belongs to the sandbox reset. The gameplay thrower must not also discard the hand.
        public bool CanDiscard() => false;
        public void Discarded() { }

        void OnDisable()
        {
            if (world && world.thrower && ReferenceEquals(world.thrower.rules, this))
            {
                world.thrower.Cancel();
                world.thrower.rules = null;
            }
        }
    }
}
