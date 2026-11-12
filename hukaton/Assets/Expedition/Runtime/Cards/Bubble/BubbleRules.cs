using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Pure geometry of «Пузырь» (tested in EditMode): a hemisphere of radius R standing on its centre (the ground
    /// point), reaching <see cref="Below"/> under it for uneven ground. Bodies are upright capsules (feet, radius,
    /// height); projectiles are points moving along a segment per physics step.
    /// </summary>
    public static class BubbleRules
    {
        /// <summary>The dome reaches this far below its centre (the ground is never quite flat), m.</summary>
        public const float Below = .35f;

        /// <summary>Horizontal radius of the dome at <paramref name="height"/> above its centre (0 at and above the top).</summary>
        public static float RadiusAt(float radius, float height)
        {
            if (radius <= 0) return 0;
            float h = Mathf.Max(0, height);
            return h >= radius ? 0 : Mathf.Sqrt(radius * radius - h * h);
        }

        /// <summary>The point is inside the dome.</summary>
        public static bool Inside(Vector3 point, Vector3 centre, float radius)
            => point.y >= centre.y - Below && (point - centre).sqrMagnitude < radius * radius;

        /// <summary>
        /// How far an upright body has to move out of the dome (0: it is outside), and which way (horizontal, away
        /// from the centre). The body is measured at its feet (or at the dome's centre height when it stands lower):
        /// the dome is widest at the ground.
        /// </summary>
        public static float Overlap(Vector3 feet, float bodyRadius, float bodyHeight, Vector3 centre, float radius, out Vector3 outward)
        {
            outward = Vector3.zero;
            if (radius <= 0) return 0;
            if (feet.y + Mathf.Max(0, bodyHeight) < centre.y - Below) return 0;
            float across = RadiusAt(radius, feet.y - centre.y);
            if (across <= 0) return 0;
            Vector3 flat = feet - centre;
            flat.y = 0;
            float distance = flat.magnitude;
            float need = across + Mathf.Max(0, bodyRadius) - distance;
            if (need <= 0) return 0;
            outward = distance > 1e-4f ? flat / distance : Vector3.forward;
            return need;
        }

        /// <summary>
        /// How far to move a body out this physics step: a body that only leans on the film is put right back on it;
        /// one caught deep inside (the dome grew around it) is pushed out at <paramref name="expelSpeed"/>.
        /// </summary>
        public static float ExpelStep(float need, float snapDepth, float expelSpeed, float deltaTime)
        {
            if (need <= 0) return 0;
            return need <= snapDepth ? need : Mathf.Min(need, Mathf.Max(0, expelSpeed) * deltaTime);
        }

        /// <summary>
        /// Velocity of a body against the film: the part heading in is removed (and, above
        /// <paramref name="bounceSpeed"/>, bounced back by <paramref name="bounce"/>). <paramref name="inward"/>: the
        /// inward speed it had.
        /// </summary>
        public static Vector3 Deflect(Vector3 velocity, Vector3 outward, float bounceSpeed, float bounce, out float inward)
        {
            inward = -Vector3.Dot(velocity, outward);
            if (inward <= 0) return velocity;
            float back = inward > bounceSpeed ? inward * Mathf.Max(0, bounce) : 0;
            return velocity + outward * (inward + back);
        }

        /// <summary>
        /// Does a point moving from <paramref name="from"/> to <paramref name="to"/> fly into the dome? True also when
        /// it already is inside. <paramref name="point"/>: where it meets the film (or where it is).
        /// </summary>
        public static bool Enters(Vector3 from, Vector3 to, Vector3 centre, float radius, out Vector3 point)
        {
            point = from;
            if (radius <= 0) return false;
            if (Inside(from, centre, radius)) return true;
            Vector3 d = to - from;
            float a = Vector3.Dot(d, d);
            if (a < 1e-10f) return false;
            Vector3 m = from - centre;
            float b = Vector3.Dot(m, d);
            float c = Vector3.Dot(m, m) - radius * radius;
            if (c > 0 && b > 0) return false;
            float disc = b * b - a * c;
            if (disc < 0) return false;
            float t = (-b - Mathf.Sqrt(disc)) / a;
            if (t < 0 || t > 1) return false;
            point = from + d * t;
            return point.y >= centre.y - Below;
        }
    }
}
