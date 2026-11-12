using UnityEngine;
using UnityEngine.EventSystems;

namespace Hakaton.Game
{
    /// <summary>A small springy feel for a non-linen button on the map (the cross): grows under the pointer, dips when pressed.</summary>
    public sealed class OutingMapPress : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Transform target;
        public float hoverScale = 1.12f, pressScale = .9f;
        [Min(1)] public float sharpness = 18;
        bool hover, down;

        public void OnPointerEnter(PointerEventData e) => hover = true;
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
        public void OnPointerDown(PointerEventData e) => down = true;
        public void OnPointerUp(PointerEventData e) => down = false;

        void OnDisable()
        {
            hover = down = false;
            if (target) target.localScale = Vector3.one;
        }

        void Update()
        {
            if (!target) return;
            float goal = down ? pressScale : hover ? hoverScale : 1;
            if (Mathf.Abs(target.localScale.x - goal) < 1e-4f) return;
            float s = Mathf.Lerp(target.localScale.x, goal, 1 - Mathf.Exp(-sharpness * Time.unscaledDeltaTime));
            target.localScale = new Vector3(s, s, 1);
        }
    }
}
