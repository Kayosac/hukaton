using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Feel
{
    /// <summary>
    /// The one UI size standard of the game: every screen-space canvas is laid out for Full HD (1920 × 1080) and
    /// magnified to the real window, so the UI takes the same share of the screen at any resolution (4K is ×2).
    /// The canvases use Canvas Scaler "Scale With Screen Size" with this reference and "Expand", so the whole
    /// 1920 × 1080 layout always fits: wide windows follow the height, narrow ones the width.
    /// </summary>
    public static class UiReference
    {
        public const float Width = 1920, Height = 1080;
        public static readonly Vector2 Resolution = new Vector2(Width, Height);

        /// <summary>Screen pixels per Full HD pixel for a window of this size (the canvases' scale factor).</summary>
        public static float ScaleFor(float screenWidth, float screenHeight)
            => Mathf.Max(.01f, Mathf.Min(screenWidth / Width, screenHeight / Height));

        /// <summary>Screen pixels per Full HD pixel for the current window.</summary>
        public static float ScreenScale => ScaleFor(Screen.width, Screen.height);

        /// <summary>True when the scaler follows the standard.</summary>
        public static bool Follows(CanvasScaler scaler)
            => scaler && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
               && scaler.referenceResolution == Resolution
               && scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand;
    }
}
