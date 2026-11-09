using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Притяжение» (тьма, ранг 2/5). One impulse toward the point of impact, I = 6 (upgraded 8), radius 3 m, on every physical body: the player,
    /// creatures, objects and loot. Δv = I / μ. A pull, not a hold; pulled loot is not picked up.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class AttractionEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Impulse I toward the centre (board: 6 → 8).")]
        [Min(0)] public float impulseBase = 6, impulseUpgraded = 8;
        [Tooltip("Radius, m (same on both levels).")]
        [Min(0)] public float radius = 3;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Attraction): like ImpulseEffect but toward c.Point: motor.ApplyNormalizedImpulse for the player, CardPhysics.Push for bodies.
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Импульс";
            baseValue = impulseBase;
            upgradedValue = impulseUpgraded;
            unit = "";
            return true;
        }
    }
}
