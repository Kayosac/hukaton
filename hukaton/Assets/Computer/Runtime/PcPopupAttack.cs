using System;
using System.Collections.Generic;
using Hakaton.Feel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Computer
{
    /// <summary>
    /// «Закрой окно»: now and then, a couple of seconds after the desktop comes up, ads pour onto the PC's screen
    /// without asking. Close them for <see cref="PcPopupSettings.roundSeconds"/>: survive and a few coins come in;
    /// let <see cref="PcPopupSettings.loseAt"/> pile up and a blue screen takes a share of the balance, then the PC
    /// restarts. Walking out of an attack (Esc, «Выключить», getting up, quitting the game) is a loss too: the blue
    /// screen comes at the next start-up. Never during a guide, a merge or a drag, nor while <see cref="Allowed"/> says
    /// no (the game: the shop tutorial, the cave). Гвоздик comments (<see cref="PcHelper.Remark"/>).
    /// The windows are configured prefabs (<see cref="adPrefab"/>, <see cref="confirmPrefab"/>) spawned into
    /// <see cref="windowsRoot"/>; the rules are <see cref="PcPopupRules"/>.
    /// </summary>
    public sealed class PcPopupAttack : MonoBehaviour
    {
        [Header("PC")]
        public PcComputer computer;
        public PcDesktop desktop;
        public PcGuide guide;
        [Tooltip("The frightened Гвоздик: no attack while he is out.")]
        public PcNailFear fear;
        public PcHelper helper;
        public PcCardsWindow cards;

        [Header("Layer")]
        [Tooltip("Over the windows and the taskbar, under Гвоздик. Holds a transparent full-screen blocker (an Image that " +
                 "takes raycasts) and, above it, the windows root. Takes clicks only during an attack.")]
        public CanvasGroup layer;
        [Tooltip("Where the windows are spawned (inside the layer, above the blocker).")]
        public RectTransform windowsRoot;
        [Tooltip("The area windows appear in: the screen above the taskbar.")]
        public RectTransform spawnArea;
        public PcPopup adPrefab;
        [Tooltip("The small «Вы уверены?» window with «Да» / «Нет».")]
        public PcPopup confirmPrefab;
        public PcPopupAd[] ads = Array.Empty<PcPopupAd>();
        public string confirmTitle = "Гоблинсофт Защитник";
        [TextArea] public string confirmQuestion = "Вы уверены, что хотите закрыть рекламу?";

        [Header("Blue screen")]
        [Tooltip("Full screen, over everything on the PC's canvas (the pointer is drawn over it anyway).")]
        public CanvasGroup crashScreen;
        public TMP_Text crashText;
        [Tooltip("{penalty} — the coins the virus took.")]
        [TextArea(6, 20)] public string crashTemplate =
            "Гоблинсофт XP\n\n" +
            "Обнаружена проблема, и работа компьютера была остановлена во избежание повреждения лавки.\n\n" +
            "РЕКЛАМА_НЕ_ЗАКРЫТА_ВОВРЕМЯ\n\n" +
            "Если вы видите этот экран впервые, перезагрузите компьютер. Если он появился снова, закрывайте окна быстрее. " +
            "Не нажимайте на «Вы выиграли!». Никогда.\n\n" +
            "Вирус «СкидкаГоблин.exe» списал со счёта лавки {penalty|монету|монеты|монет}.\n\n" +
            "Техническая информация:\n" +
            "*** STOP: 0x0000PENUS (0x0000000C, 0xDEADBEEF, 0x00000404)\n\n" +
            "Сохранение дампа памяти... 100%";

        [Header("Tuning")]
        public PcPopupSettings settings = new PcPopupSettings();

        [Header("Гвоздик")]
        [Tooltip("Lines are picked at random. {reward}, {closed}, {record}, {penalty} are filled in; {closed|окно|окна|окон} adds the agreeing word.")]
        [TextArea] public string[] startLines =
        {
            "Похоже, вы пытаетесь закрыть окно! Хотите помощь?",
            "Ой. Похоже, вы скачали что-то не то. Хотите, я прочитаю вам рекламу вслух?"
        };
        [TextArea] public string[] midLines =
        {
            "Совет дня: окна закрываются крестиком. Обычно.",
            "Не волнуйтесь, это всего лишь все предложения века одновременно!"
        };
        [TextArea] public string[] fakeLines =
        {
            "Это был не тот крестик. Тот крестик — для красоты.",
            "Ха! Двойной крестик! Классика Гоблинсофт."
        };
        [Tooltip("Won. {reward}, {closed}, {record}.")]
        [TextArea] public string[] winLines =
        {
            "Вы закрыли {closed|окно|окна|окон}! Гоблинсофт дарит вам {reward|монету|монеты|монет} за терпение. Рекорд: {record}.",
            "Реклама побеждена! Держите {reward|монету|монеты|монет}. Закрыто окон: {closed}, рекорд: {record}. Я почти не помогал!"
        };
        [Tooltip("After the restart that followed a loss. {penalty}.")]
        [TextArea] public string[] lostLines =
        {
            "Мы снова с вами! Вирус взял всего {penalty|монету|монеты|монет}. Почти даром!",
            "Перезагрузка прошла успешно. Монеты — нет: минус {penalty}."
        };
        [Tooltip("After the restart at the start-up following an attack the player walked out of. {penalty}.")]
        [TextArea] public string[] escapeLines =
        {
            "Вы выключили компьютер, но реклама никуда не делась. Она ждала. Минус {penalty|монета|монеты|монет}.",
            "Убегать от рекламы бесполезно: она у вас в сердце. И в кошельке: минус {penalty}."
        };
        [Tooltip("The middle-of-the-round line comes at this second.")]
        [Min(0)] public float midLineAt = 7.5f;

        [Header("Sound")]
        public SfxCue spawnCue;
        public SfxCue closeCue, fakeCue, dodgeCue, confirmCue, winCue, crashCue;

        [Header("Win effects")]
        [Tooltip("Where PC particles live (the merge show's «Merge FX» under the PC camera).")]
        public Transform fxRoot;
        [Tooltip("A small burst on every window popped by the win sweep (Retro Arsenal, `Fx/PC *`).")]
        public GameObject sweepBurst;
        [Min(0)] public float sweepBurstScale = 1.1f;
        [Tooltip("The win itself, in the middle of the screen.")]
        public GameObject winBurst;
        [Min(0)] public float winBurstScale = 1f;

        /// <summary>The game's say on whether an attack may come now (the shop tutorial, the cave). Null: always.</summary>
        public Func<bool> Allowed { get; set; }
        /// <summary>The cheat: the next opening brings an attack whatever the chance and the cooldowns.</summary>
        public bool ForceNext { get; set; }
        /// <summary>The saved part changed (an opening counted, an attack started, won or lost, a new record).</summary>
        public event Action Changed;

        public bool Attacking { get; private set; }
        /// <summary>An attack is rolled for this sitting and waits for its start.</summary>
        public bool Armed => armed;
        /// <summary>The next start-up begins with the blue screen (an attack was walked out of).</summary>
        public bool CrashPending => state.attackPending && !Attacking;
        public PcPopupSave State => state;

        PcPopupSave state = new PcPopupSave();
        readonly List<PcPopup> windows = new List<PcPopup>();
        readonly System.Random random = new System.Random();
        bool sitting, armed, crashShown, lostHere, midSaid, fakeSaid;
        float startAt, elapsed, nextSpawn, crashUntil;
        int closed, lastAd = -1;
        // The win sweep: the windows left, top first; which goes next and when; the reward to announce.
        readonly List<PcPopup> sweep = new List<PcPopup>();
        readonly List<PcVfx.Live> effects = new List<PcVfx.Live>();
        bool sweeping;
        int sweepAt, sweepReward;
        float sweepNext;
        string afterReboot;

        void Awake()
        {
            if (computer)
            {
                computer.Ready += OnReady;
                computer.Closed += OnClosed;
            }
            SetLayer(false);
            ShowCrashScreen(false);
        }

        void OnDestroy()
        {
            if (!computer) return;
            computer.Ready -= OnReady;
            computer.Closed -= OnClosed;
        }

        void OnDisable()
        {
            Abort();
            armed = false;
            ShowCrashScreen(false);
            crashShown = false;
        }

        // ---------- save ----------

        public PcPopupSave Save() => state.Copy();

        /// <summary>Null (a save from before the mini-game): nothing counted yet — the first attack waits the full cooldown.</summary>
        public void Restore(PcPopupSave save)
        {
            Abort();
            state = save != null ? save.Copy() : new PcPopupSave();
            if (state.opensSinceAttack < 0) state.opensSinceAttack = 0;
            if (state.record < 0) state.record = 0;
            if (float.IsNaN(state.secondsSinceAttack) || state.secondsSinceAttack < 0) state.secondsSinceAttack = 0;
        }

        /// <summary>Real seconds of play (the game calls it every frame; saved with the next save, no dirty mark).</summary>
        public void CountPlayTime(float seconds)
        {
            if (!Attacking) PcPopupRules.CountPlayTime(state, seconds);
        }

        // ---------- when ----------

        /// <summary>Nothing on the PC or in the game forbids an attack right now.</summary>
        bool Clear()
        {
            if (!computer || !computer.Running || computer.Guided) return false;
            if (guide && (guide.Running || !guide.Seen(guide.desktopTour.id))) return false;
            if (cards && cards.Busy) return false;
            if (fear && fear.Running) return false;
            return Allowed == null || Allowed();
        }

        void OnReady()
        {
            if (afterReboot != null)
            {
                if (helper && !(guide && guide.Running)) helper.Remark(afterReboot);
                afterReboot = null;
            }
            // One roll per sitting at the PC (a restart within it is not a new opening).
            if (sitting) return;
            sitting = true;
            var verdict = PcPopupRules.Roll(state, settings, Clear(), ForceNext, random.NextDouble());
            if (verdict == PcPopupVerdict.Attack)
            {
                armed = true;
                float min = Mathf.Min(settings.startDelayMin, settings.startDelayMax);
                startAt = Time.unscaledTime + Mathf.Lerp(min, Mathf.Max(min, settings.startDelayMax), (float)random.NextDouble());
            }
            else Calm();
        }

        void Calm()
        {
            PcPopupRules.Calm(state);
            Changed?.Invoke();
        }

        void OnClosed()
        {
            if (armed) { armed = false; Calm(); }
            Abort();
            sitting = false;
            if (helper && helper.Remarking) helper.Hide();
        }

        void Update()
        {
            if (crashShown) { UpdateCrash(); return; }
            if (armed && Time.unscaledTime >= startAt)
            {
                armed = false;
                // Something came up meanwhile: no attack this time, and nothing spent.
                if (Clear()) Begin();
                else Calm();
            }
            if (Attacking) Play(Time.unscaledDeltaTime);
            if (sweeping) Sweep();
            TickEffects();
        }

        // ---------- the round ----------

        void Begin()
        {
            ForceNext = false;
            PcPopupRules.Started(state);
            Changed?.Invoke();
            Attacking = true;
            elapsed = 0;
            closed = 0;
            midSaid = fakeSaid = false;
            if (desktop) desktop.CloseMenu();
            if (cards && cards.tooltip) cards.tooltip.Hide();
            SetLayer(true);
            for (int i = 0; i < settings.startWindows; i++) SpawnAd();
            nextSpawn = PcPopupRules.Interval(0, settings);
            Say(startLines);
        }

        void Play(float dt)
        {
            // Walked out (Esc, «Выключить», got up): the windows go quietly; the saved flag makes it a loss next time.
            if (!computer || !computer.Running) { Abort(); return; }
            elapsed += dt;
            while (elapsed >= nextSpawn && elapsed < settings.roundSeconds)
            {
                SpawnAd();
                nextSpawn += PcPopupRules.Interval(nextSpawn, settings);
            }
            if (!midSaid && elapsed >= midLineAt)
            {
                midSaid = true;
                Say(midLines);
            }
            bool havePointer = Pointer(out var pointer);
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var window = windows[i];
                if (!window) { windows.RemoveAt(i); continue; }
                if ((window.Tricks & PcPopupTrick.Creep) != 0) Creep(window, dt);
                if (havePointer && (window.Tricks & PcPopupTrick.Runaway) != 0 && window.Dodges < settings.dodgeTimes) Dodge(window, pointer);
            }
            if (PcPopupRules.Lost(windows.Count, settings)) Lose();
            else if (PcPopupRules.RoundWon(closed, elapsed, settings)) Win();
        }

        /// <summary>
        /// Won: the reward and the record are settled at once (walking out now changes nothing), then the windows left
        /// pop one after another, quickly, each with a rising pop and a spark, and only then the win is announced.
        /// </summary>
        void Win()
        {
            Attacking = false;
            int reward = Mathf.Max(0, settings.reward);
            if (computer && computer.workshop && reward > 0) computer.workshop.Model.Wallet.Credit(reward);
            PcPopupRules.Won(state, closed);
            Changed?.Invoke();
            sweep.Clear();
            foreach (var window in windows) if (window) sweep.Add(window);
            windows.Clear();
            // Top first: the last sibling is in front.
            sweep.Sort((a, b) => b.transform.GetSiblingIndex().CompareTo(a.transform.GetSiblingIndex()));
            // Nothing is clicked any more; the blocker still keeps the desktop shut until the win is announced.
            if (layer) layer.interactable = false;
            foreach (var window in sweep)
                foreach (var graphic in window.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
            sweepReward = reward;
            sweepAt = 0;
            sweepNext = Time.unscaledTime + settings.sweepDelay;
            sweeping = true;
        }

        void Sweep()
        {
            if (!computer || !computer.Running) { EndSweep(false); return; }
            float now = Time.unscaledTime;
            while (sweepAt < sweep.Count && now >= sweepNext)
            {
                var window = sweep[sweepAt];
                if (window)
                {
                    window.Collapse();
                    Burst(sweepBurst, window.Rect, sweepBurstScale);
                    Cue(closeCue, PcPopupRules.SweepPitch(sweepAt, settings));
                }
                sweepNext += PcPopupRules.SweepStep(sweepAt, sweep.Count, settings);
                sweepAt++;
            }
            if (sweepAt >= sweep.Count && now >= sweepNext + settings.sweepPause) EndSweep(true);
        }

        /// <summary>The sweep is over: announced (sound, confetti, Гвоздик), or cut short by leaving the PC.</summary>
        void EndSweep(bool announce)
        {
            sweeping = false;
            foreach (var window in sweep) if (window && !window.Collapsing) Destroy(window.gameObject);
            sweep.Clear();
            SetLayer(false);
            if (!announce) return;
            Cue(winCue);
            Burst(winBurst, windowsRoot, winBurstScale);
            Say(winLines, sweepReward, 0);
        }

        void Burst(GameObject prefab, RectTransform at, float scale)
        {
            if (!prefab || !fxRoot || !at) return;
            // Canvas pixels → world: the PC canvas is about 100 pixels per unit; the effect scales with it (as in the merge).
            float unitsPerPixel = Mathf.Abs(at.lossyScale.x) > 1e-6f ? Mathf.Abs(at.lossyScale.x) : .01f;
            float size = scale * unitsPerPixel * 100 / Mathf.Max(1e-4f, Mathf.Abs(fxRoot.lossyScale.x));
            var effect = PcVfx.Spawn(prefab, fxRoot, at.TransformPoint(at.rect.center), Quaternion.identity, size,
                Color.white, 0, 1, 0, Time.unscaledTime);
            if (effect != null) effects.Add(effect);
        }

        void TickEffects()
        {
            float now = Time.unscaledTime;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var effect = effects[i];
                if (effect.root && now < effect.dieAt) continue;
                if (effect.root) Destroy(effect.root);
                effects.RemoveAt(i);
            }
        }

        void Lose()
        {
            Clean();
            lostHere = true;
            if (computer) computer.Crash();
        }

        /// <summary>The attack is taken off at once (walked out of, disabled, a save restored). The saved flag stays.</summary>
        public void Abort()
        {
            // Already won: the reward is in, only the show stops.
            if (sweeping) EndSweep(false);
            if (!Attacking) return;
            Clean();
        }

        void Clean()
        {
            Attacking = false;
            foreach (var window in windows) if (window) Destroy(window.gameObject);
            windows.Clear();
            SetLayer(false);
        }

        void SetLayer(bool on)
        {
            if (!layer) return;
            layer.alpha = 1;
            layer.interactable = layer.blocksRaycasts = on;
        }

        // ---------- windows ----------

        void SpawnAd()
        {
            if (!adPrefab || !windowsRoot) return;
            var window = Instantiate(adPrefab, windowsRoot);
            window.name = "Ad " + (windows.Count + 1);
            int index = PickAd();
            var tricks = PcPopupRules.Pick(elapsed, settings, random.NextDouble);
            window.ShowAd(index >= 0 ? ads[index] : null, tricks, random.NextDouble() < .5);
            Hook(window);
            Prepare(window);
            ConfirmationsOnTop();
            window.MoveTo(RandomSpot(window));
            if ((tricks & PcPopupTrick.Creep) != 0)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                window.Drift = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * settings.creepSpeed;
            }
            windows.Add(window);
            Cue(spawnCue);
        }

        int PickAd()
        {
            if (ads == null || ads.Length == 0) return -1;
            int index = random.Next(ads.Length);
            if (ads.Length > 1 && index == lastAd) index = (index + 1 + random.Next(ads.Length - 1)) % ads.Length;
            lastAd = index;
            return index;
        }

        void Hook(PcPopup window)
        {
            window.CloseClicked += OnClose;
            window.FakeClicked += OnFake;
            window.YesClicked += OnYes;
            window.NoClicked += OnNo;
            window.Pressed += ToFront;
        }

        static void Prepare(PcPopup window)
        {
            var rect = window.Rect;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
        }

        void ToFront(PcPopup window)
        {
            if (!Attacking || !window) return;
            window.Rect.SetAsLastSibling();
            // A confirmation stays over its ad, and every «Вы уверены?» over all the ads.
            if (window.Confirmation) window.Confirmation.Rect.SetAsLastSibling();
            ConfirmationsOnTop();
        }

        /// <summary>«Вы уверены?» windows are always in front: fresh ads and a clicked ad go under them.</summary>
        void ConfirmationsOnTop()
        {
            foreach (var window in windows)
                if (window && window.IsConfirmation) window.Rect.SetAsLastSibling();
        }

        void OnClose(PcPopup window)
        {
            if (!Attacking) return;
            if (window.IsConfirmation) { OnNo(window); return; }
            if ((window.Tricks & PcPopupTrick.Confirm) != 0)
            {
                if (!window.Confirmation) Ask(window);
                else ToFront(window);
                return;
            }
            Shut(window);
        }

        void Ask(PcPopup ad)
        {
            if (!confirmPrefab || !windowsRoot) { Shut(ad); return; }
            var confirm = Instantiate(confirmPrefab, windowsRoot);
            confirm.name = "Confirm " + ad.name;
            confirm.ShowConfirmation(confirmTitle, confirmQuestion, random.NextDouble() < settings.confirmSwapChance);
            confirm.Owner = ad;
            ad.Confirmation = confirm;
            Hook(confirm);
            Prepare(confirm);
            // Over the ad's middle, kept on the screen.
            confirm.MoveTo(Fit(confirm, ad.Position));
            windows.Add(confirm);
            Cue(confirmCue);
        }

        void OnYes(PcPopup confirm)
        {
            if (!Attacking || !confirm.Owner) { Drop(confirm); return; }
            Shut(confirm.Owner);
        }

        void OnNo(PcPopup confirm)
        {
            if (!Attacking) return;
            if (confirm.Owner) confirm.Owner.Confirmation = null;
            Drop(confirm);
            Cue(closeCue);
        }

        void OnFake(PcPopup window)
        {
            if (!Attacking) return;
            Cue(fakeCue);
            for (int i = 0; i < settings.fakeSpawns; i++) SpawnAd();
            if (!fakeSaid)
            {
                fakeSaid = true;
                Say(fakeLines);
            }
        }

        /// <summary>An ad closed for good (with its confirmation): it counts.</summary>
        void Shut(PcPopup ad)
        {
            if (ad.Confirmation) Drop(ad.Confirmation);
            ad.Confirmation = null;
            Drop(ad);
            closed++;
            Cue(closeCue);
            // The winning close ends the round at once: no click in the same frame reaches the windows left.
            if (Attacking && closed >= settings.closeToWin) Win();
        }

        void Drop(PcPopup window)
        {
            if (!window) return;
            windows.Remove(window);
            window.Collapse();
        }

        // ---------- tricks ----------

        void Creep(PcPopup window, float dt)
        {
            var bounds = Bounds(window);
            var at = window.Position + window.Drift * dt;
            var drift = window.Drift;
            if (at.x < bounds.xMin || at.x > bounds.xMax) drift.x = -drift.x;
            if (at.y < bounds.yMin || at.y > bounds.yMax) drift.y = -drift.y;
            window.Drift = drift;
            window.MoveTo(new Vector2(Mathf.Clamp(at.x, bounds.xMin, bounds.xMax), Mathf.Clamp(at.y, bounds.yMin, bounds.yMax)));
        }

        void Dodge(PcPopup window, Vector2 pointer)
        {
            var cross = window.CloseRect;
            if (!cross || !cross.gameObject.activeInHierarchy) return;
            var centre = ToRoot(cross.TransformPoint(cross.rect.center));
            float reach = settings.dodgeRadius + Mathf.Max(cross.rect.width, cross.rect.height) * .5f;
            if ((pointer - centre).sqrMagnitude > reach * reach) return;
            window.Dodges++;
            // Somewhere well away from the pointer.
            var best = window.Position;
            float bestDistance = -1;
            for (int i = 0; i < 8; i++)
            {
                var spot = RandomSpot(window);
                float distance = (spot - pointer).sqrMagnitude;
                if (distance > bestDistance) { best = spot; bestDistance = distance; }
            }
            window.MoveTo(best);
            Cue(dodgeCue);
        }

        // ---------- places (anchored to the middle of the windows root) ----------

        Vector2 ToRoot(Vector3 world) => (Vector2)windowsRoot.InverseTransformPoint(world) - windowsRoot.rect.center;

        Rect Area()
        {
            var source = spawnArea ? spawnArea : windowsRoot;
            var corners = new Vector3[4];
            source.GetWorldCorners(corners);
            Vector2 a = ToRoot(corners[0]), b = ToRoot(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>Where the window's pivot may stand with the whole window inside the area.</summary>
        Rect Bounds(PcPopup window)
        {
            var area = Area();
            var rect = window.Rect;
            var size = rect.rect.size;
            var pivot = rect.pivot;
            float xMin = area.xMin + size.x * pivot.x, xMax = area.xMax - size.x * (1 - pivot.x);
            float yMin = area.yMin + size.y * pivot.y, yMax = area.yMax - size.y * (1 - pivot.y);
            if (xMin > xMax) xMin = xMax = (xMin + xMax) * .5f;
            if (yMin > yMax) yMin = yMax = (yMin + yMax) * .5f;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        Vector2 RandomSpot(PcPopup window)
        {
            var bounds = Bounds(window);
            return new Vector2(Mathf.Round(Mathf.Lerp(bounds.xMin, bounds.xMax, (float)random.NextDouble())),
                Mathf.Round(Mathf.Lerp(bounds.yMin, bounds.yMax, (float)random.NextDouble())));
        }

        Vector2 Fit(PcPopup window, Vector2 at)
        {
            var bounds = Bounds(window);
            return new Vector2(Mathf.Round(Mathf.Clamp(at.x, bounds.xMin, bounds.xMax)), Mathf.Round(Mathf.Clamp(at.y, bounds.yMin, bounds.yMax)));
        }

        bool Pointer(out Vector2 at)
        {
            at = default;
            var raycaster = computer ? computer.raycaster : null;
            if (!raycaster || !windowsRoot || Mouse.current == null) return false;
            if (!raycaster.ToLocal(windowsRoot, Mouse.current.position.ReadValue(), out var local)) return false;
            at = local - windowsRoot.rect.center;
            return true;
        }

        // ---------- the blue screen ----------

        /// <summary>Called by <see cref="PcComputer.Crash"/> (now, or at the start-up after a walked-out attack): the virus takes its share.</summary>
        public void ShowCrash()
        {
            Clean();
            armed = false;
            // No new roll right after the restart: this sitting had its attack.
            sitting = true;
            var wallet = computer && computer.workshop ? computer.workshop.Model.Wallet : null;
            int penalty = wallet != null ? PcPopupRules.Penalty(wallet.Balance, settings) : 0;
            if (penalty > 0) wallet.TrySpend(penalty);
            state.attackPending = false;
            Changed?.Invoke();
            if (crashText) crashText.text = Fill(crashTemplate, 0, penalty);
            ShowCrashScreen(true);
            crashShown = true;
            crashUntil = Time.unscaledTime + settings.crashSeconds;
            afterReboot = Fill(Pick(lostHere ? lostLines : escapeLines), 0, penalty);
            lostHere = false;
            if (helper) helper.Hide();
            Cue(crashCue);
        }

        void UpdateCrash()
        {
            if (!computer || !computer.Crashed)
            {
                // Pulled away mid-crash: the coins are already gone, the next start-up is a full one.
                crashShown = false;
                afterReboot = null;
                ShowCrashScreen(false);
                return;
            }
            if (Time.unscaledTime < crashUntil) return;
            crashShown = false;
            ShowCrashScreen(false);
            computer.Reboot();
        }

        void ShowCrashScreen(bool on)
        {
            if (!crashScreen) return;
            crashScreen.alpha = on ? 1 : 0;
            crashScreen.interactable = false;
            crashScreen.blocksRaycasts = on;
        }

        // ---------- voice and sound ----------

        string Pick(string[] lines) => lines == null || lines.Length == 0 ? null : lines[random.Next(lines.Length)];

        string Fill(string line, int reward, int penalty)
        {
            if (string.IsNullOrEmpty(line)) return line;
            line = PcPopupRules.Fill(line, "reward", reward);
            line = PcPopupRules.Fill(line, "closed", closed);
            line = PcPopupRules.Fill(line, "record", state.record);
            return PcPopupRules.Fill(line, "penalty", penalty);
        }

        void Say(string[] lines, int reward = 0, int penalty = 0)
        {
            var line = Fill(Pick(lines), reward, penalty);
            if (helper && !string.IsNullOrEmpty(line)) helper.Remark(line);
        }

        void Cue(SfxCue cue, float pitch = 1)
        {
            if (cue && computer && computer.sfx) computer.sfx.Play(cue, null, 1, pitch);
        }
    }
}
