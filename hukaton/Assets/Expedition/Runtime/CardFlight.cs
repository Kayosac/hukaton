using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// The one ballistic model shared by thrown cards and the aiming preview, so the preview never lies:
    /// gravity plus a curve in the spin's direction (x — to the right of the flight, y — up across it),
    /// integrated with fixed sub-steps.
    /// </summary>
    public static class CardFlight
    {
        public const float Step = 1f / 120f;
        /// <summary>Falling speed, m/s, over which up-spin lift fades to nothing after the top of the arc.</summary>
        public const float LiftReleaseSpeed = 1.5f;

        public static void Advance(ref Vector3 position, ref Vector3 velocity, Vector2 spin, ExpeditionSettings settings, float seconds)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / Step));
            float dt = seconds / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 curve = Vector3.zero;
                if (velocity.sqrMagnitude > .0001f)
                {
                    Vector3 forward = velocity.normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, forward);
                    // Straight up or down has no "right": the card only lifts or dips then.
                    if (side.sqrMagnitude > 1e-6f) side.Normalize();
                    Vector3 lift = side.sqrMagnitude > 1e-6f ? Vector3.Cross(forward, side) : Vector3.zero;
                    // Both bends grow with speed, like real spin: a fast card sweeps a wide arc, a slow one
                    // bends tighter but never loops.
                    float speedShare = velocity.magnitude / Mathf.Max(.1f, settings.maximumSpeed);
                    // Lift needs air flowing across the card: it fades as the flight turns vertical, so a card
                    // spun upward can't keep climbing — climbing bleeds speed, the lift weakens, gravity wins.
                    float horizontalShare = new Vector2(forward.x, forward.z).magnitude;
                    float liftStrength = spin.y * settings.liftAcceleration * speedShare * horizontalShare;
                    // Up-spin only holds a card up while it rises: past the top it lets go smoothly, so the card
                    // falls with growing speed instead of hanging in the sky. Down-spin always pushes.
                    if (liftStrength > 0) liftStrength *= Mathf.Clamp01(1 + velocity.y / LiftReleaseSpeed);
                    curve = side * (spin.x * settings.curveAcceleration * speedShare) + lift * liftStrength;
                }
                velocity += (Vector3.down * settings.cardGravity + curve) * dt;
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
