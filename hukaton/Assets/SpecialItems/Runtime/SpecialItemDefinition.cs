using UnityEngine;

namespace Hakaton.SpecialItems
{
    /// <summary>When a special item appears on the goblin card dealer's table.</summary>
    public enum SpecialItemUnlock
    {
        /// <summary>From the start.</summary>
        Always,
        /// <summary>Once the shop has made its first revenue goal.</summary>
        FirstGoal,
        /// <summary>Never by itself (a hidden condition the game opens some other way).</summary>
        Hidden,
        /// <summary>Back from the god: once the goblin on the counter has had his word (the phone, the player).</summary>
        AfterGod
    }

    /// <summary>How a special item sits with the cards while it is out.</summary>
    public enum SpecialItemUse
    {
        /// <summary>The cards stay usable while the item is in the hand (the phone).</summary>
        AlongsideCards,
        /// <summary>The cards are put away while the item is in the hand.</summary>
        InsteadOfCards
    }

    /// <summary>
    /// One special item («спецпредмет»): very expensive, bought once from the goblin card dealer and kept forever, used
    /// on outings (Q). Can't be burned, has no card levels. What it does lives in its own tool on the player.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Special items/Item")]
    public sealed class SpecialItemDefinition : ScriptableObject
    {
        [Tooltip("Stable id used by saves. Never change it once shipped.")]
        public string id = "phone";
        public string displayName = "Телефон";
        [Min(0)] public int price = 5000;
        public SpecialItemUnlock unlock = SpecialItemUnlock.AfterGod;
        public SpecialItemUse use = SpecialItemUse.AlongsideCards;
        [Tooltip("Which side of the dealer's card row it lies on.")]
        public bool leftOfCards = true;
        [Tooltip("How it lies on the dealer's table.")]
        public Sprite tableSprite;
        [Tooltip("Its picture in the Q menu.")]
        public Sprite menuIcon;
        [Tooltip("The dealer introduces it the first time it lies on his table (E / click between lines).")]
        [TextArea] public string[] introduction =
        {
            "О, партнёр, глянь, что у меня есть. Телефон. Настоящий.",
            "Там внутри автомат. Крутишь, пока бродишь по лесу. Иногда платит.",
            "Иногда нет. Бизнес есть бизнес."
        };
        [Tooltip("What the dealer says when it is bought.")]
        [TextArea] public string boughtLine = "Носи с собой. Сдачи не будет.";
        [Tooltip("Right after the purchase, after the line above: how it works, one line per E (the bought item hangs in the middle meanwhile).")]
        [TextArea] public string[] howToUse = new string[0];
    }
}
