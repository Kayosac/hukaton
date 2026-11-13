using UnityEngine;

namespace Hakaton.SpecialItems
{
    /// <summary>
    /// One reel of the phone's bandit: the drawn strip of seven symbols (twice, one under the other, for the wrap)
    /// behind a 54 × 108 window — the chosen symbol whole in the middle, its neighbours half above and below. It moves
    /// by whole pixels only, so the pixel art stays crisp.
    /// </summary>
    public sealed class PhoneReel : MonoBehaviour
    {
        [Tooltip("The two stacked strips; its position is the scroll.")]
        public RectTransform strip;
        [Tooltip("Height of one symbol on the strip, pixels.")]
        [Min(1)] public int symbolHeight = 54;
        [Tooltip("Pixels a spinning reel moves per second.")]
        [Min(1)] public float spinSpeed = 900;
        [Tooltip("Seconds a reel takes to settle on its symbol after the stop.")]
        [Min(.01f)] public float settleSeconds = .2f;
        [Tooltip("How far it runs past the symbol before falling back, pixels.")]
        [Min(0)] public float overshoot = 6;

        int Length => symbolHeight * SlotMachine.SymbolCount;
        float offset, from, distance, settleAt;
        bool spinning, settling;

        public bool Moving => spinning || settling;
        public PhoneSymbol Symbol { get; private set; }

        /// <summary>Shows <paramref name="symbol"/> in the middle at once.</summary>
        public void Show(PhoneSymbol symbol)
        {
            Symbol = symbol;
            spinning = settling = false;
            offset = Target(symbol);
            Apply();
        }

        public void Spin()
        {
            spinning = true;
            settling = false;
        }

        /// <summary>Stops on <paramref name="symbol"/>: keeps running down a little, overshoots and settles.</summary>
        public void Stop(PhoneSymbol symbol)
        {
            Symbol = symbol;
            float target = Target(symbol);
            // Symbols move down: the strip's offset falls. At least two symbols of run-out so the stop reads.
            float run = Mod(offset - target, Length);
            if (run < symbolHeight * 2) run += Length;
            from = offset;
            distance = run;
            settleAt = Time.time;
            spinning = false;
            settling = true;
        }

        void Update()
        {
            if (spinning)
            {
                offset = Mod(offset - spinSpeed * Time.deltaTime, Length);
                Apply();
            }
            else if (settling)
            {
                float t = Mathf.Clamp01((Time.time - settleAt) / settleSeconds);
                // Out fast, past the symbol, back onto it.
                float eased = 1 - Mathf.Pow(1 - t, 3);
                float back = Mathf.Sin(t * Mathf.PI) * overshoot / Mathf.Max(1, distance);
                offset = Mod(from - distance * (eased + back), Length);
                if (t >= 1) { settling = false; offset = Target(Symbol); }
                Apply();
            }
        }

        /// <summary>The strip offset that centres <paramref name="symbol"/> (half a symbol shows above it).</summary>
        float Target(PhoneSymbol symbol) => Mod((int)symbol * symbolHeight - symbolHeight / 2f, Length);

        void Apply()
        {
            if (strip) strip.anchoredPosition = new Vector2(0, Mathf.Round(Mod(offset, Length)));
        }

        static float Mod(float value, float length) => ((value % length) + length) % length;
    }
}
