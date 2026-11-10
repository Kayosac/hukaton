using System.Text;
using TMPro;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>
    /// The PC's power: a full start-up (3 s, in time with <c>pc_boot</c>: the switch, the tube opening from a line,
    /// degauss, the memory test with the POST beep at 1.5 s, the logo, the welcome), a short wake (0.5 s) and the
    /// switch-off (the picture folds into a line and then a dot). Drives the glass (<see cref="CrtScreen"/>) and the
    /// boot panels on the PC's canvas.
    /// </summary>
    public sealed class PcBoot : MonoBehaviour
    {
        public CrtScreen screen;
        [Header("Boot panels")]
        public CanvasGroup bios;
        public TMP_Text biosText;
        public CanvasGroup logo;
        [Tooltip("The three blue blocks running inside the logo's progress frame.")]
        public RectTransform logoBlocks;
        public RectTransform logoTrack;
        public CanvasGroup welcome;
        [Tooltip("The desktop: shown when the start-up is over.")]
        public CanvasGroup desktop;

        [Header("Timing")]
        [Min(.5f)] public float fullSeconds = 3f;
        [Min(.1f)] public float shortSeconds = .5f;
        [Min(.1f)] public float offSeconds = .7f;

        public enum Mode { Off, Full, Short, Running, Shutdown }
        public Mode State { get; private set; } = Mode.Off;
        public float Time01 => State == Mode.Full ? time / fullSeconds : State == Mode.Short ? time / shortSeconds : State == Mode.Shutdown ? time / offSeconds : 1;
        float time;
        int memoryShown = -1;
        readonly StringBuilder text = new StringBuilder();

        static readonly string[] BiosLines =
        {
            "МАГИЯ BIOS v2.13   (C) Гоблинсофт",
            "",
            "Процессор: Кристалл-486, 66 МГц",
            "Проверка памяти: {0} К",
            "Колода карт ........ обнаружена",
            "Жаровня ............ подключена",
            "Гоблин ............. на верёвке",
            "",
            "Загрузка системы...",
        };
        // When each line appears (seconds into the full start-up).
        static readonly float[] BiosTimes = { .55f, .6f, .7f, .85f, 1.25f, 1.33f, 1.41f, 1.5f, 1.55f };

        public void PowerOn(bool full)
        {
            State = full ? Mode.Full : Mode.Short;
            time = 0;
            memoryShown = -1;
            screen.SetPowered(true);
            Present();
        }

        /// <summary>Skips the start-up: straight to the desktop.</summary>
        public void Skip()
        {
            if (State != Mode.Full && State != Mode.Short) return;
            State = Mode.Running;
            Present();
        }

        public void PowerOff()
        {
            if (State == Mode.Off || State == Mode.Shutdown) return;
            State = Mode.Shutdown;
            time = 0;
            Present();
        }

        /// <summary>Dark glass, nothing rendering (scene load, tests).</summary>
        public void SetOff()
        {
            State = Mode.Off;
            Present();
        }

        void Update()
        {
            if (State == Mode.Off || State == Mode.Running) return;
            time += UnityEngine.Time.unscaledDeltaTime;
            Present();
        }

        void Present()
        {
            switch (State)
            {
                case Mode.Off:
                    Glass(0, 0, 0, 0, 0);
                    Panels(0, 0, 0, 0);
                    screen.SetPowered(false);
                    break;
                case Mode.Running:
                    Glass(1, 1, 0, 0, 0);
                    Panels(0, 0, 0, 1);
                    break;
                case Mode.Full:
                    FullStep(time);
                    if (time >= fullSeconds) { State = Mode.Running; Present(); }
                    break;
                case Mode.Short:
                {
                    float t = time / shortSeconds;
                    // A spark, the beam stretches out, the picture opens.
                    float spark = Pulse(t, .0f, .08f, .25f);
                    float x = EaseOut(t / .22f), y = EaseOut((t - .2f) / .45f);
                    Glass(x, y, spark * .8f, .3f * (1 - Mathf.Clamp01(t * 1.6f)), .2f * (1 - Mathf.Clamp01(t * 2)));
                    Panels(0, 0, 0, 1);
                    if (time >= shortSeconds) { State = Mode.Running; Present(); }
                    break;
                }
                case Mode.Shutdown:
                {
                    float t = time / offSeconds;
                    // A white flash, the picture collapses into a burning line, the line into a dot, the dot dies.
                    float flash = .45f * (1 - Mathf.Clamp01(t / .1f));
                    float y = 1 - EaseIn(t / .22f);
                    float x = 1 - EaseIn((t - .2f) / .16f);
                    float dot = t < .3f ? 0 : Mathf.Exp(-(t - .32f) * 5.5f) * Mathf.Clamp01((t - .3f) / .03f);
                    Glass(x, Mathf.Max(0, y), dot, 0, 0, flash);
                    Panels(0, 0, 0, 1);
                    if (time >= offSeconds) { State = Mode.Off; Present(); }
                    break;
                }
            }
        }

        void FullStep(float t)
        {
            // 0.00 switch, 0.12–0.28 the line, 0.28–0.5 the tube opens with the degauss thump.
            float x = EaseOut((t - .12f) / .16f), y = EaseOut((t - .28f) / .24f);
            float spark = Pulse(t, .1f, .14f, .3f);
            float wobble = t < .28f ? 0 : Mathf.Exp(-(t - .28f) * 3.2f) * .9f;
            float noise = t < .28f ? 0 : .55f * Mathf.Exp(-(t - .28f) * 4f);
            float flash = t > 1.48f && t < 1.56f ? .12f : 0;
            Glass(x, y, spark * .9f, wobble, noise, flash);

            float biosAlpha = t < 1.62f ? 1 : 0;
            float logoAlpha = t >= 1.62f && t < 2.55f ? 1 : 0;
            float welcomeAlpha = t >= 2.55f ? 1 - Smooth((t - 2.85f) / .15f) : 0;
            float desktopAlpha = t >= 2.85f ? Smooth((t - 2.85f) / .15f) : 0;
            Panels(biosAlpha, logoAlpha, welcomeAlpha, desktopAlpha);

            if (biosAlpha > 0) WriteBios(t);
            if (logoAlpha > 0 && logoBlocks && logoTrack)
            {
                // XP-style: three blocks sweep through the frame over and over.
                float span = logoTrack.rect.width + logoBlocks.rect.width;
                float p = ((t - 1.62f) * 150) % span;
                logoBlocks.anchoredPosition = new Vector2(Mathf.Round(p - logoBlocks.rect.width), logoBlocks.anchoredPosition.y);
            }
        }

        void WriteBios(float t)
        {
            if (!biosText) return;
            // The memory counts up to 640 K by the beep.
            int memory = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(.85f, 1.2f, t) * 40) * 16, 0, 640);
            int lines = 0;
            while (lines < BiosLines.Length && t >= BiosTimes[lines]) lines++;
            int key = lines * 1000 + memory;
            if (key == memoryShown) return;
            memoryShown = key;
            text.Clear();
            for (int i = 0; i < lines; i++)
            {
                string line = BiosLines[i];
                if (line.Contains("{0}")) line = string.Format(line, memory) + (memory >= 640 ? "  OK" : "");
                text.AppendLine(line);
            }
            // The blinking block cursor.
            if ((int)(t * 6) % 2 == 0) text.Append('_');
            biosText.text = text.ToString();
        }

        void Glass(float x, float y, float dot, float wobble, float noise, float flash = 0)
        {
            if (!screen) return;
            screen.openX = x;
            screen.openY = y;
            screen.afterglow = dot;
            screen.wobble = wobble;
            screen.noise = noise;
            screen.flash = flash;
            screen.Apply();
        }

        void Panels(float biosAlpha, float logoAlpha, float welcomeAlpha, float desktopAlpha)
        {
            Show(bios, biosAlpha);
            Show(logo, logoAlpha);
            Show(welcome, welcomeAlpha);
            Show(desktop, desktopAlpha, desktopAlpha >= 1 && State == Mode.Running);
        }

        static void Show(CanvasGroup group, float alpha, bool interactive = false)
        {
            if (!group) return;
            group.alpha = alpha;
            group.interactable = interactive;
            group.blocksRaycasts = interactive;
        }

        static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1 - (1 - t) * (1 - t) * (1 - t);
        }

        static float EaseIn(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        /// <summary>0 before <paramref name="start"/>, up to 1 at <paramref name="peak"/>, back to 0 at <paramref name="end"/>.</summary>
        static float Pulse(float t, float start, float peak, float end) =>
            t < peak ? Mathf.Clamp01((t - start) / Mathf.Max(1e-4f, peak - start)) : 1 - Mathf.Clamp01((t - peak) / Mathf.Max(1e-4f, end - peak));

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3 - 2 * t);
        }
    }
}
