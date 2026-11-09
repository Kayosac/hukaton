using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    public enum TreeAttackPhase { Ready, Windup, Strike, Recover }

    /// <summary>
    /// The awakened tree's branch strike (board*: cycle 2.5 s including a 0.8 s wind-up). A cycle starts with the
    /// wind-up; then a short strike window in which the branch can connect, at most once; then recovery until the
    /// cycle is over. The clock is passed in, so the rule is testable without a scene.
    /// </summary>
    public struct TreeAttackCycle
    {
        public readonly float Cycle, Windup, StrikeWindow;
        public TreeAttackPhase Phase { get; private set; }
        /// <summary>When the current cycle's wind-up began.</summary>
        public float StartedAt { get; private set; }
        /// <summary>The branch already hit during this cycle's strike.</summary>
        public bool Landed { get; private set; }

        public TreeAttackCycle(float cycle, float windup, float strikeWindow) : this()
        {
            Windup = Mathf.Max(0, windup);
            StrikeWindow = Mathf.Max(.01f, strikeWindow);
            Cycle = Mathf.Max(cycle, Windup + StrikeWindow);
            Phase = TreeAttackPhase.Ready;
            StartedAt = float.NegativeInfinity;
            Landed = false;
        }

        public bool Ready(float now) => Advance(now) == TreeAttackPhase.Ready;

        /// <summary>Starts the wind-up if the previous cycle is over. Returns false while a cycle runs.</summary>
        public bool Begin(float now)
        {
            if (!Ready(now)) return false;
            Phase = TreeAttackPhase.Windup;
            StartedAt = now;
            Landed = false;
            return true;
        }

        /// <summary>Moves the phase on with time and returns it.</summary>
        public TreeAttackPhase Advance(float now)
        {
            if (Phase == TreeAttackPhase.Ready) return Phase;
            float t = now - StartedAt;
            Phase = t < Windup ? TreeAttackPhase.Windup
                : t < Windup + StrikeWindow ? TreeAttackPhase.Strike
                : t < Cycle ? TreeAttackPhase.Recover
                : TreeAttackPhase.Ready;
            return Phase;
        }

        /// <summary>
        /// The branch touches its target now: true only inside the strike window and only the first time in this
        /// cycle (damage is dealt once per strike, and only on a real contact).
        /// </summary>
        public bool TryLand(float now)
        {
            if (Advance(now) != TreeAttackPhase.Strike || Landed) return false;
            Landed = true;
            return true;
        }

        /// <summary>0 → 1 through the wind-up.</summary>
        public float WindupProgress(float now) => Windup <= 0 ? 1 : Mathf.Clamp01((now - StartedAt) / Windup);
        /// <summary>0 → 1 through the strike window.</summary>
        public float StrikeProgress(float now) => Mathf.Clamp01((now - StartedAt - Windup) / StrikeWindow);
        /// <summary>0 → 1 through the recovery.</summary>
        public float RecoverProgress(float now)
        {
            float length = Cycle - Windup - StrikeWindow;
            return length <= 0 ? 1 : Mathf.Clamp01((now - StartedAt - Windup - StrikeWindow) / length);
        }

        /// <summary>Drops the running cycle (death, end of the outing).</summary>
        public void Reset()
        {
            Phase = TreeAttackPhase.Ready;
            StartedAt = float.NegativeInfinity;
            Landed = false;
        }
    }

    /// <summary>What the tree knows about one creature when it decides whom to fight.</summary>
    public struct FoeCandidate
    {
        /// <summary>Flat distances, m.</summary>
        public float DistanceToAlly, DistanceToPlayer;
        /// <summary>In an attacking state (chase, wind-up, strike, recovery, search) against the player or an ally.</summary>
        public bool AttacksSquad;
        /// <summary>Its attack target is this very tree.</summary>
        public bool AttacksThisAlly;
        /// <summary>Lost health lately: the player (or the squad) is fighting it.</summary>
        public bool RecentlyHurt;
        /// <summary>The tree's current foe.</summary>
        public bool Current;
    }

    public enum TauntDecision { Keep, Draw, Release }

    /// <summary>
    /// Whom the awakened tree fights (board*: it helps against creatures attacking the player or the squad and
    /// against the one the player attacked; it never hunts peaceful creatures on its own), and when a hostile
    /// creature turns on the tree instead of the player (the tree is closer, or the tree is hitting it).
    /// </summary>
    public static class TreeAllyTargeting
    {
        [System.Serializable]
        public struct Rules
        {
            [Tooltip("A new foe must be this close to the tree, m.")]
            public float engageRange;
            [Tooltip("…and this close to the player (the tree stays with the player), m.")]
            public float assistRadius;
            [Tooltip("The current foe is kept up to this distance from the tree / from the player, m.")]
            public float keepRange;
            [Tooltip("Farther than this from the player the tree only defends itself, m.")]
            public float leashFromPlayer;
            [Tooltip("Score bonus (m) for a creature attacking the tree itself.")]
            public float selfDefenceBonus;
            [Tooltip("Score bonus (m) for the current foe, so the tree does not flip between two.")]
            public float stickiness;

            public static Rules Default => new Rules
            {
                engageRange = 9, assistRadius = 14, keepRange = 13, leashFromPlayer = 18,
                selfDefenceBonus = 2.5f, stickiness = 1.5f,
            };
        }

        /// <summary>Should the tree fight this creature at all?</summary>
        public static bool Eligible(in FoeCandidate c, in Rules rules, float allyToPlayer)
        {
            if (!c.AttacksSquad && !c.RecentlyHurt) return false;
            float reach = c.Current ? Mathf.Max(rules.keepRange, rules.engageRange) : rules.engageRange;
            if (c.DistanceToAlly > reach) return false;
            // Defending itself works anywhere; helping keeps the tree near the player.
            if (c.AttacksThisAlly) return true;
            if (allyToPlayer > rules.leashFromPlayer) return false;
            float around = c.Current ? Mathf.Max(rules.keepRange, rules.assistRadius) : rules.assistRadius;
            return c.DistanceToPlayer <= around;
        }

        /// <summary>Index of the creature to fight, or -1 when none should be fought.</summary>
        public static int Choose(IReadOnlyList<FoeCandidate> candidates, in Rules rules, float allyToPlayer)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!Eligible(c, rules, allyToPlayer)) continue;
                float score = c.DistanceToAlly;
                if (c.AttacksThisAlly) score -= rules.selfDefenceBonus;
                if (c.Current) score -= rules.stickiness;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Does an attacking creature switch to the tree? <paramref name="toTree"/>: creature → this tree;
        /// <paramref name="toTarget"/>: creature → its current target (the player or another tree);
        /// <paramref name="toPlayer"/>: creature → player. A creature in the middle of its own attack is never switched.
        /// Draw: the tree is clearly closer than the current target. Release (back to the player): the tree is
        /// clearly farther than the player and has not hit the creature lately.
        /// </summary>
        public static TauntDecision Taunt(bool targetsThisTree, bool midAttack, bool hitByThisTreeLately,
            float toTree, float toTarget, float toPlayer, float drawRange, float drawMargin, float releaseMargin)
        {
            if (midAttack) return TauntDecision.Keep;
            if (targetsThisTree)
                return !hitByThisTreeLately && toTree > toPlayer + releaseMargin ? TauntDecision.Release : TauntDecision.Keep;
            return toTree <= drawRange && toTree + drawMargin < toTarget ? TauntDecision.Draw : TauntDecision.Keep;
        }
    }
}
