using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Лёгкость» (воздух, ранг 3/5). A burst of radius 2.5 m puts Lightness on bodies for 6 s (upgraded 9 s): received impulses ×1.5, falling speed
    /// limited to 8 m/s. Independent of Moon gravity (no multiplying of bonuses).
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class LightnessEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Duration, s (board: 6 → 9).")]
        [Min(0)] public float durationBase = 6, durationUpgraded = 9;
        [Tooltip("Burst radius, m.")]
        [Min(0)] public float radius = 2.5f;
        [Tooltip("Received impulses multiplier (same on both levels).")]
        [Min(0)] public float impulseMultiplier = 1.5f;
        [Tooltip("Falling speed limit, design m/s (same on both levels).")]
        [Min(0)] public float fallSpeedLimit = 8;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Lightness): player: motor.ApplyModifier(MotorModifier.ReceivedImpulse, …) + motor.ApplyFallSpeedLimit(…);
            // creatures: WorldCreature.ApplyLightness; other bodies: BodyModifierStatus.On(body, world).
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
