using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>A named sound with variants, randomisation and anti-spam limits. Tuned in the Inspector.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Feel/Sound cue")]
    public sealed class SfxCue : ScriptableObject
    {
        public AudioClip[] clips;
        [Range(0, 1)] public float volume = .8f;
        public Vector2 pitch = new Vector2(.95f, 1.05f);
        [Tooltip("0 = flat 2D UI sound, 1 = fully positional.")]
        [Range(0, 1)] public float spatialBlend = .85f;
        [Min(.1f)] public float minDistance = 2, maxDistance = 40;
        [Tooltip("Ignore repeats faster than this, seconds.")]
        [Min(0)] public float cooldown = .03f;
        [Tooltip("Never pick the same variant twice in a row.")]
        public bool avoidRepeat = true;

        [System.NonSerialized] public float lastPlayed = -999;
        [System.NonSerialized] public int lastIndex = -1;

        public AudioClip Pick()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            int index = Random.Range(0, clips.Length);
            if (avoidRepeat && index == lastIndex) index = (index + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
            lastIndex = index;
            return clips[index];
        }
    }
}
