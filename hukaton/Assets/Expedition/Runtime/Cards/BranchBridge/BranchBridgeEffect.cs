using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Ветвь-мост» (земля, ранг 3/5). From the immovable solid surface that was hit, a branch grows horizontally back toward the throw: length 5 m
    /// (upgraded 7 m), width 1.2 m, until the end of the outing; creatures walk on it too. Fire (and Lightning) burn it.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class BranchBridgeEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Branch length, m (board: 5 → 7).")]
        [Min(0)] public float lengthBase = 5, lengthUpgraded = 7;
        [Tooltip("Branch width, m (same on both levels).")]
        [Min(0)] public float width = 1.2f;

        public override void Apply(in ImpactContext c)
        {
            // TODO(BranchBridge): grow toward c.ThrowOrigin without passing through geometry; SpawnForOuting; implement IIgnitable on it.
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Длина";
            baseValue = lengthBase;
            upgradedValue = lengthUpgraded;
            unit = " м";
            return true;
        }
    }
}
