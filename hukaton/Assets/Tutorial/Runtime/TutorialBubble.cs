using Hakaton.DialogueUI;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// The goblin's speech: a linen plate (the button skin, not the NPC speech bubble) hanging right over the head of
    /// <see cref="speaker"/>, and «E — дальше» in the screen corner while a line waits.
    /// </summary>
    public sealed class TutorialBubble : MonoBehaviour
    {
        public CanvasGroup group;
        public DialogueBubbleLayout bubble;
        [Tooltip("«E — дальше» in the bottom-right corner while a line waits.")]
        public ContinueHint continueHint;
        [Tooltip("The drawing that speaks: the plate sits on the top of its bounds. Set by the tutorial.")]
        public Renderer speaker;
        [Tooltip("Gap between the top of the speaker's drawing and the bottom of the plate, canvas pixels.")]
        [Min(0)] public float headGap = 3;
        [Tooltip("The plate never leaves the screen by more than this margin, canvas pixels.")]
        [Min(0)] public float screenMargin = 8;

        Canvas canvas;

        public bool Visible => group && group.alpha > 0;

        public void Say(string line, bool waitForPlayer)
        {
            bubble.SetText(line);
            group.alpha = 1;
            if (continueHint) continueHint.Show(waitForPlayer);
            Place();
        }

        public void Hide()
        {
            if (group) group.alpha = 0;
        }

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            Hide();
        }

        void LateUpdate()
        {
            if (Visible) Place();
        }

        /// <summary>Bottom centre of the plate onto the top of the speaker's drawing, as the camera sees it now.</summary>
        void Place()
        {
            var body = bubble ? bubble.Body : null;
            var eye = Camera.main;
            if (!body || !speaker || !eye) return;
            Bounds bounds = speaker.bounds;
            Vector3 screen = eye.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            if (screen.z <= 0) return;
            float scale = canvas ? Mathf.Max(.0001f, canvas.scaleFactor) : 1;
            Vector2 size = body.rect.size;
            float width = Screen.width / scale, height = Screen.height / scale;
            float x = Mathf.Clamp(screen.x / scale, size.x * .5f + screenMargin, Mathf.Max(size.x * .5f + screenMargin, width - size.x * .5f - screenMargin));
            float y = Mathf.Clamp(screen.y / scale + headGap, screenMargin, Mathf.Max(screenMargin, height - size.y - screenMargin));
            body.anchorMin = body.anchorMax = Vector2.zero;
            body.pivot = new Vector2(.5f, 0);
            body.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        }
    }
}
