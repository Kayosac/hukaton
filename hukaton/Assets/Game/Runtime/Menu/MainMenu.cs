using System.Collections;
using Hakaton.Character;
using Hakaton.DialogueUI;
using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>
    /// The main menu lies over the real game: the scene is already where the player will start (the cave before it is
    /// finished, the shop after), animations and sounds run, gameplay waits. «Продолжить» / «Новая игра» take the menu
    /// away and hand over control on the spot; the HUD and the hand come up, the camera stays where it is.
    /// A new game that has to replace a save loads the scene again and skips the menu on that launch.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        [Header("Scene")]
        public GameLoop game;
        public PlayerControlGate control;
        [Tooltip("Gameplay HUD hidden while the menu is open and faded in when play begins.")]
        public CanvasGroup[] gameplayHud;
        public CardHandView hand;

        [Header("Menu")]
        public Canvas canvas;
        public CanvasGroup visibility;
        [Tooltip("The logo and the four buttons (hidden while settings or a question are open).")]
        public CanvasGroup home;
        public StitchedLinenButton continueButton, newGameButton, settingsButton, exitButton;
        public SettingsPanel settings;
        public MenuDialog dialog;

        [Header("Feel")]
        [Min(0)] public float fadeOutSeconds = .25f, hudFadeInSeconds = .35f;

        [Header("Music")]
        [Tooltip("The menu track (routed to the Music mixer group). Plays once from the top when the menu opens, no loop; " +
                 "fades out when play begins.")]
        public AudioSource music;
        [Min(0)] public float musicFadeOutSeconds = 1.2f;

        /// <summary>Set before a reload that should start playing straight away (a confirmed new game, tests).</summary>
        public static bool SkipNextLaunch;

        public bool IsOpen { get; private set; }
        bool readied;

        void Awake()
        {
            continueButton.onClick.AddListener(Continue);
            newGameButton.onClick.AddListener(NewGame);
            settingsButton.onClick.AddListener(OpenSettings);
            exitButton.onClick.AddListener(Exit);
            // The browser build cannot close itself: no «Выход» there.
            if (Application.platform == RuntimePlatform.WebGLPlayer) exitButton.gameObject.SetActive(false);
            if (SkipNextLaunch)
            {
                SkipNextLaunch = false;
                MenuView.Show(visibility, false);
                canvas.enabled = false;
                return;
            }
            Open();
        }

        void Open()
        {
            IsOpen = true;
            readied = false;
            canvas.enabled = true;
            MenuView.Show(visibility, true);
            ShowHome(true);
            // Nothing can be pressed until the game has loaded the save and knows what «Продолжить» means.
            visibility.interactable = false;
            if (control) control.SetBlocked(this, true);
            GamePause.Suspend(this, true);
            SetHudAlpha(0);
            if (hand) hand.SetLowered(true);
            if (music && music.clip)
            {
                music.loop = false;
                music.Play();
            }
        }

        void Update()
        {
            if (!IsOpen) return;
            if (!readied && game && game.Ready)
            {
                readied = true;
                continueButton.interactable = game.LoadedFromSave;
                visibility.interactable = true;
            }
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || GamePause.EscapeConsumed) return;
            // Escape closes the window on top; the menu itself stays.
            if (dialog.IsOpen) { dialog.Cancel(); GamePause.ConsumeEscape(); }
            else if (settings.IsOpen) { settings.Back(); GamePause.ConsumeEscape(); }
        }

        void OnDestroy()
        {
            GamePause.Suspend(this, false);
            if (control) control.SetBlocked(this, false);
        }

        void ShowHome(bool visible) => MenuView.Show(home, visible);

        // ---------- buttons ----------

        void Continue()
        {
            if (!IsOpen || !game.LoadedFromSave) return;
            StartPlaying();
        }

        void NewGame()
        {
            if (!IsOpen) return;
            bool replaces = game.LoadedFromSave;
            bool canSkip = MenuProgress.TutorialCompleted;
            // Nothing to replace and nothing to choose: the new game is the one already waiting behind the menu.
            if (!replaces && !canSkip && game.FreshStart) { StartPlaying(); return; }
            ShowHome(false);
            dialog.Open("Начать новую игру?", replaces ? "Текущее сохранение будет заменено." : null, canSkip,
                "Отмена", "Начать", StitchedLinenButton.Tone.Positive, BeginNewGame, () => ShowHome(true));
        }

        void BeginNewGame(bool skipTutorial)
        {
            // Already standing at the start of a fresh game: just play it.
            if (!skipTutorial && !game.LoadedFromSave && game.FreshStart && game.InCave) { StartPlaying(); return; }
            SkipNextLaunch = true;
            game.StartOver(skipTutorial ? NewGameStart.AfterTutorial : NewGameStart.Cave);
        }

        void OpenSettings()
        {
            if (!IsOpen) return;
            ShowHome(false);
            settings.Open(() => ShowHome(true));
        }

        void Exit()
        {
            if (!IsOpen) return;
            game.QuitGame();
        }

        // ---------- into the game ----------

        void StartPlaying()
        {
            IsOpen = false;
            visibility.interactable = false;
            GamePause.Suspend(this, false);
            if (control)
            {
                control.SetBlocked(this, false);
                control.CaptureCursor();
            }
            if (hand) hand.SetLowered(false);
            StartCoroutine(FadeAway());
            if (music && music.isPlaying) StartCoroutine(FadeMusicOut());
        }

        IEnumerator FadeMusicOut()
        {
            float start = music.volume, time = 0;
            while (time < musicFadeOutSeconds && music.isPlaying)
            {
                time += Time.unscaledDeltaTime;
                music.volume = Mathf.Lerp(start, 0, time / musicFadeOutSeconds);
                yield return null;
            }
            music.Stop();
            music.volume = start;
        }

        IEnumerator FadeAway()
        {
            float time = 0, menuStart = visibility.alpha;
            float total = Mathf.Max(fadeOutSeconds, hudFadeInSeconds);
            while (time < total)
            {
                time += Time.unscaledDeltaTime;
                visibility.alpha = Mathf.Lerp(menuStart, 0, fadeOutSeconds > 0 ? time / fadeOutSeconds : 1);
                SetHudAlpha(hudFadeInSeconds > 0 ? Mathf.Clamp01(time / hudFadeInSeconds) : 1);
                yield return null;
            }
            MenuView.Show(visibility, false);
            SetHudAlpha(1);
            canvas.enabled = false;
        }

        void SetHudAlpha(float alpha)
        {
            if (gameplayHud == null) return;
            foreach (var group in gameplayHud) if (group) group.alpha = alpha;
        }
    }
}
