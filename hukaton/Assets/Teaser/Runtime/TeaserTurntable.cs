using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>Turns an object about one of its local axes by an angle that follows a curve over teaser time.</summary>
    public sealed class TeaserTurntable : TeaserTrack
    {
        public Transform target;
        public Vector3 axis = Vector3.up;
        [Tooltip("Turn in degrees against teaser seconds (from the object's authored rotation).")]
        public AnimationCurve degrees = AnimationCurve.Linear(0, 0, 2, 90);

        Quaternion rest;
        bool captured;

        public override void Evaluate(float seconds)
        {
            if (!target) return;
            if (!captured) { rest = target.localRotation; captured = true; }
            target.localRotation = rest * Quaternion.AngleAxis(degrees.Evaluate(seconds), axis);
        }
    }
}
