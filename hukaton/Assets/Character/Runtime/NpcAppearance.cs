using UnityEngine;

namespace Hakaton.Character
{
    public enum NpcExpression { Neutral, Displeased, Happy, Angry }

    [CreateAssetMenu(menuName = "Hakaton/Characters/NPC Appearance", fileName = "NpcAppearance")]
    public sealed class NpcAppearance : ScriptableObject
    {
        public string displayName;
        public Sprite neutral;
        public Sprite back;
        public Sprite displeased;
        public Sprite happy;
        public Sprite angry;
        [Min(.001f)] public float bodyScale = .18f;

        public Sprite ForExpression(NpcExpression expression)
        {
            switch (expression)
            {
                case NpcExpression.Displeased: return displeased;
                case NpcExpression.Happy: return happy;
                case NpcExpression.Angry: return angry;
                default: return neutral;
            }
        }

        public bool IsComplete => neutral && back && displeased && happy && angry
            && bodyScale > 0 && !float.IsInfinity(bodyScale);
    }
}
