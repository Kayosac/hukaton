using System;
using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>
    /// The demo's settings: three volumes, the mouse sensitivity and the «Счётчик FPS» switch. Stored in PlayerPrefs,
    /// apart from the save slot, applied the moment they change (<see cref="Changed"/>; <see cref="SettingsApplier"/> does
    /// the applying, <see cref="FpsCounter"/> shows or hides itself).
    /// </summary>
    public static class GameSettings
    {
        public const float DefaultMaster = .8f, DefaultMusic = .6f, DefaultEffects = .8f, DefaultSensitivity = 1;
        public const float MinSensitivity = .2f, MaxSensitivity = 3;
        public const bool DefaultShowFps = false;
        const string MasterKey = "penus.settings.master", MusicKey = "penus.settings.music",
            EffectsKey = "penus.settings.effects", SensitivityKey = "penus.settings.sensitivity", FpsKey = "penus.settings.fps";

        static bool loaded, showFps;
        static float master, music, effects, sensitivity;

        public static event Action Changed;

        public static float Master { get { Load(); return master; } set => Set(ref master, Mathf.Clamp01(value), MasterKey); }
        public static float Music { get { Load(); return music; } set => Set(ref music, Mathf.Clamp01(value), MusicKey); }
        public static float Effects { get { Load(); return effects; } set => Set(ref effects, Mathf.Clamp01(value), EffectsKey); }
        public static float Sensitivity
        {
            get { Load(); return sensitivity; }
            set => Set(ref sensitivity, Mathf.Clamp(value, MinSensitivity, MaxSensitivity), SensitivityKey);
        }

        /// <summary>«Счётчик FPS»: the frame-rate readout in the top-right corner. Off by default.</summary>
        public static bool ShowFps
        {
            get { Load(); return showFps; }
            set
            {
                Load();
                if (showFps == value) return;
                showFps = value;
                PlayerPrefs.SetInt(FpsKey, value ? 1 : 0);
                Changed?.Invoke();
            }
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, DefaultMaster));
            music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusic));
            effects = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, DefaultEffects));
            sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity), MinSensitivity, MaxSensitivity);
            showFps = PlayerPrefs.HasKey(FpsKey) ? PlayerPrefs.GetInt(FpsKey) != 0 : DefaultShowFps;
        }

        static void Set(ref float field, float value, string key)
        {
            Load();
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat(key, value);
            Changed?.Invoke();
        }

        /// <summary>Writes the values to disk (PlayerPrefs keeps them in memory until then).</summary>
        public static void Flush() => PlayerPrefs.Save();

        /// <summary>Tests: back to the defaults and forget the stored values.</summary>
        public static void ResetForTests()
        {
            foreach (var key in new[] { MasterKey, MusicKey, EffectsKey, SensitivityKey, FpsKey }) PlayerPrefs.DeleteKey(key);
            loaded = false;
            Changed?.Invoke();
        }

        /// <summary>A 0..1 volume as a mixer attenuation: silence at 0, 0 dB at 1.</summary>
        public static float ToDecibels(float volume) => volume <= .0001f ? -80 : Mathf.Log10(volume) * 20;
    }
}
