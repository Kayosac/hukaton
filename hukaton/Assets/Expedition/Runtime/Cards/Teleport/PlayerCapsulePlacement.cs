using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Where the player's capsule can be put next to a contact point (Teleport, Swap). Board: on the side of the
    /// contact, in the nearest free pose with a minimal gap for the collider — beside a wall or under a ceiling that may
    /// be in the air; the shift never carries the capsule through an obstacle and never lifts it onto a ledge; with no
    /// free pose nearby there is no move. Poses are the capsule's BOTTOM point (the feet). Physics queries only, so the
    /// search runs the same in EditMode tests.
    /// </summary>
    public static class PlayerCapsulePlacement
    {
        /// <summary>Capsule size and search limits.</summary>
        public struct Settings
        {
            public float radius, height;
            [Tooltip("Gap between the contact surface and the capsule, m.")]
            public float gap;
            [Tooltip("Furthest shift from the touching pose, m.")]
            public float maxShift;
            [Tooltip("Highest upward shift (world up) from the touching pose: no lifting onto ledges, m.")]
            public float maxRise;
            [Tooltip("Search grid step, m.")]
            public float step;
            public int mask;

            /// <summary>Defaults for a capsule of this size: 4 cm gap, shifts up to 1.2 m, rises up to 0.3 m, 15 cm grid, player layer ignored.</summary>
            public static Settings For(float radius, float height) => new Settings
            {
                radius = radius, height = height, gap = .04f, maxShift = 1.2f, maxRise = .3f, step = .15f, mask = ~(1 << 2)
            };
        }

        static readonly Collider[] Overlaps = new Collider[64];
        static readonly RaycastHit[] Hits = new RaycastHit[64];
        static readonly List<Vector3> Offsets = new List<Vector3>();

        /// <summary>
        /// The pose touching the point from the normal's side: on a floor the bottom sphere sits on it; under a ceiling
        /// the top sphere is just below it; beside a wall the capsule's middle is at the contact's height.
        /// </summary>
        public static Vector3 ContactFeet(Vector3 point, Vector3 normal, float radius, float height, float gap)
        {
            normal = Normalised(normal);
            Vector3 sphere = point + normal * (radius + gap);
            if (normal.y >= .7f) return sphere - Vector3.up * radius;
            if (normal.y <= -.7f) return sphere - Vector3.up * (height - radius);
            return sphere - Vector3.up * (height * .5f);
        }

        /// <summary>Nothing (but ignored colliders) overlaps the capsule standing at <paramref name="feet"/>.</summary>
        public static bool IsFree(Vector3 feet, Settings s, Func<Collider, bool> ignore = null)
        {
            Axis(feet, s, out var a, out var b);
            int count = Physics.OverlapCapsuleNonAlloc(a, b, s.radius, Overlaps, s.mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (ignore == null || !ignore(Overlaps[i])) return false;
            return true;
        }

        /// <summary>Nothing (but ignored colliders) lies on the straight line from the contact to the capsule's axis.</summary>
        public static bool Reachable(Vector3 from, Vector3 feet, Settings s, Func<Collider, bool> ignore = null)
        {
            Axis(feet, s, out var a, out var b);
            Vector3 to = ClosestOnSegment(a, b, from);
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, s.mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (ignore == null || !ignore(Hits[i].collider)) return false;
            return true;
        }

        /// <summary>
        /// The nearest free, reachable pose around the touching pose: shifts away from the surface and along it, sorted
        /// by distance, never more than <see cref="Settings.maxRise"/> upward. False when there is none.
        /// </summary>
        public static bool TryFind(Vector3 point, Vector3 normal, Settings s, out Vector3 feet, Func<Collider, bool> ignore = null)
        {
            normal = Normalised(normal);
            Vector3 origin = ContactFeet(point, normal, s.radius, s.height, s.gap);
            Vector3 from = point + normal * Mathf.Min(.02f, s.gap * .5f);
            BuildOffsets(normal, s);
            foreach (var offset in Offsets)
            {
                Vector3 candidate = origin + offset;
                if (!IsFree(candidate, s, ignore)) continue;
                if (!Reachable(from, candidate, s, ignore)) continue;
                feet = candidate;
                return true;
            }
            feet = origin;
            return false;
        }

        static void BuildOffsets(Vector3 normal, Settings s)
        {
            Offsets.Clear();
            Tangents(normal, out var t1, out var t2);
            float step = Mathf.Max(.02f, s.step);
            int lateral = Mathf.CeilToInt(s.maxShift / step);
            for (int k = 0; k * step <= s.maxShift + 1e-4f; k++)
                for (int i = -lateral; i <= lateral; i++)
                    for (int j = -lateral; j <= lateral; j++)
                    {
                        Vector3 offset = normal * (k * step) + t1 * (i * step) + t2 * (j * step);
                        if (offset.magnitude > s.maxShift + 1e-4f) continue;
                        if (offset.y > s.maxRise + 1e-4f) continue;
                        Offsets.Add(offset);
                    }
            // Nearest first; at equal distance (to the millimetre), away from the surface before along it, lower before higher.
            Offsets.Sort((x, y) =>
            {
                int order = Mathf.RoundToInt(x.magnitude * 1000).CompareTo(Mathf.RoundToInt(y.magnitude * 1000));
                if (order != 0) return order;
                order = Mathf.RoundToInt(Vector3.Dot(y, normal) * 1000).CompareTo(Mathf.RoundToInt(Vector3.Dot(x, normal) * 1000));
                return order != 0 ? order : Mathf.RoundToInt(x.y * 1000).CompareTo(Mathf.RoundToInt(y.y * 1000));
            });
        }

        static void Axis(Vector3 feet, Settings s, out Vector3 a, out Vector3 b)
        {
            a = feet + Vector3.up * s.radius;
            b = feet + Vector3.up * Mathf.Max(s.radius, s.height - s.radius);
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0;
            return a + ab * t;
        }

        static void Tangents(Vector3 normal, out Vector3 t1, out Vector3 t2)
        {
            t1 = Vector3.Cross(normal, Vector3.up);
            if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.Cross(normal, Vector3.right);
            t1.Normalize();
            t2 = Vector3.Cross(normal, t1).normalized;
        }

        static Vector3 Normalised(Vector3 normal) => normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
    }
}
