using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Teaser
{
    /// <summary>Turns the HUD coin by picking its flipbook frame for an angle that follows a curve over teaser time.</summary>
    public sealed class TeaserCoinSpin : TeaserTrack
    {
        public Image image;
        [Tooltip("Frames at equal steps of one full turn about the vertical axis, starting face-on.")]
        public Sprite[] frames = new Sprite[0];
        [Tooltip("Turn in degrees against teaser seconds.")]
        public AnimationCurve turn = AnimationCurve.Linear(0, 0, 2, 120);

        public override void Evaluate(float seconds)
        {
            if (!image || frames.Length == 0) return;
            float step = 360f / frames.Length;
            int index = Mathf.RoundToInt(Mathf.Repeat(turn.Evaluate(seconds), 360f) / step) % frames.Length;
            if (frames[index] && image.sprite != frames[index]) image.sprite = frames[index];
        }
    }
}
