using System;
using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Game
{
    /// <summary>
    /// The window of a chosen map point: its name, a small view of the place and «Идти» / «Остаться».
    /// Rises a little while fading in, fades out on close; its own canvas is off while hidden.
    /// </summary>
    public sealed class OutingMapCard : MonoBehaviour
    {
        [Tooltip("The card's own canvas: switched off while hidden, so the map does not redraw the card.")]
        public Canvas canvas;
        public CanvasGroup visibility;
        public TMP_Text title;
        public Image preview;
        [Tooltip("Shown instead of the picture when the place has no preview yet.")]
        public GameObject noPreview;
        public StitchedLinenButton goButton, stayButton;
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
