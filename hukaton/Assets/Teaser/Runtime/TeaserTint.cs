using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>Brightness of a renderer's base colour over teaser time (0 = black, 1 = as authored, above 1 = flash).</summary>
    public sealed class TeaserTint : TeaserTrack
    {
        public Renderer target;
        public string colorProperty = "_BaseColor";
        public Color baseColor = Color.white;
        public AnimationCurve brightness = AnimationCurve.Constant(0, 1, 1);

        MaterialPropertyBlock block;
        int id;

        public override void Evaluate(float seconds)
        {
            if (!target) return;
            if (block == null) { block = new MaterialPropertyBlock(); id = Shader.PropertyToID(colorProperty); }
            float b = brightness.Evaluate(seconds);
            target.GetPropertyBlock(block);
            block.SetColor(id, new Color(baseColor.r * b, baseColor.g * b, baseColor.b * b, baseColor.a));
            target.SetPropertyBlock(block);
        }
    }
}
