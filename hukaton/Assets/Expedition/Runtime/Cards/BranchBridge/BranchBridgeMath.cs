using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Rules of the branch bridge (pure, tested in EditMode): which way it grows and how long it may get.
    /// It grows horizontally from the point of impact back toward where the card was thrown from; from a wall it never
    /// grows along or into the wall (at most <c>maxAngleFromNormal</c> from the wall's outward direction).
    /// An obstacle on the way shortens it; a branch shorter than the minimum is not grown at all.
    /// </summary>
    public static class BranchBridgeMath
    {
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        /// <summary>Unit horizontal direction of growth.</summary>
        /// <param name="point">Point of impact.</param>
        /// <param name="normal">Surface normal there.</param>
        /// <param name="throwOrigin">Where the card left the hand.</param>
        /// <param name="flight">Flight direction at impact (fallback when the thrower stands right above the point).</param>
        /// <param name="maxAngleFromNormal">Largest angle from a wall's outward horizontal direction, degrees.</param>
        public static Vector3 GrowthDirection(Vector3 point, Vector3 normal, Vector3 throwOrigin, Vector3 flight, float maxAngleFromNormal = 70)
        {
            Vector3 toThrower = Flat(throwOrigin - point);
            Vector3 direction = toThrower.sqrMagnitude > .01f ? toThrower.normalized : Flat(-flight);
            Vector3 wall = Flat(normal);
            // A wall: only when the surface is steep enough to have a meaningful outward direction.
            if (wall.sqrMagnitude > .25f * .25f)
            {
                wall.Normalize();
                if (direction.sqrMagnitude < 1e-6f) return wall;
                float angle = Vector3.Angle(wall, direction);
                if (angle > maxAngleFromNormal)
                    direction = Vector3.RotateTowards(wall, direction, maxAngleFromNormal * Mathf.Deg2Rad, 0);
            }
            if (direction.sqrMagnitude < 1e-6f) direction = wall.sqrMagnitude > 1e-6f ? wall.normalized : Vector3.forward;
            return Flat(direction).normalized;
        }

        /// <summary>
        /// Height of the branch's walking surface relative to the point of impact: flush with a top surface (slightly
        /// above it), at the point on a wall, and hanging below a ceiling.
        /// </summary>
        public static float TopOffset(Vector3 normal, float thickness)
        {
            float ny = normal.sqrMagnitude > 1e-6f ? normal.normalized.y : 0;
            if (ny > .5f) return .04f;
            if (ny < -.5f) return -(thickness + .03f);
            return 0;
        }

        /// <summary>
        /// The length the branch may grow to: the full <paramref name="desired"/> length, or up to the first obstacle
        /// (<paramref name="obstacleDistance"/>, measured from the root along the growth) less a margin.
        /// Returns 0 when that leaves less than <paramref name="minimum"/>: no room for a branch.
        /// </summary>
        public static float ClampLength(float desired, float? obstacleDistance, float margin, float minimum)
        {
            float length = Mathf.Max(0, desired);
            if (obstacleDistance.HasValue) length = Mathf.Min(length, obstacleDistance.Value - margin);
            return length >= minimum && length > 0 ? length : 0;
        }

        /// <summary>Growth curve 0 → 1 over the growth time: fast at first, a little springy at the end.</summary>
        public static float Growth(float t)
        {
            t = Mathf.Clamp01(t);
            float eased = 1 - Mathf.Pow(1 - t, 3);
            return Mathf.Clamp01(eased + Mathf.Sin(t * Mathf.PI) * .04f * t);
        }
    }
}
