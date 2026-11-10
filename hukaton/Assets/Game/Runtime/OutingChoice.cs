using System;
using Hakaton.Character;
using Hakaton.DialogueUI;
using Hakaton.Expeditions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Game
{
    /// <summary>
    /// The question at the door: the ready-made location or a new endless world. 1 / 2 pick, Escape or the right
    /// mouse button stays in the shop.
    /// </summary>
    public sealed class OutingChoice : MonoBehaviour
    {
        public CanvasGroup visibility;
        public TMP_Text question;
        public StitchedLinenButton readyButton, endlessButton, stayButton;
        public PlayerControlGate control;

        public bool IsOpen { get; private set; }
        Action<OutingPlace> chosen;
        int openedFrame;

        void Awake()
        {
            readyButton.onClick.AddListener(() => Choose(OutingPlace.Ready));
            endlessButton.onClick.AddListener(() => Choose(OutingPlace.Endless));
            stayButton.onClick.AddListener(Close);
            Show(false);
        }

        /// <summary>Asks where to go; <paramref name="onChosen"/> runs after the panel has closed.</summary>
        public void Open(string readyName, Action<OutingPlace> onChosen)
        {
            if (IsOpen) return;
            chosen = onChosen;
            if (readyButton.Label) readyButton.Label.text = readyName;
            IsOpen = true;
            openedFrame = Time.frameCount;
            if (control) control.SetBlocked(this, true);
            Show(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            chosen = null;
            Show(false);
            if (control) control.SetBlocked(this, false);
        }

        void Choose(OutingPlace place)
        {
            if (!IsOpen) return;
            var callback = chosen;
            Close();
            callback?.Invoke(place);
        }

        void Update()
        {
            // The key that opened the door must not answer the question in the same frame.
            if (!IsOpen || Time.frameCount == openedFrame) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)) Choose(OutingPlace.Ready);
            else if (keyboard != null && (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)) Choose(OutingPlace.Endless);
            else if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame) Close();
        }

        void OnDisable() { if (IsOpen) Close(); }

        void Show(bool visible)
        {
            if (!visibility) return;
            visibility.alpha = visible ? 1 : 0;
            visibility.interactable = visible;
            visibility.blocksRaycasts = visible;
        }
    }
}
