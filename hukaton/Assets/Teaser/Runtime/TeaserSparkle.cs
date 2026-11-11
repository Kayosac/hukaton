using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Teaser
{
    /// <summary>One pass of a pixel sparkle flipbook (a row of equal cells in one texture) on a RawImage.</summary>
    public sealed class TeaserSparkle : TeaserTrack
    {
        public RawImage image;
        [Min(1)] public int columns = 8, rows = 2;
        [Tooltip("Row of the sheet to play, counted from the top.")]
        [Min(0)] public int row;
        public float startSeconds = 15.5f;
        [Min(1)] public float framesPerSecond = 12;

        public override void Evaluate(float seconds)
        {
            if (!image) return;
            int cell = Mathf.FloorToInt((seconds - startSeconds) * framesPerSecond);
            bool visible = seconds >= startSeconds && cell < columns;
            if (image.enabled != visible) image.enabled = visible;
            if (!visible) return;
            float w = 1f / columns, h = 1f / rows;
            image.uvRect = new Rect(cell * w, 1 - (row + 1) * h, w, h);
        }
    }
}
