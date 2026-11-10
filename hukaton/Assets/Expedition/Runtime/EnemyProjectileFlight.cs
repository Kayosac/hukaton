using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Flight of a creature's projectile: a parabola through the point it was aimed at. A straight shot is the same
    /// parabola with no gravity. Pure maths plus one physics query, so the shot the creature checks before firing is
    /// exactly the shot that flies.
    /// </summary>
    public static class EnemyProjectileFlight
    {
        /// <summary>The shortest flight, seconds: a point-blank lob does not get an absurd gravity.</summary>
        public const float MinimumFlightTime = .2f;

        /// <summary>
        /// Launch velocity and gravity of a shot from <paramref name="from"/> that passes through <paramref name="to"/>.
        /// <paramref name="speed"/> is the speed along the ground (m/s). <paramref name="arcHeight"/> is how far above
        /// the straight line the middle of the flight rises (0: straight, no gravity).
        /// </summary>
        public static void Launch(Vector3 from, Vector3 to, float speed, float arcHeight, out Vector3 velocity, out float gravity)
        {
            float time = FlightTime(from, to, speed);
            // A parabola sagging by h at its middle over a flight of time t has gravity 8h / t².
            gravity = arcHeight > 0 ? 8 * arcHeight / (time * time) : 0;
            velocity = (to - from) / time + Vector3.up * (gravity * time * .5f);
        }

        /// <summary>Seconds from <paramref name="from"/> to <paramref name="to"/> at this speed.</summary>
        public static float FlightTime(Vector3 from, Vector3 to, float speed)
        {
            Vector3 delta = to - from;
            // The ground distance sets the time; a shot straight up or down still takes the vertical distance.
            float distance = new Vector2(delta.x, delta.z).magnitude;
            if (distance < .01f) distance = Mathf.Abs(delta.y);
            return Mathf.Max(MinimumFlightTime, distance / Mathf.Max(.1f, speed));
        }

        /// <summary>Exact step along the parabola (no drift whatever the step).</summary>
        public static void Advance(ref Vector3 position, ref Vector3 velocity, float gravity, float dt)
        {
            position += velocity * dt + Vector3.down * (.5f * gravity * dt * dt);
            velocity += Vector3.down * (gravity * dt);
        }

        /// <summary>Where the shot is <paramref name="time"/> seconds after leaving <paramref name="from"/>.</summary>
        public static Vector3 PositionAt(Vector3 from, Vector3 velocity, float gravity, float time)
            => from + velocity * time + Vector3.down * (.5f * gravity * time * time);

        /// <summary>Turns the velocity toward a direction by at most <paramref name="degreesPerSecond"/>, keeping its speed.</summary>
        public static Vector3 Steer(Vector3 velocity, Vector3 toward, float degreesPerSecond, float dt)
        {
            if (degreesPerSecond <= 0 || toward.sqrMagnitude < 1e-6f || velocity.sqrMagnitude < 1e-6f) return velocity;
            return Vector3.RotateTowards(velocity, toward.normalized * velocity.magnitude, degreesPerSecond * Mathf.Deg2Rad * dt, 0);
        }

        /// <summary>
        /// Shortest distance between segments [p1, q1] and [p2, q2]; <paramref name="s"/> and <paramref name="t"/> are
        /// the fractions along each where it is reached.
        /// </summary>
        public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            const float tiny = 1e-8f;
            if (a <= tiny && e <= tiny) { s = t = 0; return r.magnitude; }
            if (a <= tiny) { s = 0; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= tiny) { t = 0; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                    s = denominator > tiny ? Mathf.Clamp01((b * f - c * e) / denominator) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
        }

        static readonly RaycastHit[] pathHits = new RaycastHit[16];

        /// <summary>
        /// Nothing solid on the shot's way to <paramref name="to"/> (the last <paramref name="radius"/> excluded): no rock,
        /// trunk or other creature. Colliders of the shooter and of the target are ignored. The player rig is never
        /// in the physics mask, so a shot at the player is clear when only the player is in the way.
        /// </summary>
        public static bool IsPathClear(Vector3 from, Vector3 to, float speed, float arcHeight, float radius, Transform shooter, Transform target, int segments = 8)
        {
            Launch(from, to, speed, arcHeight, out var velocity, out float gravity);
            float time = FlightTime(from, to, speed);
            float total = Vector3.Distance(from, to);
            // Stop short of the target point so the ground under its feet does not count as an obstacle.
            float end = total > .01f ? Mathf.Clamp01(1 - (radius * 2 + .3f) / total) : 0;
            Vector3 previous = from;
            for (int i = 1; i <= segments; i++)
            {
                Vector3 next = PositionAt(from, velocity, gravity, time * end * i / segments);
                Vector3 delta = next - previous;
                float distance = delta.magnitude;
                if (distance > 1e-4f)
                {
                    int count = Physics.SphereCastNonAlloc(previous, radius, delta / distance, pathHits, distance, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < count; h++)
                    {
                        var hit = pathHits[h].collider;
                        if (!hit) continue;
                        if (shooter && hit.transform.IsChildOf(shooter)) continue;
                        if (target && hit.transform.IsChildOf(target)) continue;
                        if (!Blocks(hit)) continue;
                        return false;
                    }
                }
                previous = next;
            }
            return true;
        }

        /// <summary>Things a projectile flies through: thrown cards, other projectiles, loot lying about.</summary>
        public static bool Blocks(Collider collider)
        {
            if (!collider) return false;
            if (collider.GetComponentInParent<CardProjectile>()) return false;
            if (collider.GetComponentInParent<EnemyProjectile>()) return false;
            if (collider.GetComponentInParent<WorldLoot>()) return false;
            return true;
        }
    }
}
