using UnityEngine;
using UnityEngine.Audio;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The music of the climb to the god: two stems of one 128 s loop in D minor, started together on the same clock and
    /// only ever faded. «Dread» — a low drone, dark closed pads, a lonely falling cello, a far bell — creeps in early in
    /// the corners and is there in full by the round pocket: hopeless, close, no tune to hold on to. When the god comes
    /// into sight «glory» rises over it on the same chords — strings, choir, harp, brass, timpani, the great drum — and
    /// carries the reach, the ride and the talk, the dread sinking under it. The silence in the throat (S7) is silence
    /// for the music too; under his voice both step back.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodMusic : MonoBehaviour
    {
        public GodMoments hub;
        public GodSounds sounds;
        public AudioMixerGroup output;
        public AudioClip dread;
        public AudioClip glory;

        [Header("Dread")]
        [Tooltip("Along the route, m: it starts creeping in here (the corners) ...")]
        public float dreadFrom = 38;
        [Tooltip("... and is all there here (the round pocket).")]
        public float dreadFull = 95;
        [Range(0, 1)] public float dreadVolume = .4f;
        [Tooltip("Under the glory the dread keeps this share.")]
        [Range(0, 1)] public float dreadUnderGlory = .45f;

        [Header("Glory")]
        [Tooltip("Along the route, m: past here he is in sight (the light mouth ahead).")]
        public float gloryFrom = 168;
        [Range(0, 1)] public float gloryVolume = .45f;
        [Tooltip("Seconds for the glory to come in full.")]
        [Min(.1f)] public float gloryIn = 9;

        [Header("Voice")]
        [Range(0, 1)] public float underVoice = .45f;

        AudioSource dreadSource, glorySource;
        bool started;
        float duck = 1, rise;

        void Start()
        {
            dreadSource = Source("Музыка — безысходность", dread);
            glorySource = Source("Музыка — величие", glory);
        }

        AudioSource Source(string label, AudioClip clip)
        {
            if (!clip) return null;
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0;
            s.volume = 0;
            s.outputAudioMixerGroup = output;
            return s;
        }

        void Update()
        {
            if (!hub || !hub.Running) return;
            float m = hub.Progress;
            if (!started)
            {
                if (m < dreadFrom) return;
                started = true;
                double at = AudioSettings.dspTime + .1;
                if (dreadSource) dreadSource.PlayScheduled(at);
                if (glorySource) glorySource.PlayScheduled(at);
            }
            var stage = hub.encounter ? hub.encounter.Stage : GodStage.Path;
            bool inSight = m >= gloryFrom || stage != GodStage.Path || (hub.encounter && hub.encounter.Noticed);
            bool hush = hub.Silent;
            if (inSight && !hush) rise = Mathf.MoveTowards(rise, 1, Time.deltaTime / gloryIn);
            duck = Mathf.MoveTowards(duck, sounds && sounds.Speaking ? underVoice : 1, Time.deltaTime * .8f);

            float d = dreadVolume * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(dreadFrom, dreadFull, m))
                      * Mathf.Lerp(1, dreadUnderGlory, rise) * duck;
            float g = gloryVolume * Mathf.SmoothStep(0, 1, rise) * duck;
            if (hush) d = g = 0;
            float rate = hush ? 4 : .12f;
            if (dreadSource) dreadSource.volume = Mathf.MoveTowards(dreadSource.volume, d, Time.deltaTime * rate);
            if (glorySource) glorySource.volume = Mathf.MoveTowards(glorySource.volume, g, Time.deltaTime * (hush ? 4 : 1));
        }
    }
}
