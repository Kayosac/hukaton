using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>A sine sway on teaser time: a turn about a local axis, a shift along a local axis, or both.</summary>
    public sealed class TeaserSway : TeaserTrack
    {
        public Transform target;
        [Tooltip("Largest turn either way, degrees, about rotationAxis.")]
        public float degrees;
        public Vector3 rotationAxis = Vector3.up;
        [Tooltip("Largest shift either way, metres, along shiftAxis (local).")]
        public float shift;
        public Vector3 shiftAxis = Vector3.right;
        [Min(.05f)] public float period = 5;
        [Tooltip("Phase offset in seconds.")]
        public float phase;

        Vector3 restPosition;
        Quaternion restRotation;
        bool captured;

        public override void Evaluate(float seconds)
        {
            if (!target) return;
            if (!captured) { restPosition = target.localPosition; restRotation = target.localRotation; captured = true; }
            float s = Mathf.Sin((seconds + phase) * Mathf.PI * 2 / period);
            target.localRotation = restRotation * Quaternion.AngleAxis(degrees * s, rotationAxis);
            target.localPosition = restPosition + shiftAxis.normalized * (shift * s);
        }
    }
}
