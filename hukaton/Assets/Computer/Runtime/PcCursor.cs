using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// The PC's mouse pointer. While the PC is on, the system cursor stays hidden and this pointer is always drawn: off
    /// the picture it waits at the picture's edge. It is drawn on the PC's full-screen display canvas, right over the
    /// glass at the real mouse point, scaled to whole screen pixels per PC pixel — not inside the PC's render texture:
    /// drawn into the texture it went missing in the Windows player (the texture path differs per graphics API), and
    /// on the display it does not depend on the PC camera, the supersampled texture or the tube shader at all.
    /// </summary>
    public sealed class PcCursor : MonoBehaviour
    {
        public CrtRaycaster raycaster;
        public RectTransform canvasRect;
        [Tooltip("The pointer inside the PC's canvas (kept hidden; its sprites and size are the reference).")]
        public Image pointer;
        public Sprite arrow, grab;

        /// <summary>A card is being dragged: the pointer becomes a grabbing hand.</summary>
        public static bool Grabbing;
        Image overlay;
        Vector2 last;
        bool placed, reported;

        /// <summary>The pointer actually drawn (on the display canvas, over the glass).</summary>
        public Image Overlay => overlay;

        void OnEnable() => placed = reported = false;

        void OnDisable()
        {
            if (pointer) pointer.enabled = false;
            if (overlay) overlay.enabled = false;
            // The control gate decides what comes next (it captures the cursor again when the PC closes).
            Cursor.visible = true;
        }

        void OnDestroy()
        {
            if (overlay) Destroy(overlay.gameObject);
        }

        void LateUpdate()
        {
            if (!raycaster || !raycaster.screen || !raycaster.screen.picture) return;
            if (Cursor.visible) Cursor.visible = false;
            if (pointer && pointer.enabled) pointer.enabled = false;
            var picture = raycaster.screen.picture;
            var display = picture.canvas ? picture.canvas.rootCanvas : null;
            if (!display) return;
            if (!overlay) overlay = CreateOverlay(display);
            if (overlay.transform.GetSiblingIndex() != overlay.transform.parent.childCount - 1) overlay.transform.SetAsLastSibling();

            var mouse = Mouse.current;
            if (mouse != null)
            {
                var point = mouse.position.ReadValue();
                if (!float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.x) && !float.IsInfinity(point.y))
                {
                    bool on = raycaster.ToPicture(point, out _);
                    last = on ? point : Edge(point, picture);
                    placed = true;
                    Report(point, on, picture);
                }
            }
            if (!placed) { last = Centre(picture); placed = true; }

            var sprite = Grabbing && grab ? grab : arrow;
            overlay.sprite = sprite;
            overlay.enabled = sprite;
            if (!sprite) return;
            float scale = Mathf.Max(1, Mathf.Round(ScreenPixelsPerPcPixel(picture)));
            float toCanvas = 1 / Mathf.Max(display.scaleFactor, 1e-4f);
            var rect = overlay.rectTransform;
            rect.sizeDelta = sprite.rect.size * scale * toCanvas;
            // The hotspot is the arrow's tip (top-left); the hand is grabbed by its middle.
            rect.pivot = sprite == grab ? new Vector2(.5f, .6f) : new Vector2(0, 1);
            var screen = new Vector2(Mathf.Round(last.x), Mathf.Round(last.y));
            var displayRect = (RectTransform)display.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(displayRect, screen, null, out var local))
                rect.localPosition = new Vector3(local.x, local.y, 0);
        }

        Image CreateOverlay(Canvas display)
        {
            var go = new GameObject("PC pointer (over the glass)", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = display.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(display.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = false;
            image.enabled = false;
            return image;
        }

        /// <summary>How many real screen pixels one PC canvas pixel takes in the middle of the glass.</summary>
        float ScreenPixelsPerPcPixel(Graphic picture)
        {
            if (!canvasRect) return 1;
            var centre = Centre(picture);
            const float step = 64;
            if (!raycaster.ToLocal(canvasRect, centre, out var a) || !raycaster.ToLocal(canvasRect, centre + new Vector2(0, step), out var b))
                return 1;
            float d = Mathf.Abs(b.y - a.y);
            return d > 1e-3f && !float.IsNaN(d) ? step / d : 1;
        }

        static Vector2 Centre(Graphic picture)
        {
            var rect = picture.rectTransform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        /// <summary>
        /// The last screen point on the glass along the line from the picture's middle to <paramref name="outside"/>;
        /// if even the middle is not on the glass (the tube still opening), the point kept inside the picture's rect.
        /// </summary>
        Vector2 Edge(Vector2 outside, Graphic picture)
        {
            var inside = Centre(picture);
            if (!raycaster.ToPicture(inside, out _))
            {
                var corners = new Vector3[4];
                picture.rectTransform.GetWorldCorners(corners);
                return new Vector2(Mathf.Clamp(outside.x, corners[0].x, corners[2].x - 1), Mathf.Clamp(outside.y, corners[0].y + 1, corners[2].y));
            }
            for (int i = 0; i < 16; i++)
            {
                var middle = (inside + outside) * .5f;
                if (raycaster.ToPicture(middle, out _)) inside = middle; else outside = middle;
            }
            return inside;
        }

        /// <summary>One line in the player log per PC session: what the pointer saw (for a platform that misbehaves).</summary>
        void Report(Vector2 mouse, bool onPicture, Graphic picture)
        {
            if (reported || Application.isEditor) return;
            reported = true;
            Debug.Log("[PcCursor] " + Application.platform + " " + SystemInfo.graphicsDeviceType + " screen " + Screen.width + "x" + Screen.height
                + " mouse " + mouse + " onPicture " + onPicture + " centre " + Centre(picture) + " centreOnPicture " + raycaster.ToPicture(Centre(picture), out _)
                + " pxPerPcPx " + ScreenPixelsPerPcPixel(picture) + " displayScale " + (picture.canvas ? picture.canvas.rootCanvas.scaleFactor : 0)
                + " cursor visible " + Cursor.visible + " lock " + Cursor.lockState);
        }
    }
}
