using System;
using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Game
{
    /// <summary>
    /// The linen confirmation window shared by both menus: a question, an explanation, an optional «Пропустить обучение»
    /// box and two answers. The window grows with its text (layout on the window itself).
    /// </summary>
    public sealed class MenuDialog : MonoBehaviour
    {
        public CanvasGroup visibility;
        public TMP_Text title, body;
        public GameObject skipRow;
        public Toggle skipToggle;
        public StitchedLinenButton cancelButton, confirmButton;

        public bool IsOpen { get; private set; }
        Action<bool> confirmed;
        Action cancelled;

        void Awake()
        {
            cancelButton.onClick.AddListener(Cancel);
            confirmButton.onClick.AddListener(Confirm);
            MenuView.Show(visibility, false);
        }

        /// <param name="onConfirm">Receives the skip box's state (false when the box is hidden).</param>
        public void Open(string question, string explanation, bool offerSkip, string cancelText, string confirmText,
            StitchedLinenButton.Tone confirmTone, Action<bool> onConfirm, Action onCancel = null)
        {
            title.text = question;
            body.text = explanation ?? string.Empty;
            body.gameObject.SetActive(!string.IsNullOrEmpty(explanation));
            skipRow.SetActive(offerSkip);
            skipToggle.SetIsOnWithoutNotify(false);
            cancelButton.Label.text = cancelText;
            confirmButton.Label.text = confirmText;
            if (confirmButton.ButtonTone != confirmTone) confirmButton.Configure(confirmButton.Density, confirmTone);
            confirmed = onConfirm;
            cancelled = onCancel;
            IsOpen = true;
            MenuView.Show(visibility, true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            var callback = cancelled;
            Close();
            callback?.Invoke();
        }

        void Confirm()
        {
            if (!IsOpen) return;
            var callback = confirmed;
            bool skip = skipRow.activeSelf && skipToggle.isOn;
            Close();
            callback?.Invoke(skip);
        }

        public void Close()
        {
            IsOpen = false;
            confirmed = null;
            cancelled = null;
            MenuView.Show(visibility, false);
        }
    }

    static class MenuView
    {
        public static void Show(CanvasGroup group, bool visible)
        {
            if (!group) return;
            group.alpha = visible ? 1 : 0;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }
}
