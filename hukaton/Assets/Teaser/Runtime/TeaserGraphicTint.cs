using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Teaser
{
    /// <summary>Brightness of a UI graphic over teaser time (0 = black, 1 = as authored).</summary>
    public sealed class TeaserGraphicTint : TeaserTrack
    {
        public Graphic graphic;
        public Color baseColor = Color.white;
        public AnimationCurve brightness = AnimationCurve.Constant(0, 1, 1);

        public override void Evaluate(float seconds)
        {
            if (!graphic) return;
            float b = brightness.Evaluate(seconds);
            graphic.color = new Color(baseColor.r * b, baseColor.g * b, baseColor.b * b, baseColor.a);
        }
    }
}
