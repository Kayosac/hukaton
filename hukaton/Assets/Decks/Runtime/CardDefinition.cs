using UnityEngine;

namespace Hakaton.Decks.Unity
{
    /// <summary>Element tag: obstacles react to Fire / Freeze. Numeric values are serialized — never renumber.</summary>
    public enum CardAction
    {
        Fire = 0,
        Heal = 1,
        Impulse = 3,
        Freeze = 4,
        Strike = 6,
        HeavyStrike = 7,
        // Extended catalogue (board, 09.11.2026). Behaviour lives in each card's effect asset.
        Lure = 10,
        TarPuddle = 11,
        Attraction = 12,
        Fog = 13,
        RainCloud = 14,
        Lightning = 15,
        StoneCube = 16,
        BranchBridge = 17,
        Lightness = 18,
        MushroomTrampoline = 19,
        MoonGravity = 20,
        Rope = 21,
        Updraft = 22,
        TreeAwakening = 23,
        Swap = 24,
        Teleport = 25,
        // Second catalogue (12.11.2026): ten cards drawn for the analysis «новые карты».
        Discord = 26,
        Scarecrow = 27,
        CrookedMirror = 28,
        Draft = 29,
        StickyHour = 30,
        Gilding = 31,
        MerchantNose = 32,
        Marauder = 33,
        Echo = 34,
        Bubble = 35,
        // Третий каталог (14.11.2026): семь новых карт из таблицы KayoSpace.
        WaterJet = 36,
        Rockfall = 37,
        EvilEye = 38,
        Crows = 39,
        PowderBarrel = 40,
        Boomerang = 41,
        Fan = 42
    }

    [CreateAssetMenu(menuName = "Hakaton/Decks/Card definition")]
    public sealed class CardDefinition : ScriptableObject
    {
        public string id, displayName;
        public CardAction action;
        public Sprite artwork;
        public Color accent = Color.white;
        [Tooltip("What the card does on impact (asset under Assets/Game/Data/Effects).")]
        public ScriptableObject effectAsset;
        [TextArea]
        public string description;

        [Header("Goblin")]
        [Tooltip("Full price of one copy. Face down the goblin sells it for half, rounded up.")]
        [Min(0)] public int purchasePrice = 100;
        [Tooltip("Rarity rank 1–5: the goblin rolls a rank first, then a card of that rank.")]
        [Range(1, 5)] public int rank = 1;
        [Tooltip("The goblin's dummy («Пустышка», the config's dummy card): does nothing, never merges, always sells for " +
                 "half its price, and leaves the deck and the collection for good as an outing begins.")]
        public bool dummy;

        [Header("Levels I / II / III")]
        [Tooltip("Off keeps every copy at level I (no merging on the PC).")]
        public bool upgradable = true;
        [Tooltip("Uses of one copy per outing. 1 for ordinary cards; Teleport 1 / 2 / 3.")]
        public LevelInt uses = LevelInt.All(1);
        [Tooltip("Direct number of the simple cards: damage (Fire, Strike, Heavy strike), freeze seconds (Ice), " +
                 "healing in half hearts (Heal).")]
        public LevelInt power = LevelInt.All(1);
        [Tooltip("Push of Impulse and Heavy strike (Δv for mass 1).")]
        public LevelFloat impulse = LevelFloat.All(8);
        [Min(0)] public float effectRadius = 2.5f;

        /// <param name="asDummy">Treat it as the dummy even if its own <see cref="dummy"/> flag is off (the config's dummy card).</param>
        public CardSpec ToSpec(bool asDummy = false)
        {
            bool isDummy = dummy || asDummy;
            return new CardSpec(id, displayName, purchasePrice, rank, upgradable && !isDummy, uses.ToArray(), isDummy);
        }
    }
}
