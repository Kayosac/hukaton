using UnityEngine;
using UnityEngine.Audio;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// The god's body and voice. Every clip that moves him (Notice, Reach, Lift) starts its own sound the moment the
    /// Animator goes into it: the sounds are timed to the keys of the clips (output/mountain-god-anim-v1/clips.py) and
    /// made at his scale — sub drops, rock grinding, groaning joints, air pushed by an arm the size of a ridge. They are
    /// not placed in space: he fills the whole view, so he fills the whole ear. His lines are voiced one by one as the
    /// plate shows them; the next line cuts the one before short, the last one is let finish.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodSounds : MonoBehaviour
    {
        public GodEncounter encounter;
        public AudioMixerGroup output;

        [Header("Body (by Animator state)")]
        public AudioClip notice;
        public AudioClip reach;
        public AudioClip lift;
        [Range(0, 1)] public float bodyVolume = 1;

        [Header("Voice")]
        [Tooltip("One clip per line of the encounter, in order.")]
        public AudioClip[] lines = new AudioClip[0];
        [Range(0, 1)] public float voiceVolume = 1;

        /// <summary>He is saying a line right now.</summary>
        public bool Speaking => voice && voice.isPlaying;

        static readonly int NoticeState = Animator.StringToHash("Notice");
        static readonly int ReachState = Animator.StringToHash("Reach");
        static readonly int LiftState = Animator.StringToHash("Lift");

        AudioSource voice;
        int lastState;

        void Start()
        {
            voice = Source("Голос бога");
            if (encounter) encounter.LineShown += Say;
        }

        void OnDestroy()
        {
            if (encounter) encounter.LineShown -= Say;
        }

        AudioSource Source(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0;
            s.outputAudioMixerGroup = output;
            return s;
        }

        void Update()
        {
            var god = encounter ? encounter.god : null;
            if (!god || !god.isActiveAndEnabled) return;
            // the clip starts with the crossfade into it, not when the crossfade is over
            int state = god.IsInTransition(0) ? god.GetNextAnimatorStateInfo(0).shortNameHash
                                              : god.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (state == lastState) return;
            lastState = state;
            if (state == NoticeState) Body(notice, "Бог встаёт");
            else if (state == ReachState) Body(reach, "Бог тянется");
            else if (state == LiftState) Body(lift, "Бог поднимает");
        }

        void Body(AudioClip clip, string label)
        {
            if (!clip) return;
            var s = Source(label);
            s.clip = clip;
            s.volume = bodyVolume;
            s.Play();
            Destroy(s.gameObject, clip.length + .5f);
        }

        void Say(int line)
        {
            if (!voice) return;
            voice.Stop();
            if (line < 0 || line >= lines.Length || !lines[line]) return;
            voice.clip = lines[line];
            voice.volume = voiceVolume;
            voice.Play();
        }
    }
}
