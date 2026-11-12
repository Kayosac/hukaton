using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>
    /// The spell card in the middle of the picture, spinning and rolling as it drifts into the depth. It lives in camera space,
    /// so its screen position, size and spin carry on unchanged through a cut while the world behind it changes (and
    /// it stays upright even when the camera is rolled upside down).
    /// </summary>
    public sealed class TeaserCardFlight : TeaserTrack
    {
        public CardView card;
        public Texture front, back;
        [Min(1)] public int level = 1;
        [Tooltip("Seconds when the card appears and leaves.")]
        public float startSeconds = 2, endSeconds = 5;
        [Tooltip("Camera-space positions at the start and the end of the flight.")]
        public Vector3 fromLocal = new Vector3(0, 0, 1), toLocal = new Vector3(0, 0, 1.5f);
        [Tooltip("Flight progress 0..1 over the flight time 0..1.")]
        public AnimationCurve travel = AnimationCurve.Linear(0, 0, 1, 1);
        [Tooltip("Camera-space tilt at the start and the end of the flight.")]
        public Vector3 fromEuler = new Vector3(0, 180, 0), toEuler = new Vector3(0, 180, 0);
        [Tooltip("Spin about the card's own vertical axis, degrees against teaser seconds.")]
        public AnimationCurve spin = AnimationCurve.Linear(2, 0, 5, 540);
        [Tooltip("Roll about the view axis, degrees against teaser seconds, on top of the spin.")]
        public AnimationCurve roll = AnimationCurve.Constant(0, 1, 0);

        bool shown;

        public override void Evaluate(float seconds)
        {
            if (!card) return;
            bool visible = seconds >= startSeconds && seconds < endSeconds;
            if (visible && !shown) { card.Show(front, back, level); shown = true; }
            if (card.gameObject.activeSelf != visible) card.gameObject.SetActive(visible);
            if (!visible) return;
            float k = Mathf.InverseLerp(startSeconds, endSeconds, seconds);
            var t = card.transform;
            t.localPosition = Vector3.LerpUnclamped(fromLocal, toLocal, travel.Evaluate(k));
            t.localRotation = Quaternion.AngleAxis(roll.Evaluate(seconds), Vector3.forward)
                * Quaternion.Euler(Vector3.Lerp(fromEuler, toEuler, k)) * Quaternion.AngleAxis(spin.Evaluate(seconds), Vector3.up);
        }
    }
}
