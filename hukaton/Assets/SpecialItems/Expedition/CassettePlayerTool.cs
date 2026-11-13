using System.Collections;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// The cassette player (board «Спецпредметы», frame «Плеер»): a worn tape player with earphones in the left hand,
    /// bottom left, alongside the cards. A tap of Tab plays or pauses, Tab held turns to the next track. The tracks go
    /// in a shuffled order with no repeats until all have played. Put away, it keeps playing; when the outing ends the
    /// music fades and the next outing starts the same track from the beginning.
    /// </summary>
    public sealed class CassettePlayerTool : SpecialItemTool
    {
        enum State { Hidden, Drawing, Ready, Stowing }

        [Header("Player in the hand")]
        [Tooltip("The hand's canvas; off while the player is put away.")]
        public Canvas canvas;
        [Tooltip("The hand with the player; its pivot is at the wrist, which it turns around.")]
        public RectTransform player;
        [Tooltip("The hand and player drawing (the special items' hand-drawn material).")]
        public Image body;
        public Sprite idle, pressed;
        public Image reelTop, reelBottom;
        [Tooltip("The reels' twelve turns, 30° apart, both turning the same way.")]
        public Sprite[] reelTopFrames = new Sprite[12], reelBottomFrames = new Sprite[12];
        [Tooltip("Reel frames per second while the tape plays.")]
        [Min(1)] public float reelFps = 10;
        public Image lamp;
        public Sprite lampOn, lampOff;
        [Tooltip("Where the hand rests and how it comes in and moves (shared with the other items in the hand).")]
        public HandHold hold = new HandHold();
        [Min(.01f)] public float enterSeconds = .24f, leaveSeconds = .18f;

        [Header("Music")]
        [Tooltip("2D source on the Music mixer group, not looping, not playing on awake.")]
        public AudioSource music;
        public AudioClip[] tracks = new AudioClip[0];
        [Range(0, 1)] public float volume = .5f;
        [Tooltip("Tab held this long turns to the next track; shorter is a tap (play / pause).")]
        [Min(.05f)] public float skipHoldSeconds = .35f;
        [Tooltip("The tape gets up to speed (pitch from startPitch to 1) and slows down when paused, seconds.")]
        [Min(0)] public float startSeconds = .25f, stopSeconds = .2f;
        [Range(.1f, 1)] public float startPitch = .55f, stopPitch = .35f;
        [Tooltip("The music fades away when the outing ends.")]
        [Min(0)] public float outingEndFadeSeconds = 1.2f;

        [Header("Sound")]
        public SfxCue drawSound;
        public SfxCue playSound, stopSound, nextSound;

        State state = State.Hidden;
        float slide;
        Coroutine motion, tape;
        float pressedAt = -1;
        bool holding, skipped;
        float reelFrame;
        readonly List<int> order = new List<int>();
        int orderAt = -1;
        bool loaded;
        readonly System.Random random = new System.Random();

        public override bool IsOut => state != State.Hidden && state != State.Stowing;
        public override string ControlHint => (Playing ? "Tab — пауза" : "Tab — играть") + "\nзажать Tab — следующий трек";
        /// <summary>The music was on when the last outing ended: the next one starts with it.</summary>
        public bool ResumesNextOuting { get; private set; }
        /// <summary>The tape is playing (in the hand or put away).</summary>
        public bool Playing { get; private set; }
        public AudioClip CurrentTrack => orderAt >= 0 && orderAt < order.Count && order[orderAt] < tracks.Length ? tracks[order[orderAt]] : null;

        void Awake()
        {
            if (music) { music.playOnAwake = false; music.loop = false; music.spatialBlend = 0; }
            HideAll();
            ShowReels();
        }

        void HideAll()
        {
            state = State.Hidden;
            if (canvas) canvas.enabled = false;
            slide = 0;
            hold.End();
            hold.Apply(player, 0, 0);
            holding = false;
            pressedAt = -1;
            SetPressed(false);
        }

        // ---------- outings ----------

        public override void OutingBegan()
        {
            if (motion != null) StopCoroutine(motion);
            motion = null;
            HideAll();
            // Came home with the music on: it plays again from the start of the same track; Tab turns it off.
            if (ResumesNextOuting && Items && definition && Items.Owns(definition)) Play(false);
            ResumesNextOuting = false;
        }

        public override void OutingEnded()
        {
            if (motion != null) StopCoroutine(motion);
            motion = null;
            HideAll();
            if (Playing) ResumesNextOuting = true;
            if (Playing) RunTape(FadeAway());
            else if (music && tape == null)
            {
                if (tape != null) StopCoroutine(tape);
                tape = null;
                music.Stop();
                music.time = 0;
                music.pitch = 1;
            }
            Playing = false;
            ShowLamp();
        }

        // ---------- in and out of the hand ----------

        public override void Draw()
        {
            if (IsOut || !World || !World.Running) return;
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(DrawRoutine());
        }

        public override void Stow(bool instant = false)
        {
            if (state == State.Hidden) return;
            if (motion != null) StopCoroutine(motion);
            motion = null;
            holding = false;
            SetPressed(false);
            // The music carries on in the pocket.
            if (instant || !isActiveAndEnabled) { HideAll(); return; }
            motion = StartCoroutine(StowRoutine());
        }

        IEnumerator DrawRoutine()
        {
            state = State.Drawing;
            if (canvas) canvas.enabled = true;
            SetPressed(false);
            ShowLamp();
            ShowReels();
            hold.Begin(World);
            World.Play(drawSound);
            for (float t = 0; t < enterSeconds; t += Time.deltaTime)
            {
                slide = t / enterSeconds;
                yield return null;
            }
            slide = 1;
            state = State.Ready;
            motion = null;
        }

        IEnumerator StowRoutine()
        {
            state = State.Stowing;
            World.Play(drawSound, null, .8f, .85f);
            for (float t = 0; t < leaveSeconds; t += Time.deltaTime)
            {
                slide = 1 - t / leaveSeconds;
                yield return null;
            }
            HideAll();
            motion = null;
        }

        // ---------- Tab ----------

        void Update()
        {
            // On the way home (the Return card, a defeat) the music already fades with the screen.
            if (Playing && World && World.Returning)
            {
                ResumesNextOuting = true;
                Playing = false;
                RunTape(FadeAway());
                ShowLamp();
            }
            StepTracks();
            if (state == State.Hidden) return;
            if (Playing && reelTopFrames.Length > 0)
            {
                reelFrame = (reelFrame + reelFps * Time.deltaTime) % reelTopFrames.Length;
                ShowReels();
            }
            if (state != State.Ready || !World || !World.Running || !World.control.GameplayActive)
            {
                if (holding) { holding = false; SetPressed(false); }
                return;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            var tab = keyboard.tabKey;
            if (tab.wasPressedThisFrame)
            {
                holding = true;
                skipped = false;
                pressedAt = Time.unscaledTime;
                SetPressed(true);
            }
            if (!holding) return;
            if (tab.isPressed)
            {
                if (!skipped && Time.unscaledTime - pressedAt >= skipHoldSeconds) { skipped = true; Next(); }
                return;
            }
            holding = false;
            SetPressed(false);
            if (!skipped) Toggle();
        }

        void LateUpdate()
        {
            if (state != State.Hidden) hold.Apply(player, slide, Time.deltaTime);
        }

        /// <summary>A tap of Tab: play, or pause where it is.</summary>
        public void Toggle()
        {
            if (Playing) Pause();
            else Play();
        }

        public void Play() => Play(true);

        void Play(bool click)
        {
            if (!music || tracks.Length == 0) return;
            if (!loaded) Load(orderAt < 0 ? 0 : orderAt);
            if (click) Sound(playSound);
            Playing = true;
            RunTape(SpeedUp(music.time > 0));
            ShowLamp();
        }

        public void Pause()
        {
            if (!Playing) return;
            Sound(stopSound);
            Playing = false;
            RunTape(SlowDown());
            ShowLamp();
        }

        /// <summary>Tab held: the next track in the shuffled order, playing.</summary>
        public void Next()
        {
            if (!music || tracks.Length == 0) return;
            Sound(nextSound);
            Load(orderAt + 1);
            Playing = true;
            RunTape(SpeedUp(false));
            ShowLamp();
            hold.Hop(90);
        }

        /// <summary>A track that played to its end gives way to the next one.</summary>
        void StepTracks()
        {
            if (!Playing || !music || !loaded || tape != null) return;
            if (music.isPlaying || AudioListener.pause) return;
            Load(orderAt + 1);
            music.pitch = 1;
            music.volume = volume;
            music.Play();
        }

        /// <summary>Puts the track at <paramref name="at"/> in the shuffled order on, from its start, stopped.</summary>
        void Load(int at)
        {
            if (order.Count != tracks.Length) { Shuffle(-1); at = 0; }
            if (at >= order.Count) { Shuffle(order.Count > 0 ? order[order.Count - 1] : -1); at = 0; }
            orderAt = Mathf.Max(0, at);
            music.Stop();
            music.clip = CurrentTrack;
            music.time = 0;
            loaded = music.clip;
        }

        /// <summary>A new order of every track; <paramref name="last"/> (just played) does not come first.</summary>
        void Shuffle(int last)
        {
            order.Clear();
            for (int i = 0; i < tracks.Length; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            if (order.Count > 1 && order[0] == last) (order[0], order[1]) = (order[1], order[0]);
        }

        void RunTape(IEnumerator routine)
        {
            if (tape != null) StopCoroutine(tape);
            tape = isActiveAndEnabled ? StartCoroutine(routine) : null;
        }

        IEnumerator SpeedUp(bool resume)
        {
            music.pitch = startPitch;
            music.volume = volume;
            if (resume) music.UnPause();
            else music.Play();
            for (float t = 0; t < startSeconds; t += Time.unscaledDeltaTime)
            {
                music.pitch = Mathf.Lerp(startPitch, 1, t / startSeconds);
                yield return null;
            }
            music.pitch = 1;
            tape = null;
        }

        IEnumerator SlowDown()
        {
            float from = music.pitch;
            for (float t = 0; t < stopSeconds; t += Time.unscaledDeltaTime)
            {
                float k = t / stopSeconds;
                music.pitch = Mathf.Lerp(from, stopPitch, k);
                music.volume = volume * (1 - k * k);
                yield return null;
            }
            music.Pause();
            music.pitch = 1;
            music.volume = volume;
            tape = null;
        }

        /// <summary>The outing is over: the music fades, and the same track waits at its start for the next one.</summary>
        IEnumerator FadeAway()
        {
            float from = music.volume;
            for (float t = 0; t < outingEndFadeSeconds; t += Time.unscaledDeltaTime)
            {
                music.volume = from * (1 - t / outingEndFadeSeconds);
                yield return null;
            }
            music.Stop();
            music.time = 0;
            music.pitch = 1;
            music.volume = volume;
            tape = null;
        }

        void Sound(SfxCue cue)
        {
            if (World) World.Play(cue);
        }

        // ---------- the drawing ----------

        void SetPressed(bool down)
        {
            if (body && idle && pressed) body.sprite = down ? pressed : idle;
        }

        void ShowLamp()
        {
            if (lamp && lampOn && lampOff) lamp.sprite = Playing ? lampOn : lampOff;
        }

        void ShowReels()
        {
            int frame = Mathf.FloorToInt(reelFrame);
            if (reelTop && frame < reelTopFrames.Length && reelTopFrames[frame]) reelTop.sprite = reelTopFrames[frame];
            if (reelBottom && frame < reelBottomFrames.Length && reelBottomFrames[frame]) reelBottom.sprite = reelBottomFrames[frame];
        }

        void OnDisable()
        {
            hold.End();
            if (tape != null) StopCoroutine(tape);
            tape = null;
        }
    }
}
