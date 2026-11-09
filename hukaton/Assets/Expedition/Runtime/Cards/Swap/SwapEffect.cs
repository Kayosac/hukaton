using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Подмена» (тьма, ранг 5/5). On a real hit the player and a loose movable non-living object with μ ≤ 2 swap places, within 12 m (upgraded 18 m),
    /// if the player's capsule fits there. Living creatures (the awakened tree too) are not valid. Each keeps its own velocity.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class SwapEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Range, m (board: 12 → 18).")]
        [Min(0)] public float rangeBase = 12, rangeUpgraded = 18;
        [Tooltip("Heaviest object it swaps (μ).")]
        [Min(0)] public float maxMass = 2;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Swap): check c.Body (not a creature, mass ≤ maxMass, distance), free capsule; motor.TeleportKeepingVelocity + move the body.
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Дальность";
            baseValue = rangeBase;
            upgradedValue = rangeUpgraded;
            unit = " м";
            return true;
        }
    }
}
