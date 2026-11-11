using Hakaton.DialogueUI;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>An authored world canvas attached to the drawing's plane, without camera or input ownership.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(210)]
    public sealed class CreatureSpeechBubble : MonoBehaviour
    {
        public CreatureSpriteView spritePlane;
        public Canvas bubbleCanvas;
        public CanvasGroup content;
        public DialogueBubbleLayout layout;
        [Tooltip("Gap from the authored head height to the bottom of the speech bubble, in sprite metres.")]
        [Min(0)] public float headGap = .23f;
        [Tooltip("World metres per canvas pixel. Text remains at scale one inside the world canvas.")]
        [Min(.001f)] public float metresPerPixel = .008f;

        public bool IsVisible => content && content.alpha > 0;
        public string Text => layout && layout.Dialogue ? layout.Dialogue.text : string.Empty;

        void Awake() => Hide();
        void OnDisable() => Hide();

        public void Show(string text)
        {
            if (!content || !layout || string.IsNullOrWhiteSpace(text)) { Hide(); return; }
            layout.SetText(text);
            content.alpha = 1;
            FollowSprite();
        }

        public void Hide()
        {
            if (content) content.alpha = 0;
        }

        void LateUpdate() => FollowSprite();

        /// <summary>Inherits the exact drawing plane and head motion, but keeps the label free of body squash.</summary>
        public void FollowSprite()
        {
            if (!spritePlane || !bubbleCanvas) return;
            Transform plane = spritePlane.transform;
            Transform canvas = bubbleCanvas.transform;
            canvas.SetPositionAndRotation(plane.TransformPoint(new Vector3(0, spritePlane.height + headGap, 0)), plane.rotation);
            Vector3 inherited = canvas.parent ? canvas.parent.lossyScale : Vector3.one;
            canvas.localScale = new Vector3(metresPerPixel / Mathf.Max(.0001f, Mathf.Abs(inherited.x)),
                metresPerPixel / Mathf.Max(.0001f, Mathf.Abs(inherited.y)),
                metresPerPixel / Mathf.Max(.0001f, Mathf.Abs(inherited.z)));
        }
    }
}
