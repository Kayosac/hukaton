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
    }

    [CreateAssetMenu(menuName = "Hakaton/Decks/Prototype configuration")]
    public sealed class DeckPrototypeConfig : ScriptableObject
    {
        public CardDefinition[] cards;
        public Sprite returnArtwork;
        [Range(1, 12)]
        public int deckLimit = 12;
        [Range(1, 5)]
        public int initialHand = 3, maximumHand = 5;
        [Range(1, 3)]
        public int maximumCardLevel = 3;
        [Min(0)]
        public int startingCoins = 240, initialCopiesPerType = 3, handUpgradePrice = 60;
        [Min(.05f)]
        public float dealSeconds = .28f, discardSeconds = .48f, returnSeconds = .8f;
        [Tooltip("When specified, this is the complete starting collection. Omitted types start with zero copies.")]
        public StartingCardCopies[] initialCopies = Array.Empty<StartingCardCopies>();
        public AudioClip cardSound, coinSound, returnSound;
        public DeckRules Rules() => new DeckRules(
            deckLimit, initialHand, maximumHand, maximumCardLevel, handUpgradePrice, startingCoins);
        public DeckWorkshop CreateWorkshop(CoinWallet wallet = null) => new DeckWorkshop(Rules(), cards.Select(c => c.ToSpec()),
            initialCopiesPerType, wallet,
            initialCopies != null && initialCopies.Length > 0 ? initialCopies.ToDictionary(c => c.definitionId, c => c.count)
                                                              : null);
        public CardDefinition Find(string id) => cards.FirstOrDefault(c => c.id == id);
    }
}
