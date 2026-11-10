using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// An XP scrollbar for a <see cref="ScrollRect"/>: the thumb follows the view and can be dragged; clicking the
    /// track pages. Works through the curved glass (<see cref="CrtRaycaster.ToLocal"/>).
    /// </summary>
    public sealed class PcScrollThumb : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        public ScrollRect scroll;
        public RectTransform track, thumb;
        [Min(8)] public float minThumb = 18;
        float grabOffset;

        void LateUpdate()
        {
            if (!scroll || !track || !thumb) return;
            var content = scroll.content;
            var view = scroll.viewport ? scroll.viewport : (RectTransform)scroll.transform;
            float visible = Mathf.Clamp01(view.rect.height / Mathf.Max(1, content.rect.height));
            bool needed = visible < .999f;
            thumb.gameObject.SetActive(needed);
            if (!needed) return;
            float height = Mathf.Max(minThumb, Mathf.Round(track.rect.height * visible));
            float travel = track.rect.height - height;
            float y = Mathf.Round(travel * (1 - scroll.verticalNormalizedPosition));
            thumb.anchorMin = new Vector2(0, 1);
            thumb.anchorMax = new Vector2(1, 1);
            thumb.pivot = new Vector2(.5f, 1);
            thumb.sizeDelta = new Vector2(0, height);
            thumb.anchoredPosition = new Vector2(0, -y);
        }

        bool Local(PointerEventData e, out float y)
        {
            y = 0;
            var raycaster = CrtRaycaster.Current;
            if (!raycaster) return false;
            raycaster.ToLocal(track, e.position, out var local);
            y = track.rect.yMax - local.y;
            return true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Local(e, out float y)) return;
            float top = -thumb.anchoredPosition.y, bottom = top + thumb.rect.height;
            grabOffset = y - top;
            if (y >= top && y <= bottom) return;
            // Page towards the click.
            float page = Mathf.Clamp01(thumb.rect.height / Mathf.Max(1, track.rect.height - thumb.rect.height));
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + (y < top ? page : -page));
            grabOffset = thumb.rect.height / 2;
        }

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (!Local(e, out float y)) return;
            float travel = track.rect.height - thumb.rect.height;
            if (travel <= 0) return;
            scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01((y - grabOffset) / travel);
        }
    }
}
