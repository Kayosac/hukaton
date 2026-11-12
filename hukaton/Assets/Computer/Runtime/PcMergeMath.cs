using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Computer
{
    /// <summary>When each beat of a merge show starts, in seconds from the drop.</summary>
    public readonly struct PcMergeTimeline
    {
        public readonly float FlyEnd, ImpactAt, RevealAt, End;

        public PcMergeTimeline(float fly, float grind, float hold, float reveal)
        {
            FlyEnd = Mathf.Max(0, fly);
            ImpactAt = FlyEnd + Mathf.Max(0, grind);
            RevealAt = ImpactAt + Mathf.Max(0, hold);
            End = RevealAt + Mathf.Max(0, reveal);
        }

        public float GrindSeconds => ImpactAt - FlyEnd;

        public float At(PcMergeFx.Beat beat) => beat switch
        {
            PcMergeFx.Beat.Fly => 0,
            PcMergeFx.Beat.Grind => FlyEnd,
            PcMergeFx.Beat.Impact => ImpactAt,
            PcMergeFx.Beat.Reveal => RevealAt,
            _ => End
        };
    }

    /// <summary>Pure motion helpers of the merge show (canvas pixels; callers round to whole pixels).</summary>
    public static class PcMergeMath
    {
        /// <summary>Share of the dash spent pulling back before the card darts at the target.</summary>
        public const float WindUp = .22f;

        /// <summary>
        /// The dash of the dropped card: a short pull back away from the target, then an overshooting dart onto it
        /// that arcs up a little. <paramref name="u"/> runs 0..1; at 1 the card is exactly on <paramref name="to"/>.
        /// </summary>
        public static Vector2 Fly(Vector2 from, Vector2 to, float u, float anticipation, float overshoot, float arc)
        {
            u = Mathf.Clamp01(u);
            Vector2 back = from - Direction(from, to) * anticipation;
            if (u < WindUp) return Vector2.Lerp(from, back, Ease.OutCubic(u / WindUp));
            float v = (u - WindUp) / (1 - WindUp);
            if (v >= 1) return to;
            return Vector2.LerpUnclamped(back, to, Ease.OutBack(v, overshoot)) + Vector2.up * (arc * Mathf.Sin(v * Mathf.PI));
        }

        /// <summary>Unit direction from one point to another; straight up-and-across when they coincide.</summary>
        public static Vector2 Direction(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            return delta.sqrMagnitude > 1e-4f ? delta.normalized : new Vector2(.6f, -.8f);
        }

        /// <summary>
        /// Crunchy whole-pixel jitter: a fresh random offset every tick (a tick is e.g. 1/30 s), each axis within
        /// ±round(amplitude). Deterministic for a seed and tick.
        /// </summary>
        public static Vector2Int Jitter(int seed, int tick, float amplitude)
        {
            int a = Mathf.Max(0, Mathf.RoundToInt(amplitude));
            if (a == 0) return Vector2Int.zero;
            uint span = (uint)(2 * a + 1);
            uint h = Hash(unchecked((uint)seed * 73856093u ^ (uint)tick * 19349663u));
            int x = (int)(h % span) - a;
            h = Hash(h + 0x9e3779b9u);
            int y = (int)(h % span) - a;
            return new Vector2Int(x, y);
        }

        /// <summary>A shake after the impact: whole pixels within ±pixels, fading out linearly, zero outside 0..seconds.</summary>
        public static Vector2Int Shake(int seed, float time, float pixels, float seconds, float ticksPerSecond = 30)
        {
            if (time < 0 || time >= seconds || pixels <= 0) return Vector2Int.zero;
            return Jitter(seed, Mathf.FloorToInt(time * ticksPerSecond), pixels * (1 - time / seconds));
        }

        static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
