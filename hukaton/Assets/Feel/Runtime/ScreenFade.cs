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
        Color tint;

        void Awake() { tint = colour; Set(0); }

        /// <summary>Covers the screen in the current colour: <see cref="colour"/> unless a tinted cover is still up.</summary>
        public void Set(float amount) => Set(amount, tint);

        /// <summary>Covers the screen in a one-off colour (deep water). Once the cover clears, <see cref="colour"/> is back.</summary>
        public void Set(float amount, Color with)
        {
            Amount = Mathf.Clamp01(amount);
            tint = Amount > 0 ? with : colour;
            cover.color = new Color(tint.r, tint.g, tint.b, Amount);
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

        /// <summary>
        /// Shows a location title over the scene: rises in, holds, fades out. <paramref name="headingScale"/> enlarges the
        /// heading for this one showing (the «new goal» banner) and widens its line so it stays on one row.
        /// </summary>
        public IEnumerator ShowTitle(string heading, string caption, float hold = 1.6f, float headingScale = 1)
        {
            if (!titleGroup) yield break;
            if (scaled) RestoreHeading();
            if (headingScale > 1.001f)
            {
                scaled = true;
                headingSize = title.fontSize;
                headingRect = title.rectTransform.sizeDelta;
                title.fontSize = headingSize * headingScale;
                title.rectTransform.sizeDelta = new Vector2(headingRect.x * Mathf.Max(1, headingScale * 1.2f), headingRect.y * headingScale);
            }
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
            if (scaled) RestoreHeading();
            Set(Amount);
        }

        /// <summary>
        /// The «new goal» banner: a loud cousin of <see cref="ShowTitle"/>. A warm flash over the screen, the card slams in
        /// from big with an overshoot and a twist, shakes, the caption's letters snap together; while it holds the caption
        /// pulses between <paramref name="captionColour"/> and white. <paramref name="captionScale"/> sizes the caption against
        /// the (already scaled) heading, and the caption drops below the heading so the two big lines never overlap.
        /// </summary>
        public IEnumerator ShowBanner(string heading, string caption, float hold, float headingScale, float captionScale,
            Color captionColour)
        {
            if (!titleGroup) yield break;
            if (scaled) RestoreHeading();
            scaled = true;
            headingSize = title.fontSize;
            headingRect = title.rectTransform.sizeDelta;
            float bigHeading = headingSize * Mathf.Max(1, headingScale);
            title.fontSize = bigHeading;
            title.rectTransform.sizeDelta = new Vector2(headingRect.x * Mathf.Max(1, headingScale * 1.2f), headingRect.y * headingScale);
            title.text = heading;
            Color headingColour = title.color;

            RectTransform captionRect = subtitle ? subtitle.rectTransform : null;
            float captionSize = 0, captionSpacing = 0;
            Color captionBase = Color.white;
            Vector2 captionPosition = default, captionSizeDelta = default;
            if (subtitle)
            {
                captionSize = subtitle.fontSize;
                captionSpacing = subtitle.characterSpacing;
                captionBase = subtitle.color;
                captionPosition = captionRect.anchoredPosition;
                captionSizeDelta = captionRect.sizeDelta;
                float bigCaption = bigHeading * Mathf.Clamp(captionScale, .2f, 1.5f);
                subtitle.fontSize = bigCaption;
                subtitle.text = caption ?? "";
                subtitle.color = captionColour;
                float height = bigCaption * 1.3f;
                captionRect.sizeDelta = new Vector2(title.rectTransform.sizeDelta.x, height);
                // The caption is bottom-anchored in the card; the heading hangs from the top. Put the caption's top a little
                // under the heading's bottom.
                float cardHeight = ((RectTransform)titleGroup.transform).rect.height;
                float headingBottom = cardHeight - title.rectTransform.sizeDelta.y;
                captionRect.anchoredPosition = new Vector2(captionPosition.x, headingBottom - height + bigCaption * .08f);
            }

            var rect = (RectTransform)titleGroup.transform;
            Vector2 basePosition = rect.anchoredPosition;
            Vector3 baseScale = rect.localScale;
            Quaternion baseRotation = rect.localRotation;
            float coverBefore = Amount;
            Color coverTint = tint;
            var flash = new Color(1, .93f, .7f, 1);
            canvas.enabled = true;

            // Slam in: from 2.4x with an overshoot, a twist that settles, the letters gathering, a warm flash fading off.
            const float inTime = .55f;
            float time = 0;
            while (time < inTime)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / inTime);
                titleGroup.alpha = Mathf.Clamp01(time / .12f);
                float grow = Ease.OutBack(t);
                rect.localScale = baseScale * Mathf.LerpUnclamped(2.4f, 1, grow);
                rect.localRotation = baseRotation * Quaternion.Euler(0, 0, -9 * (1 - t) * Mathf.Cos(t * 14));
                rect.anchoredPosition = basePosition;
                if (subtitle) subtitle.characterSpacing = captionSpacing + Mathf.Lerp(70, 0, Ease.OutCubic(t));
                float white = 1 - Ease.OutCubic(Mathf.Clamp01(time / .35f));
                title.color = Color.Lerp(headingColour, Color.white, white);
                if (subtitle) subtitle.color = Color.Lerp(captionColour, Color.white, white);
                if (coverBefore < .001f) Set(.32f * white, flash);
                yield return null;
            }
            rect.localScale = baseScale;
            rect.localRotation = baseRotation;
            if (subtitle) subtitle.characterSpacing = captionSpacing;
            title.color = headingColour;
            if (coverBefore < .001f) Set(0, coverTint);

            // Impact shake, decaying.
            time = 0;
            while (time < .32f)
            {
                time += Time.unscaledDeltaTime;
                float k = 1 - time / .32f;
                rect.anchoredPosition = basePosition + new Vector2(Mathf.Sin(time * 95), Mathf.Cos(time * 120)) * 10 * k * k;
                yield return null;
            }
            rect.anchoredPosition = basePosition;

            // Hold: the caption pulses toward white, the card breathes a touch.
            time = 0;
            while (time < hold)
            {
                time += Time.unscaledDeltaTime;
                float wave = .5f + .5f * Mathf.Sin(time * 7);
                if (subtitle) subtitle.color = Color.Lerp(captionColour, Color.white, wave * .55f);
                rect.localScale = baseScale * (1 + .025f * Mathf.Sin(time * 3.5f));
                yield return null;
            }

            // Out: swells a little as it fades.
            time = 0;
            Vector3 heldScale = rect.localScale;
            while (time < .5f)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / .5f);
                titleGroup.alpha = 1 - Ease.InCubic(t);
                rect.localScale = heldScale * (1 + .18f * Ease.OutCubic(t));
                yield return null;
            }
            titleGroup.alpha = 0;
            rect.anchoredPosition = basePosition;
            rect.localScale = baseScale;
            rect.localRotation = baseRotation;
            title.color = headingColour;
            if (subtitle)
            {
                subtitle.fontSize = captionSize;
                subtitle.characterSpacing = captionSpacing;
                subtitle.color = captionBase;
                captionRect.anchoredPosition = captionPosition;
                captionRect.sizeDelta = captionSizeDelta;
            }
            RestoreHeading();
            Set(Amount);
        }

        bool scaled;
        float headingSize;
        Vector2 headingRect;
        void RestoreHeading()
        {
            scaled = false;
            title.fontSize = headingSize;
            title.rectTransform.sizeDelta = headingRect;
        }
    }
}
