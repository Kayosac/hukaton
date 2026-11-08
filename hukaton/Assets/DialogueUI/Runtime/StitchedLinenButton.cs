using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.DialogueUI
{
    /// <summary>Standard uGUI Button events with an authored, multipart linen surface.</summary>
    [ExecuteAlways, AddComponentMenu("UI/Stitched Linen Button")]
    public sealed class StitchedLinenButton : Button
    {
        public enum VisualState { Normal, Hover, Pressed, Disabled }

        [SerializeField] RectTransform surface;
        [SerializeField] Image[] surfaceImages;
        [SerializeField] TMP_Text label;
        [SerializeField] Color labelColor = new Color32(36, 27, 20, 255);
        [SerializeField] Vector2 normalOffset = Vector2.zero;
        [SerializeField] Vector2 hoverOffset = new Vector2(0, 1);
        [SerializeField] Vector2 pressedOffset = new Vector2(0, -3);
        [Tooltip("Visual preview in Edit mode only. Play mode always follows real Button input.")]
        [SerializeField] VisualState editorPreview = VisualState.Normal;

        public RectTransform Surface => surface;
        public TMP_Text Label => label;
        public VisualState CurrentVisualState { get; private set; }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            var visual = state == SelectionState.Disabled ? VisualState.Disabled :
                state == SelectionState.Pressed ? VisualState.Pressed :
                state == SelectionState.Highlighted || state == SelectionState.Selected ? VisualState.Hover : VisualState.Normal;
            if (!Application.isPlaying && state != SelectionState.Disabled) visual = editorPreview;
            ApplyVisual(visual);
        }

        protected override void InstantClearState()
        {
            base.InstantClearState();
            ApplyVisual(VisualState.Normal);
        }

        void ApplyVisual(VisualState state)
        {
            CurrentVisualState = state;
            var palette = colors;
            Color tint = state == VisualState.Pressed ? palette.pressedColor :
                state == VisualState.Hover ? palette.highlightedColor :
                state == VisualState.Disabled ? palette.disabledColor : palette.normalColor;
            tint *= palette.colorMultiplier;
            if (surfaceImages != null)
                foreach (var image in surfaceImages)
                    if (image) image.color = tint;
            if (label)
            {
                var ink = labelColor;
                if (state == VisualState.Disabled) ink.a *= .6f;
                label.color = ink;
            }
            if (surface)
            {
                Vector2 offset = state == VisualState.Pressed ? pressedOffset :
                    state == VisualState.Hover ? hoverOffset : normalOffset;
                surface.anchoredPosition = new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));
            }
        }
    }
}
