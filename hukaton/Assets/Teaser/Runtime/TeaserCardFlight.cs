using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.Teaser
{
    /// <summary>
    /// The spell card flying across the picture. It lives in camera space, so its screen position, size, angle and
    /// speed carry on unchanged through a cut while the world behind it changes (and stays upright even when the
    /// camera is rolled upside down).
    /// </summary>
    public sealed class TeaserCardFlight : TeaserTrack
    {
        public CardView card;
        public Texture front, back;
        [Min(1)] public int level = 1;
        [Tooltip("Seconds when the card enters and leaves.")]
        public float startSeconds = 2, endSeconds = 5;
        [Tooltip("Camera-space positions at the start and the end of the flight.")]
        public Vector3 fromLocal = new Vector3(-1, 0, 2), toLocal = new Vector3(1, 0, 2);
        [Tooltip("Camera-space rotation at the start and the end of the flight.")]
        public Vector3 fromEuler, toEuler;
        [Tooltip("Small flutter: degrees and cycles per second about the card's own vertical.")]
        public float flutterDegrees = 6, flutterHz = 1.3f;

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
            t.localPosition = Vector3.Lerp(fromLocal, toLocal, k);
            t.localRotation = Quaternion.Euler(Vector3.Lerp(fromEuler, toEuler, k))
                * Quaternion.Euler(0, flutterDegrees * Mathf.Sin(seconds * flutterHz * Mathf.PI * 2), 0);
        }
    }
}
