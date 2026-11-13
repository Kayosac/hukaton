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
        public FirstPersonMotor player;
        [Tooltip("Special items (board «Спецпредметы»): sold by the goblin dealer once open, kept forever, used on outings with Q.")]
        public SpecialItemsHost specialItems;
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
        [Tooltip("Celebration burst in front of the player when the revenue goal is reached.")]
        public VfxBurst goalConfetti;
        [Min(.05f)] public float departFadeSeconds = .35f;
        [Header("Progress")]
        [Min(1)] public int revenueGoal = 666;
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
        /// <summary>Map points marked «after the revenue goal» (the endless world) appear only once it is reached.</summary>
        public bool PlaceChoiceUnlocked => GoalReached;
        public string SavePath => Path.Combine(Application.persistentDataPath, "Hakaton", EditorSaveFile ?? saveFile);
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
        /// <summary>The goblin is in the shop with his word about deep water.</summary>
        public bool LectureActive => lecture && lecture.Active;
        /// <summary>The drowning lecture was heard to the end (saved): later drownings only get the notice.</summary>
        public bool DrownLectureGiven => drownLectureGiven;
        bool restoring, dirty, goalCelebrated, preserveInvalidSave, startingOver, drownLectureGiven, godMet, godLectureRunning;
        // The return notice waits until the goblin has had his say.
        string noticeAfterLecture;
        // Survives the scene reload of StartOver; read once by the next new game.
        static NewGameStart? pendingStart;
        GoblinCardShop cardShop => services.cardShop;
        long activeOuting;
        bool shopWasOpen;
        int salesAtOpen;
        float noticeUntil, nextTimedSave;

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
            // Special items: the phone and the player open after the god (once the goblin has had his word); the dealer sells them, the PC grows the battery.
            if (specialItems)
            {
                specialItems.UnlockCheck = unlock => unlock == SpecialItemUnlock.Always || unlock == SpecialItemUnlock.FirstGoal && GoalReached
                    // The goblin's word after the god ends with «у меня есть для тебя кое-что интересное»: the items open then.
                    || unlock == SpecialItemUnlock.AfterGod && godMet && !godLectureRunning;
                cardShop.SpecialItems = specialItems;
                if (services.computer) services.computer.SpecialItems = specialItems;
                specialItems.Inventory.Changed += MarkDirty;
            }
            // The bulk buyer («Скупщик») comes only once the shop has made its revenue goal. The session checks its own
            // revenue against it, so the unlock holds after loading a save and after the cheat completes the goal.
            trade.Session.BulkBuyerUnlockRevenue = revenueGoal;
            trade.ExpeditionRequested += TryDepartFromDoor;
            workshop.OutingCompleted += CompleteOuting;
            trade.Session.Changed += MarkDirty;
            workshop.Model.Changed += MarkDirty;
            if (outingMap) outingMap.Changed += MarkDirty;
            cardShop.Visits.Changed += MarkDirty;
            tutorial.Changed += MarkDirty;
            if (player.stamina) player.stamina.Upgrade.Changed += MarkDirty;
            if (lecture) lecture.Finished += OnLectureFinished;
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
            if (afterTutorial) dirty = true;
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
            if (specialItems) specialItems.Inventory.Changed -= MarkDirty;
            if (lecture) lecture.Finished -= OnLectureFinished;
        }
        void OnApplicationQuit() { SaveNow(); }
        void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
        void OnDisable() { if (Ready) SaveNow(); }
        void MarkDirty() { if (!restoring) dirty = true; }
        void LateUpdate()
        {
            if (!Ready) return;
            TrackShift();
            // The whole tutorial is behind the player: a later new game may skip it.
            if (Phase == GamePhase.Shop && !tutorial.Active && !InCave) MenuProgress.MarkTutorialCompleted();
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
        /// <summary>A worked shift (a sale, then the shop closed) lets the goblin card dealer come again.</summary>
        void TrackShift()
        {
            bool open = trade.Session.IsOpen;
            if (open && !shopWasOpen) salesAtOpen = trade.Session.SalesCount;
            if (!open && shopWasOpen) cardShop.Visits.ShiftClosed(trade.Session.SalesCount > salesAtOpen);
            shopWasOpen = open;
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
            // labels: their shadows mirror activeSelf). An older save below the goal lists it again.
            bool goalListed = !GoalReached;
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
            // the endless world grows next to it once the goal is reached.
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
            expedition.NextPlace = !PlaceChoiceUnlocked && expedition.readyWorld ? OutingPlace.Ready : OutingPlace.Endless;
            StartCoroutine(DepartWithTransition());
        }

        bool MapSiteUnlocked(OutingMap.Site site) => site.unlock == OutingMapUnlock.GodMeeting
            ? site.place == OutingPlace.God && godVisit && PlaceChoiceUnlocked && !godMet
            : expedition.HasPlace(site.place)
              && (site.unlock == OutingMapUnlock.Always || site.unlock == OutingMapUnlock.RevenueGoal && PlaceChoiceUnlocked);

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

        /// <summary>Cheats: the god's «?» comes back on the map (with the revenue goal) for another trip.</summary>
        public void ForgetGodMeeting()
        {
            godMet = false;
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
            int kept = delivery.Sum(x => x.count);
            string notice = defeated ? "Вы вернулись после поражения. Сохранено предметов: " + kept + ".\nЗдоровье восстановлено; карты и улучшения целы."
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
            cards = workshop.Model.Save(), shop = trade.Session.Save(), goblin = cardShop.Visits.Save(), tutorial = tutorial.Save(),
            player = new PlayerSave { staminaLevel = player.stamina ? player.stamina.Upgrade.Level : 0 },
            map = outingMap ? outingMap.Save() : null, godMet = godMet,
            specialItems = specialItems ? specialItems.Inventory.Save() : null
        };
        public bool ValidateSave(GameSave save) => save != null && save.version == 1 && save.coins >= 0
            && save.cards != null && save.shop != null && save.cards.coins == save.coins
            && workshop.Model.CanRestoreProgress(save.cards) && trade.Session.CanRestore(save.shop)
            && (save.player == null || save.player.staminaLevel >= 0);
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
            godMet = save.godMet;
            if (outingMap)
            {
                outingMap.Restore(save.map);
                if (godMet) outingMap.Forget(outingMap.SiteFor(OutingPlace.God));
            }
            if (specialItems) specialItems.Restore(save.specialItems);
            goalCelebrated = save.goalCelebrated;
            drownLectureGiven = save.drownLectureGiven;
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
