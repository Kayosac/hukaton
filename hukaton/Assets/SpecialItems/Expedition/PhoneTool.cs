using System.Collections;
using System.Collections.Generic;
using Hakaton.Computer;
using Hakaton.Decks.Unity;
using Hakaton.DialogueUI;
using Hakaton.Expeditions;
using Hakaton.Feel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// The phone (board «Спецпредметы», frames «Телефон»): a worn pink flip phone in the left hand, bottom left, with a
    /// one-armed bandit on its screen, played alongside the cards. Tab pulls the lever, Tab again stops the reels (they
    /// stop by themselves after a while); the outcome is drawn first and the reels are made to show it, so stopping is
    /// pure chance. A spin costs the bet, taken straight from the player's balance, and one charge of the battery, which is
    /// full at the start of every outing. Money won goes straight into the balance too (never lost on a defeat); the card
    /// prize goes straight into the hand, one-use, over the hand's limit.
    /// </summary>
    public sealed class PhoneTool : SpecialItemTool
    {
        enum State { Hidden, Drawing, Ready, Spinning, Stopping, Result, Stowing }

        [Header("Phone in the hand")]
        [Tooltip("The hand's canvas; off while the phone is put away.")]
        public Canvas canvas;
        [Tooltip("The hand with the phone; its pivot is at the wrist, which it turns around.")]
        public RectTransform phone;
        [Tooltip("The hand and phone drawing (the special items' hand-drawn material).")]
        public Image body;
        [Tooltip("Closed, lid lifting, lid open over the hinge, fully open.")]
        public Sprite[] openingFrames = new Sprite[4];
        [Tooltip("The display's own canvas (180 × 204 pixels), drawn into the screen's texture by its camera.")]
        public GameObject display;
        [Tooltip("The glass: the display's texture through the screen shader (the PC's tube, flat), on the phone.")]
        public CrtScreen screen;

        [Header("Taking out")]
        [Tooltip("Where the hand rests and how it comes in and moves (shared with the other items in the hand).")]
        public HandHold hold = new HandHold();
        [Min(.01f)] public float enterSeconds = .24f;
        [Tooltip("A beat between the hand arriving and the lid opening: two separate movements.")]
        [Min(0)] public float openPause = .06f;
        [Min(.01f)] public float frameSeconds = .045f;
        [Tooltip("The lid snaps open: the phone hops up this fast (reference pixels per second).")]
        public float openHop = 260;
        [Min(.01f)] public float leaveSeconds = .18f;

        [Header("Screens")]
        public GameObject reelsScreen;
        public GameObject winScreen, jackpotScreen, callScreen, dischargedScreen;

        [Header("Reels screen")]
        public PhoneReel[] reels = new PhoneReel[SlotMachine.Reels];
        public Image battery;
        [Tooltip("Empty to full: the battery's five drawings.")]
        public Sprite[] batteryLevels = new Sprite[5];
        public Image bet;
        public RectTransform tabKey;

        [Header("Win and jackpot")]
        public TMP_Text winAmount;
        public RectTransform winCoin;
        public TMP_Text jackpotAmount;
        public RectTransform jackpotCoin;
        [Tooltip("Pixels between the amount and the coin; the pair is centred on the display.")]
        public float amountGap = 8;

        [Header("The goblin's call")]
        public TMP_Text callTimer;
        public DialogueBubbleLayout speech;
        public CanvasGroup speechGroup;
        [Tooltip("The phone rings this long before he speaks.")]
        [Min(0)] public float ringBeforeAnswer = .9f;

        [Header("Sound")]
        public SfxCue openSound;
        public SfxCue closeSound, leverSound, spinLoop, reelStopSound, winSound, loseSound, jackpotSound, deadSound,
            refuseSound, ringSound, voiceSound, cardSound, curseSound, boostSound, healSound;

        [Header("Effects (Retro Arsenal)")]
        public VfxBurst winCoins;
        public VfxBurst jackpotCoins, jackpotConfetti;
        [Tooltip("Three hearts: the healing burst in front of the eyes, and the sparkles around it.")]
        public VfxBurst healBurst, healSparkle;
        public Color healColour = new Color(.45f, 1f, .55f);
        public Color coinColour = new Color(1f, .82f, .3f);

        State state = State.Hidden;
        float stateTime, spinStarted, resultUntil;
        int charges, capacity, stopped;
        PhoneOutcome outcome;
        PhoneSymbol[] symbols = { PhoneSymbol.Coin, PhoneSymbol.Card, PhoneSymbol.Heart };
        Coroutine motion;
        float callStarted;
        bool callAnswered;
        GameObject spinOwner, ringOwner;
        readonly System.Random random = new System.Random();
        // The way in or out: 0 away, 1 in place.
        float slide;
        Color betColour = Color.white;

        PhoneSettings Settings => Items ? Items.phone : null;
        public override bool IsOut => state != State.Hidden && state != State.Stowing;
        public override string ControlHint => state switch
        {
            State.Spinning => "Tab — стоп",
            State.Result => callScreen && callScreen.activeSelf ? "Tab — сбросить и крутить" : "Tab — крутить ещё",
            State.Stopping => "",
            _ => charges > 0 ? "Tab — крутить (" + (Settings ? Settings.bet : 10) + " монет)" : "Батарея села",
        };
        /// <summary>Spins left on this outing.</summary>
        public int Charges => charges;
        public int Capacity => capacity;
        public PhoneOutcome LastOutcome => outcome;
        public bool Spinning => state == State.Spinning || state == State.Stopping;
        public bool Open => state == State.Ready || state == State.Spinning || state == State.Stopping || state == State.Result;

        void Awake()
        {
            spinOwner = new GameObject("Phone spin sound owner");
            spinOwner.transform.SetParent(transform, false);
            ringOwner = new GameObject("Phone ring sound owner");
            ringOwner.transform.SetParent(transform, false);
            if (bet) betColour = bet.color;
            HideAll();
        }

        void HideAll()
        {
            state = State.Hidden;
            if (canvas) canvas.enabled = false;
            SetDisplay(false);
            SetSpeech(null);
            slide = 0;
            hold.End();
            hold.Apply(phone, 0, 0);
        }

        // ---------- outings ----------

        public override void OutingBegan()
        {
            capacity = Settings ? Settings.SpinsAt(Items.Inventory.PhoneBatteryLevel) : 10;
            charges = capacity;
            if (motion != null) StopCoroutine(motion);
            motion = null;
            StopSounds();
            HideAll();
            for (int i = 0; i < reels.Length; i++) if (reels[i]) reels[i].Show(symbols[i]);
        }

        public override void OutingEnded()
        {
            if (motion != null) StopCoroutine(motion);
            motion = null;
            StopSounds();
            HideAll();
            if (World && World.motor && World.motor.stamina) World.motor.stamina.ClearUnlimited();
        }

        // ---------- in and out of the hand ----------

        public override void Draw()
        {
            if (IsOut || !World || !World.Running) return;
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(DrawRoutine());
        }

        public override void Stow(bool instant = false)
        {
            if (state == State.Hidden) return;
            // A spin in progress is not lost: it is paid for, so its outcome comes at once.
            if (state == State.Spinning || state == State.Stopping) Resolve(false);
            EndResult();
            if (motion != null) StopCoroutine(motion);
            motion = null;
            if (World && World.sfx) World.sfx.StopLoop(spinOwner);
            if (instant || !isActiveAndEnabled) { HideAll(); return; }
            motion = StartCoroutine(StowRoutine());
        }

        IEnumerator DrawRoutine()
        {
            state = State.Drawing;
            if (canvas) canvas.enabled = true;
            SetDisplay(false);
            SetFrame(0);
            hold.Begin(World);
            // One: the hand comes out of the bottom-left corner, turning clockwise, closed.
            World.Play(openSound, null, .7f, .8f);
            for (float t = 0; t < enterSeconds; t += Time.deltaTime)
            {
                slide = t / enterSeconds;
                yield return null;
            }
            slide = 1;
            if (openPause > 0) yield return new WaitForSeconds(openPause);
            // Two: the lid flips open with a little hop.
            World.Play(openSound);
            for (int i = 1; i < 4; i++)
            {
                SetFrame(i);
                if (i == 1) hold.Hop(openHop);
                yield return new WaitForSeconds(frameSeconds);
            }
            SetDisplay(true);
            ShowScreen(charges > 0 ? reelsScreen : dischargedScreen);
            RefreshReelsScreen();
            state = State.Ready;
            motion = null;
        }

        IEnumerator StowRoutine()
        {
            state = State.Stowing;
            SetDisplay(false);
            World.Play(closeSound);
            for (int i = 2; i >= 0; i--)
            {
                SetFrame(i);
                yield return new WaitForSeconds(frameSeconds);
            }
            for (float t = 0; t < leaveSeconds; t += Time.deltaTime)
            {
                slide = 1 - t / leaveSeconds;
                yield return null;
            }
            HideAll();
            motion = null;
        }

        void SetDisplay(bool on)
        {
            if (display) display.SetActive(on);
            if (screen)
            {
                screen.SetPowered(on);
                screen.SetPictureVisible(on);
            }
        }

        void SetFrame(int index)
        {
            if (body && openingFrames != null && index < openingFrames.Length && openingFrames[index]) body.sprite = openingFrames[index];
        }

        // ---------- the bandit ----------

        void Update()
        {
            if (state == State.Hidden || !World) return;
            if (!World.Running) return;
            Ringtone();
            switch (state)
            {
                case State.Spinning:
                    if (Time.time - spinStarted >= (Settings ? Settings.autoStopSeconds : 3)) BeginStop();
                    break;
                case State.Stopping:
                    StepStopping();
                    break;
                case State.Result:
                    StepResult();
                    break;
            }
            if (!World.control.GameplayActive) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) Tab();
        }

        /// <summary>Tab: pulls the lever, stops the reels, or ends what is on the screen and pulls again.</summary>
        public void Tab()
        {
            switch (state)
            {
                case State.Ready: TrySpin(); break;
                case State.Spinning: BeginStop(); break;
                case State.Result:
                    EndResult();
                    if (state == State.Ready) TrySpin();
                    break;
            }
        }

        public bool TrySpin()
        {
            if (state != State.Ready || !Settings) return false;
            if (charges <= 0)
            {
                World.Play(deadSound);
                Shake();
                return false;
            }
            int price = Settings.bet;
            if (!World.workshop.Model.Wallet.TrySpend(price))
            {
                World.Play(refuseSound);
                World.Announce("Не хватает монет на ставку", 2);
                StartCoroutine(BlinkBet());
                return false;
            }
            charges--;
            outcome = SlotMachine.Roll(Settings.chances, random);
            symbols = SlotMachine.Show(outcome, random);
            state = State.Spinning;
            spinStarted = Time.time;
            stopped = 0;
            foreach (var reel in reels) if (reel) reel.Spin();
            World.Play(leverSound);
            if (tabKey) StartCoroutine(PressKey());
            RefreshReelsScreen();
            return true;
        }

        void BeginStop()
        {
            if (state != State.Spinning) return;
            state = State.Stopping;
            stateTime = Time.time;
            stopped = 0;
        }

        void StepStopping()
        {
            float step = Settings ? Settings.reelStopStep : .15f;
            while (stopped < reels.Length && Time.time - stateTime >= stopped * step)
            {
                if (reels[stopped]) reels[stopped].Stop(symbols[stopped]);
                World.Play(reelStopSound, null, 1, 1 + stopped * .06f);
                stopped++;
            }
            bool moving = false;
            foreach (var reel in reels) if (reel && reel.Moving) moving = true;
            if (stopped >= reels.Length && !moving) Resolve(true);
        }

        void LateUpdate()
        {
            if (state != State.Hidden) hold.Apply(phone, slide, Time.deltaTime);
            if (!World || !World.sfx) return;
            if (state == State.Spinning || state == State.Stopping && stopped < reels.Length)
                World.sfx.Loop(spinOwner, spinLoop, World.viewCamera.transform.position);
            else World.sfx.StopLoop(spinOwner);
        }

        /// <summary>The reels are still: the outcome comes. <paramref name="shown"/> false: the phone is being put away.</summary>
        void Resolve(bool shown)
        {
            if (state != State.Spinning && state != State.Stopping) return;
            for (int i = 0; i < reels.Length; i++) if (reels[i]) reels[i].Show(symbols[i]);
            state = State.Result;
            stateTime = Time.time;
            resultUntil = Time.time + .45f;
            var settings = Settings;
            switch (outcome)
            {
                case PhoneOutcome.Nothing:
                    World.Play(loseSound);
                    break;
                case PhoneOutcome.Pair:
                    Money(settings.pairCoins, false, shown);
                    break;
                case PhoneOutcome.Coins:
                    Money(settings.coinsPrize, false, shown);
                    break;
                case PhoneOutcome.Jackpot:
                    Money(settings.jackpotCoins, true, shown);
                    break;
                case PhoneOutcome.Card:
                    GrantCard();
                    break;
                case PhoneOutcome.Heal:
                    Heal();
                    break;
                case PhoneOutcome.Boost:
                    Boost(settings);
                    break;
                case PhoneOutcome.Curse:
                    Curse(settings);
                    break;
                case PhoneOutcome.Call:
                    if (shown) StartCall();
                    break;
            }
            if (!shown) state = State.Ready;
        }

        void Money(int coins, bool jackpot, bool shown)
        {
            World.CreditCoins(coins, coinColour);
            World.Play(jackpot ? jackpotSound : winSound);
            var eye = World.viewCamera.transform;
            Vector3 front = eye.position + eye.forward * 1.6f - eye.right * .55f - eye.up * .35f;
            if (jackpot)
            {
                World.Spawn(jackpotCoins, front, Quaternion.identity, coinColour);
                World.Spawn(jackpotConfetti, eye.position + eye.forward * 2.5f + eye.up * .6f, Quaternion.identity, Color.white);
                if (World.feel) World.feel.PunchFov(-10);
            }
            else if (coins >= Settings.coinsPrize) World.Spawn(winCoins, front, Quaternion.identity, coinColour, .6f);
            if (!shown) return;
            ShowScreen(jackpot ? jackpotScreen : winScreen);
            Amount(jackpot ? jackpotAmount : winAmount, jackpot ? jackpotCoin : winCoin, coins);
            resultUntil = Time.time + Settings.winSeconds;
        }

        void Amount(TMP_Text label, RectTransform coin, int value)
        {
            if (!label) return;
            label.text = "+" + value;
            label.ForceMeshUpdate();
            // The amount and the coin as one group, centred on the display, by whole pixels.
            float width = Mathf.Ceil(label.preferredWidth);
            float group = width + amountGap + (coin ? coin.rect.width : 0);
            float left = Mathf.Round((180 - group) / 2);
            var rect = label.rectTransform;
            rect.anchoredPosition = new Vector2(left, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            if (coin) coin.anchoredPosition = new Vector2(left + width + amountGap, coin.anchoredPosition.y);
        }

        void GrantCard()
        {
            var config = World.workshop.config;
            var pool = new List<CardDefinition>();
            foreach (var card in config.cards)
                if (card && card != config.dummyCard && card.artwork) pool.Add(card);
            var trip = World.workshop.Model.Current;
            if (pool.Count == 0 || trip == null) return;
            var pick = pool[random.Next(pool.Count)];
            // One use, over the hand's limit; R discards it like any card, and it is gone with the outing.
            if (trip.GrantToHand(pick.ToSpec(), 1, 1) == null) return;
            World.Play(cardSound);
            World.Announce("Три карты: «" + pick.displayName + "» в руку — один бросок", 3);
        }

        void Boost(PhoneSettings settings)
        {
            World.Play(boostSound);
            float seconds = settings.boostSeconds;
            if (random.Next(2) == 0 && World.motor.stamina)
            {
                World.motor.stamina.GrantUnlimited(seconds);
                World.Announce("Три молнии: бег без устали " + Mathf.RoundToInt(seconds) + " с", 3);
            }
            else
            {
                World.motor.ApplyModifier(Hakaton.Character.MotorModifier.MoveSpeed, settings.fastRunMultiplier, seconds);
                World.Announce("Три молнии: быстрые ноги " + Mathf.RoundToInt(seconds) + " с", 3);
            }
        }

        void Heal()
        {
            World.vitals.Heal(World.vitals.MaximumHalfHearts);
            World.Play(healSound);
            var eye = World.viewCamera.transform;
            World.Spawn(healBurst, eye.position + eye.forward * 3.2f - eye.up * .3f, Quaternion.LookRotation(-eye.forward), healColour, .9f);
            World.Spawn(healSparkle, eye.position + eye.forward * 3.4f, Quaternion.identity, healColour, .7f);
            World.Announce("Три сердца: здоровье полное", 2.5f);
        }

        /// <summary>Three skulls: the hearts drop to one at once; with only that one left already, it is death.</summary>
        void Curse(PhoneSettings settings)
        {
            World.Play(curseSound);
            var vitals = World.vitals;
            if (vitals.DrainTo(settings.curseKeepHalfHearts))
            {
                World.Announce("Три черепа: осталось одно сердце", 3);
                return;
            }
            if (vitals.Immortal || !vitals.Drain()) return;
            World.Announce("Три черепа: последнее сердце забрали", 3);
            if (World.feel) { World.feel.AddTrauma(.8f); World.feel.Kick(new Vector3(-150, 0, 120)); }
            World.ReturnToShop(true);
        }

        /// <summary>The incoming call rings until the goblin answers.</summary>
        void Ringtone()
        {
            bool calling = state == State.Result && callScreen && callScreen.activeSelf && !callAnswered;
            if (!World.sfx) return;
            if (calling) World.sfx.Loop(ringOwner, ringSound, World.viewCamera.transform.position);
            else World.sfx.StopLoop(ringOwner);
        }

        // ---------- the goblin's call ----------

        void StartCall()
        {
            ShowScreen(callScreen);
            callStarted = Time.time;
            callAnswered = false;
            if (callTimer) callTimer.text = "00:00";
            resultUntil = float.PositiveInfinity;
        }

        void StepCall()
        {
            if (!callAnswered && Time.time - callStarted >= ringBeforeAnswer)
            {
                callAnswered = true;
                callStarted = Time.time;
                var lines = Settings.callLines;
                string line = lines != null && lines.Length > 0 ? lines[random.Next(lines.Length)] : "Алло?";
                SetSpeech(line);
                World.Play(voiceSound);
            }
            if (!callAnswered) return;
            int seconds = Mathf.FloorToInt(Time.time - callStarted);
            if (callTimer) callTimer.text = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            bool typing = speech && speech.IsTyping;
            if (!typing && Time.time - callStarted >= Settings.callSeconds) EndResult();
        }

        void SetSpeech(string line)
        {
            if (!speech) return;
            if (string.IsNullOrEmpty(line))
            {
                if (speechGroup) speechGroup.alpha = 0;
                speech.ForgetLine();
                return;
            }
            speech.SetText(line);
            if (speechGroup) speechGroup.alpha = 1;
        }

        // ---------- results ----------

        void StepResult()
        {
            if (callScreen && callScreen.activeSelf) { StepCall(); return; }
            if (Time.time >= resultUntil) EndResult();
        }

        void EndResult()
        {
            if (state != State.Result) return;
            SetSpeech(null);
            callAnswered = false;
            state = State.Ready;
            ShowScreen(charges > 0 ? reelsScreen : dischargedScreen);
            if (charges <= 0) World.Play(deadSound);
            RefreshReelsScreen();
        }

        void ShowScreen(GameObject screen)
        {
            foreach (var s in new[] { reelsScreen, winScreen, jackpotScreen, callScreen, dischargedScreen })
                if (s) s.SetActive(s == screen);
        }

        void RefreshReelsScreen()
        {
            if (!battery || batteryLevels == null || batteryLevels.Length == 0) return;
            int top = batteryLevels.Length - 1;
            int level = charges <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(top * charges / (float)Mathf.Max(1, capacity)), 1, top);
            battery.sprite = batteryLevels[level];
        }

        void Shake()
        {
            if (!isActiveAndEnabled) return;
            StartCoroutine(ShakeRoutine());
        }

        IEnumerator ShakeRoutine()
        {
            for (int i = 0; i < 6; i++)
            {
                hold.shake = new Vector2(i % 2 == 0 ? 3 : -3, 0);
                yield return new WaitForSeconds(.03f);
            }
            hold.shake = Vector2.zero;
        }

        IEnumerator BlinkBet()
        {
            if (!bet) yield break;
            for (int i = 0; i < 6; i++)
            {
                bet.color = i % 2 == 0 ? new Color(1, .35f, .3f, betColour.a) : betColour;
                yield return new WaitForSeconds(.08f);
            }
            bet.color = betColour;
        }

        IEnumerator PressKey()
        {
            var home = tabKey.anchoredPosition;
            tabKey.anchoredPosition = home + new Vector2(0, -2);
            yield return new WaitForSeconds(.1f);
            tabKey.anchoredPosition = home;
        }

        void StopSounds()
        {
            if (World && World.sfx) { World.sfx.StopLoop(spinOwner); World.sfx.StopLoop(ringOwner); }
        }

        void OnDisable()
        {
            StopSounds();
            hold.End();
        }
    }
}
