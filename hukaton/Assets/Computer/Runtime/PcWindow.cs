using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>One XP-style window on the PC: title bar with a close button, a status bar, focus and z-order.</summary>
    public sealed class PcWindow : MonoBehaviour, IPointerDownHandler
    {
        public string title = "Окно";
        public Sprite icon;
        public TMP_Text titleText;
        public Image frame;
        public Sprite activeFrame, inactiveFrame;
        public Button closeButton;
        public CanvasGroup group;
        [Tooltip("The status bar line at the bottom of the window.")]
        public TMP_Text status;

        public bool IsOpen { get; private set; }
        public bool Focused { get; private set; }
        public event Action<PcWindow> FocusRequested, CloseRequested;
        /// <summary>The window has just been shown (refresh its contents).</summary>
        public event Action Opened;

        static readonly Color StatusColour = new Color(0, 0, 0, 1), ErrorColour = new Color(.75f, .1f, .05f, 1);

        void Awake()
        {
            if (titleText) titleText.text = title;
            if (closeButton) closeButton.onClick.AddListener(() => CloseRequested?.Invoke(this));
        }

        public void SetOpen(bool open)
        {
            bool was = IsOpen;
            IsOpen = open;
            group.alpha = open ? 1 : 0;
            group.interactable = group.blocksRaycasts = open;
            if (open && !was) Opened?.Invoke();
        }

        public void SetFocused(bool focused)
        {
            Focused = focused;
            if (frame) frame.sprite = focused ? activeFrame : inactiveFrame;
            if (titleText) titleText.color = focused ? Color.white : new Color(.85f, .89f, .98f, 1);
            if (focused) transform.SetAsLastSibling();
        }

        public void SetStatus(string text, bool error = false)
        {
            if (!status) return;
            status.text = text ?? "";
            status.color = error ? ErrorColour : StatusColour;
        }

        public void OnPointerDown(PointerEventData eventData) => FocusRequested?.Invoke(this);
    }
}
