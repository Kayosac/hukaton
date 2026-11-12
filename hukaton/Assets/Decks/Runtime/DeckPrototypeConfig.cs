using System;
using System.Linq;
using UnityEngine;
using Hakaton.Economy;

namespace Hakaton.Decks.Unity
{
    [Serializable]
    public sealed class StartingCardCopies
    {
        public string definitionId;
        [Min(0)]
        public int count;
        [Tooltip("How many of these copies start in the deck (the rest wait in the collection).")]
        [Min(0)]
        public int inDeck;
        [Tooltip("The cave's starting copies: the brazier refuses them.")]
        public bool protectedFromBurning;
    }

    [Serializable]
    public sealed class DeckLimitStep
    {
        [Min(1)] public int limit;
        [Min(0)] public int price;
    }

    [CreateAssetMenu(menuName = "Hakaton/Decks/Prototype configuration")]
    public sealed class DeckPrototypeConfig : ScriptableObject
    {
        public CardDefinition[] cards;
        [Tooltip("Cards that exist only in the story (the cave's instruction cards): never sold, dealt or kept.")]
        public CardDefinition[] storyCards = Array.Empty<CardDefinition>();
        [Tooltip("The goblin's dummy («Пустышка»): the penalty for buying blind. Kept apart from the cards above: never in " +
                 "his rank draw, never merged, never given by the debug unlock. Bought, it takes a place in the collection " +
                 "and the deck like any card, and leaves both for good as the next outing begins.")]
        public CardDefinition dummyCard;
        [Tooltip("Chance for each of the goblin's ten places to hold the dummy instead of a rolled card (never on his first visit).")]
        [Range(0, 1)] public float dummyChance = .1f;
        public Sprite returnArtwork;
        [Tooltip("Deck limit at the start. The collection holds twice the current deck limit.")]
        [Min(1)]
        public int deckLimit = 12;
        [Tooltip("Further deck limits bought on the PC, in order (board: up to about 50).")]
        public DeckLimitStep[] deckLimitSteps = Array.Empty<DeckLimitStep>();
        [Tooltip("Cards in the hand at the start and after every bought step. The outing fan shows up to 20 (Expedition CardFanLayout.MaxCards); keys 1–9 and 0 select the first ten.")]
        [Range(1, 20)]
        public int initialHand = 3, maximumHand = 5;
        [Range(1, 3)]
        public int maximumCardLevel = 3;
        [Tooltip("Hand +1 costs this, times the step: 3 → 4 for 200, 4 → 5 for 400 … 9 → 10 for 1400.")]
        [Min(0)]
        public int handUpgradePrice = 200;
        [Min(0)]
        public int startingCoins;
        [Min(.05f)]
        public float dealSeconds = .28f, discardSeconds = .48f, returnSeconds = .8f;
        [Tooltip("The complete starting collection and starting deck. Omitted types start with zero copies.")]
        public StartingCardCopies[] initialCopies = Array.Empty<StartingCardCopies>();
        public AudioClip cardSound, coinSound, returnSound;

        public DeckRules Rules() => new DeckRules(deckLimit, initialHand, maximumHand, maximumCardLevel, handUpgradePrice,
            startingCoins, deckLimitSteps.Select(s => s.limit).ToArray(), deckLimitSteps.Select(s => s.price).ToArray());

        public DeckWorkshop CreateWorkshop(CoinWallet wallet = null) => new DeckWorkshop(Rules(), Specs(),
            wallet, initialCopies.Select(c => new StartingCopies(c.definitionId, c.count, c.inDeck, c.protectedFromBurning)));

        /// <summary>The workshop's catalogue: the cards and, when assigned, the dummy (saves holding it must load).</summary>
        public System.Collections.Generic.IEnumerable<CardSpec> Specs()
        {
            foreach (var card in cards) yield return card.ToSpec();
            if (dummyCard) yield return dummyCard.ToSpec(true);
        }

        public CardDefinition Find(string id) => cards.FirstOrDefault(c => c.id == id) ?? storyCards.FirstOrDefault(c => c && c.id == id)
            ?? (dummyCard && dummyCard.id == id ? dummyCard : null);
    }
}
