using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>The board's tar states: СМОЛА (slows) → ГОРЕНИЕ (burns, no slow) → ВЫГОРЕЛА; rain washes it at any point.</summary>
    public enum TarPhase { Tar, Burning, BurntOut, Washed }

    /// <summary>
    /// Rules of one tar puddle, free of Unity objects (EditMode-tested). Unlit tar lasts until the end of the outing.
    /// Fire or lightning lights the whole puddle for <see cref="BurnSeconds"/>; a second ignition of burning tar is
    /// ignored (the timer is not refreshed); after the fire the puddle is gone for good. Rain removes it whole.
    /// </summary>
    public sealed class TarPuddleState
    {
        public TarPhase Phase { get; private set; } = TarPhase.Tar;
        public float BurnSeconds { get; }
        public float BurnElapsed { get; private set; }

        public TarPuddleState(float burnSeconds) { BurnSeconds = Mathf.Max(0, burnSeconds); }

        /// <summary>Unlit tar: everyone on it is slowed.</summary>
        public bool Slows => Phase == TarPhase.Tar;
        /// <summary>The fire is on: everyone in it takes damage over time, nobody is slowed.</summary>
        public bool Burns => Phase == TarPhase.Burning;
        /// <summary>Burnt out or washed away: nothing left, and it never comes back.</summary>
        public bool Gone => Phase == TarPhase.BurntOut || Phase == TarPhase.Washed;
        public float BurnRemaining => Burns ? Mathf.Max(0, BurnSeconds - BurnElapsed) : 0;
        /// <summary>0 when lit, 1 when burnt out.</summary>
        public float BurnProgress => Burns ? (BurnSeconds > 0 ? Mathf.Clamp01(BurnElapsed / BurnSeconds) : 1) : Phase == TarPhase.BurntOut ? 1 : 0;

        /// <summary>Lights unlit tar; true when it caught fire now. Burning, burnt or washed tar ignores it.</summary>
        public bool Ignite()
        {
            if (Phase != TarPhase.Tar) return false;
            Phase = TarPhase.Burning;
            BurnElapsed = 0;
            return true;
        }

        /// <summary>Advances the fire; true on the step it burns out.</summary>
        public bool Advance(float seconds)
        {
            if (Phase != TarPhase.Burning) return false;
            BurnElapsed += Mathf.Max(0, seconds);
            if (BurnElapsed < BurnSeconds) return false;
            Phase = TarPhase.BurntOut;
            return true;
        }

        /// <summary>Rain: tar or fire alike is washed away; true when there was something to wash.</summary>
        public bool Wash()
        {
            if (Gone) return false;
            Phase = TarPhase.Washed;
            return true;
        }
    }

    /// <summary>
    /// Time-in-fire bookkeeping shared by every burning puddle of an outing (board: damage by the time a fighter
    /// spends in the fire; crossing the edge back and forth or standing where two puddles overlap gives no extra hits;
    /// the same tar fire does not stack). Each fighter accrues damage units at most once per frame, whatever the number
    /// of puddles it stands in; a hit is due every full unit. The leftover fraction stays, so stepping out and back in
    /// never deals a fresh instant hit.
    /// </summary>
    public sealed class TarFireExposure
    {
        sealed class Entry
        {
            public float units;
            public int frame = int.MinValue;
        }

        readonly Dictionary<object, Entry> entries = new Dictionary<object, Entry>();

        /// <summary>
        /// The fighter stood in fire during <paramref name="frame"/> worth <paramref name="units"/> damage units
        /// (seconds × damage per second). Returns the whole units due now (usually 0 or 1). A second call for the same
        /// fighter in the same frame (another puddle) adds nothing.
        /// </summary>
        public int Expose(object fighter, int frame, float units)
        {
            if (fighter == null || units <= 0) return 0;
            if (!entries.TryGetValue(fighter, out var entry)) entries[fighter] = entry = new Entry();
            if (entry.frame == frame) return 0;
            entry.frame = frame;
            entry.units += units;
            int due = Mathf.FloorToInt(entry.units + 1e-4f);
            entry.units = Mathf.Max(0, entry.units - due);
            return due;
        }

        /// <summary>Accrued fraction of the next hit for this fighter (0 when unknown).</summary>
        public float Pending(object fighter) => fighter != null && entries.TryGetValue(fighter, out var entry) ? entry.units : 0;

        public void Clear() => entries.Clear();
    }
}
