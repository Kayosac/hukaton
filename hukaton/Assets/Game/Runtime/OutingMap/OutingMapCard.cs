using System;
using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Game
{
    /// <summary>The window of a chosen map point: its name, a small view of the place and «Идти» / «Остаться».</summary>
    public sealed class OutingMapCard : MonoBehaviour
    {
        public CanvasGroup visibility;
        public TMP_Text title;
        public Image preview;
        [Tooltip("Shown instead of the picture when the place has no preview yet.")]
        public GameObject noPreview;
        public StitchedLinenButton goButton, stayButton;
        [Min(.01f)] public float showSeconds = .12f;

        public bool IsOpen { get; private set; }
        Action go, stay;
        float shownAt;

        void Awake()
        {
            goButton.onClick.AddListener(() => go?.Invoke());
            stayButton.onClick.AddListener(() => stay?.Invoke());
            Show(false);
        }

        public void Open(string placeName, Sprite picture, Action onGo, Action onStay)
        {
            go = onGo; stay = onStay;
            if (title) title.text = placeName;
            if (preview) { preview.sprite = picture; preview.enabled = picture; }
            if (noPreview) noPreview.SetActive(!picture);
            IsOpen = true;
            shownAt = Time.unscaledTime;
            Show(true);
        }

        public void Close()
        {
            IsOpen = false;
            go = stay = null;
            Show(false);
        }

        void Update()
        {
            if (!IsOpen || !visibility) return;
            visibility.alpha = showSeconds > 0 ? Mathf.Clamp01((Time.unscaledTime - shownAt) / showSeconds) : 1;
        }

        void Show(bool visible)
        {
            if (!visibility) return;
            visibility.alpha = visible ? (showSeconds > 0 ? 0 : 1) : 0;
            visibility.interactable = visible;
            visibility.blocksRaycasts = visible;
        }
    }
}
