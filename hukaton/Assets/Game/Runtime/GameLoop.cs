using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hakaton.Backroom;
using Hakaton.Character;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Economy.Unity;
using Hakaton.Expeditions;
using Hakaton.Feel;
using Hakaton.GodMountains;
using Hakaton.Hapay;
using Hakaton.ShopBlockout;
using Hakaton.SpecialItems;
using Hakaton.Trading;
using Hakaton.Trading.Unity;
using Hakaton.Tutorial;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hakaton.Game
{
    public enum GamePhase { Shop, Expedition }

    /// <summary>Where a new game begins: the cave prologue, or right where the tutorial ends (for testing).</summary>
    public enum NewGameStart { Cave, AfterTutorial }

    /// <summary>Coordinates authored subsystems. Owns transitions and a single save, never constructs the scene.</summary>
    public sealed class GameLoop : MonoBehaviour
    {
        [Header("Authored game systems")]
        public TradingPrototype trade;
        public DeckPrototype workshop;
        public ExpeditionWorld expedition;
        public CoinWalletHost wallet;
        public ShopBackroomServices services;
        [Tooltip("A new game starts with the goblin on the counter.")]
        public ShopTutorial tutorial;
        [Tooltip("A new game opens in the cave prologue; the shop tutorial follows it. Empty: straight to the shop.")]
        public CaveTutorial cave;
        [Tooltip("The map at the door: opened outing points, a card with «Идти» / «Остаться». Empty: straight to the endless world.")]
        public OutingMap outingMap;
        [Tooltip("The one trip to the god: the «?» on the map from the revenue goal until he is met. Empty: no «?».")]
        public GodMountainsVisit godVisit;
        [Tooltip("The goblin's word in the shop after the first drowning (once per save). Empty: just the notice.")]
        public GoblinLecture lecture;
        [Tooltip("The record of the brazier's and the PC's first-open guides (saved with the game).")]
        public Hakaton.Guide.GuideBook guides;
        [TextArea] public string[] drownLecture =
        {
            "Когда-нибудь ты научишься плавать, но не сегодня.",
            "Больше так не рискуй."
        };
        [Tooltip("Back from the god: the goblin waits on the counter (the shop tutorial's goblin) and has a word, then poofs.")]
        [TextArea] public string[] godReturnLecture =
        {
            "Где ты был? Почему ты вернулся с пустыми руками?",
            "Так никуда не годится, партнёр.",
            "Но ладно, в этот раз прощаю, я видел по бухгалтерии. От тебя есть польза.",
            "А у меня есть для тебя кое-что интересное. Заглядывай."
        };
        [Header("The stranger at the end of a shift")]
        [Tooltip("Once the first outing into the endless world is back, each shift that ends by itself (time up or the display " +
                 "sold out) brings one more visitor with this chance — once per save: a stranger (Visitor roster → Stranger, " +
                 "the blacksmith's look) who buys nothing and only talks, his lines read on like any visitor's.")]
        [Range(0, 1)] public float strangerChance = .3f;
        [Tooltip("The bad-luck guard: if the chance has not brought him before, the shift that ends by itself with this number " +
                 "(counted from the first endless outing's return) brings him for sure.")]
        [Min(1)] public int strangerGuaranteedAt = 6;
        [TextArea] public string[] strangerLines =
        {
            "Странно, эта лавка так давно пустовала.",
            "А ты кто такой?",
            "Где прежний хозяин, знаешь?",
            "...",
            "Не хочешь со мной говорить?",
            "Я бы на твоём месте был поаккуратнее."
        };
        [Tooltip("His face (and voice) on each of his lines, in order; missing entries are neutral.")]
        public CustomerMood[] strangerMoods =
        {
            CustomerMood.Neutral, CustomerMood.Neutral, CustomerMood.Neutral,
            CustomerMood.Displeased, CustomerMood.Displeased, CustomerMood.Angry
        };
        [Tooltip("When the shift is over after him, the goblin poofs in on the counter (the shop tutorial's goblin), the view " +
                 "turns to him, he says these lines (E or a click) and poofs away.")]
        [TextArea] public string[] strangerGoblinLines =
        {
            "Кто это был?",
            "Не слушай этого идиота.",
            "За работу, партнёр!",
            "И загляни ко мне. Нашёл тут кое-что от прежнего хозяина."
        };
        [Tooltip("Seconds from the shift's end (the view back from the counter) to the goblin's puff.")]
        [Min(0)] public float strangerGoblinDelay = .6f;
        public FirstPersonMotor player;
        [Tooltip("Special items (board «Спецпредметы»): sold by the goblin dealer once open, kept forever, used on outings with Q.")]
        public SpecialItemsHost specialItems;
        [Tooltip("хаPAY (board «Спецпредметы»): the collectors' site on the PC, opened by the special item «Аккаунт на хаPAY». " +
                 "Its clocks run while the game runs; a sale takes the goods from storage, then the display, into the shop's revenue.")]
        public HapayHost hapay;
        [Tooltip("Id of the special item that opens хаPAY.")]
        public string hapayAccountId = "hapay_account";
        [Header("Authored phase roots")]
        public GameObject shopGeometry;
        public GameObject expeditionGeometry;
        public Canvas shopHud;
        public Canvas progressCanvas;
        public TMP_Text objectiveLabel, resultLabel;
        [Tooltip("The goals list on the shop HUD (title and the revenue goal with their shadows). A completed goal leaves the "
            + "list; the revenue goal is the only one, so the whole block hides once it is reached.")]
        public GameObject goalsBlock;
        [Header("Presentation")]
        public ScreenFade fade;
        public SfxPlayer sfx;
        public SfxCue departCue, titleCue, goalCue;
        [Tooltip("How long the big «new goal» banner holds once the first goal is given (the shop tutorial is over).")]
        [Min(.5f)] public float goalBannerSeconds = 2.8f;
        [Tooltip("Heading size of the «new goal» banner against the location title's (big letters).")]
        [Min(1)] public float goalBannerScale = 1.5f;
        [Tooltip("Size of the «Новая цель лавки» line against the banner's heading (1 = as big).")]
        [Range(.3f, 1.2f)] public float goalCaptionScale = .85f;
        [Tooltip("Colour of the «Новая цель лавки» line; while the banner holds it pulses toward white.")]
        public Color goalCaptionColour = new Color(1, .8f, .1f, 1);
        [Tooltip("Celebration burst in front of the player when the revenue goal is reached.")]
        public VfxBurst goalConfetti;
        [Min(.05f)] public float departFadeSeconds = .35f;
        [Header("Progress")]
        [Min(1)] public int revenueGoal = 404;
        public bool persistProgress = true;
        public string saveFile = "hakaton-v1.json";
        [Tooltip("A new game started «after the tutorial» gets what the two lesson sales would have earned (cap 69 + fang 104).")]
        [Min(0)] public int tutorialEarnings = 173;
        [Header("Locomotion tuning")]
        public float shopWalkSpeed = 2.1f, shopSprintSpeed = 3.5f, shopJumpHeight = .45f;
        public float worldWalkSpeed = 4f, worldSprintSpeed = 6.5f, worldJumpHeight = 1.2f;
        public GamePhase Phase { get; private set; }
        public bool GoalReached => trade.Session != null && trade.Session.GrossRevenue >= revenueGoal;
        public bool CanCompleteActiveGoal => Ready && !InCave && !tutorial.Active && !GoalReached;
        /// <summary>The first goal is the player's once the cave and the shop tutorial are behind him: only then is it listed.</summary>
        public bool GoalGiven => !InCave && !tutorial.Active;
        /// <summary>Map points marked «after the revenue goal» (the god's «?») appear only once it is reached.</summary>
        public bool PlaceChoiceUnlocked => GoalReached;
        /// <summary>The endless (procedural) world opens only once the god has been met (in a scene without the god's
        /// trip, with the revenue goal).</summary>
        public bool EndlessWorldUnlocked => godMet || !godVisit && PlaceChoiceUnlocked;
        /// <summary>The save file, in <see cref="GameSaveFolder.Current"/> (the player's folder; a temporary one under tests).</summary>
        public string SavePath => Path.Combine(GameSaveFolder.Current, EditorSaveFile ?? saveFile);
#if UNITY_EDITOR
        /// <summary>
        /// Dev tooling: an editor (a worktree's) can play on a save file of its own (SessionState «Hakaton.SaveFile», set
        /// per editor process), so its tests never touch the player's progress. Never in a build.
        /// </summary>
        static string EditorSaveFile
        {
            get { string file = UnityEditor.SessionState.GetString("Hakaton.SaveFile", ""); return file.Length > 0 ? file : null; }
        }
#else
        static string EditorSaveFile => null;
#endif
        public string LastNotice { get; private set; }
        public bool Ready { get; private set; }
        /// <summary>This launch continued a saved game (the main menu's «Продолжить»).</summary>
        public bool LoadedFromSave { get; private set; }
        /// <summary>This launch began a new game by itself: no save, nothing broken (the cave, or the shop for tests).</summary>
        public bool FreshStart { get; private set; }
        /// <summary>The cave prologue is running: leaving now starts it again next time.</summary>
        public bool InCave => cave && cave.Active;
        /// <summary>Leaving now loses something: the cave run or the current outing (the shop is saved as it is).</summary>
        public bool LeavingLosesProgress => Ready && (InCave || InGodMountains || Phase == GamePhase.Expedition);
        /// <summary>The trip to the god is running: leaving now leaves him unmet, his «?» stays on the map.</summary>
        public bool InGodMountains => godVisit && godVisit.Active;
        /// <summary>The god has been met (saved): his «?» is gone from the map for good.</summary>
        public bool GodMet => godMet;
        /// <summary>An anthill's Queen has been slain (saved): the ant compass is for sale.</summary>
        public bool QueenSlain => queenSlain;
        [Tooltip("The name of the anthill's Queen (WorldCreature.displayName): slaying her the first time opens the ant compass.")]
        public string queenName = "Матка";
        bool queenSlain;

        void NoteSlain(Hakaton.Expeditions.WorldCreature creature)
        {
            if (queenSlain || !creature || creature.displayName != queenName) return;
            queenSlain = true;
            dirty = true;
        }
        /// <summary>The goblin is in the shop with his word about deep water.</summary>
        public bool LectureActive => lecture && lecture.Active;
        /// <summary>The drowning lecture was heard to the end (saved): later drownings only get the notice.</summary>
        public bool DrownLectureGiven => drownLectureGiven;
        bool restoring, dirty, goalCelebrated, preserveInvalidSave, startingOver, drownLectureGiven, godMet, godLectureRunning;
        // The stranger: an endless outing is back; he has come (saved); sent for this closing; the goblin's word after him.
        bool endlessVisited, strangerVisited, strangerSent, strangerForced, strangerGoblinDue, strangerLectureRunning;
        float strangerGoblinAt;
        // Shifts that ended by themselves since the first endless outing came back, while he had not come yet (saved).
        int strangerChances;
        /// <summary>The stranger has come (saved): he does not come again.</summary>
        public bool StrangerVisited => strangerVisited;
        /// <summary>An outing into the endless world has come back (saved): from now on the stranger may come.</summary>
        public bool EndlessVisited => endlessVisited;
        /// <summary>Shifts that have ended by themselves since then, each a chance for the stranger (saved).</summary>
        public int StrangerChances => strangerChances;
        // The return notice waits until the goblin has had his say.
        string noticeAfterLecture;
        // Survives the scene reload of StartOver; read once by the next new game.
        static NewGameStart? pendingStart;
        GoblinCardShop cardShop => services.cardShop;
        long activeOuting;
        bool shopWasOpen;
        int salesAtOpen;
        float noticeUntil, nextTimedSave;
        // The big «new goal» banner: armed when the tutorial ends in front of the player (or a new game starts after it),
        // shown once the screen is free. A loaded save never shows it again.
        bool goalBannerPending, tutorialWasRunning;
        float goalBannerAt;

        void Start()
        {
            if (!trade || trade.Session == null || !workshop || workshop.Model == null || !expedition || !wallet
                || !services || !services.cardShop || !tutorial || !player || !shopGeometry || !expeditionGeometry || !shopHud || !progressCanvas
                || !objectiveLabel || !resultLabel || !goalsBlock
                || trade.walletHost != wallet || workshop.walletHost != wallet)
            { Debug.LogError("Hakaton scene references are incomplete. Assign GameLoop in the saved scene.", this); enabled = false; return; }
            // The PC sells the shop's shift upgrade («Время смены»); the session is built at runtime, so it is handed over here.
            if (services.computer) services.computer.Shop = trade.Session;
            // «Выносливость» on the PC: the player's own upgrade, handed over the same way.
            if (services.computer && player.stamina) services.computer.Stamina = player.stamina.Upgrade;
            // «Сердца» on the PC: the outing hearts of the player's vitals.
            if (services.computer && expedition.vitals) services.computer.Hearts = expedition.vitals.Upgrade;
            // «Закрой окно» on the PC: never during the shop tutorial or in the cave; its counters are saved.
            if (Popups)
            {
                Popups.Allowed = () => !tutorial.Active && !InCave;
                Popups.Changed += MarkDirty;
            }
            // The frightened Гвоздик: after the stranger, then after хаPAY; never in the tutorial or the cave; saved.
            if (NailFear)
            {
                NailFear.Allowed = () => !tutorial.Active && !InCave;
                NailFear.StrangerMet = () => strangerVisited;
                NailFear.HapayOwned = () => specialItems && specialItems.Inventory.Owns(hapayAccountId);
                NailFear.Changed += MarkDirty;
            }
            // Special items: the phone and the player open after the god (once the goblin has had his word); the dealer sells them, the PC grows the battery.
            if (specialItems)
            {
                specialItems.UnlockCheck = unlock => unlock == SpecialItemUnlock.Always || unlock == SpecialItemUnlock.FirstGoal && GoalReached
                    // The goblin's word after the god ends with «у меня есть для тебя кое-что интересное»: the items open then.
                    || unlock == SpecialItemUnlock.AfterGod && godMet && !godLectureRunning
                    // The goblin's word after the stranger ends with «загляни ко мне…»: the хаPAY account opens then.
                    || unlock == SpecialItemUnlock.AfterStranger && strangerVisited && !strangerGoblinDue && !strangerLectureRunning
                    // The first slain Queen: the ant compass lies on the goblin's table from the next visit.
                    || unlock == SpecialItemUnlock.AfterQueen && queenSlain;
                if (expedition) expedition.CreatureDied += NoteSlain;
                cardShop.SpecialItems = specialItems;
                if (services.computer) services.computer.SpecialItems = specialItems;
                specialItems.Inventory.Changed += MarkDirty;
            }
            if (hapay)
            {
                hapay.Stock = new HapayShopStock(trade.Session, trade.lootCatalog);
                hapay.SellGoods = SellToCollector;
                hapay.ItemName = id => { var item = trade.lootCatalog ? trade.lootCatalog.Find(id) : null; return item ? item.displayName : id; };
                hapay.ItemIcon = id => { var item = trade.lootCatalog ? trade.lootCatalog.Find(id) : null; return item ? item.icon : null; };
                hapay.Market.Changed += MarkDirty;
                if (services.computer) services.computer.Hapay = hapay;
            }
            // The bulk buyer («Скупщик») comes only once the shop has made its revenue goal. The session checks its own
            // revenue against it, so the unlock holds after loading a save and after the cheat completes the goal.
            trade.Session.BulkBuyerUnlockRevenue = revenueGoal;
            // A shift that ends by itself may bring the stranger as its last visitor.
            trade.Session.ClosingVisitor = StrangerAtClosing;
            trade.ExpeditionRequested += TryDepartFromDoor;
            workshop.OutingCompleted += CompleteOuting;
            trade.Session.Changed += MarkDirty;
            workshop.Model.Changed += MarkDirty;
            if (outingMap) outingMap.Changed += MarkDirty;
            cardShop.Visits.Changed += MarkDirty;
            tutorial.Changed += MarkDirty;
            if (player.stamina) player.stamina.Upgrade.Changed += MarkDirty;
            if (expedition.vitals) expedition.vitals.Upgrade.Changed += MarkDirty;
            if (lecture) lecture.Finished += OnLectureFinished;
            if (guides) guides.Progress.Changed += MarkDirty;
            // Anthills are small until a prize chest has been opened (saved): a new game starts with none opened.
            Hakaton.Expeditions.DungeonProgress.Restore(false);
            Hakaton.Expeditions.DungeonProgress.Changed += MarkDirty;
            restoring = true;
            string warning = null;
            bool loaded = false;
            if (persistProgress)
            {
                if (GameSaveStore.Load(SavePath, ValidateSave, out var save, out warning)) loaded = Restore(save);
                else if (warning != null)
                {
                    preserveInvalidSave = !GameSaveStore.PreserveInvalidFiles(SavePath, out var preservationError);
                    if (preserveInvalidSave) warning = preservationError;
                }
            }
            LoadedFromSave = loaded;
            SetPhase(GamePhase.Shop);
            // A new game (no save): the cave prologue, then the goblin partner on the counter.
            bool afterTutorial = false;
            if (!loaded && warning == null)
            {
                FreshStart = true;
                afterTutorial = pendingStart == NewGameStart.AfterTutorial;
                if (afterTutorial) wallet.Wallet.Credit(tutorialEarnings);
                else if (cave) EnterCave();
                else tutorial.Begin();
            }
            pendingStart = null;
            restoring = false; Ready = true;
            if (afterTutorial) { dirty = true; ArmGoalBanner(); }
            tutorialWasRunning = !GoalGiven;
            if (tutorial.Active || cave && cave.Active) Notify(string.Empty, 0);
            else Notify(warning ?? (trade.Session.GrossRevenue == 0 && trade.Session.Inventory.Count == 0
                ? "Колоды готовы. Выйдите через дверь за первой добычей.\nЦель лавки — " + revenueGoal + " монет выручки."
                : "С возвращением. Запас, колоды и выручка сохранены."), 9);
            RefreshProgress();
            nextTimedSave = Time.unscaledTime + 15;
        }

        void OnDestroy()
        {
            if (trade) trade.ExpeditionRequested -= TryDepartFromDoor;
            if (outingMap) outingMap.Changed -= MarkDirty;
            if (workshop) workshop.OutingCompleted -= CompleteOuting;
            if (trade?.Session != null) trade.Session.Changed -= MarkDirty;
            if (workshop?.Model != null) workshop.Model.Changed -= MarkDirty;
            if (services && cardShop) cardShop.Visits.Changed -= MarkDirty;
            if (tutorial) tutorial.Changed -= MarkDirty;
            if (player && player.stamina) player.stamina.Upgrade.Changed -= MarkDirty;
            if (expedition && expedition.vitals) expedition.vitals.Upgrade.Changed -= MarkDirty;
            if (specialItems) specialItems.Inventory.Changed -= MarkDirty;
            if (hapay) hapay.Market.Changed -= MarkDirty;
            if (lecture) lecture.Finished -= OnLectureFinished;
            if (guides) guides.Progress.Changed -= MarkDirty;
            if (Popups) Popups.Changed -= MarkDirty;
            if (NailFear) NailFear.Changed -= MarkDirty;
            Hakaton.Expeditions.DungeonProgress.Changed -= MarkDirty;
        }
        /// <summary>The PC's popup mini-game (part of the backroom PC prefab).</summary>
        Hakaton.Computer.PcPopupAttack Popups => services && services.computer ? services.computer.popups : null;
        Hakaton.Computer.PcNailFear NailFear => services && services.computer ? services.computer.nailFear : null;
        // Quitting (and leaving Play mode in the editor) saves here, while everything is still alive; then the scene is torn
        // down in no particular order, so OnDisable must not save again: the special items and хаPAY would already be
        // destroyed and the save would be overwritten without them (they were lost on every quit).
        bool quitting;
        void OnApplicationQuit() { SaveNow(); quitting = true; }
        void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
        void OnDisable() { if (Ready && !quitting) SaveNow(); }
        void MarkDirty() { if (!restoring) dirty = true; }
        void LateUpdate()
        {
            if (!Ready) return;
            if (Popups) Popups.CountPlayTime(Time.unscaledDeltaTime);
            // Once the endless world is open, the first shift opened with a full display starts with the bulk buyer.
            trade.Session.BulkBuyerPromised = EndlessWorldUnlocked;
            TrackShift();
            // The whole tutorial is behind the player: a later new game may skip it.
            if (Phase == GamePhase.Shop && !tutorial.Active && !InCave) MenuProgress.MarkTutorialCompleted();
            if (tutorialWasRunning && GoalGiven) ArmGoalBanner();
            tutorialWasRunning = !GoalGiven;
            TryShowGoalBanner();
            TrackStranger();
            TickHapay();
            if (Phase == GamePhase.Shop && !expedition.Returning)
            {
                if (GoalReached && !goalCelebrated && !trade.Session.IsOpen && !services.IsOpen && !LectureActive)
                {
                    goalCelebrated = true; dirty = true;
                    if (sfx) sfx.Play(goalCue);
                    var eye = trade.World.ViewCamera.transform;
                    VfxBurst.Spawn(goalConfetti, eye.position + eye.forward * 2.5f + Vector3.up * .6f, Quaternion.identity, null, Color.white);
                    if (fade) fade.StartCoroutine(fade.ShowTitle("Лавка процветает!", revenueGoal + " монет выручки · можно продолжать", 2.4f));
                    Notify("Лавка процветает! " + revenueGoal + " монет выручки.\nЦель достигнута — у двери теперь можно выбрать место вылазки.", 10);
                }
                if (dirty || Time.unscaledTime >= nextTimedSave) SaveNow();
            }
            RefreshProgress();
        }
        /// <summary>хаPAY is open once the account is bought; its clocks run while the game runs (not paused, not in a menu).</summary>
        void TickHapay()
        {
            if (!hapay) return;
            hapay.Open = specialItems && specialItems.Inventory.Owns(hapayAccountId);
            if (!GamePause.Suspended) hapay.Tick(Time.unscaledDeltaTime);
        }

        /// <summary>A collector buys a set on хаPAY: from storage first, then the display, into the shop's revenue.</summary>
        bool SellToCollector(IReadOnlyList<HapayWant> wants, int price)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var want in wants) list.Add(new KeyValuePair<string, int>(want.item, want.count));
            if (!trade.Session.SellSet(list, price)) return false;
            trade.RefreshPresentation();
            dirty = true;
            return true;
        }

        /// <summary>Notes a worked shift (a sale, then the shop closed) for the goblin dealer's save; it no longer gates his call.</summary>
        void TrackShift()
        {
            bool open = trade.Session.IsOpen;
            if (open && !shopWasOpen) salesAtOpen = trade.Session.SalesCount;
            if (!open && shopWasOpen) cardShop.Visits.ShiftClosed(trade.Session.SalesCount > salesAtOpen);
            shopWasOpen = open;
        }
        /// <summary>
        /// The shop is about to close by itself: after the first endless outing, once per save, with <see cref="strangerChance"/>
        /// (for sure on the <see cref="strangerGuaranteedAt"/>-th such closing, or by the cheat), one more visitor — the stranger
        /// who only talks. Null: the shop closes as usual.
        /// </summary>
        CustomerTerms StrangerAtClosing()
        {
            if (tutorial.Active || InCave || strangerLines == null || strangerLines.Length == 0) return null;
            if (!strangerForced)
            {
                if (!endlessVisited || strangerVisited) return null;
                strangerChances++;
                MarkDirty();
                if (!StrangerComes(strangerChances, strangerGuaranteedAt, strangerChance, UnityEngine.Random.value)) return null;
            }
            strangerForced = false;
            strangerSent = true;
            return new CustomerTerms { Name = "Незнакомец", Talk = strangerLines, TalkMoods = strangerMoods };
        }

        /// <summary>
        /// The stranger at the counter counts as come (saved). Once the shift has really ended after him (the shop closed, the
        /// view back from the counter, nothing in front), the goblin poofs in on the counter with his word.
        /// </summary>
        void TrackStranger()
        {
            var customer = trade.Session.CurrentCustomer;
            if (strangerSent && customer != null && customer.IsTalker)
            {
                strangerSent = false;
                strangerGoblinDue = true;
                if (!strangerVisited) { strangerVisited = true; dirty = true; }
            }
            if (!strangerGoblinDue) return;
            if (Phase != GamePhase.Shop || trade.Session.IsOpen || trade.ViewTransitioning || services.IsOpen || LectureActive
                || GamePause.Suspended || expedition.Returning || departing)
            {
                strangerGoblinAt = Time.unscaledTime + strangerGoblinDelay;
                return;
            }
            if (Time.unscaledTime < strangerGoblinAt) return;
            strangerGoblinDue = false;
            if (lecture && tutorial.goblin && !tutorial.Active && strangerGoblinLines != null && strangerGoblinLines.Length > 0)
                strangerLectureRunning = lecture.Play(strangerGoblinLines, tutorial.goblin, true);
        }

        /// <summary>
        /// Does the stranger come at the <paramref name="closing"/>-th shift that ends by itself (from 1)? With
        /// <paramref name="chance"/> against <paramref name="roll"/> (0..1), and for sure from the <paramref name="guaranteedAt"/>-th.
        /// </summary>
        public static bool StrangerComes(int closing, int guaranteedAt, float chance, float roll)
            => closing >= Mathf.Max(1, guaranteedAt) || roll < chance;

        /// <summary>Cheats: the next shift that ends by itself brings the stranger (and the goblin after him), whatever was before.</summary>
        public void ForceStrangerNext()
        {
            strangerForced = true;
            MarkDirty();
        }

        void ArmGoalBanner()
        {
            goalBannerPending = true;
            // Lets the goblin's poof settle first.
            goalBannerAt = Time.unscaledTime + 1;
        }

        /// <summary>The first goal is given: its name comes up in big letters over the shop, once nothing else holds the screen.</summary>
        void TryShowGoalBanner()
        {
            if (!goalBannerPending) return;
            if (GoalReached) { goalBannerPending = false; return; }
            if (Time.unscaledTime < goalBannerAt || Phase != GamePhase.Shop || !GoalGiven || GamePause.Suspended || LectureActive
                || services.IsOpen || trade.Session.IsOpen || expedition.Returning || departing || !fade || fade.Busy) return;
            goalBannerPending = false;
            if (sfx) sfx.Play(titleCue);
            fade.StartCoroutine(fade.ShowBanner(("Выручка: " + revenueGoal + " " + Coins(revenueGoal)).ToUpperInvariant(),
                "Новая цель лавки", goalBannerSeconds, goalBannerScale, goalCaptionScale, goalCaptionColour));
        }
        static string Coins(int n)
        {
            int tens = n % 100, ones = n % 10;
            if (tens >= 11 && tens <= 14) return "монет";
            return ones == 1 ? "монета" : ones >= 2 && ones <= 4 ? "монеты" : "монет";
        }

        bool progressHudLost;
        void RefreshProgress()
        {
            if (!Ready) return;
            if (!progressCanvas || !objectiveLabel || !resultLabel || !goalsBlock || trade.Session == null)
            {
                if (!progressHudLost)
                {
                    progressHudLost = true;
                    Debug.LogError("GameLoop: Progress HUD was destroyed at runtime (canvas=" + (bool)progressCanvas
                        + ", objective=" + (bool)objectiveLabel + ", goals=" + (bool)goalsBlock + ", result=" + (bool)resultLabel
                        + ", session=" + (trade.Session != null) + "). The revenue HUD is skipped; the game keeps running.", this);
                }
                return;
            }
            progressCanvas.enabled = Phase == GamePhase.Shop && !services.IsOpen && !trade.Session.IsOpen;
            // A completed goal leaves the list. The revenue goal is the only one, so its title goes too (the block, not the
            // labels: their shadows mirror activeSelf). An older save below the goal lists it again. During the tutorial
            // the goal is not the player's yet, so nothing is listed until it ends (with the big banner).
            bool goalListed = GoalGiven && !GoalReached;
            if (goalsBlock.activeSelf != goalListed) goalsBlock.SetActive(goalListed);
            if (goalListed) objectiveLabel.text = "- Выручка: " + trade.Session.GrossRevenue + "/" + revenueGoal;
            resultLabel.text = Time.unscaledTime < noticeUntil && string.IsNullOrEmpty(trade.Notice) ? LastNotice : string.Empty;
        }
        public void Notify(string message, float seconds = 5)
        { LastNotice = message; noticeUntil = Time.unscaledTime + seconds; }
        bool departing;
        void TryDepartFromDoor()
        {
            if (departing) return;
            if (!CanDepartNow(out var error)) { if (error != null) Notify(error); return; }
            // The door opens the map. Before the revenue goal it holds only the ready-made location (the old apiary);
            // the god's «?» grows next to it with the goal, and the endless world once the god has been met.
            if (outingMap)
            {
                outingMap.Open(MapSiteUnlocked, MapPlaceName, place =>
                {
                    if (departing) return;
                    if (place == OutingPlace.God)
                    {
                        if (CanVisitGodNow()) StartCoroutine(VisitGod());
                        return;
                    }
                    if (!CanDepartNow(out var refusal)) { if (refusal != null) Notify(refusal); return; }
                    expedition.NextPlace = place;
                    StartCoroutine(DepartWithTransition());
                });
                if (outingMap.IsOpen) return;
            }
            expedition.NextPlace = !EndlessWorldUnlocked && expedition.readyWorld ? OutingPlace.Ready : OutingPlace.Endless;
            StartCoroutine(DepartWithTransition());
        }

        bool MapSiteUnlocked(OutingMap.Site site) => site.unlock == OutingMapUnlock.GodMeeting
            ? site.place == OutingPlace.God && godVisit && PlaceChoiceUnlocked && !godMet
            : expedition.HasPlace(site.place)
              && (site.unlock == OutingMapUnlock.Always || site.unlock == OutingMapUnlock.RevenueGoal && PlaceChoiceUnlocked
                  || site.unlock == OutingMapUnlock.AfterGod && EndlessWorldUnlocked);

        string MapPlaceName(OutingPlace place) => place == OutingPlace.God && godVisit ? godVisit.mapName : expedition.PlaceName(place);

        /// <summary>The god's trip takes no deck: only the door's own conditions apply (no open shop, window or talk).</summary>
        bool CanVisitGodNow() => Ready && godVisit && !godMet && !godVisit.Active && Phase == GamePhase.Shop && !expedition.Returning
            && !trade.Session.IsOpen && !services.IsOpen && !trade.ExitsLocked && !trade.ViewTransitioning
            && !player.control.HasModalOwner && !workshop.Model.IsAway;

        /// <summary>
        /// The «?»: the usual dark of the door but no title (the player must not learn where he goes), the shop saved as
        /// it stands, then the visit loads the mountains and opens the screen itself.
        /// </summary>
        System.Collections.IEnumerator VisitGod()
        {
            departing = true;
            player.control.SetBlocked(this, true);
            if (sfx) sfx.Play(departCue);
            if (fade) yield return fade.FadeTo(1, departFadeSeconds);
            player.control.SetBlocked(this, false);
            departing = false;
            if (!CanVisitGodNow()) { if (fade) yield return fade.FadeTo(0, .6f); yield break; }
            SaveNow();
            SetPhase(GamePhase.Expedition);
            godVisit.Finished += LeaveGodMountains;
            godVisit.Begin();
        }

        /// <summary>The screen is dark and the player stands in the shop: the god was met (saved) or the trip broke off.</summary>
        void LeaveGodMountains(bool met)
        {
            godVisit.Finished -= LeaveGodMountains;
            SetPhase(GamePhase.Shop);
            if (met)
            {
                godMet = true;
                if (outingMap) outingMap.Forget(outingMap.SiteFor(OutingPlace.God));
                // the goblin is on the counter as the picture comes back: where were you, and empty-handed?
                if (lecture && !(tutorial && tutorial.Active)) godLectureRunning = lecture.Play(godReturnLecture, tutorial ? tutorial.goblin : null);
            }
            dirty = true;
            if (fade) StartCoroutine(fade.FadeTo(0, .6f));
        }

        /// <summary>Cheats: the god's «?» comes back on the map (with the revenue goal) for another trip; the endless world
        /// leaves it until he is met again.</summary>
        public void ForgetGodMeeting()
        {
            godMet = false;
            if (outingMap) outingMap.Forget(outingMap.SiteFor(OutingPlace.Endless));
            MarkDirty();
        }

        /// <summary>Cheats: as if the god had been met (no trip, no talk): his «?» leaves the map, the endless world opens.</summary>
        public void MarkGodMet()
        {
            godMet = true;
            if (outingMap) outingMap.Forget(outingMap.SiteFor(OutingPlace.God));
            MarkDirty();
        }

        bool CanDepartNow(out string error)
        {
            error = null;
            if (!Ready || Phase != GamePhase.Shop || expedition.Returning || trade.Session.IsOpen || services.IsOpen || trade.ExitsLocked
                || trade.ViewTransitioning || player.control.HasModalOwner) return false;
            error = workshop.Model.CanDepart();
            return error == null;
        }

        /// <summary>Fade out of the shop, switch phases in the dark, fade into the valley under its title.</summary>
        System.Collections.IEnumerator DepartWithTransition()
        {
            departing = true;
            player.control.SetBlocked(this, true);
            if (sfx) sfx.Play(departCue);
            if (fade) yield return fade.FadeTo(1, departFadeSeconds);
            player.control.SetBlocked(this, false);
            bool departed = TryDepart();
            departing = false;
            if (!fade) yield break;
            if (departed)
            {
                if (sfx) sfx.Play(titleCue);
                fade.StartCoroutine(fade.ShowTitle(expedition.NextLocationName, "Колоды собраны · добыча ждёт", 1.4f));
            }
            yield return fade.FadeTo(0, .6f);
        }

        public bool TryDepart()
        {
            if (!CanDepartNow(out var error)) { if (error != null) Notify(error); return false; }
            // Capture the safe state before cards are shuffled or the world is changed.
            SaveNow();
            restoring = true;
            activeOuting = trade.BeginExternalExpedition();
            if (activeOuting <= 0) { restoring = false; return false; }
            SetPhase(GamePhase.Expedition);
            error = workshop.TryDepart();
            if (error != null)
            {
                trade.CompleteExternalExpedition(activeOuting, Array.Empty<StockDelivery>());
                activeOuting = 0; SetPhase(GamePhase.Shop); Notify(error);
            }
            restoring = false;
            return error == null;
        }
        void CompleteOuting(IReadOnlyList<LootSave> loot, bool defeated)
        {
            if (Phase != GamePhase.Expedition || activeOuting <= 0) return;
            var delivery = loot.Where(x => x.count > 0).Select(x => new StockDelivery(x.id, x.count)).ToArray();
            restoring = true;
            if (!trade.CompleteExternalExpedition(activeOuting, delivery))
            {
                restoring = false;
                Debug.LogError("Outing delivery contains unknown stock. Check saved loot IDs and shop catalogue.", this);
                return;
            }
            activeOuting = 0;
            SetPhase(GamePhase.Shop);
            restoring = false;
            cardShop.Visits.OutingReturned();
            // The first outing into the endless world is back: from now on a shift's end may bring the stranger.
            if (expedition.NextPlace == OutingPlace.Endless) endlessVisited = true;
            int kept = delivery.Sum(x => x.count);
            // After a defeat the world's own line says what came home (DefeatLoot.Notice): «Поражение. Домой пришло …».
            string notice = defeated ? (string.IsNullOrEmpty(expedition.LastOutingNotice) ? "Поражение." : expedition.LastOutingNotice)
                : kept > 0 ? "Доставлено предметов: " + kept + ". Смена восстановлена.\nВыложите добычу на витрину и откройте лавку."
                : "Вы вернулись без добычи. Остаток смены сохранён.";
            // The first drowning: the goblin waits in the shop (the screen is still dark) with a word about deep water;
            // the notice comes once he is gone.
            if (defeated && expedition.LastOutingDrowned && !drownLectureGiven && !tutorial.Active && lecture && lecture.Play(drownLecture))
            { noticeAfterLecture = notice; Notify(string.Empty, 0); }
            else Notify(notice, 8);
            dirty = true;
        }

        void OnLectureFinished()
        {
            // The goblin's word after the stranger: nothing to record.
            if (strangerLectureRunning) { strangerLectureRunning = false; return; }
            // The goblin's word after the god: the special items open now (UnlockCheck), not the drowning lecture.
            if (godLectureRunning) { godLectureRunning = false; dirty = true; return; }
            drownLectureGiven = true;
            dirty = true;
            if (!string.IsNullOrEmpty(noticeAfterLecture)) Notify(noticeAfterLecture, 8);
            noticeAfterLecture = null;
        }

        /// <summary>Cheats: the next drowning brings the goblin's word again.</summary>
        public void ForgetDrownLecture()
        {
            drownLectureGiven = false;
            MarkDirty();
        }

        /// <summary>The brazier's and the PC's first-open guides come again at their next opening.</summary>
        public void ForgetGuides()
        {
            if (guides) guides.Progress.Forget();
        }
        /// <summary>The prologue runs in the expedition phase: nothing is saved there, quitting starts the cave again.</summary>
        void EnterCave()
        {
            SetPhase(GamePhase.Expedition);
            cave.Finished += LeaveCave;
            cave.Begin();
        }

        /// <summary>The screen is dark after the goblin's last line: the shop, the goblin on the counter, light.</summary>
        void LeaveCave()
        {
            cave.Finished -= LeaveCave;
            SetPhase(GamePhase.Shop);
            tutorial.Begin();
            // Out of the dark straight at the partner on the counter.
            if (tutorial.goblin && player.view)
                player.view.ResetPose(Quaternion.LookRotation(tutorial.goblin.transform.position - player.view.pitchPivot.position));
            dirty = true;
            if (fade) StartCoroutine(fade.FadeTo(0, .6f));
        }

        /// <summary>
        /// Cheats: erases the save and its backup (a copy stays next to it as «.before-restart-&lt;time&gt;») and loads the scene again as a new
        /// game — from the cave, or from where the tutorial ends: shop closed, goblin gone, backroom open.
        /// </summary>
        public void StartOver(NewGameStart from)
        {
            startingOver = true;
            GameSaveStore.Discard(SavePath);
            pendingStart = from;
            SceneManager.LoadScene(gameObject.scene.path);
        }

        /// <summary>
        /// Back to the main menu: the shop is saved as it stands; an outing or the cave is dropped (the save on disk is
        /// the shop before it). The scene loads again and opens on the menu over the new starting point.
        /// </summary>
        public void ReturnToMainMenu()
        {
            SaveNow();
            startingOver = true;
            SceneManager.LoadScene(gameObject.scene.path);
        }

        /// <summary>Closes the game under the same rule as <see cref="ReturnToMainMenu"/>.</summary>
        public void QuitGame()
        {
            SaveNow();
            startingOver = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Skips whatever part of the tutorial is running (cheats, tests).</summary>
        public void SkipTutorial()
        {
            if (cave && cave.Active) cave.Skip();
            tutorial.Skip();
        }

        /// <summary>Debug credit to the shared balance, without recording a sale or advancing the revenue goal.</summary>
        public bool AddThousandCoins()
        {
            if (!Ready || wallet.Wallet.Balance > int.MaxValue - 1000) return false;
            wallet.Wallet.Credit(1000);
            MarkDirty();
            if (workshop.EmbeddedOpen) workshop.RefreshWorkshop();
            SaveNow(); // Away from the shop, the dirty state is saved on return.
            return true;
        }

        /// <summary>Completes the current revenue goal; the normal goal celebration and unlocks still apply.</summary>
        public bool CompleteActiveGoal()
        {
            if (!CanCompleteActiveGoal || !trade.Session.CompleteRevenueGoal(revenueGoal)) return false;
            RefreshProgress();
            SaveNow(); // During an outing, Changed keeps the progress dirty until the return to the shop.
            return true;
        }

        void SetPhase(GamePhase phase)
        {
            Phase = phase;
            bool atShop = phase == GamePhase.Shop;
            shopGeometry.SetActive(atShop);
            expeditionGeometry.SetActive(!atShop);
            trade.enabled = atShop;
            shopHud.enabled = atShop;
            services.enabled = atShop;
            player.walkSpeed = atShop ? shopWalkSpeed : worldWalkSpeed;
            player.sprintSpeed = atShop ? shopSprintSpeed : worldSprintSpeed;
            player.jumpHeight = atShop ? shopJumpHeight : worldJumpHeight;
            if (atShop) { expedition.hand.Hide(); workshop.CloseEmbedded(); trade.RefreshPresentation(); }
            else trade.World.HeldItem.SetActive(false);
        }
        public GameSave Capture() => new GameSave {
            coins = wallet.Wallet.Balance, goalCelebrated = goalCelebrated, drownLectureGiven = drownLectureGiven,
            outingFirsts = expedition.SaveFirsts(),
            cards = workshop.Model.Save(), shop = trade.Session.Save(), goblin = cardShop.Visits.Save(), tutorial = tutorial.Save(),
            player = new PlayerSave { staminaLevel = player.stamina ? player.stamina.Upgrade.Level : 0,
                heartsLevel = expedition.vitals ? expedition.vitals.Upgrade.Level : 0 },
            map = outingMap ? outingMap.Save() : null, godMet = godMet, queenSlain = queenSlain,
            anthillPrizeOpened = Hakaton.Expeditions.DungeonProgress.AnthillPrizeOpened,
            endlessVisited = endlessVisited, strangerVisited = strangerVisited, strangerChances = strangerChances,
            specialItems = specialItems ? specialItems.Inventory.Save() : null,
            guides = guides ? guides.Progress.Save() : null,
            hapay = hapay ? hapay.Market.Save() : null,
            popups = Popups ? Popups.Save() : null,
            nailFear = NailFear ? NailFear.Save() : null
        };
        /// <summary>Items taken out of the game. Old saves still hold them in stock: they quietly drop out on load.</summary>
        static readonly string[] RetiredItems = { "common_find", "special_find", "blue_mushroom", "fire_seed", "ice_amber", "wind_feather" };
        static void DropRetiredItems(GameSave save)
        {
            if (save?.shop?.items == null) return;
            save.shop.items.RemoveAll(entry => entry != null && Array.IndexOf(RetiredItems, entry.definitionId) >= 0);
        }

        public bool ValidateSave(GameSave save)
        {
            DropRetiredItems(save);
            return save != null && save.version == 1 && save.coins >= 0
                && save.cards != null && save.shop != null && save.cards.coins == save.coins
                && workshop.Model.CanRestoreProgress(save.cards) && trade.Session.CanRestore(save.shop)
                && (save.player == null || save.player.staminaLevel >= 0);
        }
        public bool Restore(GameSave save)
        {
            if (Phase != GamePhase.Shop || trade.Session.IsOpen || trade.Session.ExpeditionId != 0
                || workshop.Busy || workshop.Model.IsAway || expedition.Returning || !ValidateSave(save)) return false;
            bool wasRestoring = restoring; restoring = true;
            if (!trade.Session.TryRestore(save.shop) || !workshop.Model.TryRestoreProgress(save.cards))
            { restoring = wasRestoring; return false; }
            wallet.Wallet.TryRestoreBalance(save.coins);
            if (save.goblin != null) cardShop.Visits.TryRestore(save.goblin, id => workshop.Model.Spec(id) != null);
            if (save.tutorial != null) tutorial.TryRestore(save.tutorial);
            // Resizes the stamina to the bought steps and fills it (older saves: none bought).
            if (player.stamina) player.stamina.Upgrade.Restore(save.StaminaLevel);
            if (expedition.vitals) expedition.vitals.Upgrade.Restore(save.HeartsLevel);
            godMet = save.godMet;
            queenSlain = save.queenSlain;
            Hakaton.Expeditions.DungeonProgress.Restore(save.anthillPrizeOpened);
            endlessVisited = save.endlessVisited;
            strangerVisited = save.strangerVisited;
            strangerChances = Math.Max(0, save.strangerChances);
            if (outingMap)
            {
                outingMap.Restore(save.map);
                if (godMet) outingMap.Forget(outingMap.SiteFor(OutingPlace.God));
                // Saves from before the rule: the endless world goes until the god has been met.
                else outingMap.Forget(outingMap.SiteFor(OutingPlace.Endless));
            }
            if (specialItems) specialItems.Restore(save.specialItems);
            if (guides) guides.Progress.Restore(save.guides);
            if (hapay) hapay.Market.Restore(save.hapay);
            if (Popups) Popups.Restore(save.popups);
            if (NailFear) NailFear.Restore(save.nailFear);
            goalCelebrated = save.goalCelebrated;
            drownLectureGiven = save.drownLectureGiven;
            // Saves from before the forgiven first drowning: a player who has heard the goblin about water has drowned already.
            expedition.RestoreFirsts(save.outingFirsts ?? new Hakaton.Expeditions.OutingFirstsSave { drownForgiven = save.drownLectureGiven });
            trade.RefreshPresentation(); workshop.RefreshWorkshop();
            restoring = wasRestoring; dirty = false;
            RefreshProgress(); // The goals list follows the restored revenue at once.
            return true;
        }
        public bool SaveNow()
        {
            if (!Ready || !persistProgress || restoring || preserveInvalidSave || startingOver || Phase != GamePhase.Shop || expedition.Returning) return false;
            var snapshot = Capture();
            if (!ValidateSave(snapshot)) { Notify("Сохранение отложено: состояние игры не прошло проверку."); return false; }
            bool success = GameSaveStore.Save(SavePath, snapshot, out var error);
            if (!success) Notify(error, 8);
            else dirty = false;
            nextTimedSave = Time.unscaledTime + 15;
            return success;
        }
    }
}
