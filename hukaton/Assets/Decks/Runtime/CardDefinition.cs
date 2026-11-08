using UnityEngine;

namespace Hakaton.Decks.Unity
{
    /// <summary>Element tag: obstacles react to Fire / Freeze. Numeric values are serialized — never renumber.</summary>
    public enum CardAction { Fire = 0, Heal = 1, Impulse = 3, Freeze = 4, Strike = 6, HeavyStrike = 7 }
    [CreateAssetMenu(menuName="Hakaton/Decks/Card definition")]
    public sealed class CardDefinition : ScriptableObject
    {
        public string id, displayName;
        public DeckKind kind;
        public CardAction action;
        public Sprite artwork;
        public Color accent=Color.white;
        [Tooltip("What the card does on impact (asset under Assets/Game/Data/Effects).")]
        public ScriptableObject effectAsset;
        [TextArea] public string description;
        [Min(0)] public int purchasePrice=30, upgradePrice=20, basePower=1, powerPerLevel=1;
        [Header("Physical effect")]
        [Min(0)] public float impulse=8, impulsePerLevel=1.5f, effectRadius=2.5f;
        public float ImpulseAtLevel(int level) => impulse+Mathf.Max(0,level-1)*impulsePerLevel;
        public CardSpec ToSpec() => new CardSpec(id,displayName,kind,purchasePrice,upgradePrice,basePower,powerPerLevel);
    }
}
