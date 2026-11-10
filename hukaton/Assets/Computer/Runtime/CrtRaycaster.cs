using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// Lets the mouse work on the PC's canvas, which is drawn into a texture and shown full screen through a bulging
    /// CRT: the pointer is followed through the bulge (<see cref="CrtScreen.TryPicturePixel"/>) and the canvas is
    /// hit-tested at that pixel. Event positions stay in real screen pixels; PC widgets convert them with <see cref="ToLocal"/>.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class CrtRaycaster : BaseRaycaster
    {
        public CrtScreen screen;
        [Tooltip("Just above the PC's own full-screen display canvas (its black bars block the shop HUD below).")]
        public int sortPriority = 51;

        Canvas canvas;
        readonly List<Graphic> hits = new List<Graphic>();
        static CrtRaycaster current;

        /// <summary>The raycaster of the PC that is on (one at a time).</summary>
        public static CrtRaycaster Current => current;

        Canvas Canvas => canvas ? canvas : canvas = GetComponent<Canvas>();
        public override Camera eventCamera => screen ? screen.contentCamera : null;
        public override int sortOrderPriority => sortPriority;
        public override int renderOrderPriority => sortPriority;

        protected override void OnEnable()
        {
            base.OnEnable();
            current = this;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (current == this) current = null;
        }

        /// <summary>Real screen point → pixel on the PC's canvas camera. False off the picture.</summary>
        public bool ToPicture(Vector2 screenPoint, out Vector2 pixel)
        {
            pixel = default;
            return screen && screen.TryPicturePixel(screenPoint, out pixel);
        }

        /// <summary>Real screen point → local point in a rect on the PC's canvas.</summary>
        public bool ToLocal(RectTransform rect, Vector2 screenPoint, out Vector2 local)
        {
            local = default;
            bool inside = ToPicture(screenPoint, out var pixel);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pixel, eventCamera, out local);
            return inside;
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            var camera = eventCamera;
            if (!camera || !camera.isActiveAndEnabled || !ToPicture(eventData.position, out var pixel)) return;
            var graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(Canvas);
            hits.Clear();
            for (int i = 0; i < graphics.Count; i++)
            {
                var g = graphics[i];
                if (!g.raycastTarget || g.canvasRenderer.cull || g.depth == -1) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(g.rectTransform, pixel, camera, g.raycastPadding)) continue;
                if (!g.Raycast(pixel, camera)) continue;
                hits.Add(g);
            }
            hits.Sort((a, b) => b.depth.CompareTo(a.depth));
            for (int i = 0; i < hits.Count; i++)
            {
                var g = hits[i];
                var go = g.gameObject;
                float distance = Vector3.Dot(g.rectTransform.position - camera.transform.position, camera.transform.forward);
                resultAppendList.Add(new RaycastResult
                {
                    gameObject = go,
                    module = this,
                    distance = distance,
                    screenPosition = eventData.position,
                    displayIndex = eventData.displayIndex,
                    index = resultAppendList.Count,
                    depth = g.depth,
                    sortingLayer = Canvas.sortingLayerID,
                    sortingOrder = Canvas.sortingOrder,
                    worldPosition = g.rectTransform.position,
                    worldNormal = -g.rectTransform.forward
                });
            }
        }
    }
}
