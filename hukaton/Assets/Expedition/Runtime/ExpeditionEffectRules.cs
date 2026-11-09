using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Shared units for impacts: creature HP, player half-hearts and normalized impulse.</summary>
    public static class ExpeditionEffectRules
    {
        /// <summary>Inclusive lower boundaries: 0 / 1 / 2 / 4 damage units.</summary>
        public static int ImpactDamage(float normalSpeed, float firstThreshold, float step)
        {
            if (normalSpeed < firstThreshold) return 0;
            if (normalSpeed < firstThreshold + step) return 1;
            if (normalSpeed < firstThreshold + 2 * step) return 2;
            return 4;
        }

        public static float NormalSpeed(Vector3 relativeVelocity, Vector3 surfaceNormal)
        {
            return Mathf.Abs(Vector3.Dot(relativeVelocity, surfaceNormal.normalized));
        }

        /// <summary>At the impact centre a foot-level cast has a defined upward direction.</summary>
        public static Vector3 RadialImpulse(Vector3 centre, Vector3 receiver, float impulse)
        {
            Vector3 direction = receiver - centre;
            return (direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.up) * impulse;
        }

        /// <summary>
        /// Height of a water surface above (<paramref name="x"/>, <paramref name="z"/>): the highest of the world-space
        /// <paramref name="triangles"/> (three corners each) that covers the point seen from above. Walls of the mesh are
        /// skipped. <c>false</c> when no triangle covers the point: it is beside the water, not in it.
        /// </summary>
        public static bool SurfaceHeight(Vector3[] triangles, float x, float z, out float height)
        {
            height = float.NegativeInfinity;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-7f) continue;
                float u = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                float v = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                const float edge = -1e-4f;
                if (u < edge || v < edge || 1 - u - v < edge) continue;
                height = Mathf.Max(height, u * a.y + v * b.y + (1 - u - v) * c.y);
            }
            return !float.IsNegativeInfinity(height);
        }
    }
}
