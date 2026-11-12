using System;

namespace Hakaton.Character
{
    /// <summary>How one pip of the stamina arc looks.</summary>
    public enum StaminaPipLook { Full, Empty, Tired }

    /// <summary>
    /// The stamina arc's rules, without Unity: one pip per <c>perPip</c> stamina (a second of sprint), a pip stays lit
    /// while any stamina is left in it, and out of breath every pip is hollow and red until the recovery threshold.
    /// </summary>
    public static class StaminaGauge
    {
        /// <summary>The most pips the arc lays out, whatever the tuning.</summary>
        public const int MaxPips = 32;

        /// <summary>Pips for a stamina maximum: 100 at 20 per pip is 5, every bought step one more.</summary>
        public static int Pips(float capacity, float perPip)
        {
            if (!(perPip > 0) || !(capacity > 0) || float.IsInfinity(capacity)) return 1;
            return Math.Max(1, Math.Min(MaxPips, (int)Math.Ceiling(capacity / perPip - 1e-3)));
        }

        /// <summary>Lit pips: every pip with stamina left in it (the last one goes out only when the budget is empty).</summary>
        public static int Lit(float value, float perPip, int pips)
        {
            if (!(perPip > 0) || !(value > 0)) return 0;
            if (float.IsInfinity(value)) return pips;
            return Math.Max(0, Math.Min(pips, (int)Math.Ceiling(value / perPip - 1e-4)));
        }

        public static StaminaPipLook Look(int index, int lit, bool exhausted)
            => exhausted ? StaminaPipLook.Tired : index < lit ? StaminaPipLook.Full : StaminaPipLook.Empty;

        /// <summary>
        /// Slot of a pip on the arc, in whole layout units from the arc's root (the middle of the arc, straight under the
        /// aim): the pips sit on a circle of <paramref name="radius"/> around the aim, <paramref name="spacing"/> apart along
        /// it, centred under the aim with the ends curling up to its sides. The whole arc never spans more than
        /// <paramref name="maxSpanDegrees"/>: a bigger budget packs the pips closer instead of wrapping over the aim.
        /// </summary>
        public static (int x, int y) Arc(int index, int count, int spacing, int radius, float maxSpanDegrees)
        {
            if (count <= 1 || radius <= 0) return (0, 0);
            double step = Math.Min((double)spacing / radius, maxSpanDegrees * Math.PI / 180 / (count - 1));
            double angle = (index - (count - 1) * .5) * step;
            int x = (int)Math.Round(radius * Math.Sin(angle), MidpointRounding.AwayFromZero);
            int y = (int)Math.Round(radius * (1 - Math.Cos(angle)), MidpointRounding.AwayFromZero);
            return (x, y);
        }
    }
}
