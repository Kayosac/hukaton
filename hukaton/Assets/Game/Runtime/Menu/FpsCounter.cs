using System.Globalization;
using Hakaton.Feel;
using TMPro;
using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>
    /// «Счётчик FPS» from the settings: a small "60 FPS" readout at the top right, on its own canvas above every other one
    /// (menus, the PC, fades). Off by default; it follows <see cref="GameSettings.ShowFps"/> by switching its canvas, so this
    /// component stays active and keeps listening. Counts real frames over <see cref="refreshSeconds"/> of unscaled time,
    /// so the pause (time scale 0) does not stop it. The readout sits just below the footprint of the shop's shift clock
    /// (<see cref="clearOf"/>), the same place in the shop, on outings and in the menus: it never covers the clock or the
    /// outing's pickup rows, which share that corner.
    /// </summary>
    public sealed class FpsCounter : MonoBehaviour
    {
        [Tooltip("Own Screen Space Overlay canvas (pixel perfect, sort order 200). Enabled only while the setting is on.")]
        public Canvas canvas;
        [Tooltip("Whole-number magnification of the reference layout (736 × 414 units, like the HUDs) the readout lives in.")]
        public ReferenceUiScale layout;
        [Tooltip("The readout block (text and its shadow), anchored to the layout's top-right corner.")]
        public RectTransform readout;
        public TMP_Text label;
        [Tooltip("Distance from the top-right corner in reference units when there is no clock to clear.")]
        public Vector2 cornerInset = new Vector2(8, 8);
        [Tooltip("The shop's shift clock, top right of the shop HUD. The readout always sits below its footprint, "
            + "whether the clock is showing or not, so it keeps one place everywhere.")]
        public RectTransform clearOf;
        [Tooltip("The reference layout the clock lives in (the shop HUD's): converts the clock's units into this layout's.")]
        public ReferenceUiScale clearOfLayout;
        [Tooltip("Reference units between the clock's lower edge and the readout.")]
        [Min(0)] public float gapBelow = 4;
        [Tooltip("Seconds of real time per reading: the readout shows the average frame rate over that window.")]
        [Min(.05f)] public float refreshSeconds = .5f;

        static readonly string[] Readouts = new string[1000];
        readonly Vector3[] corners = new Vector3[4];
        bool shown;
        int frames;
        float seconds;

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            shown = false;
            Apply();
        }

        void OnDisable() => GameSettings.Changed -= Apply;

        void Apply()
        {
            if (!canvas) return;
            bool show = GameSettings.ShowFps;
            canvas.enabled = show;
            if (show == shown) return;
            shown = show;
            frames = 0;
            seconds = 0;
            if (!show) return;
            // A first reading from this frame alone (none after a hitch); the averaged one follows after refreshSeconds.
            float delta = Time.unscaledDeltaTime;
            if (label) label.text = delta > 0 && delta < refreshSeconds ? Readout(Average(1, delta)) : string.Empty;
            Place();
        }

        void Update()
        {
            if (!shown) return;
            if (Sample(ref frames, ref seconds, Time.unscaledDeltaTime, refreshSeconds, out int fps) && label)
                label.text = Readout(fps);
        }

        void LateUpdate()
        {
            if (shown) Place();
        }

        void Place()
        {
            if (!readout) return;
            var inset = Inset();
            var position = new Vector2(-inset.x, -inset.y);
            if (readout.anchoredPosition != position) readout.anchoredPosition = position;
        }

        /// <summary>
        /// Reference units from the top-right corner to the readout: right under the clock's footprint, its right edge in
        /// line with the clock's; the corner inset when there is no clock to clear.
        /// </summary>
        Vector2 Inset()
        {
            if (!clearOf || !clearOfLayout || !layout || !canvas) return cornerInset;
            var hud = (RectTransform)clearOfLayout.transform;
            var hudRect = hud.rect;
            clearOf.GetWorldCorners(corners);
            // The clock's lower and right edges, measured from the top-right of its own layout in that layout's units:
            // this holds while the shop HUD's canvas is off (on outings) and its world size is stale.
            float depth = hudRect.yMax - hud.InverseTransformPoint(corners[0]).y;
            float right = hudRect.xMax - hud.InverseTransformPoint(corners[2]).x;
            if (!(depth > 0 && depth < 10000) || !(right >= 0 && right < 10000)) return cornerInset;
            var screen = ((RectTransform)canvas.transform).rect.size;
            float units = clearOfLayout.ScaleFor(screen) / layout.ScaleFor(screen);
            // Whole units: with a whole-number magnification the text stays on the pixel grid.
            return new Vector2(Mathf.Round(right * units), Mathf.Max(cornerInset.y, Mathf.Ceil(depth * units + gapBelow - .01f)));
        }

        /// <summary>Average frame rate of <paramref name="frameCount"/> frames over <paramref name="elapsed"/> real seconds,
        /// rounded to whole frames; 0 for an empty window.</summary>
        public static int Average(int frameCount, float elapsed) =>
            frameCount > 0 && elapsed > 0 ? Mathf.RoundToInt(frameCount / elapsed) : 0;

        /// <summary>
        /// Adds one frame of <paramref name="delta"/> real seconds to the running window. Once the window spans
        /// <paramref name="window"/> seconds it hands out its average in <paramref name="fps"/>, returns true and starts over.
        /// </summary>
        public static bool Sample(ref int frameCount, ref float elapsed, float delta, float window, out int fps)
        {
            frameCount++;
            elapsed += Mathf.Max(0, delta);
            if (elapsed < window)
            {
                fps = 0;
                return false;
            }
            fps = Average(frameCount, elapsed);
            frameCount = 0;
            elapsed = 0;
            return true;
        }

        /// <summary>"60 FPS". Cached per value below 1000, so steady readings allocate nothing.</summary>
        public static string Readout(int fps)
        {
            if (fps < 0) fps = 0;
            if (fps >= Readouts.Length) return fps.ToString(CultureInfo.InvariantCulture) + " FPS";
            return Readouts[fps] ??= fps.ToString(CultureInfo.InvariantCulture) + " FPS";
        }
    }
}
