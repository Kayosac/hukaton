using System;
using Hakaton.Character;
using Hakaton.Expeditions.Cards;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>The effects an oddity can have, with weights. Effects that would do nothing right now are skipped.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Expedition/Oddity table")]
    public sealed class OddityTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public OddityEffect effect;
            [Min(0)] public float weight;
        }

        public Entry[] entries = new Entry[0];

        /// <summary>A weighted pick among the effects that can happen now (any effect when none can).</summary>
        public OddityEffect Pick(ExpeditionWorld world, float roll)
        {
            var effect = Pick(roll, e => e.CanHappen(world));
            return effect ? effect : Pick(roll, e => true);
        }

        OddityEffect Pick(float roll, Func<OddityEffect, bool> allowed)
        {
            float total = 0;
            foreach (var e in entries) if (e.effect && e.weight > 0 && allowed(e.effect)) total += e.weight;
            if (total <= 0) return null;
            float pick = Mathf.Clamp01(roll) * total * .99999f;
            OddityEffect last = null;
            foreach (var e in entries)
            {
                if (!e.effect || e.weight <= 0 || !allowed(e.effect)) continue;
                last = e.effect;
                pick -= e.weight;
                if (pick < 0) return e.effect;
            }
            return last;
        }
    }
}
