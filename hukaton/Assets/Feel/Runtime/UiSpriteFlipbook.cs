using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Feel
{
    /// <summary>Loops a UI Image through drawn frames at a fixed rate (e.g. a spinning coin).</summary>
    [RequireComponent(typeof(Image))]
    public sealed class UiSpriteFlipbook : MonoBehaviour
    {
        public Sprite[] frames = new Sprite[0];
        [Min(.01f)] public float frameSeconds = .16f;
        [Tooltip("Keeps spinning while the game is paused or slowed down.")]
        public bool unscaledTime = true;
        Image image;
        float startedAt;

        void OnEnable()
        {
            image = GetComponent<Image>();
            startedAt = Now;
        }

        float Now => unscaledTime ? Time.unscaledTime : Time.time;

        void Update()
        {
            if (!image || frames == null || frames.Length == 0) return;
            int frame = (int)((Now - startedAt) / frameSeconds) % frames.Length;
            if (image.sprite != frames[frame]) image.sprite = frames[frame];
        }
    }
}
