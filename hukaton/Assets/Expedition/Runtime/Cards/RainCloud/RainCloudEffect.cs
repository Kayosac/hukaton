using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Облако дождя» (вода*, ранг 2/5*). A still rain cloud over the point of impact: radius 3 m, 15 s (upgraded 30 s). Rain washes tar away, puts fire out
    /// and makes everyone under it wet (player, creatures, allies); wet lasts 30 s after the last contact.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class RainCloudEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Cloud duration, s (board*: 15 → 30).")]
        [Min(0)] public float durationBase = 15, durationUpgraded = 30;
        [Tooltip("Rain radius, m (board*; same on both levels).")]
        [Min(0)] public float radius = 3;
        [Tooltip("Wet after leaving the rain, s (confirmed 30 on both levels).")]
        [Min(0)] public float wetLingerSeconds = WetStatus.DefaultLingerSeconds;

        public override void Apply(in ImpactContext c)
        {
            // TODO(RainCloud): spawn the cloud zone (Kind Rain); every tick WetStatus.Soak(body, world, wetLingerSeconds) for fighters inside
            // (world.Combatants) and IWashable.Wash(this) for zones it overlaps (world.Zones.Overlapping).
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Длительность";
            baseValue = durationBase;
            upgradedValue = durationUpgraded;
            unit = " с";
            return true;
        }
    }
}
