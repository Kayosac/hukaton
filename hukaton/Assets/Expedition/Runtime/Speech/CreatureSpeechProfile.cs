using System;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public enum CreatureSpeechMood { Neutral, Fear, Aggression, Attack }

    /// <summary>One character's words and speaking rhythm. Assign this to an authored HumanoidSpeech prefab.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Enemies/Speech profile", fileName = "Creature speech")]
    public sealed class CreatureSpeechProfile : ScriptableObject
    {
        [Header("A quiet encounter: greeting, one remark, then silence")]
        [TextArea(1, 3)] public string[] greetings = Array.Empty<string>();
        [TextArea(1, 3)] public string[] neutral = Array.Empty<string>();
        [Header("Reactions")]
        [TextArea(1, 3)] public string[] fear = Array.Empty<string>();
        [TextArea(1, 3)] public string[] aggression = Array.Empty<string>();
        [TextArea(1, 3)] public string[] attack = Array.Empty<string>();

        [Header("Distance and visibility")]
        [Tooltip("Hard speaking limit in metres. Leaving this radius immediately hides the bubble.")]
        [Min(.1f)] public float reactionDistance = 4;
        public bool requireLineOfSight = true;
        [Tooltip("Solid objects that prevent a face-to-face encounter. Ignore Raycast is excluded by default.")]
        public LayerMask occluderMask = ~(1 << 2);

        [Header("Rhythm (seconds, game time)")]
        public Vector2 initialDelay = new Vector2(.2f, .65f);
        [Min(.1f)] public float displaySeconds = 3.5f;
        [Tooltip("Pause after the greeting before the one neutral follow-up.")]
        [Min(0)] public float encounterFollowupDelay = 1;
        [Tooltip("Must stay outside the speaking radius this long before a new greeting is allowed.")]
        [Min(0)] public float reencounterDelay = 10;
        [Tooltip("Silence after a combat/fear line. Prevents a new line on every attack animation.")]
        public Vector2 reactionPause = new Vector2(3, 5);
        [Tooltip("A reaction can replace a neutral line only after this minimum reading time.")]
        [Min(0)] public float minimumReadSeconds = 1.2f;
        [Tooltip("Remember a brief attack state this long while waiting to show its reaction.")]
        [Min(0)] public float reactionMemorySeconds = 2.5f;

        public string[] GetLines(CreatureSpeechMood mood)
        {
            switch (mood)
            {
                case CreatureSpeechMood.Fear: return fear;
                case CreatureSpeechMood.Aggression: return aggression;
                case CreatureSpeechMood.Attack: return attack;
                default: return neutral;
            }
        }
    }
}
