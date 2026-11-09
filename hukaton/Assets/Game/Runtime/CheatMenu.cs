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
        }

        // ---------- Actions (public so tests and other tools can call them) ----------

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
            int handLimit = world.hand.slots.Length;
            if (trip.Deck.Hand.Count >= handLimit) { status = "Рука уже полна (" + handLimit + " карт)."; return null; }
            int maxLevel = world.workshop.config.maximumCardLevel;
            var card = trip.GrantToHand(definition.ToSpec(), Mathf.Clamp(level, 1, maxLevel));
            if (card == null) { status = "Не удалось выдать карту."; return null; }
            world.thrower.Select(trip.Deck.Hand.Count - 1);
            status = "В руке: " + definition.displayName + " / ур. " + card.Level;
            return card;
        }

        // ---------- Presentation ----------

        void BuildStyles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { font = titleFont, fontSize = 32, wordWrap = false };
            body = new GUIStyle(GUI.skin.label) { font = bodyFont, fontSize = 18, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { font = bodyFont, fontSize = 18, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 6, 6) };
            box = new GUIStyle(GUI.skin.box);
            title.normal.textColor = new Color(.95f, .86f, .55f);
            body.normal.textColor = new Color(.92f, .9f, .84f);
        }

        void OnGUI()
        {
            if (!IsOpen || !Available) return;
            BuildStyles();
            GUI.matrix = Matrix4x4.Scale(new Vector3(pixelScale, pixelScale, 1));
            var rect = new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y);
            GUI.Box(rect, GUIContent.none, box);
            GUI.Box(rect, GUIContent.none, box); // second pass darkens the translucent default skin
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, rect.height - 16));
            GUILayout.Label("Читы", title);
            GUILayout.Label("F1 или Esc — закрыть", body);
            GUILayout.Space(8);

            GUILayout.Label("Лавка", title);
            GUI.enabled = game.Phase == GamePhase.Shop && !game.trade.Session.IsOpen;
            if (GUILayout.Button("Случайные товары на все пустые слоты", button)) StockDisplayRandomly();
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
                scroll = GUILayout.BeginScrollView(scroll);
                foreach (var definition in world.workshop.config.cards.Where(c => c))
                    if (GUILayout.Button(definition.displayName, button)) GrantCard(definition, grantLevel);
                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, body);
            GUILayout.EndArea();
        }
    }
}
