using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// Pooled one-shot and looping playback for <see cref="SfxCue"/> assets.
    /// Sources are authored children of this object; nothing is created at runtime.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        [Tooltip("Authored pool of one-shot sources (children). Oldest is stolen when all are busy.")]
        public AudioSource[] pool;
        [Range(0, 1)] public float masterVolume = 1;
        int next;
        readonly Dictionary<Object, AudioSource> loops = new Dictionary<Object, AudioSource>();

        public AudioSource Play(SfxCue cue, Vector3? position = null, float volumeScale = 1, float pitchScale = 1)
        {
            if (!cue || pool == null || pool.Length == 0) return null;
            float now = Time.unscaledTime;
            if (now - cue.lastPlayed < cue.cooldown) return null;
            var clip = cue.Pick();
            if (!clip) return null;
            cue.lastPlayed = now;
            var source = NextFree();
            source.transform.position = position ?? transform.position;
            source.spatialBlend = position.HasValue ? cue.spatialBlend : 0;
            source.minDistance = cue.minDistance;
            source.maxDistance = cue.maxDistance;
            source.pitch = Random.Range(cue.pitch.x, cue.pitch.y) * pitchScale;
            source.volume = cue.volume * volumeScale * masterVolume;
            source.loop = false;
            source.clip = clip;
            source.Play();
            return source;
        }

        /// <summary>Starts (or keeps) a loop owned by <paramref name="owner"/>; volume can be updated every frame.</summary>
        public void Loop(Object owner, SfxCue cue, Vector3 position, float volumeScale = 1)
        {
            if (!owner || !cue) return;
            if (!loops.TryGetValue(owner, out var source) || !source)
            {
                var clip = cue.Pick();
                if (!clip) return;
                source = NextFree();
                loops[owner] = source;
                source.clip = clip;
                source.loop = true;
                source.pitch = Random.Range(cue.pitch.x, cue.pitch.y);
                source.spatialBlend = cue.spatialBlend;
                source.minDistance = cue.minDistance;
                source.maxDistance = cue.maxDistance;
                source.Play();
            }
            source.transform.position = position;
            source.volume = cue.volume * volumeScale * masterVolume;
        }

        public void StopLoop(Object owner)
        {
            if (owner && loops.TryGetValue(owner, out var source))
            {
                if (source) { source.Stop(); source.loop = false; }
                loops.Remove(owner);
            }
        }

        public void StopAll()
        {
            foreach (var source in pool) if (source) { source.Stop(); source.loop = false; }
            loops.Clear();
        }

        AudioSource NextFree()
        {
            for (int i = 0; i < pool.Length; i++)
            {
                var candidate = pool[(next + i) % pool.Length];
                if (!candidate.isPlaying && !loops.ContainsValue(candidate)) { next = (next + i + 1) % pool.Length; return candidate; }
            }
            // All busy: steal the next non-loop source in round-robin order.
            for (int i = 0; i < pool.Length; i++)
            {
                var candidate = pool[(next + i) % pool.Length];
                if (!loops.ContainsValue(candidate)) { next = (next + i + 1) % pool.Length; return candidate; }
            }
            return pool[0];
        }
    }
}
