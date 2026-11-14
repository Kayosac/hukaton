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

        [Header("Sound")]
        public PcComputer computer;
        public AudioClip[] bleeps = new AudioClip[0];
        public AudioClip popSound;
        [Range(0, 1)] public float volume = .5f;

        float shown, wanted;
        SpringFloat pop;
        string lastLine;
        bool left, pointing;
        float frameClock;
        int frame;

        void Awake()
        {
            pop.Snap(0);
            Apply(0);
            Place(false);
        }

        public void Say(string line, bool waitsForPlayer)
        {
            if (wanted <= 0)
            {
                pop.Snap(.4f);
                pop.Kick(9);
                if (computer && popSound) computer.Play(popSound, volume);
            }
            wanted = 1;
            if (balloon) balloon.SetText(line);
            if (hint) hint.text = waitsForPlayer ? hintWaiting : hintDoing;
            if (line != lastLine && computer && bleeps != null && bleeps.Length > 0)
            {
                var clip = bleeps[Random.Range(0, bleeps.Length)];
                if (clip) computer.Play(clip, volume);
            }
            lastLine = line;
        }

        public void Hide()
        {
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
            shown = Mathf.MoveTowards(shown, wanted, dt * 6);
            pop.Step(wanted, 14, .45f, dt);
            Apply(shown);
            if (!figure) return;
            frameClock += dt;
            if (frameClock >= frameSeconds)
            {
                frameClock = 0;
                frame++;
            }
            bool talking = balloon && balloon.IsTyping;
            Sprite sprite = null;
            if (talking && talk != null && talk.Length > 0) sprite = talk[frame % talk.Length];
            else if (pointing && point) sprite = point;
            else if (idle != null && idle.Length > 0)
            {
                // Mostly the first frame; a blink now and then.
                int cycle = frame % 24;
                sprite = idle[cycle < 22 || idle.Length == 1 ? 0 : 1 + cycle % (idle.Length - 1)];
            }
            if (sprite && figure.sprite != sprite) figure.sprite = sprite;
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
