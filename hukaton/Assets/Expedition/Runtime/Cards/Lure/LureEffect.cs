using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Манок» (воздух, ранг 1/5). In the point of impact a sound source plays for 6 s (upgraded 10 s). Calm creatures within 10 m go to check it
    /// but never leave their territory; creatures already attacking are not distracted.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class LureEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Sound duration, s (board: 6 → 10).")]
        [Min(0)] public float durationBase = 6, durationUpgraded = 10;
        [Tooltip("Calm creatures within this radius come to check the noise, m (same on both levels).")]
        [Min(0)] public float radius = 10;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Lure): spawn the noise source (SpawnForOuting) for c.Pick(durationBase, durationUpgraded) s and call
            // WorldCreature.Investigate(point, seconds) for creatures within radius (it checks calm + territory itself).
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Звук";
            baseValue = durationBase;
            upgradedValue = durationUpgraded;
            unit = " с";
            return true;
        }
    }
}
