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
    }
}
