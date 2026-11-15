using System;
using System.Collections;
using Hakaton.Guide;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>What the frightened Гвоздик has already shown (saved with the game).</summary>
    [Serializable]
    public sealed class PcNailFearSave
    {
        /// <summary>Scenes shown: 0 none, 1 the first (after the stranger), 2 both (the second after хаPAY).</summary>
        public int shown;
        /// <summary>Outings made (the workshop's journeys) when the last scene was shown.</summary>
        public int journeysAtLast;

        public PcNailFearSave Copy() => new PcNailFearSave { shown = shown, journeysAtLast = journeysAtLast };
    }

    /// <summary>When a frightened Гвоздик scene is due (pure, for tests).</summary>
    public static class PcNailFearRules
    {
        /// <summary>
        /// The scene due now: -1 none; 0 the first (the stranger has been met); 1 the second (хаPAY is bought and at least
        /// <paramref name="outingsBetween"/> outings have passed since the first). Each is shown once.
        /// </summary>
        public static int Due(PcNailFearSave save, bool strangerMet, bool hapayOwned, int journeys, int outingsBetween)
        {
            if (save == null) return -1;
            if (save.shown <= 0) return strangerMet ? 0 : -1;
            if (save.shown == 1) return hapayOwned && journeys - save.journeysAtLast >= Mathf.Max(0, outingsBetween) ? 1 : -1;
            return -1;
        }

        /// <summary>One roll per start-up of the PC: a forced scene always comes, otherwise with <paramref name="chance"/>.</summary>
        public static bool Comes(bool forced, float chance, double roll) => forced || roll < chance;
    }

    /// <summary>
    /// A frightened Гвоздик. After the stranger at the shift's end, one PC start-up in six (on average) begins with the
    /// helper uneasy and trembling in the corner: «Только не говори ему ничего...» Once the хаPAY account is bought, and at least four
    /// outings after that, another, and now he is terrified: «Мне страшно, когда ты оставляешь меня с ним...». Each scene is shown once. The PC's
    /// music goes quiet and the desktop takes no clicks meanwhile; E or a left click reads on, Esc ends it. Never over a guide or a popup attack.
    /// </summary>
    public sealed class PcNailFear : MonoBehaviour
    {
        public PcComputer computer;
        public PcHelper helper;
        public PcGuide guide;
        public PcPopupAttack popups;

        [Header("When")]
        [Tooltip("Chance on each start-up of the PC (one roll per sitting) once a scene is due.")]
        [Range(0, 1)] public float chance = 1f / 6f;
        [Tooltip("The second scene comes no sooner than this many outings after the first.")]
        [Min(0)] public int outingsBetween = 4;
        [Tooltip("Seconds after the desktop comes up before Гвоздик creeps out.")]
        [Min(0)] public float delay = .9f;

        [Header("Lines")]
        [TextArea] public string[] firstLines =
        {
            "Только не говори ему ничего...",
            "Прошлый хозяин был бы очень недоволен..."
        };
        [TextArea] public string[] secondLines =
        {
            "Мне страшно, когда ты оставляешь меня с ним...",
            "Почему?..",
            "Надеюсь, всё ещё наладится."
        };
        [Tooltip("Text Animator tags around every line (empty: plain).")]
        public string lineOpen = "<shake>", lineClose = "</shake>";
        [TextArea] public string hint = "E — дальше";

        [Header("Look")]
        [Tooltip("The first scene: uneasy (wide eyes, drooping ears, a nervous mouth), a light shiver.")]
        public Sprite[] firstIdle = new Sprite[0];
        public Sprite[] firstTalk = new Sprite[0];
        [Min(0)] public float firstShiverPixels = 1;
        [Min(.01f)] public float firstShiverSeconds = .09f;
        [Tooltip("The second scene: terrified (huge bloodshot eyes, gritted teeth, sweat), a hard shiver.")]
        public Sprite[] secondIdle = new Sprite[0];
        public Sprite[] secondTalk = new Sprite[0];
        [Min(0)] public float secondShiverPixels = 1;
        [Min(.01f)] public float secondShiverSeconds = .035f;

        [Header("Sound")]
        [Tooltip("A quiet low sting when he appears.")]
        public AudioClip sting;
        [Range(0, 1)] public float stingVolume = .55f;

        /// <summary>The game's word on the stranger having come (the first scene waits for him).</summary>
        public Func<bool> StrangerMet { get; set; }
        /// <summary>The хаPAY account is bought (the second scene waits for it).</summary>
        public Func<bool> HapayOwned { get; set; }
        /// <summary>The game's say on whether a scene may come now (the shop tutorial, the cave). Null: always.</summary>
        public Func<bool> Allowed { get; set; }
        /// <summary>The cheat: the next start-up shows the scene that is due next, whatever the chance and the wait.</summary>
        public bool ForceNext { get; set; }
        /// <summary>The saved part changed (a scene was shown).</summary>
        public event Action Changed;

        public bool Running => routine != null;
        public PcNailFearSave State => state;

        PcNailFearSave state = new PcNailFearSave();
        readonly System.Random random = new System.Random();
        Coroutine routine;
        bool sitting, armed, raycasterWas = true;
        int armedScene;
        float startAt;

        void Awake()
        {
            if (!computer) return;
            computer.Ready += OnReady;
            computer.Closed += OnClosed;
        }

        void OnDestroy()
        {
            if (!computer) return;
            computer.Ready -= OnReady;
            computer.Closed -= OnClosed;
        }

        void OnDisable() => Stop();

        // ---------- save ----------

        public PcNailFearSave Save() => state.Copy();

        /// <summary>Null (a save from before Гвоздик was frightened): nothing shown yet.</summary>
        public void Restore(PcNailFearSave save)
        {
            Stop();
            state = save != null ? save.Copy() : new PcNailFearSave();
            state.shown = Mathf.Clamp(state.shown, 0, 2);
        }

        // ---------- when ----------

        int Journeys => computer && computer.workshop ? computer.workshop.Model.Journeys : 0;

        /// <summary>The scene due next, ignoring the wait between them when forced (-1: none).</summary>
        int DueScene(bool forced)
        {
            bool stranger = StrangerMet != null && StrangerMet();
            bool hapay = HapayOwned != null && HapayOwned();
            if (forced)
            {
                if (state.shown <= 0) return 0;
                return state.shown == 1 ? 1 : -1;
            }
            return PcNailFearRules.Due(state, stranger, hapay, Journeys, outingsBetween);
        }

        /// <summary>Nothing else is on the PC: no guide (nor one about to start), no popup attack, the game agrees.</summary>
        bool Clear()
        {
            if (!computer || !computer.Running || computer.Guided || !helper) return false;
            if (guide && (guide.Running || !guide.Seen(guide.desktopTour.id))) return false;
            if (guide && computer.hapay && computer.hapay.Available && !guide.Seen(guide.hapayTour.id)) return false;
            if (popups && (popups.Attacking || popups.Armed || popups.CrashPending)) return false;
            if (helper.Remarking) return false;
            return Allowed == null || Allowed();
        }

        void OnReady()
        {
            // One roll per sitting at the PC (a restart within it is not a new start-up).
            if (sitting) return;
            sitting = true;
            bool forced = ForceNext;
            int scene = DueScene(forced);
            if (scene < 0 || !Clear()) return;
            if (!PcNailFearRules.Comes(forced, chance, random.NextDouble())) return;
            armed = true;
            armedScene = scene;
            startAt = Time.unscaledTime + delay;
        }

        void OnClosed()
        {
            armed = false;
            sitting = false;
            Stop();
        }

        void Update()
        {
            if (!armed || Time.unscaledTime < startAt) return;
            armed = false;
            // Something came up meanwhile (a popup attack is on its way, a guide began): not this time.
            if (!Clear()) return;
            ForceNext = false;
            routine = StartCoroutine(Play(armedScene));
        }

        // ---------- the scene ----------

        IEnumerator Play(int scene)
        {
            state.shown = scene + 1;
            state.journeysAtLast = Journeys;
            Changed?.Invoke();
            computer.Guided = true;
            computer.Hushed = true;
            // The desktop does not take clicks while he talks (no window, no guide opens under him).
            if (computer.raycaster) { raycasterWas = computer.raycaster.enabled; computer.raycaster.enabled = false; }
            bool first = scene == 0;
            helper.scaredIdle = first ? firstIdle : secondIdle;
            helper.scaredTalk = first ? firstTalk : secondTalk;
            helper.shiverPixels = first ? firstShiverPixels : secondShiverPixels;
            helper.shiverSeconds = first ? firstShiverSeconds : secondShiverSeconds;
            helper.Scared = true;
            if (sting) computer.Play(sting, first ? stingVolume * .7f : stingVolume);
            var lines = scene == 0 ? firstLines : secondLines;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                helper.Say(lineOpen + line + lineClose, true);
                helper.SetHint(hint);
                // The press that ended the last line must not also end this one.
                yield return null;
                bool skipped = false;
                while (true)
                {
                    if (GuideInput.Skip) { skipped = true; break; }
                    if (GuideInput.Advance && !helper.SkipTyping()) break;
                    yield return null;
                }
                if (skipped) break;
                yield return null;
            }
            routine = null;
            Finish();
        }

        void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            bool was = routine != null;
            routine = null;
            if (was) Finish();
        }

        void Finish()
        {
            if (helper) helper.Hide();
            if (!computer) return;
            computer.Guided = false;
            computer.Hushed = false;
            if (computer.raycaster && computer.Running) computer.raycaster.enabled = raycasterWas;
        }
    }
}
