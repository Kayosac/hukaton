using Hakaton.Computer;
using UnityEngine;

namespace Hakaton.Game
{
    /// <summary>
    /// The shop's background music («Лавка за стеной»): a very quiet seamless loop that plays in the shop only once the
    /// tutorial is behind the player. Silent in the cave, during the shop tutorial, on outings, under the main menu (it
    /// has its own track) and while the backroom PC is on (so is the PC). Fades in and out instead of cutting; when silent
    /// the loop pauses and later carries on where it stopped.
    /// </summary>
    public sealed class ShopMusic : MonoBehaviour
    {
        public GameLoop game;
        [Tooltip("Looping 2D source on the Music mixer group, not playing on awake; its clip is the shop track.")]
        public AudioSource music;
        [Tooltip("The main menu lies over the live shop with its own track: no shop music while it is open.")]
        public MainMenu menu;
        [Tooltip("The backroom PC plays its own quiet music: the shop music steps aside while the PC is on.")]
        public PcComputer computer;
        [Tooltip("Very quiet: a background, not a soundtrack.")]
        [Range(0, 1)] public float volume = .08f;
        [Min(0)] public float fadeInSeconds = 4f, fadeOutSeconds = 1.5f;

        bool started, paused;

        /// <summary>The music belongs here now: the game runs in the shop after the tutorial, and nothing else is playing.</summary>
        public bool ShouldPlay => game && game.Ready && game.Phase == GamePhase.Shop && !game.InCave
            && game.tutorial && !game.tutorial.Active && !(menu && menu.IsOpen) && !(computer && computer.IsOpen);

        void Update()
        {
            if (!music || !music.clip) return;
            bool on = ShouldPlay;
            if (on && (!started || paused))
            {
                if (!started) { music.loop = true; music.volume = 0; music.Play(); started = true; }
                else music.UnPause();
                paused = false;
            }
            float target = on ? volume : 0;
            float seconds = on ? fadeInSeconds : fadeOutSeconds;
            float step = seconds > 0 ? volume / seconds * Time.unscaledDeltaTime : 1;
            music.volume = Mathf.MoveTowards(music.volume, target, step);
            if (!on && started && !paused && music.volume <= 0) { music.Pause(); paused = true; }
        }

        void OnDisable()
        {
            if (music && started && !paused) { music.volume = 0; music.Pause(); paused = true; }
        }
    }
}
