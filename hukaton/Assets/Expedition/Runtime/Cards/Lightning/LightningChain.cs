using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// Who a lightning strike hits (pure rules, EditMode-tested). The first target is the one hit directly. A dry
    /// first target ends the strike there. From a wet one the strike jumps, in a line without branching, to the
    /// nearest wet fighter not hit yet within <c>range</c> that is in direct sight of the current one, until
    /// <c>maxTargets</c> fighters in all (the first included) or no candidate is left. Sides do not matter: the wet
    /// player and wet allies are candidates like any creature. Each fighter is hit at most once per strike.
    /// </summary>
    public static class LightningChain
    {
        public static List<T> Build<T>(T first, IReadOnlyList<T> candidates, int maxTargets, float range,
            Func<T, bool> isWet, Func<T, Vector3> position, Func<T, T, bool> inSight) where T : class
        {
            var chain = new List<T>();
            if (first == null || maxTargets <= 0) return chain;
            chain.Add(first);
            if (!isWet(first)) return chain;
            var options = new List<(T fighter, float distance)>();
            var current = first;
            while (chain.Count < maxTargets)
            {
                options.Clear();
                Vector3 from = position(current);
                if (candidates != null)
                    foreach (var candidate in candidates)
                    {
                        if (candidate == null || Contains(chain, candidate) || !isWet(candidate)) continue;
                        float distance = Vector3.Distance(from, position(candidate));
                        if (distance <= range) options.Add((candidate, distance));
                    }
                options.Sort((a, b) => a.distance.CompareTo(b.distance));
                T next = null;
                foreach (var option in options)
                    if (inSight == null || inSight(current, option.fighter)) { next = option.fighter; break; }
                if (next == null) break;
                chain.Add(next);
                current = next;
            }
            return chain;
        }

        /// <summary>Damage to one target: base, or base × the wet multiplier (rounded) when it is wet.</summary>
        public static int Damage(int baseDamage, bool wet, float wetMultiplier)
            => wet ? Mathf.RoundToInt(baseDamage * wetMultiplier) : baseDamage;

        static bool Contains<T>(List<T> chain, T fighter) where T : class
        {
            foreach (var hit in chain) if (ReferenceEquals(hit, fighter)) return true;
            return false;
        }
    }
}
