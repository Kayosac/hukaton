using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The cave's sound: while the cave scene is loaded, every sound the player hears gets a clear echo and a long
    /// stony tail. Both are filters put on the player's AudioListener when the cave loads and taken off when it
    /// unloads, so the shop and the valley sound as before. Tune the values here, in the cave scene.
    /// Audio filters do not run in WebGL builds: there the cave sounds dry.
    /// </summary>
    public sealed class CaveAcoustics : MonoBehaviour
    {
        [Header("Echo: distinct repeats off the far walls")]
        [Tooltip("Time between repeats, ms.")]
        [Range(10, 1000)] public float echoDelay = 330;
        [Tooltip("Each repeat this much quieter than the one before (0 = a single repeat).")]
        [Range(0, 1)] public float echoDecay = .45f;
        [Range(0, 1)] public float echoWet = .5f;
        [Range(0, 1)] public float echoDry = 1;

        [Header("Reverb: the stone around")]
        public AudioReverbPreset reverb = AudioReverbPreset.Cave;

        AudioEchoFilter echo;
        AudioReverbFilter tail;

        public bool Applied => echo || tail;

        void OnEnable()
        {
            var listener = Object.FindAnyObjectByType<AudioListener>();
            if (!listener) return;
            var go = listener.gameObject;
            echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = echoDelay;
            echo.decayRatio = echoDecay;
            echo.wetMix = echoWet;
            echo.dryMix = echoDry;
            tail = go.AddComponent<AudioReverbFilter>();
            tail.reverbPreset = reverb;
        }

        void OnDisable()
        {
            if (echo) Destroy(echo);
            if (tail) Destroy(tail);
            echo = null;
            tail = null;
        }
    }
}
