using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Feel
{
    /// <summary>Full-screen fade with an optional title card. Uses unscaled time so hit-stop never stalls it.</summary>
    public sealed class ScreenFade : MonoBehaviour
    {
        public Canvas canvas;
        public Image cover;
        public CanvasGroup titleGroup;
        public TMP_Text title, subtitle;
        public Color colour = new Color(.035f, .04f, .045f, 1);

        public float Amount { get; private set; }
        public bool Busy { get; private set; }

        void Awake() => Set(0);

        public void Set(float amount)
        {
            Amount = Mathf.Clamp01(amount);
            cover.color = new Color(colour.r, colour.g, colour.b, Amount);
            cover.raycastTarget = Amount > .01f;
            canvas.enabled = Amount > .001f || (titleGroup && titleGroup.alpha > .001f);
        }

        public IEnumerator FadeTo(float target, float seconds)
        {
            Busy = true;
            float start = Amount, time = 0;
            while (time < seconds)
            {
                time += Time.unscaledDeltaTime;
                Set(Mathf.Lerp(start, target, Ease.InOutSine(time / Mathf.Max(.001f, seconds))));
                yield return null;
            }
            Set(target);
            Busy = false;
        }

        /// <summary>Shows a location title over the scene: rises in, holds, fades out.</summary>
        public IEnumerator ShowTitle(string heading, string caption, float hold = 1.6f)
        {
            if (!titleGroup) yield break;
            title.text = heading;
            if (subtitle) subtitle.text = caption ?? "";
            var rect = (RectTransform)titleGroup.transform;
            Vector2 basePosition = rect.anchoredPosition;
            float time = 0;
            canvas.enabled = true;
            while (time < .5f)
            {
                time += Time.unscaledDeltaTime;
                float k = Ease.OutCubic(time / .5f);
                titleGroup.alpha = k;
                rect.anchoredPosition = basePosition + Vector2.down * (1 - k) * 24;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(hold);
            time = 0;
            while (time < .6f)
            {
                time += Time.unscaledDeltaTime;
                titleGroup.alpha = 1 - Ease.InCubic(time / .6f);
                yield return null;
            }
            titleGroup.alpha = 0;
            rect.anchoredPosition = basePosition;
            Set(Amount);
        }
    }
}
