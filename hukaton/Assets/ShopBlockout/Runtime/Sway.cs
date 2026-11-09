using UnityEngine;

namespace Hakaton.ShopBlockout
{
    /// <summary>Lazy hanging sway of flags, banners and garlands around the point they hang from.</summary>
    public sealed class Sway : MonoBehaviour
    {
        [Tooltip("Local point that stays put: the top edge or the string the decoration hangs from.")]
        public Vector3 pivot;
        [Tooltip("Swing amplitude in degrees around local X (towards/away), Y (twist) and Z (side to side).")]
        public Vector3 angles = new Vector3(0, 0, 3);
        [Min(.1f)] public float period = 3.5f;
        [Tooltip("Offset in seconds so neighbouring flags do not move in lockstep.")]
        public float phase;

        Vector3 basePosition;
        Quaternion baseRotation;

        void OnEnable()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
        }

        void OnDisable() => transform.SetLocalPositionAndRotation(basePosition, baseRotation);

        void LateUpdate()
        {
            float t = (Time.time + phase) * 2 * Mathf.PI / period;
            // Two incommensurate waves read as a draught rather than a metronome.
            float a = Mathf.Sin(t) * .75f + Mathf.Sin(t * 1.618f + 1.3f) * .25f;
            float b = Mathf.Sin(t * .87f + 2.1f) * .7f + Mathf.Sin(t * 1.93f + .4f) * .3f;
            var swing = Quaternion.Euler(angles.x * a, angles.y * b, angles.z * b);
            Vector3 hang = Vector3.Scale(transform.localScale, pivot);
            transform.SetLocalPositionAndRotation(
                basePosition + baseRotation * (hang - swing * hang), baseRotation * swing);
        }
    }
}
