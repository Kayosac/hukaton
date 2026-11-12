using System;
using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Game
{
    /// <summary>
    /// The window of a chosen map point: its name, a small view of the place and «Идти» / «Остаться».
    /// Rises a little while fading in, fades out on close; its own canvas is off while hidden. The card is laid out
    /// around its content every time it opens: a long name widens it (up to <see cref="maxTitleWidth"/>), past that the
    /// name wraps and the card grows taller — the text always fits, at its fixed pixel-font size.
    /// </summary>
    public sealed class OutingMapCard : MonoBehaviour
    {
        [Tooltip("The card's own canvas: switched off while hidden, so the map does not redraw the card.")]
        public Canvas canvas;
        public CanvasGroup visibility;
        public TMP_Text title;
        public Image preview;
        [Tooltip("The dark frame holding the preview (its size is the picture area).")]
        public RectTransform previewFrame;
        [Tooltip("Shown instead of the picture when the place has no preview yet.")]
        public GameObject noPreview;
        public StitchedLinenButton goButton, stayButton;
        [Header("Layout (canvas pixels)")]
        [Tooltip("Left/right, top, bottom: from the card edge to the content (includes the linen border).")]
        public Vector3 padding = new Vector3(26, 22, 26);
        [Min(0)] public float titleGap = 4, buttonsGap = 12, buttonSpacing = 12;
        [Tooltip("The card widens for a long name up to this content width; beyond it the name wraps.")]
        [Min(100)] public float maxTitleWidth = 860;
        [Min(.01f)] public float showSeconds = .18f;
        [Min(.01f)] public float hideSeconds = .1f;
        [Tooltip("How far below its place (canvas pixels) the card starts rising from. Whole pixels only.")]
        [Min(0)] public float rise = 24;

        public bool IsOpen { get; private set; }
        Action go, stay;
        RectTransform rect;
        Vector2 home;
        float shown, from, start;

        void Awake()
        {
            rect = (RectTransform)transform;
            home = rect.anchoredPosition;
            goButton.onClick.AddListener(() => go?.Invoke());
            stayButton.onClick.AddListener(() => stay?.Invoke());
            Apply(0);
            if (canvas) canvas.enabled = false;
        }

        public void Open(string placeName, Sprite picture, Action onGo, Action onStay)
        {
            go = onGo; stay = onStay;
            if (title) title.text = placeName;
            if (preview) { preview.sprite = picture; preview.enabled = picture; }
            if (noPreview) noPreview.SetActive(!picture);
            Layout();
            IsOpen = true;
            if (canvas) canvas.enabled = true;
            if (visibility) { visibility.interactable = true; visibility.blocksRaycasts = true; }
            from = shown; start = Time.unscaledTime;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            go = stay = null;
            // Only stop clicks: a non-interactable group would grey the buttons out while the card fades.
            if (visibility) visibility.blocksRaycasts = false;
            from = shown; start = Time.unscaledTime;
        }

        /// <summary>Sizes the card around the name, the picture and the buttons; whole pixels only.</summary>
        void Layout()
        {
            if (!rect || !title || !previewFrame) return;
            var go = (RectTransform)goButton.transform;
            var stay = (RectTransform)stayButton.transform;
            Vector2 picture = previewFrame.sizeDelta;
            Vector2 button = new Vector2(Mathf.Max(go.sizeDelta.x, stay.sizeDelta.x), Mathf.Max(go.sizeDelta.y, stay.sizeDelta.y));
            float row = button.x * 2 + buttonSpacing;
            title.textWrappingMode = TMPro.TextWrappingModes.Normal;
            var natural = title.GetPreferredValues(title.text, Mathf.Infinity, Mathf.Infinity);
            float content = Even(Mathf.Max(picture.x, row, Mathf.Min(natural.x, maxTitleWidth)));
            float titleHeight = Even(title.GetPreferredValues(title.text, content, Mathf.Infinity).y);
            float width = content + 2 * padding.x;
            float height = padding.y + titleHeight + titleGap + picture.y + buttonsGap + button.y + padding.z;
            rect.sizeDelta = new Vector2(width, Even(height));
            float top = rect.sizeDelta.y / 2, bottom = -top;
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(.5f, .5f);
            title.rectTransform.sizeDelta = new Vector2(content, titleHeight);
            title.rectTransform.anchoredPosition = new Vector2(0, top - padding.y - titleHeight / 2);
            previewFrame.anchoredPosition = new Vector2(0, Mathf.Round(top - padding.y - titleHeight - titleGap - picture.y / 2));
            float buttonsY = Mathf.Round(bottom + padding.z + button.y / 2);
            float half = (button.x + buttonSpacing) / 2;
            go.anchoredPosition = new Vector2(-half, buttonsY);
            stay.anchoredPosition = new Vector2(half, buttonsY);
        }

        static float Even(float value) => Mathf.Ceil(value / 2) * 2;

        void Update()
        {
            float target = IsOpen ? 1 : 0;
            if (Mathf.Approximately(shown, target)) return;
            float k = Mathf.Clamp01((Time.unscaledTime - start) / (IsOpen ? showSeconds : hideSeconds));
            shown = Mathf.Lerp(from, target, k);
            Apply(shown);
            if (!IsOpen && shown <= 0 && canvas) canvas.enabled = false;
        }

        void Apply(float amount)
        {
            if (visibility) visibility.alpha = amount;
            // Rising, never scaling: the linen buttons keep their built size. Whole pixels keep the linen crisp.
            float eased = 1 - (1 - amount) * (1 - amount);
            if (rect) rect.anchoredPosition = home + new Vector2(0, Mathf.Round(-rise * (1 - eased)));
        }
    }
}
