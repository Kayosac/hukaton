using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Hakaton.Feel
{
    /// <summary>
    /// The mix group a sound plays in. <see cref="General"/> (UI, player, cards, shop) plays exactly as asked.
    /// The other buses are managed: a voice budget, a per-cue limit, one voice per owner, audibility culling,
    /// background sounds rationed and ducked under important ones - so a crowd of creatures never turns into a din.
    /// </summary>
    public enum SfxBus { General, Creature, CreatureStep, CreatureLoop }

    /// <summary>Inspector-tuned limits of one managed bus.</summary>
    [Serializable]
    public sealed class SfxBusRules
    {
        public SfxBus bus;
        [Tooltip("Voices of this bus sounding at once; a new sound only takes a slot from a weaker one.")]
        [Min(1)] public int maxVoices = 6;
        [Tooltip("Voices of one and the same cue at once (a herd of one kind is heard as two, not twenty).")]
        [Min(1)] public int maxPerCue = 2;
        [Tooltip("An owner (a creature) never talks over itself: a new sound replaces its own only if it matters as much.")]
        public bool oneVoicePerOwner = true;
        [Tooltip("A sound quieter than this at the listener is not played at all.")]
        [Range(0, .3f)] public float minAudibleGain = .05f;
        [Tooltip("Sounds below this priority are background chatter (calm creatures).")]
        [Range(0, 1)] public float backgroundPriority = .4f;
        [Tooltip("Background sounds of the whole bus start no more often than this, seconds.")]
        [Min(0)] public float backgroundInterval = .7f;
        [Tooltip("Reach of a background sound as a share of its cue's max distance.")]
        [Range(.1f, 1)] public float backgroundReach = .6f;
        [Tooltip("Sounds at or above this priority are important (aimed at the player).")]
        [Range(0, 1)] public float importantPriority = .75f;
        [Tooltip("Background voices of the bus drop to this volume while an important one sounds.")]
        [Range(0, 1)] public float backgroundDuck = .45f;
        [Tooltip("The bus gets quieter by this share for every extra voice sounding at once.")]
        [Range(0, .3f)] public float crowdDuck = .07f;
    }

    /// <summary>
    /// Pooled one-shot and looping playback for <see cref="SfxCue"/> assets, with a small mixer on top
    /// (see <see cref="SfxBus"/>). Sources are authored children of this object; nothing is created at runtime.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        [Tooltip("Authored pool of sources (children). Oldest is stolen when all are busy.")]
        public AudioSource[] pool;
        [Range(0, 1)] public float masterVolume = 1;
        [Tooltip("Where the ears are (the gameplay camera). Set by the game layer that owns the camera; without it managed buses skip distance culling.")]
        public Transform listener;
        [Tooltip("Fade used when a managed voice is replaced or a loop goes quiet, seconds.")]
        [Min(.01f)] public float fadeSeconds = .08f;
        public SfxBusRules[] buses =
        {
            new SfxBusRules { bus = SfxBus.Creature, maxVoices = 6, maxPerCue = 2, oneVoicePerOwner = true },
            new SfxBusRules { bus = SfxBus.CreatureStep, maxVoices = 2, maxPerCue = 1, oneVoicePerOwner = true, backgroundInterval = .25f, backgroundDuck = .3f, crowdDuck = 0 },
            new SfxBusRules { bus = SfxBus.CreatureLoop, maxVoices = 3, maxPerCue = 2, oneVoicePerOwner = true, backgroundInterval = 0, backgroundReach = .7f, backgroundDuck = .5f, crowdDuck = .1f },
        };

        sealed class Voice
        {
            public SfxBus bus;
            public Object owner;
            public SfxCue cue;
            public float priority, baseVolume, spatialBlend, fade = 1, fadeTarget = 1;
            public bool loop;
            public LoopRequest request;
            public bool Managed => bus != SfxBus.General;
        }

        sealed class LoopRequest
        {
            public Object owner;
            public SfxCue cue;
            public SfxBus bus;
            public Vector3 position;
            public float volume, priority, pitch, seen, score;
            public int voice = -1;
        }

        sealed class BusState
        {
            public float lastBackground = -999, gain = 1, targetGain = 1;
            public bool important;
        }

        int next;
        Voice[] voices;
        readonly Dictionary<Object, int> loops = new Dictionary<Object, int>();
        readonly Dictionary<Object, LoopRequest> requests = new Dictionary<Object, LoopRequest>();
        readonly List<LoopRequest> ranked = new List<LoopRequest>();
        readonly List<Object> stale = new List<Object>();
        readonly BusState[] state = { new BusState(), new BusState(), new BusState(), new BusState() };
        static readonly Comparison<LoopRequest> ByScore = (a, b) => b.score.CompareTo(a.score);

        Voice[] Voices
        {
            get
            {
                if (voices == null || voices.Length != pool.Length)
                {
                    voices = new Voice[pool.Length];
                    for (int i = 0; i < voices.Length; i++) voices[i] = new Voice();
                }
                return voices;
            }
        }

        // ---------- General (unmanaged) ----------

        public AudioSource Play(SfxCue cue, Vector3? position = null, float volumeScale = 1, float pitchScale = 1)
        {
            if (!cue || pool == null || pool.Length == 0) return null;
            float now = Time.unscaledTime;
            if (now - cue.lastPlayed < cue.cooldown) return null;
            var clip = cue.Pick();
            if (!clip) return null;
            cue.lastPlayed = now;
            int index = NextFree(false, float.MaxValue);
            if (index < 0) return null;
            var source = pool[index];
            Claim(index, SfxBus.General, null, cue, .5f, cue.volume * volumeScale * masterVolume, false);
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
            if (!owner || !cue || pool == null || pool.Length == 0) return;
            if (!loops.TryGetValue(owner, out int index) || !pool[index])
            {
                var clip = cue.Pick();
                if (!clip) return;
                index = NextFree(false, float.MaxValue);
                if (index < 0) return;
                loops[owner] = index;
                var fresh = pool[index];
                Claim(index, SfxBus.General, owner, cue, 1, 0, true);
                fresh.clip = clip;
                fresh.loop = true;
                fresh.pitch = Random.Range(cue.pitch.x, cue.pitch.y);
                fresh.spatialBlend = cue.spatialBlend;
                fresh.minDistance = cue.minDistance;
                fresh.maxDistance = cue.maxDistance;
                fresh.Play();
            }
            var source = pool[index];
            source.transform.position = position;
            source.volume = cue.volume * volumeScale * masterVolume;
        }

        // ---------- managed buses ----------

        /// <summary>
        /// A one-shot in a managed bus. <paramref name="priority"/> 0..1 says how much it matters to the player
        /// (an attack on him ~1, a calm creature's grunt ~.2); it decides who gets a voice when the bus is full.
        /// </summary>
        public AudioSource Play(SfxCue cue, Vector3 position, SfxBus bus, Object owner, float priority, float volumeScale = 1, float pitchScale = 1)
        {
            if (bus == SfxBus.General) return Play(cue, position, volumeScale, pitchScale);
            if (!cue || pool == null || pool.Length == 0) return null;
            var rules = Rules(bus);
            if (rules == null) return Play(cue, position, volumeScale, pitchScale);
            var bs = state[(int)bus];
            float now = Time.unscaledTime;
            if (now - cue.lastPlayed < cue.cooldown) return null;
            bool background = priority < rules.backgroundPriority;
            if (background && now - bs.lastBackground < rules.backgroundInterval) return null;
            float reach = Mathf.Max(cue.minDistance + .1f, cue.maxDistance * (background ? rules.backgroundReach : 1));
            float volume = cue.volume * volumeScale * masterVolume;
            float gain = Audibility(cue.spatialBlend, cue.minDistance, reach, position, volume);
            if (gain < rules.minAudibleGain) return null;
            float score = priority * gain;

            var all = Voices;
            int replace = -1;
            if (rules.oneVoicePerOwner && owner)
                for (int i = 0; i < all.Length; i++)
                    if (Sounding(i) && !all[i].loop && all[i].bus == bus && all[i].owner == owner)
                    {
                        if (priority < all[i].priority) return null;
                        replace = i;
                        break;
                    }
            if (replace < 0 && Count(bus, cue) >= rules.maxPerCue)
            {
                replace = Weakest(bus, cue);
                if (replace < 0 || score <= Score(replace)) return null;
            }
            if (replace < 0 && Count(bus, null) >= rules.maxVoices)
            {
                replace = Weakest(bus, null);
                if (replace < 0 || score <= Score(replace)) return null;
            }
            if (replace >= 0) FadeOut(replace);

            var clip = cue.Pick();
            if (!clip) return null;
            int index = NextFree(true, score);
            if (index < 0) return null;
            cue.lastPlayed = now;
            if (background) bs.lastBackground = now;
            var source = pool[index];
            Claim(index, bus, owner, cue, priority, volume, false);
            source.transform.position = position;
            source.spatialBlend = listener ? EdgeBlend(cue.spatialBlend, Vector3.Distance(listener.position, position), reach) : cue.spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = cue.minDistance;
            source.maxDistance = reach;
            source.pitch = Random.Range(cue.pitch.x, cue.pitch.y) * pitchScale;
            source.loop = false;
            source.clip = clip;
            source.volume = volume * MixGain(bus, priority, rules);
            source.Play();
            return source;
        }

        /// <summary>
        /// Asks for a loop in a managed bus; call every frame while it should sound. Only the few that matter most
        /// at the listener really play, the rest stay silent until they earn a voice.
        /// </summary>
        public void Loop(Object owner, SfxCue cue, Vector3 position, float volumeScale, SfxBus bus, float priority)
        {
            if (bus == SfxBus.General || Rules(bus) == null) { Loop(owner, cue, position, volumeScale); return; }
            if (!owner || !cue) return;
            if (!requests.TryGetValue(owner, out var request))
            {
                request = new LoopRequest { owner = owner, pitch = Random.Range(cue.pitch.x, cue.pitch.y) };
                requests[owner] = request;
            }
            request.cue = cue;
            request.bus = bus;
            request.position = position;
            request.volume = cue.volume * volumeScale * masterVolume;
            request.priority = priority;
            request.seen = Time.unscaledTime;
            if (request.voice >= 0)
            {
                pool[request.voice].transform.position = position;
                Voices[request.voice].baseVolume = request.volume;
                Voices[request.voice].priority = priority;
            }
        }

        public void StopLoop(Object owner)
        {
            if (!owner) return;
            if (loops.TryGetValue(owner, out int index))
            {
                var source = pool[index];
                if (source) { source.Stop(); source.loop = false; }
                Voices[index].loop = false;
                Voices[index].owner = null;
                loops.Remove(owner);
            }
            if (requests.TryGetValue(owner, out var request))
            {
                if (request.voice >= 0) FadeOut(request.voice);
                requests.Remove(owner);
            }
        }

        public void StopAll()
        {
            if (pool != null) foreach (var source in pool) if (source) { source.Stop(); source.loop = false; }
            loops.Clear();
            requests.Clear();
            if (pool != null) for (int i = 0; i < Voices.Length; i++) Claim(i, SfxBus.General, null, null, 0, 0, false);
        }

        // ---------- mixing ----------

        void Update()
        {
            if (pool == null || pool.Length == 0) return;
            float dt = Time.unscaledDeltaTime;
            ArrangeLoops();
            var all = Voices;
            for (int b = 1; b < state.Length; b++)
            {
                var rules = Rules((SfxBus)b);
                if (rules == null) continue;
                int count = 0;
                bool important = false;
                for (int i = 0; i < all.Length; i++)
                    if (all[i].bus == (SfxBus)b && Sounding(i))
                    {
                        count++;
                        if (all[i].priority >= rules.importantPriority) important = true;
                    }
                state[b].important = important;
                state[b].targetGain = 1 / (1 + rules.crowdDuck * Mathf.Max(0, count - 1));
                state[b].gain = Mathf.MoveTowards(state[b].gain, state[b].targetGain, dt * 2);
            }
            for (int i = 0; i < all.Length; i++)
            {
                var voice = all[i];
                if (!voice.Managed) continue;
                var source = pool[i];
                if (!source) continue;
                voice.fade = Mathf.MoveTowards(voice.fade, voice.fadeTarget, dt / fadeSeconds);
                if (voice.fadeTarget <= 0 && voice.fade <= 0)
                {
                    source.Stop();
                    source.loop = false;
                    Claim(i, SfxBus.General, null, null, 0, 0, false);
                    continue;
                }
                if (!source.isPlaying) continue;
                if (listener && voice.spatialBlend > 0)
                    source.spatialBlend = EdgeBlend(voice.spatialBlend, Vector3.Distance(listener.position, source.transform.position), source.maxDistance);
                var rules = Rules(voice.bus);
                source.volume = voice.baseVolume * voice.fade * (rules != null ? MixGain(voice.bus, voice.priority, rules) : 1);
            }
        }

        /// <summary>The loops of managed buses that matter most get a voice; the others fade out.</summary>
        void ArrangeLoops()
        {
            float now = Time.unscaledTime;
            stale.Clear();
            foreach (var pair in requests)
                if (!pair.Key || now - pair.Value.seen > .3f) stale.Add(pair.Key);
            foreach (var owner in stale)
            {
                var request = requests[owner];
                if (request.voice >= 0) FadeOut(request.voice);
                requests.Remove(owner);
            }
            if (requests.Count == 0) return;
            for (int b = 1; b < state.Length; b++)
            {
                var bus = (SfxBus)b;
                var rules = Rules(bus);
                if (rules == null) continue;
                ranked.Clear();
                foreach (var request in requests.Values)
                {
                    if (request.bus != bus) continue;
                    bool background = request.priority < rules.backgroundPriority;
                    float reach = Mathf.Max(request.cue.minDistance + .1f, request.cue.maxDistance * (background ? rules.backgroundReach : 1));
                    float gain = Audibility(request.cue.spatialBlend, request.cue.minDistance, reach, request.position, request.volume);
                    // A loop already sounding keeps its voice unless clearly outranked (no flapping).
                    request.score = gain < rules.minAudibleGain ? 0 : request.priority * gain * (request.voice >= 0 ? 1.25f : 1);
                    ranked.Add(request);
                }
                ranked.Sort(ByScore);
                int granted = 0;
                for (int r = 0; r < ranked.Count; r++)
                {
                    var request = ranked[r];
                    bool deserves = request.score > 0 && granted < rules.maxVoices && CountGranted(r, request.cue) < rules.maxPerCue;
                    if (deserves)
                    {
                        granted++;
                        if (request.voice < 0) Grant(request, rules);
                    }
                    else if (request.voice >= 0)
                    {
                        FadeOut(request.voice);
                        request.voice = -1;
                    }
                }
            }
        }

        int CountGranted(int upTo, SfxCue cue)
        {
            int count = 0;
            for (int i = 0; i < upTo; i++) if (ranked[i].cue == cue && ranked[i].voice >= 0) count++;
            return count;
        }

        void Grant(LoopRequest request, SfxBusRules rules)
        {
            var clip = request.cue.Pick();
            if (!clip) return;
            int index = NextFree(true, request.score);
            if (index < 0) return;
            var source = pool[index];
            Claim(index, request.bus, request.owner, request.cue, request.priority, request.volume, true);
            var voice = Voices[index];
            voice.request = request;
            voice.fade = 0;
            request.voice = index;
            bool background = request.priority < rules.backgroundPriority;
            source.clip = clip;
            source.loop = true;
            source.pitch = request.pitch;
            source.spatialBlend = request.cue.spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = request.cue.minDistance;
            source.maxDistance = Mathf.Max(request.cue.minDistance + .1f, request.cue.maxDistance * (background ? rules.backgroundReach : 1));
            source.transform.position = request.position;
            source.volume = 0;
            // Starts somewhere inside the loop: two swarms side by side never buzz in unison.
            source.time = Random.Range(0, clip.length * .9f);
            source.Play();
        }

        float MixGain(SfxBus bus, float priority, SfxBusRules rules)
        {
            var bs = state[(int)bus];
            float gain = bs.gain;
            if (bs.important && priority < rules.backgroundPriority) gain *= rules.backgroundDuck;
            return gain;
        }

        /// <summary>Volume of a sound at the listener with the pool's linear rolloff.</summary>
        /// <remarks>
        /// A managed voice turns fully positional toward the edge of its reach (<see cref="EdgeBlend"/>): the flat
        /// part of a cue's blend would otherwise carry every far creature into the mix at a constant level.
        /// </remarks>
        float Audibility(float spatialBlend, float minDistance, float reach, Vector3 at, float volume)
        {
            if (!listener || spatialBlend <= 0) return volume;
            float distance = Vector3.Distance(listener.position, at);
            if (distance >= reach) return 0;
            float near = distance <= minDistance ? 1 : 1 - (distance - minDistance) / Mathf.Max(.01f, reach - minDistance);
            return volume * Mathf.Lerp(1, near, EdgeBlend(spatialBlend, distance, reach));
        }

        static float EdgeBlend(float spatialBlend, float distance, float reach)
            => spatialBlend <= 0 ? 0 : Mathf.Lerp(spatialBlend, 1, Mathf.Clamp01(distance / Mathf.Max(.01f, reach)));

        float Score(int index)
        {
            var voice = Voices[index];
            var source = pool[index];
            float gain = Audibility(voice.spatialBlend, source.minDistance, source.maxDistance, source.transform.position, voice.baseVolume);
            // A voice near its end is cheap to cut.
            float left = source.clip && source.clip.length > 0 && !source.loop ? 1 - Mathf.Clamp01(source.time / source.clip.length) * .6f : 1;
            return voice.priority * gain * left;
        }

        int Count(SfxBus bus, SfxCue cue)
        {
            var all = Voices;
            int count = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i].bus == bus && !all[i].loop && (cue == null || all[i].cue == cue) && Sounding(i)) count++;
            return count;
        }

        int Weakest(SfxBus bus, SfxCue cue)
        {
            var all = Voices;
            int weakest = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].bus != bus || all[i].loop || (cue != null && all[i].cue != cue) || !Sounding(i)) continue;
                float score = Score(i);
                if (score < lowest) { lowest = score; weakest = i; }
            }
            return weakest;
        }

        /// <summary>Playing and not on its way out.</summary>
        bool Sounding(int index) => pool[index] && pool[index].isPlaying && Voices[index].fadeTarget > 0;

        void FadeOut(int index)
        {
            var voice = Voices[index];
            if (voice.request != null) { voice.request.voice = -1; voice.request = null; }
            if (!voice.Managed) { if (pool[index]) pool[index].Stop(); return; }
            voice.fadeTarget = 0;
        }

        void Claim(int index, SfxBus bus, Object owner, SfxCue cue, float priority, float baseVolume, bool loop)
        {
            var voice = Voices[index];
            if (voice.request != null && voice.request.voice == index) voice.request.voice = -1;
            if (voice.loop && !voice.Managed && voice.owner) loops.Remove(voice.owner);
            voice.bus = bus;
            voice.owner = owner;
            voice.cue = cue;
            voice.priority = priority;
            voice.baseVolume = baseVolume;
            voice.spatialBlend = cue ? cue.spatialBlend : 0;
            voice.loop = loop;
            voice.request = null;
            voice.fade = 1;
            voice.fadeTarget = 1;
        }

        SfxBusRules Rules(SfxBus bus)
        {
            if (buses == null) return null;
            foreach (var rules in buses) if (rules != null && rules.bus == bus) return rules;
            return null;
        }

        /// <summary>
        /// A free source, or one to steal. A managed sound steals only a weaker managed one-shot (never UI or a loop);
        /// a general sound steals the weakest managed one-shot first, then the oldest general one.
        /// </summary>
        int NextFree(bool managed, float score)
        {
            var all = Voices;
            for (int i = 0; i < pool.Length; i++)
            {
                int candidate = (next + i) % pool.Length;
                if (pool[candidate] && !pool[candidate].isPlaying && !all[candidate].loop) { next = (candidate + 1) % pool.Length; return candidate; }
            }
            int weakest = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i] || all[i].loop || !all[i].Managed) continue;
                float s = all[i].fadeTarget <= 0 ? -1 : Score(i);
                if (s < lowest) { lowest = s; weakest = i; }
            }
            if (weakest >= 0 && (!managed || lowest < score)) { pool[weakest].Stop(); return weakest; }
            if (managed) return -1;
            for (int i = 0; i < pool.Length; i++)
            {
                int candidate = (next + i) % pool.Length;
                if (pool[candidate] && !all[candidate].loop) { next = (candidate + 1) % pool.Length; pool[candidate].Stop(); return candidate; }
            }
            return -1;
        }
    }
}
