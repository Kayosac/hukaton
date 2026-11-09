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

        /// <summary>Per-card flight state: time since launch and when the up/down spin let go.</summary>
        public struct Clock
        {
            public float time;
            public bool released;
            public float releasedAt;
        }

        /// <param name="clock">Starts as default at launch; advanced by <paramref name="seconds"/>. Up/down spin climbs exactly
        /// as it always did, but lets go once the card stalls at the top (or after liftHoldSeconds at most), so the
        /// card falls faster and faster instead of hanging in the sky or climbing forever.</param>
        public static void Advance(ref Vector3 position, ref Vector3 velocity, ref Clock clock, Vector2 spin, ExpeditionSettings settings, float seconds)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / Step));
            float dt = seconds / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 curve = Vector3.zero;
                if (!clock.released && velocity.magnitude < settings.liftReleaseSpeed)
                {
                    clock.released = true;
                    clock.releasedAt = clock.time;
                }
                if (velocity.sqrMagnitude > .0001f)
                {
                    Vector3 forward = velocity.normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, forward);
                    // Straight up or down has no "right": the card only lifts or dips then.
                    if (side.sqrMagnitude > 1e-6f) side.Normalize();
                    Vector3 lift = side.sqrMagnitude > 1e-6f ? Vector3.Cross(forward, side) : Vector3.up;
                    // Sideways bend grows with speed, like real spin: a fast card sweeps a wide arc, a slow one
                    // bends tighter but never loops. Lift is full off the hand (up-spin climbs), then wears off.
                    float speedShare = velocity.magnitude / Mathf.Max(.1f, settings.maximumSpeed);
                    curve = side * (spin.x * settings.curveAcceleration * speedShare) + lift * (spin.y * settings.liftAcceleration * LiftShare(clock, settings));
                }
                velocity += (Vector3.down * settings.cardGravity + curve) * dt;
                position += velocity * dt;
                clock.time += dt;
            }
        }

        /// <summary>Share of the up/down spin lift still acting: 1 until the card stalls or the hold runs out, then eases to 0.</summary>
        public static float LiftShare(Clock clock, ExpeditionSettings settings)
        {
            float start = clock.released ? Mathf.Min(clock.releasedAt, settings.liftHoldSeconds) : settings.liftHoldSeconds;
            if (clock.time <= start) return 1;
            if (settings.liftFadeSeconds <= 0) return 0;
            float t = Mathf.Clamp01((clock.time - start) / settings.liftFadeSeconds);
            return 1 - t * t * (3 - 2 * t);
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
