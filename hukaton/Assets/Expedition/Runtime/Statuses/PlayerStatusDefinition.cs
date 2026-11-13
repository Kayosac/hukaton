using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// One player debuff (<see cref="PlayerStatusKind"/>): its default duration and strength, how it can be removed,
    /// its HUD icon and its sounds. An attack or a trap may override the duration and strength (<see cref="StatusInflict"/>).
    /// What <see cref="strength"/> and <see cref="interval"/> mean depends on the kind (see the enum).
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Player status")]
    public sealed class PlayerStatusDefinition : ScriptableObject
    {
        public PlayerStatusKind kind;
        public string displayName;
        [TextArea(2, 4)] public string description;

        [Header("Defaults (an attack or a trap may override seconds and strength)")]
        [Tooltip("How long it lasts, s. 0: until the outing ends (Rot).")]
        [Min(0)] public float seconds = 6;
        [Tooltip("Slow: speed multiplier. Poison, Burn: half hearts per tick. Deafen: low-pass cutoff, Hz. Blind: metres still seen. " +
                 "Crooked hands: max degrees off the aim. Leaky pocket: coins per drop. Fragile: damage multiplier. Mark: radius, m. " +
                 "Warp: how hard (1 normal). Rot: half hearts off the maximum per hit. Shuffle: unused.")]
        public float strength = 1;
        [Tooltip("Poison, Burn: seconds between ticks. Shuffle: seconds between shuffles. Leaky pocket: metres walked per drop. Mark: seconds between calls.")]
        [Min(.05f)] public float interval = 1;

        [Header("Removal")]
        [Tooltip("Ends as soon as the player gets wet (wading in water, rain).")]
        public bool washedByWater;
        [Tooltip("Cannot start while the player is wet.")]
        public bool blockedWhileWet;
        [Tooltip("The «Лечение» card cures it.")]
        public bool curedByHealing;

        [Header("HUD badge")]
        [Tooltip("The badge icon (drawn by the author). Empty: the placeholder of the badge prefab.")]
        public Sprite icon;
        [Tooltip("Optional: drawn frames of the icon turning, like the other badges (played in a loop instead of the single icon).")]
        public Sprite[] iconFrames = new Sprite[0];
        [Tooltip("Tint of the placeholder icon while there is no own icon.")]
        public Color placeholderTint = Color.white;

        [Header("Sound")]
        public SfxCue startCue;
        [Tooltip("Each tick (Poison, Burn), shuffle, dropped coin.")]
        public SfxCue tickCue;
        public SfxCue endCue;
        [Tooltip("Removed by an action (put out, washed off, cured) rather than running out.")]
        public SfxCue removedCue;
        [Tooltip("Played in a loop while it lasts (fire crackle, ringing).")]
        public SfxCue loopCue;

        /// <summary>No timer: lasts until the outing ends.</summary>
        public bool Permanent => seconds <= 0;
    }
}
