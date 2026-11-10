using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.DialogueUI
{
    /// <summary>
    /// Standard uGUI Button events with an authored, multipart linen surface. Never scale the button: its size is
    /// the RectTransform, the size of the linen pieces is <see cref="Density"/> (8 — the full skin, 16 — half-size
    /// corners and edges for small buttons, label 32 → 16). <see cref="Rebuild"/> lays the pieces out for it.
    /// The tone colours the decision: green to agree, red to decline.
    /// </summary>
    [ExecuteAlways, AddComponentMenu("UI/Stitched Linen Button")]
    public sealed class StitchedLinenButton : Button
    {
        public enum VisualState { Normal, Hover, Pressed, Disabled }
        public enum Tone { Plain, Positive, Negative }

        [SerializeField] RectTransform surface;
        [SerializeField] Image[] surfaceImages;
        [SerializeField] TMP_Text label;
        [SerializeField] Color labelColor = new Color32(36, 27, 20, 255);
        [SerializeField] Vector2 normalOffset = Vector2.zero;
        [SerializeField] Vector2 hoverOffset = new Vector2(0, 1);
        [SerializeField] Vector2 pressedOffset = new Vector2(0, -3);
        [Tooltip("Texture pixels per UI unit of the linen pieces: 8 is the full-size skin, 16 draws them at half size. " +
                 "Changing it rebuilds the frame; never scale the button itself.")]
        [SerializeField, Min(1)] float density = 8;
        [Tooltip("Plain linen, green for a positive decision (buy, burn, agree), red for a negative one (leave, cancel).")]
        [SerializeField] Tone tone;
        [Tooltip("Paint the exact colours below instead of the tone's (approved mockup tints).")]
        [SerializeField] bool customTint;
        [SerializeField] Color normalTint = new Color32(0x94, 0xCE, 0x80, 0xFF);
        [Tooltip("Colour of the non-interactable button when Custom Tint is on.")]
        [SerializeField] Color disabledTint = new Color(.6f, .58f, .54f, .75f);
        [Tooltip("Visual preview in Edit mode only. Play mode always follows real Button input.")]
        [SerializeField] VisualState editorPreview = VisualState.Normal;

        public float Density => density;
        public Tone ButtonTone => tone;
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

        /// <summary>Authoring: sets the density and tone and rebuilds the frame (the inspector does this on change).</summary>
        public void Configure(float pixelsPerUnit, Tone colour)
        {
            density = Mathf.Max(1, pixelsPerUnit);
            tone = colour;
            Rebuild();
        }

        /// <summary>Authoring: exact normal and disabled colours instead of the tone's palette.</summary>
        public void SetTint(Color normal, Color disabled)
        {
            customTint = true;
            normalTint = normal;
            disabledTint = disabled;
            Rebuild();
        }


        /// <summary>
        /// Brings the whole skin to <see cref="Density"/>: every piece of the shadow and the frame, the shadow drop,
        /// the label, the hover/press travel, the text padding and the button's own size are rescaled from the
        /// density they were built at, so corners and edges keep their exact proportions; then the tone's colours.
        /// Pure layout — safe in the editor and at runtime, and a no-op when nothing changed.
        /// </summary>
        public void Rebuild()
        {
            var pieces = GetComponentsInChildren<Image>(true);
            float built = 8;
            foreach (var piece in pieces)
                if (piece.gameObject != gameObject && piece.sprite) { built = piece.pixelsPerUnitMultiplier; break; }
            float ratio = built / density;
            if (!Mathf.Approximately(ratio, 1))
            {
                foreach (var piece in pieces)
                {
                    if (piece.gameObject == gameObject) continue;
                    Rescale(piece.rectTransform, ratio);
                    piece.pixelsPerUnitMultiplier = density;
                }
                foreach (Transform layer in transform)
                    if (layer is RectTransform r && !layer.GetComponent<Image>() && !layer.GetComponent<TMP_Text>()) Rescale(r, ratio);
                if (label)
                {
                    label.fontSize = Mathf.Round(label.fontSize * ratio);
                    Rescale(label.rectTransform, ratio);
                }
                normalOffset = Travel(normalOffset, ratio);
                hoverOffset = Travel(hoverOffset, ratio);
                pressedOffset = Travel(pressedOffset, ratio);
                if (TryGetComponent(out DialogueBubbleLayout layout)) layout.Rescale(ratio);
                ((RectTransform)transform).sizeDelta *= ratio;
            }
            var block = colors;
            if (customTint)
            {
                Paint(ref block, normalTint);
                block.disabledColor = disabledTint;
            }
            else switch (tone)
            {
                case Tone.Positive: Paint(ref block, new Color(.62f, .86f, .52f)); break;
                case Tone.Negative: Paint(ref block, new Color(.95f, .55f, .48f)); break;
                default: Paint(ref block, new Color(.93f, .89f, .82f)); break;
            }
            colors = block;
            ApplyVisual(CurrentVisualState);
        }

        static void Rescale(RectTransform r, float ratio)
        {
            r.anchoredPosition *= ratio;
            r.sizeDelta *= ratio;
        }

        // Whole pixels, and a non-zero travel never disappears.
        static Vector2 Travel(Vector2 offset, float ratio) => new Vector2(Whole(offset.x * ratio), Whole(offset.y * ratio));
        static float Whole(float value) => value == 0 ? 0 : Mathf.Sign(value) * Mathf.Max(1, Mathf.Round(Mathf.Abs(value)));

        static void Paint(ref ColorBlock block, Color normal)
        {
            block.normalColor = normal;
            block.highlightedColor = block.selectedColor = Color.Lerp(normal, Color.white, .35f);
            block.pressedColor = normal * new Color(.82f, .8f, .78f, 1);
            block.disabledColor = new Color(.6f, .58f, .54f, .75f);
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
