using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// The PC's own mouse pointer, drawn inside the picture so it bends with the glass. While the PC is on, the system
    /// cursor stays hidden and this pointer is always drawn: off the picture it waits at the picture's edge. (Hiding the
    /// system cursor over the picture and showing it again off it left the standalone player with no cursor at all.)
    /// </summary>
    public sealed class PcCursor : MonoBehaviour
    {
        public CrtRaycaster raycaster;
        public RectTransform canvasRect;
        public Image pointer;
        public Sprite arrow, grab;

        /// <summary>A card is being dragged: the pointer becomes a grabbing hand.</summary>
        public static bool Grabbing;
        Vector2 last;
        bool placed;

        void OnEnable() => placed = false;

        void OnDisable()
        {
            if (pointer) pointer.enabled = false;
            // The control gate decides what comes next (it captures the cursor again when the PC closes).
            Cursor.visible = true;
        }

        void LateUpdate()
        {
            if (!raycaster || !pointer || !canvasRect) return;
            if (Cursor.visible) Cursor.visible = false;
            var mouse = Mouse.current;
            var r = canvasRect.rect;
            if (mouse != null)
            {
                var point = mouse.position.ReadValue();
                if (!raycaster.ToPicture(point, out _)) point = Edge(point);
                raycaster.ToLocal(canvasRect, point, out var local);
                if (!float.IsNaN(local.x) && !float.IsNaN(local.y) && !float.IsInfinity(local.x) && !float.IsInfinity(local.y))
                {
                    last = new Vector2(Mathf.Clamp(local.x, r.xMin, r.xMax - 1), Mathf.Clamp(local.y, r.yMin + 1, r.yMax));
                    placed = true;
                }
            }
            if (!placed) { last = r.center; placed = true; }
            pointer.enabled = true;
            var sprite = Grabbing && grab ? grab : arrow;
            if (pointer.sprite != sprite)
            {
                pointer.sprite = sprite;
                pointer.SetNativeSize();
            }
            // The hotspot is the arrow's tip (top-left); the hand is grabbed by its middle.
            var rect = pointer.rectTransform;
            rect.pivot = sprite == grab ? new Vector2(.5f, .6f) : new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(Mathf.Round(last.x), Mathf.Round(last.y));
        }

        /// <summary>The last screen point on the glass along the line from the picture's middle to <paramref name="outside"/>.</summary>
        Vector2 Edge(Vector2 outside)
        {
            var screen = raycaster.screen;
            if (!screen || !screen.picture) return outside;
            var rect = screen.picture.rectTransform;
            Vector2 inside = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            if (!raycaster.ToPicture(inside, out _)) return outside;
            for (int i = 0; i < 16; i++)
            {
                var middle = (inside + outside) * .5f;
                if (raycaster.ToPicture(middle, out _)) inside = middle; else outside = middle;
            }
            return inside;
        }
    }
}
