using Hakaton.Character;
using Hakaton.DialogueUI;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Game
{
    /// <summary>
    /// Escape during play: the world, the game's clocks and the current line stop under a light dim (no blur).
    /// Available in free play, at the counter, in dialogue and the tutorial; not while the screen is fading (loading,
    /// leaving for an outing, coming back) or while a window with its own Escape is open (the PC, the workshop...).
    /// Leaving for the menu or the desktop asks first only when something would be lost: the outing or the cave.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class PauseMenu : MonoBehaviour
    {
        [Header("Scene")]
        public GameLoop game;
        public PlayerControlGate control;
        public TimeFeel time;
        public ScreenFade fade;
        public MainMenu mainMenu;

        [Header("Menu")]
        public Canvas canvas;
        public CanvasGroup visibility;
        [Tooltip("The title and the four buttons (hidden while settings or a question are open).")]
        public CanvasGroup home;
        public StitchedLinenButton resumeButton, settingsButton, menuButton, quitButton;
        public SettingsPanel settings;
        public MenuDialog dialog;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            resumeButton.onClick.AddListener(Resume);
            settingsButton.onClick.AddListener(OpenSettings);
            menuButton.onClick.AddListener(() => Leave(false));
            quitButton.onClick.AddListener(() => Leave(true));
            if (Application.platform == RuntimePlatform.WebGLPlayer) quitButton.gameObject.SetActive(false);
            MenuView.Show(visibility, false);
            canvas.enabled = false;
        }

        bool CanOpen => game && game.Ready && control && control.PauseAllowed
            && !(mainMenu && mainMenu.IsOpen)
            && !(fade && (fade.Busy || fade.Amount > .01f))
            && !game.trade.ViewTransitioning;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || GamePause.EscapeConsumed) return;
            if (IsOpen)
            {
                if (dialog.IsOpen) dialog.Cancel();
                else if (settings.IsOpen) settings.Back();
                else Resume();
                GamePause.ConsumeEscape();
            }
            else if (CanOpen)
            {
                Open();
                GamePause.ConsumeEscape();
            }
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            SetFrozen(true);
            control.SetBlocked(this, true);
            canvas.enabled = true;
            MenuView.Show(visibility, true);
            MenuView.Show(home, true);
        }

        public void Resume()
        {
            if (!IsOpen) return;
            IsOpen = false;
            settings.Close();
            dialog.Close();
            MenuView.Show(visibility, false);
            canvas.enabled = false;
            SetFrozen(false);
            control.SetBlocked(this, false);
        }

        void SetFrozen(bool frozen)
        {
            if (time) time.SetPaused(frozen);
            else Time.timeScale = frozen ? 0 : 1;
            AudioListener.pause = frozen;
            GamePause.SetPaused(frozen);
        }

        void OnDestroy()
        {
            // A scene reload from the menu: nothing may stay frozen in the next one.
            if (IsOpen) SetFrozen(false);
        }

        void OpenSettings()
        {
            if (!IsOpen) return;
            MenuView.Show(home, false);
            settings.Open(() => MenuView.Show(home, true));
        }

        /// <param name="quit">true: close the game; false: back to the main menu.</param>
        void Leave(bool quit)
        {
            if (!IsOpen) return;
            if (!game.LeavingLosesProgress) { Go(quit); return; }
            string question = quit ? "Выйти из игры?" : "Выйти в главное меню?";
            string warning = game.InCave
                ? "Прогресс пещеры будет потерян. В следующий раз она начнётся заново."
                : "Прогресс вылазки и добыча будут потеряны. Продолжение — из лавки перед вылазкой.";
            MenuView.Show(home, false);
            dialog.Open(question, warning, false, "Остаться", quit ? "Выйти" : "В меню",
                StitchedLinenButton.Tone.Negative, _ => Go(quit), () => MenuView.Show(home, true));
        }

        void Go(bool quit)
        {
            SetFrozen(false);
            IsOpen = false;
            if (quit) game.QuitGame();
            else game.ReturnToMainMenu();
        }
    }
}
