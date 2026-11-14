using UnityEngine;

namespace Hakaton.Trading.Unity
{
    /// <summary>
    /// The arithmetic of a close look at a displayed item (<see cref="DisplayItemInspector"/>): how big, how far and how
    /// it turns. Distances are along the view; an item is measured by its bounding sphere so it fits in any turn.
    /// </summary>
    public static class InspectionFraming
    {
        /// <summary>
        /// The scale at which a sphere of <paramref name="radius"/> (at scale 1) held at <paramref name="depth"/> spans
        /// <paramref name="heightShare"/> of the view's height (its diameter against the frustum's height there).
        /// </summary>
        public static float FitScale(float radius, float depth, float tanHalfFov, float heightShare)
            => radius > 1e-5f ? heightShare * depth * tanHalfFov / radius : 1;

        /// <summary>The fitted sphere's radius as a share of its distance: the same at any hold distance.</summary>
        public static float RadiusRatio(float tanHalfFov, float heightShare) => heightShare * tanHalfFov;

        /// <summary>
        /// Where the dimmed backdrop stands: just behind the item's far side when it is held farthest away
        /// (<paramref name="farthestZoom"/> × the hold distance).
        /// </summary>
        public static float BackdropDepth(float depth, float farthestZoom, float radiusRatio)
            => depth * (farthestZoom + radiusRatio) * BackdropGap;

        /// <summary>Room left between the item's far side and the backdrop, as a factor.</summary>
        public const float BackdropGap = 1.08f;

        /// <summary>
        /// How far in front of the eye the item is held: <paramref name="preferred"/>, unless the shelf or a wall is so
        /// close (<paramref name="clearance"/>, the nearest surface in view) that the backdrop would stand behind it —
        /// then nearer, so nothing comes between the eye and the backdrop. Never nearer than <paramref name="minimum"/>.
        /// </summary>
        public static float HoldDepth(float preferred, float clearance, float farthestZoom, float radiusRatio, float minimum)
        {
            float fits = clearance * .96f / ((farthestZoom + radiusRatio) * BackdropGap);
            return Mathf.Max(minimum, Mathf.Min(preferred, fits));
        }

        /// <summary>
        /// The nearest zoom (a multiple of the hold distance) that keeps the item's near side at least
        /// <paramref name="nearLimitRatio"/> × the hold distance in front of the eye (past the camera's near plane).
        /// </summary>
        public static float NearestZoom(float wanted, float radiusRatio, float nearLimitRatio)
            => Mathf.Max(wanted, radiusRatio + nearLimitRatio);

        /// <summary>One wheel step: scrolling up (positive) brings the item closer by <paramref name="step"/>, down takes it away.</summary>
        public static float StepZoom(float zoom, float scroll, float step, float nearest, float farthest)
        {
            if (scroll > 0) zoom *= step;
            else if (scroll < 0) zoom /= step;
            return Mathf.Clamp(zoom, nearest, Mathf.Max(nearest, farthest));
        }

        /// <summary>
        /// The item's spin (degrees per second, x: about the view's up, y: about its right). While the mouse moves the
        /// spin catches up with it (<paramref name="grip"/> per second: smooth, not jittery); once it stops, the item
        /// coasts and slows by <paramref name="drag"/> per second — a flick keeps it turning a little, like a real thing.
        /// </summary>
        public static Vector2 Spin(Vector2 spin, Vector2 turnedDegrees, float dt, float grip, float drag, float maxSpin)
        {
            if (dt <= 0) return spin;
            if (turnedDegrees != Vector2.zero) spin = Vector2.Lerp(spin, turnedDegrees / dt, 1 - Mathf.Exp(-grip * dt));
            else spin *= Mathf.Exp(-drag * dt);
            if (spin.sqrMagnitude < .01f) spin = Vector2.zero;
            return Vector2.ClampMagnitude(spin, maxSpin);
        }

        /// <summary>Ease in and out, flat at both ends.</summary>
        public static float Smoother(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        /// <summary>Ease out that passes its end a little and settles back (<paramref name="overshoot"/> 0: none).</summary>
        public static float BackOut(float t, float overshoot)
        {
            t = Mathf.Clamp01(t) - 1;
            return 1 + t * t * ((overshoot + 1) * t + overshoot);
        }
    }
}
