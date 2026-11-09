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
        [Header("Voice (optional; the shop's common cues play when empty)")]
        [Tooltip("Steps up to the counter.")]
        public Hakaton.Feel.SfxCue voiceArrive;
        [Tooltip("Weighs a neutral offer.")]
        public Hakaton.Feel.SfxCue voiceThinking;
        [Tooltip("Likes the offer, and buys.")]
        public Hakaton.Feel.SfxCue voiceHappy;
        [Tooltip("Dislikes the offer, and walks out.")]
        public Hakaton.Feel.SfxCue voiceAngry;

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
