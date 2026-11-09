using System;
using System.Collections.Generic;

namespace Hakaton.Character
{
    /// <summary>
    /// Shuffle bag of visitors: each round everyone comes once, in a new random order, and the first of a round is
    /// never the one who came last. The roster is read anew for every round, so added visitors join the next round
    /// and removed ones leave it.
    /// </summary>
    public sealed class VisitorBag
    {
        readonly Func<IReadOnlyList<NpcAppearance>> roster;
        readonly Func<int, int> random;
        readonly List<NpcAppearance> round = new List<NpcAppearance>();

        public NpcAppearance Last { get; private set; }
        public int Remaining => round.Count;

        /// <param name="random">Returns 0 ≤ value &lt; max for max; UnityEngine.Random by default.</param>
        public VisitorBag(Func<IReadOnlyList<NpcAppearance>> roster, Func<int, int> random = null)
        {
            this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
            this.random = random ?? (max => UnityEngine.Random.Range(0, max));
        }

        /// <summary>The next visitor, without using them up (a visit can still fail to start).</summary>
        public NpcAppearance Peek()
        {
            round.RemoveAll(a => !a);
            if (round.Count == 0) Refill();
            return round.Count > 0 ? round[round.Count - 1] : null;
        }

        /// <summary>The visit started: this visitor is done for the round.</summary>
        public void Take(NpcAppearance visitor)
        {
            round.Remove(visitor);
            Last = visitor;
        }

        void Refill()
        {
            var all = roster();
            if (all == null) return;
            foreach (var visitor in all)
                if (visitor && !round.Contains(visitor)) round.Add(visitor);
            for (int i = round.Count - 1; i > 0; i--)
            {
                int j = random(i + 1);
                (round[i], round[j]) = (round[j], round[i]);
            }
            if (round.Count > 1 && round[round.Count - 1] == Last)
                (round[0], round[round.Count - 1]) = (round[round.Count - 1], round[0]);
        }
    }
}
