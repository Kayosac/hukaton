using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Guide
{
    /// <summary>
    /// The guide's light: everything but the lit place is dimmed, and a frame pulses around it. One graphic draws the
    /// dim (four quads around the hole) and the frame, snapped to whole canvas pixels. It takes the pointer everywhere
    /// except, when <see cref="PassThrough"/> is on, inside the hole: there clicks reach the place itself.
    /// Stretch it over the canvas (or over the PC's screen).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class GuideSpotlight : MaskableGraphic, ICanvasRaycastFilter
    {
        [Header("Dim")]
        public Color dim = new Color(.02f, .015f, .04f, .62f);
        [Min(.01f)] public float fadeSeconds = .25f;
        [Tooltip("How fast the hole moves to the next place.")]
        [Min(1)] public float follow = 12;

        [Header("Frame")]
        public Color frameColour = new Color(1f, .78f, .3f, 1);
        [Tooltip("Frame line, in canvas pixels.")]
        [Min(0)] public float frameWidth = 2;
        [Tooltip("A softer second line outside the frame, in canvas pixels (0: none).")]
        [Min(0)] public float glowWidth = 2;
        [Range(0, 1)] public float glowAlpha = .35f;
        [Tooltip("Gap between the lit place and the frame line.")]
        [Min(0)] public float gap = 1;
        [Min(0)] public float pulsesPerSecond = 1.1f;
        [Range(0, 1)] public float pulseLow = .45f;
        [Tooltip("The frame breathes out by this many whole pixels at the top of the pulse.")]
        [Min(0)] public int breathe = 1;

        /// <summary>Clicks inside the hole reach what is under it (a «сделай сам» step).</summary>
        public bool PassThrough { get; set; }
        /// <summary>The overlay is up: it takes the pointer (all of it, or all but the hole).</summary>
        public bool Blocking { get; private set; }
        public bool HasHole => target || fixedHole.HasValue;
        /// <summary>The lit place in this rect's local space, as drawn now (null: nothing lit).</summary>
        public Rect? Hole => HasHole ? hole : (Rect?)null;

        RectTransform target;
        float targetPadding;
        Rect? fixedHole;
        Rect hole;
        float holeShown; // 0: no hole (all dim), 1: hole open
        float shown, wanted;
        bool snapNext = true;
        static readonly Vector3[] Corners = new Vector3[4];

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = true;
        }

        /// <summary>Fade the light in (taking the pointer) or out (letting it go at once).</summary>
        public void Show(bool on)
        {
            wanted = on ? 1 : 0;
            Blocking = on;
            if (on && shown <= 0) snapNext = true;
            if (!on) PassThrough = false;
            SetVerticesDirty();
        }

        /// <summary>Light this rect (followed every frame, so it may move or scroll); null dims everything.</summary>
        public void Focus(RectTransform rect, float padding = 4)
        {
            target = rect;
            targetPadding = padding;
            fixedHole = null;
        }

        /// <summary>Light a rect given in this graphic's local space.</summary>
        public void Focus(Rect local)
        {
            target = null;
            fixedHole = local;
        }

        public void Clear()
        {
            target = null;
            fixedHole = null;
        }

        /// <summary>A rect anywhere (another canvas too) in this graphic's local space.</summary>
        public Rect LocalRectOf(RectTransform rect, float padding = 0)
        {
            rect.GetWorldCorners(Corners);
            Vector2 a = rectTransform.InverseTransformPoint(Corners[0]);
            Vector2 b = rectTransform.InverseTransformPoint(Corners[2]);
            var min = Vector2.Min(a, b) - Vector2.one * padding;
            var max = Vector2.Max(a, b) + Vector2.one * padding;
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>The centre of a rect in this graphic's local space (where a ghost pointer goes).</summary>
        public Vector2 LocalCentreOf(RectTransform rect) => LocalRectOf(rect).center;

        Rect? Wanted()
        {
            if (target) return target.gameObject.activeInHierarchy ? LocalRectOf(target, targetPadding) : (Rect?)null;
            return fixedHole;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            shown = Mathf.MoveTowards(shown, wanted, dt / fadeSeconds);
            var aim = Wanted();
            if (aim.HasValue)
            {
                if (snapNext || holeShown <= 0) hole = aim.Value;
                else
                {
                    float k = Ease.Damp(follow, dt);
                    hole = Rect.MinMaxRect(Mathf.Lerp(hole.xMin, aim.Value.xMin, k), Mathf.Lerp(hole.yMin, aim.Value.yMin, k),
                        Mathf.Lerp(hole.xMax, aim.Value.xMax, k), Mathf.Lerp(hole.yMax, aim.Value.yMax, k));
                }
                holeShown = Mathf.MoveTowards(holeShown, 1, dt / fadeSeconds);
            }
            else holeShown = Mathf.MoveTowards(holeShown, 0, dt / fadeSeconds);
            snapNext = false;
            if (shown > 0 || wanted > 0) SetVerticesDirty();
        }

        static float Snap(float v) => Mathf.Round(v);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (shown <= 0) return;
            var r = rectTransform.rect;
            var dimColour = dim;
            dimColour.a *= Ease.OutCubic(shown);
            // The hole grows out of its centre as it opens and shrinks back when nothing is lit.
            var c = hole.center;
            var size = hole.size * Ease.OutCubic(holeShown);
            var h = Rect.MinMaxRect(Snap(c.x - size.x * .5f), Snap(c.y - size.y * .5f), Snap(c.x + size.x * .5f), Snap(c.y + size.y * .5f));
            h.xMin = Mathf.Clamp(h.xMin, r.xMin, r.xMax); h.xMax = Mathf.Clamp(h.xMax, r.xMin, r.xMax);
            h.yMin = Mathf.Clamp(h.yMin, r.yMin, r.yMax); h.yMax = Mathf.Clamp(h.yMax, r.yMin, r.yMax);
            if (holeShown <= 0 || h.width < 1 || h.height < 1)
            {
                Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, dimColour);
                return;
            }
            Quad(vh, r.xMin, h.yMax, r.xMax, r.yMax, dimColour);
            Quad(vh, r.xMin, r.yMin, r.xMax, h.yMin, dimColour);
            Quad(vh, r.xMin, h.yMin, h.xMin, h.yMax, dimColour);
            Quad(vh, h.xMax, h.yMin, r.xMax, h.yMax, dimColour);

            // The frame: a pulsing line around the hole and a softer one outside it.
            float pulse = .5f - .5f * Mathf.Cos(Time.unscaledTime * pulsesPerSecond * Mathf.PI * 2);
            float alpha = Mathf.Lerp(pulseLow, 1, pulse) * Ease.OutCubic(shown) * Ease.OutCubic(holeShown);
            float grow = gap + Mathf.Round(pulse * breathe);
            var line = frameColour;
            line.a *= alpha;
            Border(vh, Grow(h, grow), frameWidth, line);
            if (glowWidth > 0)
            {
                var glow = frameColour;
                glow.a *= alpha * glowAlpha;
                Border(vh, Grow(h, grow + frameWidth), glowWidth, glow);
            }
        }

        static Rect Grow(Rect rect, float by) => Rect.MinMaxRect(rect.xMin - by, rect.yMin - by, rect.xMax + by, rect.yMax + by);

        static void Border(VertexHelper vh, Rect inner, float width, Color colour)
        {
            if (width <= 0) return;
            var o = Grow(inner, width);
            Quad(vh, o.xMin, inner.yMax, o.xMax, o.yMax, colour);
            Quad(vh, o.xMin, o.yMin, o.xMax, inner.yMin, colour);
            Quad(vh, o.xMin, inner.yMin, inner.xMin, inner.yMax, colour);
            Quad(vh, inner.xMax, inner.yMin, o.xMax, inner.yMax, colour);
        }

        static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color colour)
        {
            if (x1 <= x0 || y1 <= y0) return;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), colour, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), colour, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), colour, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), colour, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!Blocking) return false;
            if (!PassThrough || !HasHole) return true;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var local)) return true;
            return !hole.Contains(local);
        }
    }
}
