using System.Collections;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Guide
{
    /// <summary>
    /// A see-through pointer the guide moves to show an action (hover, hold, drag). Lives in the guide's overlay, next
    /// to the spotlight, and takes no clicks. Positions are in its parent's local space, snapped to whole pixels.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class GhostPointer : MonoBehaviour
    {
        public Image image;
        public CanvasGroup group;
        public Sprite normal, grabbing;
        [Range(0, 1)] public float opacity = .85f;
        [Tooltip("Size while pressed, against the normal size.")]
        [Range(.5f, 1)] public float pressedScale = .85f;
        [Min(.01f)] public float fadeSeconds = .2f;

        RectTransform rect;
        float shown, wanted;
        SpringFloat press;
        Vector2 position;

        RectTransform Rect => rect ? rect : rect = (RectTransform)transform;
        public Vector2 Position => position;

        void Awake()
        {
            press.Snap(1);
            if (image) image.raycastTarget = false;
            if (group) { group.blocksRaycasts = false; group.interactable = false; group.alpha = 0; }
        }

        public void Show(bool on) => wanted = on ? 1 : 0;

        public void Place(Vector2 local)
        {
            position = local;
            Rect.anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
        }

        public void Press(bool down)
        {
            if (image && grabbing && normal) image.sprite = down ? grabbing : normal;
            press.Kick(down ? -4 : 4);
            pressed = down;
        }

        bool pressed;

        /// <summary>Glides to a point with a small arc, slow at both ends.</summary>
        public IEnumerator MoveTo(Vector2 local, float seconds, float arc = 10)
        {
            var from = position;
            float t = 0;
            while (t < 1)
            {
                t = seconds > 0 ? Mathf.Min(1, t + Time.unscaledDeltaTime / seconds) : 1;
                float k = Ease.InOutSine(t);
                Place(Vector2.Lerp(from, local, k) + Vector2.up * (Mathf.Sin(t * Mathf.PI) * arc));
                yield return null;
            }
        }

        public static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            shown = Mathf.MoveTowards(shown, wanted, dt / fadeSeconds);
            if (group) group.alpha = shown * opacity;
            press.Step(pressed ? pressedScale : 1, 16, .5f, dt);
            Rect.localScale = Vector3.one * press.Value;
        }
    }
}
