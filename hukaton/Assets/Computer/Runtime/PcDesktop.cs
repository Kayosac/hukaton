using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Computer
{
    /// <summary>
    /// The XP-like desktop: icons open windows, the taskbar lists open windows and focuses them, the start menu opens
    /// the windows or switches the PC off, the tray shows the coins and the clock.
    /// </summary>
    public sealed class PcDesktop : MonoBehaviour
    {
        [Serializable]
        public sealed class Program
        {
            public PcWindow window;
            [Tooltip("The desktop icon.")] public Button icon;
            [Tooltip("The start menu entry.")] public Button menuEntry;
            [Tooltip("The window's button on the taskbar.")] public Button taskButton;
        }

        public Program[] programs = Array.Empty<Program>();
        public Sprite taskNormal, taskActive;
        [Header("Start menu")]
        public Button startButton;
        public CanvasGroup startMenu;
        [Tooltip("A transparent full-desktop button under the menu: clicking elsewhere closes it.")]
        public Button menuBlocker;
        public Button shutdownEntry;
        [Header("Tray")]
        public TMP_Text clock;
        public TMP_Text coins;

        /// <summary>Asked to switch the PC off (start menu).</summary>
        public event Action ShutdownRequested;
        public event Action<string> Sound;

        /// <summary>The window that was in front last; reopened after a short wake.</summary>
        public PcWindow LastFocused { get; private set; }

        void Awake()
        {
            foreach (var program in programs)
            {
                var p = program;
                if (p.icon) p.icon.onClick.AddListener(() => Open(p.window));
                if (p.menuEntry) p.menuEntry.onClick.AddListener(() => { CloseMenu(); Open(p.window); });
                if (p.taskButton) p.taskButton.onClick.AddListener(() => Focus(p.window));
                p.window.FocusRequested += Focus;
                p.window.CloseRequested += Close;
                p.window.SetOpen(false);
            }
            if (startButton) startButton.onClick.AddListener(ToggleMenu);
            if (menuBlocker) menuBlocker.onClick.AddListener(CloseMenu);
            if (shutdownEntry) shutdownEntry.onClick.AddListener(() => { CloseMenu(); ShutdownRequested?.Invoke(); });
            CloseMenu();
            RefreshTaskbar();
        }

        public void Open(PcWindow window)
        {
            if (!window) return;
            if (!window.IsOpen) Sound?.Invoke("open");
            window.SetOpen(true);
            Focus(window);
        }

        public void Close(PcWindow window)
        {
            if (!window || !window.IsOpen) return;
            window.SetOpen(false);
            Sound?.Invoke("close");
            PcWindow next = null;
            foreach (var p in programs)
                if (p.window.IsOpen) next = p.window;
            if (next) Focus(next);
            else
            {
                window.SetFocused(false);
                LastFocused = null;
                RefreshTaskbar();
            }
        }

        public void Focus(PcWindow window)
        {
            if (!window || !window.IsOpen) return;
            foreach (var p in programs) p.window.SetFocused(p.window == window);
            LastFocused = window;
            RefreshTaskbar();
        }

        /// <summary>Closes every window (a full start-up begins on an empty desktop).</summary>
        public void CloseAll()
        {
            foreach (var p in programs) { p.window.SetOpen(false); p.window.SetFocused(false); }
            LastFocused = null;
            CloseMenu();
            RefreshTaskbar();
        }

        /// <summary>Topmost open window, if any (Esc closes it first).</summary>
        public PcWindow Front => LastFocused && LastFocused.IsOpen ? LastFocused : null;

        public bool MenuOpen => startMenu && startMenu.alpha > 0;

        public void ToggleMenu()
        {
            if (MenuOpen) CloseMenu();
            else
            {
                SetMenu(true);
                Sound?.Invoke("click");
            }
        }

        public void CloseMenu() => SetMenu(false);

        void SetMenu(bool open)
        {
            if (startMenu)
            {
                startMenu.alpha = open ? 1 : 0;
                startMenu.interactable = startMenu.blocksRaycasts = open;
            }
            if (menuBlocker) menuBlocker.gameObject.SetActive(open);
        }

        void RefreshTaskbar()
        {
            foreach (var p in programs)
            {
                if (!p.taskButton) continue;
                p.taskButton.gameObject.SetActive(p.window.IsOpen);
                var image = p.taskButton.targetGraphic as Image;
                if (image) image.sprite = p.window.Focused ? taskActive : taskNormal;
            }
        }

        public void SetCoins(int value)
        {
            if (coins) coins.text = value.ToString();
        }

        void Update()
        {
            if (clock) clock.text = DateTime.Now.ToString("HH:mm");
        }
    }
}
