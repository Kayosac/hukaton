using System;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>
    /// How a merge plays right in the PC's card grid, per result level: the dropped card dashes onto the other one,
    /// they grind together, smash in a white flash, and the card flips over showing its new level. Timings, motion,
    /// CRT pulses, Retro Arsenal bursts and sound cues live here; the higher the level, the bigger the show.
    /// Played by <see cref="PcMergeShow"/>. The asset is <c>Assets/Computer/Fx/PcMergeFx.asset</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Hakaton/Computer/Merge show")]
    public sealed class PcMergeFx : ScriptableObject
    {
        /// <summary>The moments of the show; bursts and cues fire at a beat plus their own delay.</summary>
        public enum Beat
        {
            [Tooltip("The drop: the card winds up and dashes onto the other one.")] Fly = 0,
            [Tooltip("The two cards grind together, shaking harder and harder.")] Grind = 1,
            [Tooltip("The smash: white flash, the dropped card is gone.")] Impact = 2,
            [Tooltip("The card flips over and shows its new level.")] Reveal = 3
        }

        [Serializable]
        public struct Burst
        {
            [Tooltip("A particle prefab, best a PC copy of a Retro Arsenal effect from Assets/Computer/Fx (layer PC Screen, " +
                     "unscaled time, no sound). Any particle prefab works: it is moved to the PC layer when spawned.")]
            public GameObject prefab;
            public Beat beat;
            [Min(0)] public float delay;
            [Tooltip("From the middle of the card, canvas pixels (y up).")]
            public Vector2 offset;
            [Tooltip("Euler angles. Effects that lie on the ground (novas, rings, collapses) face the screen at x = -90.")]
            public Vector3 rotation;
            [Tooltip("1 = the prefab's own world size; a card is about 1 unit wide (100 canvas pixels per unit). 0 counts as 1.")]
            [Min(0)] public float scale;
            [Tooltip("How much the start colour turns into the card's accent colour (0: the effect's own colours).")]
            [Range(0, 1)] public float accent;
            [Tooltip("Particle playback speed. 0 counts as 1.")]
            [Min(0)] public float speed;
            [Tooltip("Swallowed by the impact: cleared the moment the cards smash (for gathering effects).")]
            public bool clearOnImpact;
            [Tooltip("Seconds before it stops emitting (for looping effects); 0: plays out by itself.")]
            [Min(0)] public float hold;
        }

        [Serializable]
        public struct Cue
        {
            public SfxCue cue;
            public Beat beat;
            [Min(0)] public float delay;
            [Tooltip("Volume scale. 0 counts as 1.")]
            [Range(0, 2)] public float volume;
            [Tooltip("Pitch scale. 0 counts as 1.")]
            [Range(0, 3)] public float pitch;
        }

        [Serializable]
        public sealed class Level
        {
            public string label = "II";
            [Tooltip("The merged card's level (2: «Улучшение I», 3: «Улучшение II»). A level without its own entry uses the highest one below it.")]
            [Min(1)] public int resultLevel = 2;

            [Header("Timing, seconds")]
            [Min(0)] public float flySeconds = .2f;
            [Min(0)] public float grindSeconds = .42f;
            [Tooltip("The frozen moment right after the smash.")]
            [Min(0)] public float holdSeconds = .06f;
            [Tooltip("The flip and the new level; the grid settles at its end.")]
            [Min(0)] public float revealSeconds = .5f;

            [Header("Dash, canvas pixels and degrees")]
            [Tooltip("The dropped card first pulls back this far from the target.")]
            [Min(0)] public float anticipationPixels = 6;
            [Tooltip("Overshoot of the dash (Ease.OutBack).")]
            [Min(0)] public float overshoot = 1.5f;
            [Tooltip("The dash arcs up by this much.")]
            public float arcPixels = 8;
            [Tooltip("When the grinding starts the cards still overlap with this offset; it closes to nothing by the impact.")]
            [Min(0)] public float overlapPixels = 12;
            [Tooltip("Both cards lean towards each other.")]
            [Range(0, 30)] public float tiltDegrees = 4;
            [Tooltip("The dropped card swells by this fraction while it winds up, then slams down.")]
            [Range(0, 1)] public float windUpSwell = .1f;

            [Header("Grind")]
            [Tooltip("Whole-pixel jitter while grinding: from (start) and to (impact).")]
            public Vector2 jitterPixels = new Vector2(1, 2);
            [Tooltip("The accent-coloured frame pulses this many times per second.")]
            [Min(0)] public float glowPulseHz = 10;
            [Tooltip("White flash over both cards by the end of the grind (0..1).")]
            [Range(0, 1)] public float grindFlash = .6f;

            [Header("Impact and reveal")]
            [Tooltip("The card's art is squashed to this scale for the frozen moment.")]
            public Vector2 impactSquash = new Vector2(1.12f, .9f);
            [Tooltip("The Cards window shakes by up to this many pixels.")]
            [Min(0)] public float windowShakePixels = 3;
            [Min(0)] public float windowShakeSeconds = .25f;
            [Tooltip("Spring kick of the art's scale as the flip opens (overshoot).")]
            [Min(0)] public float swell = 4;
            [Tooltip("The new level mark hops up by about this many pixels (pixel-safe: never scaled or rotated).")]
            [Min(0)] public float markHopPixels = 6;
            [Tooltip("Speed of the squash-flip, Hz.")]
            [Min(1)] public float flipFrequency = 12;

            [Header("CRT")]
            [Range(0, 1)] public float grindWobble = .04f;
            [Range(0, 1)] public float grindNoise = .03f;
            [Range(0, 1)] public float impactWobble = .1f;
            [Range(0, 1)] public float impactFlash = .25f;
            [Tooltip("The CRT pulse fades out over about this long after the impact.")]
            [Min(.01f)] public float crtFadeSeconds = .25f;

            [Header("Effects and sound")]
            public Burst[] bursts = new Burst[0];
            public Cue[] cues = new Cue[0];

            public PcMergeTimeline Timeline => new PcMergeTimeline(flySeconds, grindSeconds, holdSeconds, revealSeconds);

            /// <summary>Motion and timing for a result level, no effects or sounds (also the fallback without the asset).</summary>
            public static Level Defaults(int resultLevel)
            {
                if (resultLevel >= 3)
                    return new Level
                    {
                        label = "III", resultLevel = 3,
                        flySeconds = .24f, grindSeconds = .6f, holdSeconds = .08f, revealSeconds = .58f,
                        anticipationPixels = 8, overshoot = 1.8f, arcPixels = 12, overlapPixels = 14, tiltDegrees = 6, windUpSwell = .14f,
                        jitterPixels = new Vector2(1, 3), glowPulseHz = 14, grindFlash = .75f,
                        impactSquash = new Vector2(1.18f, .86f), windowShakePixels = 4, windowShakeSeconds = .32f,
                        swell = 6, markHopPixels = 9, flipFrequency = 11,
                        grindWobble = .07f, grindNoise = .05f, impactWobble = .18f, impactFlash = .4f, crtFadeSeconds = .32f
                    };
                return new Level { label = "II", resultLevel = Mathf.Max(1, resultLevel) };
            }
        }

        [Tooltip("One entry per result level (II, III).")]
        public Level[] levels = { Level.Defaults(2), Level.Defaults(3) };

        /// <summary>The entry for a merge result: the highest level at or below it, else the lowest entry; null when empty.</summary>
        public Level For(int resultLevel) => Pick(levels, resultLevel);

        public static Level Pick(Level[] levels, int resultLevel)
        {
            Level best = null, lowest = null;
            if (levels == null) return null;
            foreach (var level in levels)
            {
                if (level == null) continue;
                if (lowest == null || level.resultLevel < lowest.resultLevel) lowest = level;
                if (level.resultLevel <= resultLevel && (best == null || level.resultLevel > best.resultLevel)) best = level;
            }
            return best ?? lowest;
        }

        /// <summary>Back to the default motion for II and III, without effects or sounds.</summary>
        public void ResetToDefaults() => levels = new[] { Level.Defaults(2), Level.Defaults(3) };
    }
}
