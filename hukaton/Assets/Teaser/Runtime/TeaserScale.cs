using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>Uniform scale of a transform over teaser time.</summary>
    public sealed class TeaserScale : TeaserTrack
    {
        public Transform target;
        public AnimationCurve scale = AnimationCurve.Constant(0, 1, 1);

        public override void Evaluate(float seconds)
        {
            if (target) target.localScale = Vector3.one * scale.Evaluate(seconds);
        }
    }
}
