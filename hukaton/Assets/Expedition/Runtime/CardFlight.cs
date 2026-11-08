using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The one ballistic model shared by thrown cards and the aiming preview, so the preview never lies:
    /// gravity plus a sideways curve proportional to spin, integrated with fixed sub-steps.
    /// </summary>
    public static class CardFlight
    {
        public const float Step = 1f / 120f;

        public static void Advance(ref Vector3 position, ref Vector3 velocity, float spin, ExpeditionSettings settings, float seconds)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / Step));
            float dt = seconds / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 side = velocity.sqrMagnitude > .0001f ? Vector3.Cross(Vector3.up, velocity.normalized) : Vector3.zero;
                velocity += (Vector3.down * settings.cardGravity + side * spin * settings.curveAcceleration) * dt;
                position += velocity * dt;
            }
        }

        /// <summary>Layers a thrown card collides with: everything except the player and other cards.</summary>
        public static int CollisionMask
        {
            get
            {
                int mask = ~0;
                mask &= ~(1 << 2); // Ignore Raycast: the player rig
                int cards = LayerMask.NameToLayer("Cards");
                if (cards >= 0) mask &= ~(1 << cards);
                return mask;
            }
        }

        /// <summary>Sweeps one segment. Triggers never stop a card; water has a solid surface collider.</summary>
        public static bool Sweep(Vector3 from, Vector3 to, float radius, out RaycastHit hit)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-5f) { hit = default; return false; }
            return Physics.SphereCast(from, radius, delta / distance, out hit, distance, CollisionMask, QueryTriggerInteraction.Ignore);
        }
    }
}
