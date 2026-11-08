using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// Punch for pixel-font UI text, which must never scale or rotate: a springy hop and a shake,
    /// both snapped to whole canvas pixels around the authored rest position.
    /// </summary>
    public sealed class TextPunch
    {
        readonly RectTransform target;
        readonly Vector2 rest;
        SpringFloat hop;
        float shakeUntil, shakePixels;

        public TextPunch(RectTransform target)
        {
            this.target = target;
            rest = target ? target.anchoredPosition : Vector2.zero;
        }

        /// <summary>Throws the text up by roughly this many pixels; it settles back with a small bounce.</summary>
        public void Hop(float pixels) => hop.Kick(pixels * 45f);

        /// <summary>Rattles the text sideways, fading out over the given time.</summary>
        public void Shake(float seconds, float pixels)
        {
            shakeUntil = Time.unscaledTime + seconds;
            shakePixels = pixels;
        }

        public void Update(float deltaTime)
        {
            if (!target) return;
            float y = hop.Step(0, 5, .45f, deltaTime);
            float x = 0;
            float left = shakeUntil - Time.unscaledTime;
            if (left > 0) x = Mathf.Sin(Time.unscaledTime * 70f) * shakePixels * Mathf.Clamp01(left * 4);
            target.anchoredPosition = rest + new Vector2(Mathf.Round(x), Mathf.Round(y));
        }
    }
}
