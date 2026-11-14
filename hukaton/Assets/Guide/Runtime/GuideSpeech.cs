using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;

namespace Hakaton.Guide
{
    /// <summary>
    /// A guide's voice on the game's linen speech plate (the same plate and typing as every spoken line), with the
    /// speaker's mumble on each new line, «E — дальше» while a line waits and «Esc — пропустить» under it. The plate
    /// stands at the top of the screen like the goblin's, and moves down while the lit place is up there.
    /// </summary>
    public sealed class GuideSpeech : MonoBehaviour, IGuideVoice
    {
        public CanvasGroup group;
        public DialogueBubbleLayout bubble;
        [Tooltip("The plate's frame: moved between the top and bottom places.")]
        public RectTransform plate;
        public ContinueHint continueHint;
        [Tooltip("«Esc — пропустить»: shown with every line.")]
        public TMP_Text skipHint;
        [Tooltip("The plate's anchored position at the top and at the bottom of the screen.")]
        public Vector2 topPlace = new Vector2(0, -40), bottomPlace = new Vector2(0, 40);
        [Header("Voice")]
        public AudioSource voiceSource;
        public AudioClip[] voice = new AudioClip[0];
        [Range(0, 1)] public float voiceVolume = .55f;

        string lastLine;
        bool atBottom;

        void Awake() => Hide();

        public void Say(string line, bool waitsForPlayer)
        {
            if (!bubble) return;
            bubble.SetText(line);
            if (group) group.alpha = 1;
            if (continueHint) continueHint.Show(waitsForPlayer);
            if (skipHint) skipHint.gameObject.SetActive(true);
            if (line != lastLine && voiceSource && voice != null && voice.Length > 0)
            {
                var clip = voice[Random.Range(0, voice.Length)];
                if (clip) voiceSource.PlayOneShot(clip, voiceVolume);
            }
            lastLine = line;
        }

        public void Hide()
        {
            if (group) group.alpha = 0;
            if (bubble) bubble.ForgetLine();
            if (continueHint) continueHint.Show(false);
            lastLine = null;
        }

        public bool SkipTyping() => bubble && bubble.SkipTyping();

        public void KeepClearOf(Rect? lit, RectTransform overlay)
        {
            if (!plate) return;
            bool bottom = false;
            var body = bubble && bubble.Body ? bubble.Body : plate;
            if (lit.HasValue && overlay && body.rect.height > 1)
            {
                // The plate where it stands now and mirrored to the other edge, in the overlay's space: it takes the
                // place that covers less of the lit area (the top when it covers nothing).
                var corners = new Vector3[4];
                body.GetWorldCorners(corners);
                Vector2 a = overlay.InverseTransformPoint(corners[0]), b = overlay.InverseTransformPoint(corners[2]);
                var here = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                var area = overlay.rect;
                var top = here;
                var low = here;
                if (atBottom) top.y = area.yMax - (here.yMin - area.yMin) - here.height;
                else low.y = area.yMin + (area.yMax - here.yMax);
                float coverTop = Cover(top, lit.Value), coverLow = Cover(low, lit.Value);
                bottom = coverTop > 0 && coverLow < coverTop * .5f;
            }
            if (bottom == atBottom && plate.anchoredPosition == (bottom ? bottomPlace : topPlace)) return;
            atBottom = bottom;
            var anchor = bottom ? new Vector2(.5f, 0) : new Vector2(.5f, 1);
            plate.anchorMin = plate.anchorMax = plate.pivot = anchor;
            plate.anchoredPosition = bottom ? bottomPlace : topPlace;
        }

        static float Cover(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0 && h > 0 ? w * h : 0;
        }
    }
}
