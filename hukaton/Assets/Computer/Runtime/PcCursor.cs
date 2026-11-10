using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// The PC's own mouse pointer, drawn inside the picture so it bends with the glass. Over the picture the system
    /// cursor is hidden; off the picture it comes back.
    /// </summary>
    public sealed class PcCursor : MonoBehaviour
    {
        public CrtRaycaster raycaster;
        public RectTransform canvasRect;
        public Image pointer;
        public Sprite arrow, grab;

        /// <summary>A card is being dragged: the pointer becomes a grabbing hand.</summary>
        public static bool Grabbing;
        bool hidSystem;

        void OnDisable()
        {
            if (pointer) pointer.enabled = false;
            ShowSystem(true);
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (!raycaster || !pointer || mouse == null) return;
            bool inside = raycaster.ToLocal(canvasRect, mouse.position.ReadValue(), out var local);
            pointer.enabled = inside;
            ShowSystem(!inside);
            if (!inside) return;
            var sprite = Grabbing && grab ? grab : arrow;
            if (pointer.sprite != sprite)
            {
                pointer.sprite = sprite;
                pointer.SetNativeSize();
            }
            // The hotspot is the arrow's tip (top-left); the hand is grabbed by its middle.
            var rect = pointer.rectTransform;
            rect.pivot = sprite == grab ? new Vector2(.5f, .6f) : new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
        }

        void ShowSystem(bool show)
        {
            if (show == !hidSystem) return;
            hidSystem = !show;
            Cursor.visible = show;
        }
    }
}
