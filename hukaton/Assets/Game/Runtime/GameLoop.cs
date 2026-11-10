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
using Hakaton.ShopBlockout;
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
        public FirstPersonMotor player;
        [Header("Authored phase roots")]
        public GameObject shopGeometry;
        public GameObject expeditionGeometry;
        public Canvas shopHud;
        public Canvas progressCanvas;
        public TMP_Text objectiveLabel, resultLabel;
        [Header("Presentation")]
        public ScreenFade fade;
        public SfxPlayer sfx;
        public SfxCue departCue, titleCue, goalCue;
        [Tooltip("Celebration burst in front of the player when the revenue goal is reached.")]
        public VfxBurst goalConfetti;
        [Min(.05f)] public float departFadeSeconds = .35f;
        [Header("Progress")]
        [Min(1)] public int revenueGoal = 1000;
        public bool persistProgress = true;
        public string saveFile = "hakaton-v1.json";
        [Tooltip("A new game started «after the tutorial» gets what the two lesson sales would have earned (cap 69 + fang 104).")]
        [Min(0)] public int tutorialEarnings = 173;
        [Header("Locomotion tuning")]
        public float shopWalkSpeed = 2.1f, shopSprintSpeed = 3.5f, shopJumpHeight = .45f;
        public float worldWalkSpeed = 4f, worldSprintSpeed = 6.5f, worldJumpHeight = 1.2f;
        public GamePhase Phase { get; private set; }
        public bool GoalReached => trade.Session != null && trade.Session.GrossRevenue >= revenueGoal;
        public string SavePath => Path.Combine(Application.persistentDataPath, "Hakaton", saveFile);
        public string LastNotice { get; private set; }
        public bool Ready { get; private set; }
        bool restoring, dirty, goalCelebrated, preserveInvalidSave, startingOver;
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
                || !objectiveLabel || !resultLabel
                || trade.walletHost != wallet || workshop.walletHost != wallet)
            { Debug.LogError("Hakaton scene references are incomplete. Assign GameLoop in the saved scene.", this); enabled = false; return; }
            trade.ExpeditionRequested += TryDepartFromDoor;
            workshop.OutingCompleted += CompleteOuting;
            trade.Session.Changed += MarkDirty;
            workshop.Model.Changed += MarkDirty;
            cardShop.Visits.Changed += MarkDirty;
            tutorial.Changed += MarkDirty;
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
            SetPhase(GamePhase.Shop);
            // A new game (no save): the cave prologue, then the goblin partner on the counter.
            bool afterTutorial = false;
            if (!loaded && warning == null)
            {
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
            if (workshop) workshop.OutingCompleted -= CompleteOuting;
            if (trade?.Session != null) trade.Session.Changed -= MarkDirty;
            if (workshop?.Model != null) workshop.Model.Changed -= MarkDirty;
            if (services && cardShop) cardShop.Visits.Changed -= MarkDirty;
            if (tutorial) tutorial.Changed -= MarkDirty;
        }
        void OnApplicationQuit() { SaveNow(); }
        void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
        void OnDisable() { if (Ready) SaveNow(); }
        void MarkDirty() { if (!restoring) dirty = true; }
        void LateUpdate()
        {
            if (!Ready) return;
            TrackShift();
            if (Phase == GamePhase.Shop && !expedition.Returning)
            {
                if (GoalReached && !goalCelebrated && !trade.Session.IsOpen && !services.IsOpen)
                {
                    goalCelebrated = true; dirty = true;
                    if (sfx) sfx.Play(goalCue);
                    var eye = trade.World.ViewCamera.transform;
                    VfxBurst.Spawn(goalConfetti, eye.position + eye.forward * 2.5f + Vector3.up * .6f, Quaternion.identity, null, Color.white);
                    if (fade) fade.StartCoroutine(fade.ShowTitle("Лавка процветает!", revenueGoal + " монет выручки · можно продолжать", 2.4f));
                    Notify("Лавка процветает! " + revenueGoal + " монет выручки.\nЦель достигнута — можно продолжать свободную игру.", 10);
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
            if (!progressCanvas || !objectiveLabel || !resultLabel || trade.Session == null)
            {
                if (!progressHudLost)
                {
                    progressHudLost = true;
                    Debug.LogError("GameLoop: Progress HUD was destroyed at runtime (canvas=" + (bool)progressCanvas
                        + ", objective=" + (bool)objectiveLabel + ", result=" + (bool)resultLabel
                        + ", session=" + (trade.Session != null) + "). The revenue HUD is skipped; the game keeps running.", this);
                }
                return;
            }
            progressCanvas.enabled = Phase == GamePhase.Shop && !services.IsOpen && !trade.Session.IsOpen;
            objectiveLabel.text = "- Выручка: " + trade.Session.GrossRevenue + "/" + revenueGoal;
            resultLabel.text = Time.unscaledTime < noticeUntil && string.IsNullOrEmpty(trade.Notice) ? LastNotice : string.Empty;
        }
        public void Notify(string message, float seconds = 5)
        { LastNotice = message; noticeUntil = Time.unscaledTime + seconds; }
        bool departing;
        void TryDepartFromDoor()
        {
            if (departing) return;
            if (!CanDepartNow(out var error)) { if (error != null) Notify(error); return; }
            StartCoroutine(DepartWithTransition());
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
                fade.StartCoroutine(fade.ShowTitle(expedition.locationName, "Колоды собраны · добыча ждёт", 1.4f));
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
            Notify(defeated ? "Вы вернулись после поражения. Сохранено предметов: " + kept + ".\nЗдоровье восстановлено; карты и улучшения целы."
                : kept > 0 ? "Доставлено предметов: " + kept + ". Смена восстановлена.\nВыложите добычу на витрину и откройте лавку."
                : "Вы вернулись без добычи. Остаток смены сохранён.", 8);
            dirty = true;
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

        /// <summary>Skips whatever part of the tutorial is running (cheats, tests).</summary>
        public void SkipTutorial()
        {
            if (cave && cave.Active) cave.Skip();
            tutorial.Skip();
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
            coins = wallet.Wallet.Balance, goalCelebrated = goalCelebrated,
            cards = workshop.Model.Save(), shop = trade.Session.Save(), goblin = cardShop.Visits.Save(), tutorial = tutorial.Save()
        };
        public bool ValidateSave(GameSave save) => save != null && save.version == 1 && save.coins >= 0
            && save.cards != null && save.shop != null && save.cards.coins == save.coins
            && workshop.Model.CanRestoreProgress(save.cards) && trade.Session.CanRestore(save.shop);
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
            goalCelebrated = save.goalCelebrated;
            trade.RefreshPresentation(); workshop.RefreshWorkshop();
            restoring = wasRestoring; dirty = false;
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
