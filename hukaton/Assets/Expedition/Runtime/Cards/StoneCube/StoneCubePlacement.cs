using System;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Where a stone cube may appear (pure rules, tested in EditMode). The cube stands next to the surface that was
    /// hit, pushed out along its normal by half a side; when that spot intersects geometry the search moves further
    /// out from the surface, then upward, then diagonally — never into the surface. No free spot means a miss.
    /// </summary>
    public static class StoneCubePlacement
    {
        /// <summary>Gap between the surface and the cube so the cube never starts touching it.</summary>
        public const float Skin = .02f;

        /// <summary>The cube stands upright; it faces along the flight (yaw only) so its top stays a level surface.</summary>
        public static Quaternion Orientation(Vector3 flightDirection, float yawJitterDegrees = 0)
        {
            Vector3 flat = new Vector3(flightDirection.x, 0, flightDirection.z);
            float yaw = flat.sqrMagnitude > 1e-6f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : 0;
            return Quaternion.Euler(0, yaw + yawJitterDegrees, 0);
        }

        /// <summary>The centre right next to the surface: half a side (plus the skin) out along the normal.</summary>
        public static Vector3 BaseCentre(Vector3 point, Vector3 normal, float side)
        {
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            return point + n * (side * .5f + Skin);
        }

        /// <summary>Number of candidate centres <see cref="Candidate"/> produces.</summary>
        public const int CandidateCount = 10;

        /// <summary>
        /// Candidate centre <paramref name="index"/> in the order they are tried: the base spot; then 1/4, 1/2, 3/4 and
        /// one whole side further from the surface; then 1/4, 1/2, 3/4 of a side up; then two diagonal steps (out and up).
        /// </summary>
        public static Vector3 Candidate(int index, Vector3 point, Vector3 normal, float side)
        {
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 centre = BaseCentre(point, n, side);
            if (index <= 0) return centre;
            if (index <= 4) return centre + n * (side * .25f * index);
            if (index <= 7) return centre + Vector3.up * (side * .25f * (index - 4));
            Vector3 diagonal = (n + Vector3.up).sqrMagnitude > 1e-6f ? (n + Vector3.up).normalized : Vector3.up;
            return centre + diagonal * (side * .4f * (index - 7));
        }

        /// <summary>The first candidate for which <paramref name="isFree"/> holds; false when there is no room at all.</summary>
        public static bool TryFind(Vector3 point, Vector3 normal, float side, Func<Vector3, bool> isFree, out Vector3 centre)
        {
            for (int i = 0; i < CandidateCount; i++)
            {
                centre = Candidate(i, point, normal, side);
                if (isFree(centre)) return true;
            }
            centre = BaseCentre(point, normal, side);
            return false;
        }
    }
}
