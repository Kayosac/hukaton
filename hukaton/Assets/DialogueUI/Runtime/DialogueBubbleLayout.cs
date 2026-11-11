using TMPro;
using UnityEngine;

namespace Hakaton.DialogueUI
{
    /// <summary>Sizes an authored bubble around TMP text; never creates UI objects.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class DialogueBubbleLayout : MonoBehaviour
    {
        [SerializeField] RectTransform body;
        [SerializeField] TMP_Text dialogue;
        [SerializeField, Min(1)] float minimumWidth = 240;
        [SerializeField, Min(1)] float maximumWidth = 560;
        [SerializeField, Min(1)] float minimumHeight = 180;
        [Tooltip("Left, top, right, bottom in canvas pixels. Includes the frame and stitches.")]
        [SerializeField] Vector4 padding = new Vector4(64, 60, 64, 88);

        string lastText;
        TMP_FontAsset lastFont;
        float lastFontSize;
        bool dirty = true;

        public TMP_Text Dialogue => dialogue;
        public RectTransform Body => body;
        public float MaximumWidth => maximumWidth;
        public Vector4 Padding => padding;

        public void SetText(string value)
        {
            if (!dialogue) return;
            TypedLine.Say(dialogue, value ?? string.Empty);
            RefreshLayout();
        }

        /// <summary>Letters of the line are still being typed.</summary>
        public bool IsTyping => dialogue && dialogue.TryGetComponent(out TypedLine typed) && typed.IsTyping;

        /// <summary>Prints the rest of the line at once; true if it was still typing (the press is spent on that).</summary>
        public bool SkipTyping() => dialogue && dialogue.TryGetComponent(out TypedLine typed) && typed.Skip();

        void OnEnable() => dirty = true;
        void OnValidate()
        {
            minimumWidth = Mathf.Max(1, minimumWidth);
            maximumWidth = Mathf.Max(minimumWidth, maximumWidth);
            minimumHeight = Mathf.Max(1, minimumHeight);
            padding = Vector4.Max(Vector4.zero, padding);
            dirty = true;
        }

        void LateUpdate()
        {
            if (dialogue && (dirty || dialogue.text != lastText || dialogue.font != lastFont ||
                dialogue.fontSize != lastFontSize || dialogue.havePropertiesChanged))
                RefreshLayout();
        }

        /// <summary>Scales the frame metrics (a skin rebuilt at another pixel density).</summary>
        public void Rescale(float ratio)
        {
            minimumWidth *= ratio;
            maximumWidth *= ratio;
            minimumHeight *= ratio;
            padding *= ratio;
            dirty = true;
        }

        /// <summary>Can also be called after changing text style or localization.</summary>
        public void RefreshLayout()
        {
            if (!body || !dialogue || !dialogue.font) return;
            float horizontal = padding.x + padding.z;
            float vertical = padding.y + padding.w;
            float naturalWidth = dialogue.GetPreferredValues(dialogue.text,
                float.PositiveInfinity, float.PositiveInfinity).x;
            float width = Mathf.Ceil(Mathf.Clamp(naturalWidth + horizontal,
                Mathf.Max(minimumWidth, horizontal + 1),
                Mathf.Max(maximumWidth, minimumWidth, horizontal + 1)));
            float preferredHeight = dialogue.GetPreferredValues(dialogue.text,
                width - horizontal, float.PositiveInfinity).y;
            float height = Mathf.Ceil(Mathf.Max(minimumHeight, preferredHeight + vertical));

            var textRect = dialogue.rectTransform;
            if (textRect.anchorMin != Vector2.zero) textRect.anchorMin = Vector2.zero;
            if (textRect.anchorMax != Vector2.one) textRect.anchorMax = Vector2.one;
            var lower = new Vector2(padding.x, padding.w);
            var upper = new Vector2(-padding.z, -padding.y);
            if (textRect.offsetMin != lower) textRect.offsetMin = lower;
            if (textRect.offsetMax != upper) textRect.offsetMax = upper;
            if (!Mathf.Approximately(body.rect.width, width))
                body.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            if (!Mathf.Approximately(body.rect.height, height))
                body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            lastText = dialogue.text;
            lastFont = dialogue.font;
            lastFontSize = dialogue.fontSize;
            dirty = false;
        }
    }
}
