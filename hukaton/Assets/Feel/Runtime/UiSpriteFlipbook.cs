using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Feel
{
    /// <summary>
    /// Loops a UI Image through drawn frames at a fixed rate (e.g. a spinning coin). The phase follows the clock, not
    /// when the image appeared, so every flipbook with the same period turns in step (coin, hearts, effect rings).
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class UiSpriteFlipbook : MonoBehaviour
    {
        public Sprite[] frames = new Sprite[0];
        [Min(.01f)] public float frameSeconds = .16f;
        [Tooltip("Keeps spinning while the game is paused or slowed down.")]
        public bool unscaledTime = true;
        Image image;

        void OnEnable() => image = GetComponent<Image>();

        float Now => unscaledTime ? Time.unscaledTime : Time.time;

        void Update()
        {
            if (!image || frames == null || frames.Length == 0) return;
            int frame = (int)(Now / frameSeconds) % frames.Length;
            if (image.sprite != frames[frame]) image.sprite = frames[frame];
        }
    }
}
