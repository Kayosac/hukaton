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
        public Sprite returnArtwork;
        [Tooltip("Deck limit at the start. The collection holds twice the current deck limit.")]
        [Min(1)]
        public int deckLimit = 12;
        [Tooltip("Further deck limits bought on the PC, in order (board: up to about 50).")]
        public DeckLimitStep[] deckLimitSteps = Array.Empty<DeckLimitStep>();
        [Range(1, 5)]
        public int initialHand = 3, maximumHand = 5;
        [Range(1, 3)]
        public int maximumCardLevel = 3;
        [Tooltip("Hand +1 costs this, times the step (board: 3 → 4 for 200, 4 → 5 for 400).")]
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

        public DeckWorkshop CreateWorkshop(CoinWallet wallet = null) => new DeckWorkshop(Rules(), cards.Select(c => c.ToSpec()),
            wallet, initialCopies.Select(c => new StartingCopies(c.definitionId, c.count, c.inDeck, c.protectedFromBurning)));

        public CardDefinition Find(string id) => cards.FirstOrDefault(c => c.id == id);
    }
}
