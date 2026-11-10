using System;
using System.Globalization;
using Hakaton.DialogueUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hakaton.Game
{
    /// <summary>
    /// The settings window shared by both menus: overall volume, music, effects, mouse sensitivity. Every change applies
    /// at once and is kept apart from the save slot; «Назад» returns to whichever menu opened it.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public CanvasGroup visibility;
        [Tooltip("Whole percent, 0–100.")]
        public Slider master, music, effects;
        [Tooltip("Tenths: 2–30 is 0.2×–3.0×.")]
        public Slider sensitivity;
        public TMP_Text masterValue, musicValue, effectsValue, sensitivityValue;
        public StitchedLinenButton backButton;

        public bool IsOpen { get; private set; }
        Action back;

        void Awake()
        {
            Range(master, 0, 100);
            Range(music, 0, 100);
            Range(effects, 0, 100);
            Range(sensitivity, Mathf.RoundToInt(GameSettings.MinSensitivity * 10), Mathf.RoundToInt(GameSettings.MaxSensitivity * 10));
            master.onValueChanged.AddListener(v => { GameSettings.Master = v / 100; Refresh(); });
            music.onValueChanged.AddListener(v => { GameSettings.Music = v / 100; Refresh(); });
            effects.onValueChanged.AddListener(v => { GameSettings.Effects = v / 100; Refresh(); });
            sensitivity.onValueChanged.AddListener(v => { GameSettings.Sensitivity = v / 10; Refresh(); });
            backButton.onClick.AddListener(Back);
            MenuView.Show(visibility, false);
        }

        static void Range(Slider slider, int min, int max)
        {
            slider.wholeNumbers = true;
            slider.minValue = min;
            slider.maxValue = max;
        }

        public void Open(Action onBack)
        {
            back = onBack;
            master.SetValueWithoutNotify(Mathf.Round(GameSettings.Master * 100));
            music.SetValueWithoutNotify(Mathf.Round(GameSettings.Music * 100));
            effects.SetValueWithoutNotify(Mathf.Round(GameSettings.Effects * 100));
            sensitivity.SetValueWithoutNotify(Mathf.Round(GameSettings.Sensitivity * 10));
            Refresh();
            IsOpen = true;
            MenuView.Show(visibility, true);
        }

        /// <summary>«Назад» or Escape: keep the values on disk and return to the menu that opened the window.</summary>
        public void Back()
        {
            if (!IsOpen) return;
            Close();
            GameSettings.Flush();
            var callback = back;
            back = null;
            callback?.Invoke();
        }

        public void Close()
        {
            IsOpen = false;
            MenuView.Show(visibility, false);
        }

        void Refresh()
        {
            masterValue.text = Percent(GameSettings.Master);
            musicValue.text = Percent(GameSettings.Music);
            effectsValue.text = Percent(GameSettings.Effects);
            sensitivityValue.text = GameSettings.Sensitivity.ToString("0.0", CultureInfo.InvariantCulture) + "×";
        }

        static string Percent(float value) => Mathf.RoundToInt(value * 100) + "%";
    }
}
