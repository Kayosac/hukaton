using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The rope's rules for the player (pure, tested in EditMode): the hands hang on a cord of fixed length from the
    /// anchor (a taut rope pulls, a slack one does not push), the pull on the anchor, and the speed kept on release:
    /// the real tangential velocity, without any bonus — only the outward radial part a taut rope cannot have is removed.
    /// Controls: W/S move along the rope when looking up or down beyond the climb angle, otherwise they swing the body
    /// forward/back like A/D swing it sideways (all relative to the view).
    /// </summary>
    public static class RopeMath
    {
        /// <summary>The point pulled back onto the sphere of <paramref name="length"/> around the anchor when it is farther away.</summary>
        public static Vector3 ConstrainToLength(Vector3 anchor, Vector3 point, float length)
        {
            Vector3 offset = point - anchor;
            float distance = offset.magnitude;
            if (distance <= length || distance < 1e-6f) return point;
            return anchor + offset * (length / distance);
        }

        /// <summary>The rope is stretched to its length (within <paramref name="slack"/>).</summary>
        public static bool IsTaut(Vector3 anchor, Vector3 point, float length, float slack = .02f)
            => (point - anchor).magnitude >= length - slack;

        /// <summary>
        /// Velocity the body keeps when it lets go: the actual velocity, minus an outward radial component if the rope
        /// is taut (a taut rope cannot be moving away from the anchor). Never longer than the input.
        /// </summary>
        public static Vector3 ReleaseVelocity(Vector3 anchor, Vector3 point, Vector3 velocity, float length, float slack = .02f)
        {
            if (!IsTaut(anchor, point, length, slack)) return velocity;
            Vector3 offset = point - anchor;
            if (offset.sqrMagnitude < 1e-8f) return velocity;
            Vector3 radial = offset.normalized;
            float outward = Vector3.Dot(velocity, radial);
            return outward > 0 ? velocity - radial * outward : velocity;
        }

        /// <summary>
        /// One position-based step of a body hanging on the rope: accelerate, move, pull back onto the rope.
        /// Returns the correction the rope applied (towards the anchor); see <see cref="TensionImpulse"/>.
        /// </summary>
        public static Vector3 Step(ref Vector3 position, ref Vector3 velocity, Vector3 anchor, float length, Vector3 acceleration, float dt)
        {
            Vector3 predicted = position + (velocity + acceleration * dt) * dt;
            Vector3 constrained = ConstrainToLength(anchor, predicted, length);
            velocity = (constrained - position) / dt;
            position = constrained;
            return constrained - predicted;
        }

        /// <summary>Impulse the rope gives its anchor while correcting a body of <paramref name="mass"/> by <paramref name="correction"/> in one step (towards the body).</summary>
        public static Vector3 TensionImpulse(Vector3 correction, float mass, float dt) => dt > 0 ? -correction * (mass / dt) : Vector3.zero;

        /// <summary>
        /// The player lets go of the rope: Space, or E unless loot is in focus (E picks it up). The E press that grabbed
        /// the rope this very frame is not also a release — otherwise the hands would let go in the frame they took hold.
        /// </summary>
        public static bool WantsToLetGo(bool jumpPressed, bool interactPressed, bool lootFocused, bool grabbedThisFrame)
            => jumpPressed || (interactPressed && !lootFocused && !grabbedThisFrame);

        /// <summary>Climbing speed along the rope (+ up): W/S only when the view is pitched beyond <paramref name="climbLookAngle"/>.</summary>
        public static float ClimbRate(Vector2 move, float pitchDegrees, float climbLookAngle, float climbSpeed)
        {
            if (Mathf.Abs(pitchDegrees) < climbLookAngle) return 0;
            return Mathf.Clamp(move.y, -1, 1) * Mathf.Sign(pitchDegrees) * climbSpeed;
        }

        /// <summary>Horizontal swinging push from the movement keys relative to the view (W/S only when not climbing).</summary>
        public static Vector3 PumpAcceleration(Vector3 viewForward, Vector3 viewRight, Vector2 move, float pitchDegrees, float climbLookAngle, float acceleration)
        {
            Vector3 forward = new Vector3(viewForward.x, 0, viewForward.z);
            Vector3 right = new Vector3(viewRight.x, 0, viewRight.z);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.zero;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.zero;
            float ahead = Mathf.Abs(pitchDegrees) < climbLookAngle ? move.y : 0;
            return Vector3.ClampMagnitude(forward * ahead + right * move.x, 1) * acceleration;
        }

        /// <summary>
        /// Closest approach between a ray (origin, unit direction, length) and the segment a–b.
        /// Returns the distance; <paramref name="rayT"/> is along the ray, <paramref name="segmentU"/> 0..1 along the segment.
        /// </summary>
        public static float RayToSegment(Vector3 origin, Vector3 direction, float rayLength, Vector3 a, Vector3 b, out float rayT, out float segmentU)
        {
            Vector3 d1 = direction, d2 = b - a, r = origin - a;
            float aa = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float c = Vector3.Dot(d1, r), bb = Vector3.Dot(d1, d2);
            float denominator = aa * e - bb * bb;
            float s = denominator > 1e-8f ? Mathf.Clamp((bb * f - c * e) / denominator, 0, rayLength) : 0;
            float t = e > 1e-8f ? Mathf.Clamp01((bb * s + f) / e) : 0;
            // Re-project the ray parameter for the clamped segment point.
            s = aa > 1e-8f ? Mathf.Clamp((Vector3.Dot(d1, a + d2 * t - origin)) / aa, 0, rayLength) : 0;
            rayT = s;
            segmentU = t;
            return Vector3.Distance(origin + d1 * s, a + d2 * t);
        }
    }
}
