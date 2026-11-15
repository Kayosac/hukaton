using Hakaton.DialogueUI;
using Hakaton.Feel;
using Hakaton.Guide;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// «Гоблинсофт»'s helper on the PC: a little character who pops up in the corner of the desktop and speaks in a
    /// yellow XP balloon (typed, with a bleep per line). It stands bottom-right over the tray and moves to the
    /// bottom-left when the lit place is behind its balloon. Drawn in the PC's own picture, so it bends with the glass.
    /// </summary>
    public sealed class PcHelper : MonoBehaviour, IGuideVoice
    {
        [Tooltip("Helper and balloon together: moved between the corners, popped in and out.")]
        public RectTransform root;
        public CanvasGroup group;
        public Image figure;
        [Tooltip("Frames: idle (blinking), talking (mouth), pointing.")]
        public Sprite[] idle = new Sprite[0], talk = new Sprite[0];
        public Sprite point;
        [Min(.02f)] public float frameSeconds = .14f;
        public DialogueBubbleLayout balloon;
        [Tooltip("The balloon's little tail towards the helper; mirrored with it.")]
        public RectTransform tail;
        [Tooltip("«E — дальше · Esc — пропустить» at the bottom of the balloon.")]
        public TMP_Text hint;
        [TextArea] public string hintWaiting = "E — дальше     Esc — пропустить";
        [TextArea] public string hintDoing = "Esc — пропустить";
        [Tooltip("Anchored positions of the root in the right and the left corner (anchored to the bottom corners).")]
        public Vector2 rightPlace = new Vector2(-6, 32), leftPlace = new Vector2(6, 32);
        [Tooltip("A passing remark (Remark, not a guide step) stays this long after its typing ends, then goes.")]
        [Min(.5f)] public float remarkSeconds = 3.5f;

        [Header("Sound")]
        public PcComputer computer;
        public AudioClip[] bleeps = new AudioClip[0];
        public AudioClip popSound;
        [Range(0, 1)] public float volume = .5f;

        [Header("Frightened")]
        [Tooltip("Frames while frightened (PcNailFear): idle (the eyes dart now and then) and talking.")]
        public Sprite[] scaredIdle = new Sprite[0], scaredTalk = new Sprite[0];
        [Tooltip("He shivers: a whole-pixel jolt of the figure this often, up to this many pixels.")]
        [Min(0)] public float shiverPixels = 1;
        [Min(.01f)] public float shiverSeconds = .05f;
        [Tooltip("His bleeps are this much quieter while frightened.")]
        [Range(0, 1)] public float scaredVolume = .45f;

        float shown, wanted;
        SpringFloat pop;
        string lastLine;
        bool left, pointing;
        float frameClock;
        int frame;
        float remarkUntil = -1, remarkHold;
        bool scared;
        float shiverClock;
        Vector2 figureHome;
        bool figureHomeKnown;

        /// <summary>
        /// Frightened: shaking, wide-eyed, quiet, no cheerful pop. Set before he speaks; once he has gone (hidden and
        /// faded) he is himself again.
        /// </summary>
        public bool Scared
        {
            get => scared;
            set
            {
                scared = value;
                if (!value) StillFigure();
            }
        }

        /// <summary>A passing remark is up (it goes by itself).</summary>
        public bool Remarking => remarkUntil >= 0;

        void Awake()
        {
            pop.Snap(0);
            Apply(0);
            Place(false);
            if (figure)
            {
                figureHome = figure.rectTransform.anchoredPosition;
                figureHomeKnown = true;
            }
        }

        /// <summary>The hint under the balloon, for a speaker other than the guides (after <see cref="Say"/>).</summary>
        public void SetHint(string text)
        {
            if (!hint) return;
            hint.gameObject.SetActive(!string.IsNullOrEmpty(text));
            hint.text = text ?? string.Empty;
        }

        public void Say(string line, bool waitsForPlayer)
        {
            remarkUntil = -1;
            if (hint) hint.gameObject.SetActive(true);
            Speak(line);
            if (hint) hint.text = waitsForPlayer ? hintWaiting : hintDoing;
        }

        /// <summary>
        /// A passing remark outside the guides («Закрой окно»): no «E — дальше» hint, nothing waits for the player;
        /// it goes by itself <paramref name="seconds"/> after its typing ends (default <see cref="remarkSeconds"/>).
        /// </summary>
        public void Remark(string line, float seconds = -1)
        {
            if (string.IsNullOrEmpty(line)) return;
            pointing = false;
            Speak(line);
            if (hint) hint.gameObject.SetActive(false);
            remarkHold = seconds > 0 ? seconds : remarkSeconds;
            remarkUntil = Time.unscaledTime + remarkHold;
        }

        void Speak(string line)
        {
            if (wanted <= 0)
            {
                pop.Snap(scared ? .8f : .4f);
                pop.Kick(scared ? 3 : 9);
                if (computer && popSound && !scared) computer.Play(popSound, volume);
            }
            wanted = 1;
            if (balloon) balloon.SetText(line);
            if (line != lastLine && computer && bleeps != null && bleeps.Length > 0)
            {
                var clip = bleeps[Random.Range(0, bleeps.Length)];
                if (clip) computer.Play(clip, scared ? volume * scaredVolume : volume);
            }
            lastLine = line;
        }

        public void Hide()
        {
            remarkUntil = -1;
            wanted = 0;
            if (balloon) balloon.ForgetLine();
            lastLine = null;
        }

        public bool SkipTyping() => balloon && balloon.SkipTyping();

        public void KeepClearOf(Rect? lit, RectTransform overlay)
        {
            pointing = lit.HasValue;
            if (!balloon || !overlay) return;
            // Where the balloon stands now, in the overlay's space, and where it would stand in the other corner
            // (mirrored about the overlay's centre). It keeps to the right corner unless the lit place is under it there.
            var body = balloon.Body ? balloon.Body : (RectTransform)balloon.transform;
            var corners = new Vector3[4];
            body.GetWorldCorners(corners);
            Vector2 a = overlay.InverseTransformPoint(corners[0]), b = overlay.InverseTransformPoint(corners[2]);
            var here = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            var there = here;
            there.x = overlay.rect.xMin + overlay.rect.xMax - here.xMax;
            bool hereBlocked = lit.HasValue && here.Overlaps(lit.Value);
            bool thereBlocked = lit.HasValue && there.Overlaps(lit.Value);
            if (left && !thereBlocked) Place(false);
            else if (!left && hereBlocked && !thereBlocked) Place(true);
        }

        void Place(bool toLeft)
        {
            left = toLeft;
            if (!root) return;
            // Mirrored about its bottom-right corner: on the left the helper stands in the corner and the balloon opens
            // to the right. The text inside is turned back.
            root.anchorMin = root.anchorMax = new Vector2(toLeft ? 0 : 1, 0);
            root.pivot = new Vector2(1, 0);
            root.anchoredPosition = toLeft ? leftPlace + new Vector2(root.rect.width, 0) : rightPlace;
            root.localScale = new Vector3(toLeft ? -1 : 1, 1, 1);
            if (balloon) balloon.transform.localScale = new Vector3(toLeft ? -1 : 1, 1, 1);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (remarkUntil >= 0)
            {
                if (balloon && balloon.IsTyping) remarkUntil = Time.unscaledTime + remarkHold;
                else if (Time.unscaledTime >= remarkUntil) Hide();
            }
            shown = Mathf.MoveTowards(shown, wanted, dt * 6);
            pop.Step(wanted, 14, .45f, dt);
            Apply(shown);
            if (!figure) return;
            // Gone and faded: the fright is over.
            if (scared && wanted <= 0 && shown <= 0) Scared = false;
            Shiver(dt);
            frameClock += dt;
            if (frameClock >= frameSeconds)
            {
                frameClock = 0;
                frame++;
            }
            bool talking = balloon && balloon.IsTyping;
            Sprite sprite = null;
            if (scared && talking && scaredTalk != null && scaredTalk.Length > 0) sprite = scaredTalk[frame % scaredTalk.Length];
            else if (scared && scaredIdle != null && scaredIdle.Length > 0)
            {
                // Mostly staring; the eyes dart aside now and then.
                int cycle = frame % 14;
                sprite = scaredIdle[cycle < 10 || scaredIdle.Length == 1 ? 0 : 1 + cycle % (scaredIdle.Length - 1)];
            }
            else if (talking && talk != null && talk.Length > 0) sprite = talk[frame % talk.Length];
            else if (pointing && point) sprite = point;
            else if (idle != null && idle.Length > 0)
            {
                // Mostly the first frame; a blink now and then.
                int cycle = frame % 24;
                sprite = idle[cycle < 22 || idle.Length == 1 ? 0 : 1 + cycle % (idle.Length - 1)];
            }
            if (sprite && figure.sprite != sprite) figure.sprite = sprite;
        }

        void Shiver(float dt)
        {
            if (!scared || !figureHomeKnown || shiverPixels <= 0) return;
            shiverClock += dt;
            if (shiverClock < shiverSeconds) return;
            shiverClock = 0;
            int reach = Mathf.Max(1, Mathf.RoundToInt(shiverPixels));
            figure.rectTransform.anchoredPosition = figureHome + new Vector2(Random.Range(-reach, reach + 1), Random.Range(-reach, reach + 1) * .5f);
        }

        void StillFigure()
        {
            if (figure && figureHomeKnown) figure.rectTransform.anchoredPosition = figureHome;
        }

        void Apply(float alpha)
        {
            if (group)
            {
                group.alpha = alpha;
                group.blocksRaycasts = group.interactable = false;
            }
            if (root)
            {
                // Pops up from its feet; whole-pixel bob while shown.
                float s = Mathf.Max(0, pop.Value);
                root.localScale = new Vector3((left ? -1 : 1) * s, s, 1);
            }
        }
    }
}
