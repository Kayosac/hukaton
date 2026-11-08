using UnityEngine;

namespace Hakaton.Decks.Unity
{
    public enum CardAction { Fire, Heal, Stone, Wind, Freeze, Curse }
    [CreateAssetMenu(menuName="Hakaton/Decks/Card definition")]
    public sealed class CardDefinition : ScriptableObject
    {
        public string id, displayName;
        public DeckKind kind;
        public CardAction action;
        public Sprite artwork;
        public Color accent=Color.white;
        [TextArea] public string description;
        [Min(0)] public int purchasePrice=30, upgradePrice=20, basePower=1, powerPerLevel=1;
        public CardSpec ToSpec() => new CardSpec(id,displayName,kind,purchasePrice,upgradePrice,basePower,powerPerLevel);
    }
}
