using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The goblin's speech over the game view: the stitched linen bubble and «E — дальше» while a line waits.
    /// It stays where it is authored; while it is told to follow a speaker (the goblin's comments during the shop
    /// tutorial's trading, so they never cover the visitor's lines) it hangs right over his head instead.
    /// </summary>
    public sealed class TutorialBubble : MonoBehaviour
    {
        public CanvasGroup group;
        public DialogueBubbleLayout bubble;
        [Tooltip("«E — дальше» in the bottom-right corner while a line waits.")]
        public ContinueHint continueHint;
        [Tooltip("Gap between the speaker's head and the bottom of the plate while it follows him (canvas units).")]
        [Min(0)] public float headGap = 10;
        [Tooltip("Size of the plate while it follows a speaker (1: as authored).")]
        [Min(.1f)] public float followScale = 1.35f;
        [Tooltip("While following, the plate sits this share of its width to the right of the speaker's head.")]
        public float followShift = .3f;

        Renderer speaker;
        Camera view;
        Canvas canvas;
        RectTransform plate;
        Vector2 rest;
        Vector3 restScale = Vector3.one;

        public bool Visible => group && group.alpha > 0;

        /// <summary>A press while the line is still typing only finishes the line.</summary>
        public bool SkipTyping() => bubble && bubble.SkipTyping();

        public void Say(string line, bool waitForPlayer)
        {
            bubble.SetText(line);
            group.alpha = 1;
            if (continueHint) continueHint.Show(waitForPlayer);
        }

        public void Hide()
        {
            if (group) group.alpha = 0;
            if (bubble) bubble.ForgetLine();
        }

        /// <summary>
        /// Hangs the plate right over <paramref name="body"/> as <paramref name="camera"/> sees it (null: back to its authored place).
        /// </summary>
        public void FollowOverHead(Renderer body, Camera camera)
        {
            speaker = body;
            view = camera;
        }

        void Awake()
        {
            plate = bubble ? (RectTransform)bubble.transform : null;
            if (plate) { rest = plate.anchoredPosition; restScale = plate.localScale; }
            Hide();
        }

        void LateUpdate()
        {
            if (!plate) return;
            plate.anchoredPosition = rest;
            bool following = Visible && speaker && speaker.gameObject.activeInHierarchy && view;
            var size = restScale * (following ? followScale : 1);
            if (plate.localScale != size) plate.localScale = size;
            if (!following) return;
            var parent = plate.parent;
            float scale = parent ? parent.lossyScale.x : 1;
            if (scale <= 0) return;
            var bounds = speaker.bounds;
            Vector3 head = view.WorldToViewportPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            if (head.z <= 0) return;
            // Measured in the overlay canvas's own space (its world corners): the camera's pixel size can differ from it
            // (a HiDPI Game View), the viewport fraction cannot.
            var root = canvas ? canvas : canvas = plate.GetComponentInParent<Canvas>().rootCanvas;
            ((RectTransform)root.transform).GetWorldCorners(Corners);
            Vector2 low = Corners[0], high = Corners[2];
            float headX = Mathf.Lerp(low.x, high.x, head.x), headY = Mathf.Lerp(low.y, high.y, head.y);
            var frame = bubble.Body ? bubble.Body : plate;
            frame.GetWorldCorners(Corners);
            float width = Corners[2].x - Corners[0].x, height = Corners[2].y - Corners[0].y;
            float gap = headGap * scale, margin = 8 * scale;
            float x = Mathf.Clamp(headX + width * followShift, low.x + margin + width / 2, high.x - margin - width / 2);
            float bottom = Mathf.Clamp(headY + gap, low.y + margin, high.y - margin - height);
            var shift = new Vector2(x - (Corners[0].x + Corners[2].x) / 2, bottom - Corners[0].y) / scale;
            plate.anchoredPosition = rest + new Vector2(Mathf.Round(shift.x), Mathf.Round(shift.y));
        }

        static readonly Vector3[] Corners = new Vector3[4];
    }
}
