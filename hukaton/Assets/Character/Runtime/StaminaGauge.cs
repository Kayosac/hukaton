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
        /// Slot of a pip on the arc, in whole layout units from the arc's root: centred left to right <paramref name="spacing"/>
        /// apart, the ends rising by <paramref name="curve"/> per squared unit from the middle (a gentle smile under the aim).
        /// </summary>
        public static (int x, int y) Arc(int index, int count, int spacing, float curve)
        {
            int left = -((count - 1) * spacing) / 2;
            int x = left + index * spacing;
            int y = (int)Math.Round(curve * x * x, MidpointRounding.AwayFromZero);
            return (x, y);
        }
    }
}
