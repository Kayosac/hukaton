using System;
using Hakaton.Character;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hakaton.Computer
{
    /// <summary>
    /// The backroom PC. Sitting down is a quick, smooth blink into black; the PC then opens full screen (4:3 with
    /// black bars) and starts up (fully the first time and after every outing, otherwise a short wake). The XP-like
    /// desktop opens the card and improvement windows. Esc, the right mouse button or «Пуск → Выключить» switch it
    /// off, and a blink brings the shop back. Works on the shared collection of <see cref="DeckPrototype"/>.
    /// </summary>
    public sealed class PcComputer : MonoBehaviour
    {
        public enum UiSound { Click, Open, Close, Error }
        enum Phase { Off, Closing, Booting, Running, ShuttingDown, Opening }

        [Header("Game")]
        [Tooltip("The shared collection; its control gate is claimed while the PC is open.")]
        public DeckPrototype workshop;

        [Header("Screen")]
        [Tooltip("The full-screen display canvas: black bars and the 4:3 picture. Its alpha is the fade to and from black.")]
        public Canvas display;
        public CanvasGroup displayGroup;
        [Tooltip("The blink into the PC and back out of it.")]
        [Min(.01f)] public float blinkInSeconds = .16f, blinkOutSeconds = .2f;
        public CrtScreen screen;
        public CrtRaycaster raycaster;
        public PcCursor cursor;
        public PcBoot boot;
        public PcDesktop desktop;
        public PcCardsWindow cards;
        public PcUpgradesWindow upgrades;

        [Header("Sound")]
        public SfxPlayer sfx;
        public SfxCue bootCue, wakeCue, offCue, clickCue, errorCue;
        [Tooltip("2D source for the cards' own sounds (card, coin, merge).")]
        public AudioSource uiSource;

        [Header("Music")]
        [Tooltip("The PC's own quiet looping music (the 'phone earpiece' sound is baked into the clip, so it is the same in WebGL). " +
                 "Fades in only once the desktop is up, fades out when the PC switches off.")]
        public AudioSource music;
        [Range(0, 1)] public float musicVolume = .15f;
        [Min(0)] public float musicFadeInSeconds = 2.5f, musicFadeOutSeconds = .4f;

        Phase phase;
        bool everBooted;
        int bootJourneys;
        AudioSource bootVoice;
        float blinkTime, blinkFrom;
        PlayerControlGate Gate => workshop ? workshop.control : null;
        float fadeVoice = -1, fadeFrom;
        bool fullBoot, musicOn, musicPaused;

        public bool IsOpen => phase != Phase.Off;
        /// <summary>The desktop is up and takes input.</summary>
        public bool Running => phase == Phase.Running;
        public event Action Closed;

        void Awake()
        {
            desktop.ShutdownRequested += Close;
            desktop.Sound += name => Ui(name == "open" ? UiSound.Open : name == "close" ? UiSound.Close : UiSound.Click);
            SetInput(false);
            boot.SetOff();
            SetDisplay(0);
        }

        /// <summary>0: the world; 1: black bars and the tube. Off below a sliver, so it costs nothing in the shop.</summary>
        void SetDisplay(float alpha)
        {
            if (displayGroup)
            {
                displayGroup.alpha = alpha;
                displayGroup.blocksRaycasts = alpha > .99f;
                displayGroup.interactable = false;
            }
            if (display) display.enabled = alpha > .001f;
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3 - 2 * t);
        }

        void OnDisable() => CloseImmediately();

        /// <summary>Sit down at the PC. False while away, while an outing is being delivered, or already open.</summary>
        public bool Open()
        {
            if (phase != Phase.Off || !isActiveAndEnabled || !workshop || workshop.Model.IsAway || workshop.Busy) return false;
            if (Gate) Gate.SetBlocked(this, true);
            screen.SetPictureVisible(false);
            Blink(Phase.Closing);
            return true;
        }

        /// <summary>Switch off (animated) and get up.</summary>
        public void Close()
        {
            switch (phase)
            {
                case Phase.Closing:
                    screen.SetPictureVisible(false);
                    Blink(Phase.Opening);
                    break;
                case Phase.Booting:
                case Phase.Running:
                    SetInput(false);
                    StopBootVoice();
                    MusicOff(false);
                    boot.PowerOff();
                    if (sfx) sfx.Play(offCue);
                    phase = Phase.ShuttingDown;
                    break;
            }
        }

        /// <summary>Off at once, no blink (scene changes, the game loop's transitions).</summary>
        public void CloseImmediately()
        {
            if (phase == Phase.Off) return;
            SetInput(false);
            StopBootVoice();
            MusicOff(true);
            if (boot) boot.SetOff();
            SetDisplay(0);
            Release();
        }

        void Release()
        {
            phase = Phase.Off;
            if (Gate) Gate.SetBlocked(this, false);
            Closed?.Invoke();
        }

        void Blink(Phase next)
        {
            blinkFrom = displayGroup ? displayGroup.alpha : 0;
            blinkTime = 0;
            phase = next;
        }

        void Update()
        {
            FadeBootVoice();
            FadeMusic();
            switch (phase)
            {
                case Phase.Closing:
                {
                    // Eyes shut: the shop goes black, the PC opens in the dark.
                    blinkTime += Time.unscaledDeltaTime;
                    float t = blinkTime / blinkInSeconds;
                    SetDisplay(Mathf.Lerp(blinkFrom, 1, Smooth(t)));
                    if (t >= 1) PowerOn();
                    break;
                }
                case Phase.Booting:
                    if (BackPressed()) { Close(); break; }
                    if (SkipPressed()) { boot.Skip(); FadeOutBootVoice(); }
                    if (boot.State == PcBoot.Mode.Running) BecomeReady();
                    break;
                case Phase.Running:
                    desktop.SetCoins(workshop.Model.Coins);
                    if (BackPressed())
                    {
                        if (desktop.MenuOpen) desktop.CloseMenu();
                        else Close();
                    }
                    break;
                case Phase.ShuttingDown:
                    if (boot.State == PcBoot.Mode.Off)
                    {
                        screen.SetPictureVisible(false);
                        Blink(Phase.Opening);
                    }
                    break;
                case Phase.Opening:
                {
                    // Eyes open again on the shop, exactly where the player was looking.
                    blinkTime += Time.unscaledDeltaTime;
                    float t = blinkTime / blinkOutSeconds;
                    SetDisplay(Mathf.Lerp(blinkFrom, 0, Smooth(t)));
                    if (t >= 1)
                    {
                        SetDisplay(0);
                        Release();
                    }
                    break;
                }
            }
        }

        void PowerOn()
        {
            SetDisplay(1);
            screen.SetPictureVisible(true);
            bool full = !everBooted || bootJourneys != workshop.Model.Journeys;
            fullBoot = full;
            everBooted = true;
            bootJourneys = workshop.Model.Journeys;
            if (full) desktop.CloseAll();
            desktop.SetCoins(workshop.Model.Coins);
            bootVoice = sfx ? sfx.Play(full ? bootCue : wakeCue) : null;
            boot.PowerOn(full);
            phase = Phase.Booting;
        }

        void BecomeReady()
        {
            phase = Phase.Running;
            SetInput(true);
            MusicOn(fullBoot);
            // Windows left open stay open across a short wake; refresh what they show.
            if (cards.window.IsOpen) cards.Refresh();
            if (upgrades.window.IsOpen) upgrades.Refresh();
        }

        void SetInput(bool on)
        {
            if (raycaster) raycaster.enabled = on;
            if (cursor) cursor.enabled = on;
            if (!on && cards) cards.CancelDrag();
            if (!on) desktop.CloseMenu();
        }

        static bool BackPressed()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame || mouse != null && mouse.rightButton.wasPressedThisFrame;
        }

        static bool SkipPressed()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            return keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame)
                || mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        // ---------- sound ----------

        public void Ui(UiSound sound)
        {
            if (!sfx) return;
            sfx.Play(sound == UiSound.Error ? errorCue : clickCue, null, sound == UiSound.Close ? .7f : 1, sound == UiSound.Open ? 1.12f : 1);
        }

        public void Play(AudioClip clip, float volume = .65f)
        {
            if (clip && uiSource) uiSource.PlayOneShot(clip, volume);
        }

        // ---------- music ----------

        /// <summary>Fade the music in; after a full boot it starts from the top, after a short wake it carries on.</summary>
        void MusicOn(bool fromStart)
        {
            if (!music || !music.clip) return;
            if (fromStart || !musicPaused)
            {
                music.volume = 0;
                music.Play();
            }
            else music.UnPause();
            musicPaused = false;
            musicOn = true;
        }

        void MusicOff(bool now)
        {
            musicOn = false;
            if (!music || !now) return;
            music.volume = 0;
            PauseMusic();
        }

        void PauseMusic()
        {
            if (!music.isPlaying) return;
            music.Pause();
            musicPaused = true;
        }

        void FadeMusic()
        {
            if (!music || !music.isPlaying) return;
            float target = musicOn ? musicVolume : 0;
            float seconds = musicOn ? musicFadeInSeconds : musicFadeOutSeconds;
            float step = seconds > 0 ? musicVolume / seconds * Time.unscaledDeltaTime : 1;
            music.volume = Mathf.MoveTowards(music.volume, target, step);
            if (!musicOn && music.volume <= 0) PauseMusic();
        }

        void FadeOutBootVoice()
        {
            if (!bootVoice || !bootVoice.isPlaying) return;
            fadeFrom = bootVoice.volume;
            fadeVoice = 0;
        }

        void FadeBootVoice()
        {
            if (fadeVoice < 0 || !bootVoice) return;
            fadeVoice += Time.unscaledDeltaTime;
            bootVoice.volume = Mathf.Lerp(fadeFrom, 0, fadeVoice / .25f);
            if (fadeVoice >= .25f) StopBootVoice();
        }

        void StopBootVoice()
        {
            if (bootVoice && bootVoice.isPlaying && (fadeVoice >= 0 || phase == Phase.Booting)) bootVoice.Stop();
            fadeVoice = -1;
            bootVoice = null;
        }
    }
}
