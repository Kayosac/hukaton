using System.Linq;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Hakaton.Game
{
    /// <summary>
    /// Debug cheat menu toggled with F1. Presentation is immediate-mode GUI so the panel needs no
    /// authored canvas; all game state changes go through the domain helpers on the authored systems.
    /// </summary>
    public sealed class CheatMenu : MonoBehaviour
    {
        [Header("Authored references")]
        public GameLoop game;
        public Font titleFont;
        public Font bodyFont;
        [Header("Behaviour")]
        public Key toggleKey = Key.F1;
        [Tooltip("Second toggle key: on a MacBook F1 needs Fn, so ` (Ё) also works.")]
        public Key alternativeKey = Key.Backquote;
        [Tooltip("Turn off to strip the menu from player builds. It is always available in the Editor.")]
        public bool availableInPlayerBuilds = true;
        [Min(1)] public int pixelScale = 1;
        public Vector2Int panelPosition = new Vector2Int(16, 16);
        public Vector2Int panelSize = new Vector2Int(460, 560);

        public bool IsOpen { get; private set; }
        string status = string.Empty;
        NewGameStart? armedRestart;
        int grantLevel = 1;
        Vector2 scroll;
        GUIStyle title, body, button, box;
        readonly System.Random picker = new System.Random();

        bool Available => Application.isEditor || availableInPlayerBuilds;

        // Safety net: if the saved scene has no menu (e.g. the open scene was saved over the authored one),
        // spawn one next to the GameLoop so the cheats always work.
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (_, __) => EnsureExists(); }
            EnsureExists();
        }

        static void EnsureExists()
        {
            if (FindFirstObjectByType<CheatMenu>(FindObjectsInactive.Include)) return;
            var loop = FindFirstObjectByType<GameLoop>();
            if (!loop) return;
            var go = new GameObject("Cheat Menu (F1, auto)");
            go.SetActive(false);
            go.AddComponent<CheatMenu>().game = loop;
            go.SetActive(true);
        }

        void Awake()
        {
            if (!game) { Debug.LogError("CheatMenu: assign GameLoop in the saved scene.", this); enabled = false; }
        }

        void Update()
        {
            if (!Available || !game.Ready) return;
            var keys = Keyboard.current;
            if (keys == null) return;
            bool toggle = (toggleKey != Key.None && keys[toggleKey].wasPressedThisFrame)
                || (alternativeKey != Key.None && keys[alternativeKey].wasPressedThisFrame);
            if (toggle) SetOpen(!IsOpen);
            else if (IsOpen && keys.escapeKey.wasPressedThisFrame) SetOpen(false);
        }

        void OnDisable() { SetOpen(false); }

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            var control = game && game.player ? game.player.control : null;
            if (control) control.SetBlocked(this, open);
            if (open) status = string.Empty;
            armedRestart = null;
        }

        // ---------- Actions (public so tests and other tools can call them) ----------

        public bool UnlockCollection()
        {
            if (!game.Ready || game.Phase != GamePhase.Shop || game.expedition.Returning)
            { status = "Коллекция доступна после возвращения в лавку."; return false; }
            var model = game.workshop.Model;
            string error = model.UnlockAllCards(out int added);
            if (error != null) { status = error; return false; }
            if (game.workshop.EmbeddedOpen) game.workshop.RefreshWorkshop();
            var shop = game.trade.Session;
            shop.MaximizeShift();
            // The bulk buyer's chance too; his PC track still stays hidden until he has been met.
            shop.MaximizeBulkBuyer();
            // «Выносливость» too: every pip of sprint.
            var stamina = game.player.stamina ? game.player.stamina.Upgrade : null;
            stamina?.Maximize();
            game.SaveNow();
            status = "Добавлено карт: " + added + ". Рука: " + model.HandSize
                + ", колода: " + model.DeckLimit + ", коллекция: " + model.CollectionLimit
                + ", смена: " + Mathf.RoundToInt(shop.ShiftLength) + " с"
                + ", скупщик: " + Mathf.RoundToInt(shop.BulkBuyerChance * 100) + "%"
                + (stamina != null ? ", бег: " + Mathf.RoundToInt(stamina.Seconds) + " с." : ".");
            return true;
        }

        /// <summary>
        /// The phone for free, in the shop or right on an outing (then Q takes it out at once); the dealer's introduction
        /// is skipped. Kept like a bought one.
        /// </summary>
        public bool GrantPhone()
        {
            var items = game.specialItems;
            var phone = items ? items.Find("phone") : null;
            if (!phone) { status = "Телефона в каталоге спецпредметов нет."; return false; }
            if (!game.Ready || game.InCave || game.expedition.Returning) { status = "Телефон выдаётся в лавке или на вылазке."; return false; }
            items.Inventory.MarkIntroduced(phone.id);
            items.Inventory.MarkSeen(phone.id);
            if (!items.Grant(phone)) { status = "Телефон уже есть: Q на вылазке."; return false; }
            game.SaveNow(); // On an outing the change is kept dirty and saved on the way home.
            status = "Телефон получен: Q на вылазке, Tab — автомат.";
            return true;
        }

        /// <summary>Every special item for free (introductions skipped) and the phone's battery at its last step.</summary>
        public bool GrantSpecialItems()
        {
            var items = game.specialItems;
            if (!items || !items.catalog) { status = "Спецпредметов в сцене нет."; return false; }
            if (!game.Ready || game.Phase != GamePhase.Shop || game.expedition.Returning)
            { status = "Спецпредметы выдаются в лавке."; return false; }
            int added = 0;
            foreach (var item in items.catalog.items)
            {
                if (!item) continue;
                items.Inventory.MarkIntroduced(item.id);
                items.Inventory.MarkSeen(item.id);
                if (items.Grant(item)) added++;
            }
            if (items.phone) items.Inventory.SetPhoneBattery(items.phone.BatterySteps);
            game.SaveNow();
            status = "Спецпредметов выдано: " + added + ". Батарея телефона на максимуме.";
            return true;
        }

        /// <summary>
        /// The next visitor the shop generates is the bulk buyer («Скупщик»), whatever his chance, the revenue goal or an
        /// earlier visit this shift; he still needs goods on the display and stays away during the tutorial.
        /// </summary>
        public bool NextVisitorIsBulkBuyer()
        {
            if (!game.Ready || game.Phase != GamePhase.Shop) { status = "Скупщик приходит только в лавку."; return false; }
            var shop = game.trade.Session;
            shop.ForceBulkBuyerNext = true;
            status = shop.IsOpen ? "Следующий посетитель — скупщик."
                : "Откройте лавку: первым придёт скупщик (нужен хотя бы один товар на витрине).";
            return true;
        }

        public bool AddThousandCoins()
        {
            bool granted = game.AddThousandCoins();
            status = granted ? "+1000 монет. Баланс: " + game.wallet.Wallet.Balance
                : "Не удалось добавить монеты: игра не готова или достигнут предел баланса.";
            return granted;
        }

        public bool CompleteActiveGoal()
        {
            bool completed = game.CompleteActiveGoal();
            status = completed ? "Цель выполнена. Выручка: " + game.trade.Session.GrossRevenue + "/" + game.revenueGoal + "."
                : "Нет активной цели. Цель выручки доступна после обучения.";
            return completed;
        }

        /// <summary>The goblin's word about deep water comes again after the next drowning.</summary>
        public bool ResetDrownLecture()
        {
            if (!game.Ready) { status = "Игра не готова."; return false; }
            game.ForgetDrownLecture();
            status = "Гоблин снова встретит в лавке после того, как утонете.";
            return true;
        }

        public int StockDisplayRandomly()
        {
            var trade = game.trade;
            if (game.Phase != GamePhase.Shop) { status = "Витрина доступна только в лавке."; return 0; }
            if (trade.Session.IsOpen) { status = "Сначала закройте лавку."; return 0; }
            int added = trade.Session.StockEmptySlotsRandomly(picker);
            if (added > 0) trade.RefreshPresentation();
            status = added > 0 ? "Добавлено на витрину: " + added : "Все слоты витрины уже заняты.";
            return added;
        }

        public CardCopy GrantCard(CardDefinition definition, int level)
        {
            var world = game.expedition;
            if (!world.Running || definition == null) { status = "Карты выдаются только во время вылазки."; return null; }
            var trip = world.workshop.Model.Current;
            int maxLevel = world.workshop.config.maximumCardLevel;
            var card = trip.GrantToHand(definition.ToSpec(), Mathf.Clamp(level, 1, maxLevel));
            if (card == null) { status = "Не удалось выдать карту."; return null; }
            world.thrower.Select(trip.Deck.Hand.Count - 1);
            status = "В руке: " + definition.displayName + " / ур. " + card.Level;
            return card;
        }

        /// <summary>A second press confirms: the first only arms the button, so a stray click never wipes the progress.</summary>
        void RestartButton(NewGameStart from, string label)
        {
            bool armed = armedRestart == from;
            if (!GUILayout.Button(armed ? label + " — точно?" : label, button)) return;
            if (!armed) { armedRestart = from; return; }
            armedRestart = null;
            SetOpen(false);
            game.StartOver(from);
        }

        // ---------- Presentation ----------

        void BuildStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { font = titleFont, fontSize = 32, wordWrap = false };
            body = new GUIStyle(GUI.skin.label) { font = bodyFont, fontSize = 18, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { font = bodyFont, fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = true,
                padding = new RectOffset(10, 10, 6, 6) };
            box = new GUIStyle(GUI.skin.box);
            title.normal.textColor = new Color(.95f, .86f, .55f);
            body.normal.textColor = new Color(.92f, .9f, .84f);
        }

        void OnGUI()
        {
            if (!IsOpen || !Available) return;
            BuildStyles();
            float scale = pixelScale * Hakaton.Feel.UiReference.ScreenScale; // laid out for Full HD
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var rect = new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y);
            GUI.Box(rect, GUIContent.none, box);
            GUI.Box(rect, GUIContent.none, box); // second pass darkens the translucent default skin
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, rect.height - 16));
            GUILayout.Label("Читы", title);
            GUILayout.Label("F1 / Ё / Esc — закрыть", body);
            GUILayout.Space(8);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("Коллекция и деньги", title);
            GUI.enabled = game.Ready && game.Phase == GamePhase.Shop && !game.expedition.Returning;
            if (GUILayout.Button("Все карты + прокачки", button)) UnlockCollection();
            if (GUILayout.Button("Все спецпредметы (Q на вылазке)", button)) GrantSpecialItems();
            GUI.enabled = game.Ready && !game.InCave && !game.expedition.Returning;
            if (GUILayout.Button("Получить телефон", button)) GrantPhone();
            GUI.enabled = game.Ready;
            if (GUILayout.Button("+1000 монет", button)) AddThousandCoins();
            GUI.enabled = true;
            GUILayout.Space(8);

            GUILayout.Label("Цель", title);
            GUI.enabled = game.CanCompleteActiveGoal;
            if (GUILayout.Button("Выполнить активную цель", button)) CompleteActiveGoal();
            GUI.enabled = game.Ready && game.DrownLectureGiven;
            if (GUILayout.Button("Гоблин про воду — ещё раз", button)) ResetDrownLecture();
            GUI.enabled = true;
            GUILayout.Space(8);

            GUILayout.Label("Новая игра", title);
            RestartButton(NewGameStart.Cave, "С начала (пещера)");
            RestartButton(NewGameStart.AfterTutorial, "Сразу после обучения");
            if (armedRestart.HasValue) GUILayout.Label("Нажмите ещё раз: прогресс сотрётся (копия останется рядом, .before-restart).", body);
            GUILayout.Space(8);

            if (game.tutorial.Active || game.cave && game.cave.Active)
            {
                if (GUILayout.Button("Пропустить туториал", button)) game.SkipTutorial();
                GUILayout.Space(8);
            }

            GUILayout.Label("Лавка", title);
            GUI.enabled = game.Phase == GamePhase.Shop && !game.trade.Session.IsOpen;
            if (GUILayout.Button("Случайные товары на все пустые слоты", button)) StockDisplayRandomly();
            GUI.enabled = game.Phase == GamePhase.Shop;
            if (GUILayout.Button("Следующий — скупщик", button)) NextVisitorIsBulkBuyer();
            GUI.enabled = true;
            GUILayout.Space(8);

            GUILayout.Label("Карта в руку", title);
            var world = game.expedition;
            if (!world.Running) GUILayout.Label("Доступно во время вылазки.", body);
            else
            {
                int maxLevel = world.workshop.config.maximumCardLevel;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Уровень: " + grantLevel, body, GUILayout.Width(140));
                if (GUILayout.Button("-", button, GUILayout.Width(40))) grantLevel = Mathf.Max(1, grantLevel - 1);
                if (GUILayout.Button("+", button, GUILayout.Width(40))) grantLevel = Mathf.Min(maxLevel, grantLevel + 1);
                GUILayout.EndHorizontal();
                grantLevel = Mathf.Clamp(grantLevel, 1, maxLevel);
                foreach (var definition in world.workshop.config.cards.Where(c => c))
                    if (GUILayout.Button(definition.displayName, button)) GrantCard(definition, grantLevel);
            }

            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, body);
            GUILayout.EndArea();
        }
    }
}
