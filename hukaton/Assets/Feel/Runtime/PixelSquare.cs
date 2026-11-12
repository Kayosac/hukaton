using UnityEngine;

namespace Hakaton.Feel
{
    /// <summary>
    /// Keeps a small UI square (the aim reticle) an even whole number of screen pixels on a side:
    /// <see cref="pixelsPerStep"/> × the screen pixels per layout unit, rounded to a whole number (6 px at 1080p).
    /// The size is set in layout units of the <see cref="ReferenceUiScale"/> it lives in, so the square stays crisp
    /// and centred at any window size; the Canvas's Pixel Perfect snaps its edges to the screen grid.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [DefaultExecutionOrder(100)] // After ReferenceUiScale has picked this frame's magnification.
    public sealed class PixelSquare : MonoBehaviour
    {
        [Tooltip("The HUD layout this square lives in: its magnification turns screen pixels into layout units.")]
        public ReferenceUiScale layout;
        [Tooltip("Screen pixels per side for each whole screen pixel per layout unit; 2 keeps the side even.")]
        [Min(1)] public int pixelsPerStep = 2;

        RectTransform rect;
        Canvas canvas;
        // Marks the size as driven, like Unity's layout components: not saved, no prefab overrides.
        DrivenRectTransformTracker tracker;

        /// <summary>Side in screen pixels when one layout unit covers <paramref name="pixelsPerUnit"/> screen pixels.</summary>
        public static int SidePixels(float pixelsPerUnit, int pixelsPerStep)
            => Mathf.Max(1, pixelsPerStep) * Mathf.Max(1, Mathf.RoundToInt(pixelsPerUnit));

        void OnEnable()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            tracker.Clear();
            tracker.Add(this, rect, DrivenTransformProperties.SizeDelta);
            Apply();
        }

        void OnDisable() => tracker.Clear();

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (!rect) rect = (RectTransform)transform;
            float pixelsPerUnit = PixelsPerUnit();
            if (pixelsPerUnit <= 0) return;
            float side = SidePixels(pixelsPerUnit, pixelsPerStep) / pixelsPerUnit;
            var size = new Vector2(side, side);
            if (rect.sizeDelta != size) rect.sizeDelta = size;
        }

        /// <summary>Screen pixels per layout unit: the layout's magnification times the root canvas's scale factor.</summary>
        float PixelsPerUnit()
        {
            float scale = layout ? layout.Scale : 1;
            if (canvas) scale *= canvas.rootCanvas.scaleFactor;
            return scale;
        }
    }
}
