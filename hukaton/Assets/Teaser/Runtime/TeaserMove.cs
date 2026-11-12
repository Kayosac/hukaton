using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>Moves a transform between two local positions; progress follows a curve over teaser time.</summary>
    public sealed class TeaserMove : TeaserTrack
    {
        public Transform target;
        public Vector3 fromLocal, toLocal;
        [Tooltip("Progress 0..1 against teaser seconds.")]
        public AnimationCurve progress = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public override void Evaluate(float seconds)
        {
            if (target) target.localPosition = Vector3.LerpUnclamped(fromLocal, toLocal, progress.Evaluate(seconds));
        }
    }
}
