using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// Lays out its children in the mockup's reference screen (e.g. 736 × 368 units) and magnifies them
    /// to fit the real window, so the HUD keeps the mockup's composition at any resolution and aspect.
    /// Children anchor to the corners/edges as in the mockup; this rect is stretched over its parent and
    /// resized to parent / scale. The scale follows the window height and is limited by its width, so
    /// on narrow windows the corner groups never run into each other.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ReferenceUiScale : MonoBehaviour
    {
        [Tooltip("Parent height (screen pixels at Canvas scale 1) that shows the layout at exactly 1:1. "
            + "Mockups are 368 units over a 960 px crop of a 1080 px frame: 414.")]
        [Min(1)] public float referenceHeight = 414;
        [Tooltip("Parent width that shows the layout at 1:1 (mockup 736 units over 1920 px).")]
        [Min(1)] public float referenceWidth = 736;
        [Tooltip("Round the magnification down to a whole number: crisper pixel fonts, but the HUD's share "
            + "of the screen then jumps between resolutions.")]
        public bool wholeNumbersOnly;
        [Min(.1f)] public float minScale = .5f;
        [Min(.1f)] public float maxScale = 12;

        public float Scale { get; private set; } = 1;
        RectTransform rect;
        Vector2 lastParentSize = new Vector2(-1, -1);
        float lastScale = -1;
        bool lastWhole;
        // Marks the rect as driven, like Unity's layout components: not saved, no prefab overrides.
        DrivenRectTransformTracker tracker;

        void OnEnable()
        {
            rect = (RectTransform)transform;
            lastParentSize = new Vector2(-1, -1);
            tracker.Clear();
            tracker.Add(this, rect, DrivenTransformProperties.Anchors | DrivenTransformProperties.Pivot
                | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta | DrivenTransformProperties.Scale);
            Apply();
        }

        void OnDisable() => tracker.Clear();

        void LateUpdate() => Apply();

        void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled) Apply();
        }

        /// <summary>Magnification for a parent of this size.</summary>
        public float ScaleFor(Vector2 size)
        {
            float scale = Mathf.Min(size.y / referenceHeight, size.x / referenceWidth);
            if (wholeNumbersOnly) scale = Mathf.Max(1, Mathf.Floor(scale + .25f));
            return Mathf.Clamp(scale, minScale, maxScale);
        }

        void Apply()
        {
            if (!rect) rect = (RectTransform)transform;
            var parent = rect.parent as RectTransform;
            if (!parent) return;
            var size = parent.rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            if (size == lastParentSize && lastWhole == wholeNumbersOnly && Mathf.Approximately(rect.localScale.x, lastScale)) return;
            lastParentSize = size;
            lastWhole = wholeNumbersOnly;
            Scale = lastScale = ScaleFor(size);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size / Scale - size;
            rect.localScale = new Vector3(Scale, Scale, 1);
        }
    }
}
