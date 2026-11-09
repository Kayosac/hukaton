using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Туман» (вода, ранг 2/5). A fog area of radius 3 m for 8 s (upgraded 12 s) blocks vision for the player and for creatures. Losing sight does
    /// not erase aggression: creatures search at the last known place. Fog does not make anyone wet.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class FogEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Fog duration, s (board: 8 → 12).")]
        [Min(0)] public float durationBase = 8, durationUpgraded = 12;
        [Tooltip("Fog radius, m (same on both levels).")]
        [Min(0)] public float radius = 3;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Fog): spawn a fog zone (CardZoneBehaviour, Kind Fog) implementing IVisionBlocker; creatures already ask
            // world.IsVisionBlocked. Add the visual (Retro Arsenal smoke) for the player.
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
