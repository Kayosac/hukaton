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
        [Tooltip("A visitor who explains himself line by line (the bulk buyer): one phrase per line on screen, in order; " +
                 "the last one is for his closing line (the offer). Empty for ordinary visitors.")]
        public Hakaton.Feel.SfxCue[] voiceLines;

        /// <summary>The phrase for line <paramref name="line"/> of <paramref name="count"/>: the last phrase always goes with the last line.</summary>
        public Hakaton.Feel.SfxCue VoiceLine(int line, int count)
        {
            if (voiceLines == null || voiceLines.Length == 0 || line < 0 || count <= 0) return null;
            if (line >= count - 1) return voiceLines[voiceLines.Length - 1];
            return voiceLines[Mathf.Min(line, Mathf.Max(0, voiceLines.Length - 2))];
        }

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
