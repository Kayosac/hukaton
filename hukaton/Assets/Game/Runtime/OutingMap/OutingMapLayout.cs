using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>One opened point of the map: where it was drawn and whether the player has looked at it yet.</summary>
    [Serializable]
    public sealed class OutingMapSiteSave
    {
        public string id;
        public float x, y;
        public bool seen;
    }

    /// <summary>The map as the player drew it: opened points with their places on the paper. Null in old saves.</summary>
    [Serializable]
    public sealed class OutingMapSave
    {
        public int seed;
        public List<OutingMapSiteSave> sites = new List<OutingMapSiteSave>();
    }

    /// <summary>
    /// Where a newly opened point goes: a short step from the point it grows from, inside the paper, away from the
    /// points already drawn. Positions are normalized to the map field (0..1 both ways); distances are measured in
    /// field heights, so a step looks the same length in any direction.
    /// </summary>
    public static class OutingMapLayout
    {
        public struct Rules
        {
            public Rect bounds;
            public float aspect, minStep, maxStep, clearance;

            public static Rules Default(float aspect) => new Rules
            { bounds = new Rect(.1f, .12f, .8f, .76f), aspect = aspect, minStep = .4f, maxStep = .52f, clearance = .34f };
        }

        public static Vector2 PlaceNear(Vector2 parent, IReadOnlyList<Vector2> taken, int seed, Rules rules)
        {
            var random = new System.Random(seed);
            Vector2 best = Clamp(parent + new Vector2(rules.minStep / rules.aspect, 0), rules.bounds);
            float bestScore = float.NegativeInfinity;
            for (int attempt = 0; attempt < 160; attempt++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                float step = Mathf.Lerp(rules.minStep, rules.maxStep, (float)random.NextDouble());
                var candidate = parent + new Vector2(Mathf.Cos(angle) * step / rules.aspect, Mathf.Sin(angle) * step);
                if (!rules.bounds.Contains(candidate)) continue;
                float nearest = float.PositiveInfinity;
                foreach (var other in taken) nearest = Mathf.Min(nearest, Distance(candidate, other, rules.aspect));
                if (nearest >= rules.clearance) return candidate;
                if (nearest > bestScore) { bestScore = nearest; best = candidate; }
            }
            return best;
        }

        /// <summary>Distance in field heights.</summary>
        public static float Distance(Vector2 a, Vector2 b, float aspect)
        {
            var d = a - b;
            d.x *= aspect;
            return d.magnitude;
        }

        /// <summary>A stable per-point seed: the same map grows the same way from the same save.</summary>
        public static int SeedFor(int mapSeed, string id)
        {
            unchecked
            {
                int hash = mapSeed * 31 + 17;
                foreach (char c in id ?? string.Empty) hash = hash * 31 + c;
                return hash;
            }
        }

        static Vector2 Clamp(Vector2 p, Rect r) => new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));

        /// <summary>
        /// A hand-drawn route between two points: a gentle bow to one side (fixed by the seed) with a slight
        /// wobble, sampled into points at equal spacing along it. Endpoints are trimmed so dashes stop short of the icons.
        /// </summary>
        public static List<Vector2> DashPoints(Vector2 from, Vector2 to, float spacing, float trim, int seed)
        {
            var points = new List<Vector2>();
            float length = Vector2.Distance(from, to);
            if (length <= trim * 2 + spacing) return points;
            var random = new System.Random(seed);
            var dir = (to - from) / length;
            var side = new Vector2(-dir.y, dir.x);
            float bow = length * Mathf.Lerp(.08f, .2f, (float)random.NextDouble()) * (random.Next(2) == 0 ? -1 : 1);
            float wobble = length * .025f, phase = (float)(random.NextDouble() * Math.PI * 2);
            Vector2 At(float t) => Vector2.Lerp(from, to, t) + side * (bow * 4 * t * (1 - t) + wobble * Mathf.Sin(phase + t * 9.4f) * Mathf.Sin(Mathf.PI * t));
            // Arc-length sampling of the curve.
            const int steps = 200;
            var curve = new Vector2[steps + 1];
            var along = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                curve[i] = At(i / (float)steps);
                if (i > 0) along[i] = along[i - 1] + Vector2.Distance(curve[i - 1], curve[i]);
            }
            float total = along[steps];
            int k = 0;
            for (float s = trim; s <= total - trim; s += spacing)
            {
                while (k < steps - 1 && along[k + 1] < s) k++;
                float t = Mathf.InverseLerp(along[k], along[k + 1], s);
                points.Add(Vector2.Lerp(curve[k], curve[k + 1], t));
            }
            return points;
        }
    }
}
