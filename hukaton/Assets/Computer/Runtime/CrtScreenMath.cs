using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>The CRT glass's shape: how a point on the monitor's screen quad maps into the PC's picture.</summary>
    [System.Serializable]
    public struct CrtShape
    {
        [Tooltip("Width / height of the glass quad.")] public float quadAspect;
        [Tooltip("Width / height of the picture (the render texture).")] public float contentAspect;
        [Tooltip("Share of the glass height the picture fills.")] public float fill;
        [Tooltip("How much the picture bulges.")] public float curve;
        [Tooltip("Rounded picture corners, in picture units.")] public float corner;

        public static CrtShape Default => new CrtShape { quadAspect = 4f / 3, contentAspect = 4f / 3, fill = .92f, curve = .1f, corner = .05f };
    }

    /// <summary>
    /// The same mapping as <c>CRT Screen.shader</c> (Centred, ContentUV). Keep both in step: the mouse picks what the
    /// shader shows.
    /// </summary>
    public static class CrtScreenMath
    {
        /// <summary>Glass position (0..1 over the quad) → picture UV (0..1 inside the picture; outside means the black border).</summary>
        public static Vector2 ContentUV(Vector2 glass, CrtShape shape)
        {
            var c = glass - new Vector2(.5f, .5f);
            c.x *= shape.quadAspect / shape.contentAspect;
            c *= 2 / shape.fill;
            c *= 1 + shape.curve * Vector2.Dot(c, c);
            return c * .5f + new Vector2(.5f, .5f);
        }

        /// <summary>Inside the picture, rounded corners included.</summary>
        public static bool InsidePicture(Vector2 uv, CrtShape shape)
        {
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return false;
            var e = new Vector2(Mathf.Abs(uv.x - .5f), Mathf.Abs(uv.y - .5f)) - Vector2.one * (.5f - shape.corner);
            return new Vector2(Mathf.Max(e.x, 0), Mathf.Max(e.y, 0)).magnitude <= shape.corner;
        }

        /// <summary>
        /// Where a point lies on a four-cornered quad (bl, br, tr, tl, all in one plane): (0,0) at bl, (1,1) at tr.
        /// Inverse bilinear, so slightly crooked screens map exactly.
        /// </summary>
        public static bool InverseBilinear(Vector2 p, Vector2 bl, Vector2 br, Vector2 tr, Vector2 tl, out Vector2 uv)
        {
            Vector2 e = br - bl, f = tl - bl, g = bl - br + tr - tl, h = p - bl;
            float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
            float k2 = Cross(g, f), k1 = Cross(e, f) + Cross(h, g), k0 = Cross(h, e);
            float v;
            if (Mathf.Abs(k2) < 1e-7f)
            {
                if (Mathf.Abs(k1) < 1e-9f) { uv = default; return false; }
                v = -k0 / k1;
            }
            else
            {
                float w = k1 * k1 - 4 * k0 * k2;
                if (w < 0) { uv = default; return false; }
                w = Mathf.Sqrt(w);
                float v1 = (-k1 - w) / (2 * k2), v2 = (-k1 + w) / (2 * k2);
                v = Mathf.Abs(v1 - .5f) < Mathf.Abs(v2 - .5f) ? v1 : v2;
            }
            Vector2 denom = e + g * v;
            float u = Mathf.Abs(denom.x) > Mathf.Abs(denom.y) ? (h.x - f.x * v) / denom.x : (h.y - f.y * v) / denom.y;
            uv = new Vector2(u, v);
            return !float.IsNaN(u) && !float.IsNaN(v);
        }
    }
}
