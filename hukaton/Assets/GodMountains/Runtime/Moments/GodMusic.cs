using UnityEngine;
using UnityEngine.Audio;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The music of the climb to the god: two stems of one 128 s loop, started on the same clock and never stopped, only
    /// faded. «Air» comes in slowly from the round pocket on (the stone frame, the white shelf); «might» — brass, drums,
    /// deep choir — joins when he rises in front of the player and carries the reach and the ride. While he speaks both
    /// step back under his voice. In the silence of the throat (S7) the music is silent too.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodMusic : MonoBehaviour
    {
        public GodMoments hub;
        public GodSounds sounds;
        public AudioMixerGroup output;
        public AudioClip air;
        public AudioClip might;

        [Tooltip("Along the route, m: «air» starts here (the round pocket) ...")]
        public float fadeFrom = 90;
        [Tooltip("... and is all there here (on the shelf).")]
        public float fadeTo = 118;
        [Range(0, 1)] public float airVolume = .32f;
        [Range(0, 1)] public float mightVolume = .38f;
        [Tooltip("Seconds for «might» to come in when he rises.")]
        [Min(.1f)] public float mightIn = 5;
        [Tooltip("Under his voice the music is this loud.")]
        [Range(0, 1)] public float underVoice = .45f;

        AudioSource airSource, mightSource;
        bool started;
        float duck = 1;

        void Start()
        {
            airSource = Source("Музыка — воздух", air);
            mightSource = Source("Музыка — сила", might);
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
                if (m < fadeFrom) return;
                started = true;
                double at = AudioSettings.dspTime + .1;
                if (airSource) airSource.PlayScheduled(at);
                if (mightSource) mightSource.PlayScheduled(at);
            }
            var stage = hub.encounter ? hub.encounter.Stage : GodStage.Path;
            bool risen = stage != GodStage.Path || (hub.encounter && hub.encounter.Noticed);
            duck = Mathf.MoveTowards(duck, sounds && sounds.Speaking ? underVoice : 1, Time.deltaTime * .8f);

            float a = airVolume * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(fadeFrom, fadeTo, m)) * duck;
            float b = risen ? mightVolume * duck : 0;
            // the silence in the throat (S7) is silence: the music drops out with the breath and comes back after
            bool hush = hub.Silent;
            if (hush) a = b = 0;
            if (airSource) airSource.volume = Mathf.MoveTowards(airSource.volume, a, Time.deltaTime * (hush ? 4 : airVolume / 3));
            if (mightSource) mightSource.volume = Mathf.MoveTowards(mightSource.volume, b, Time.deltaTime * (hush ? 4 : mightVolume / mightIn));
        }
    }
}
