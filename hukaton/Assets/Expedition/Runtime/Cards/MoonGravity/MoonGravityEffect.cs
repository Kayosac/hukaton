using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Лунная гравитация» (тьма*, ранг 3/5*). On impact (anywhere) the player's gravity becomes 25 % for 12 s (upgraded 45 s). Only the player; existing speed is
    /// kept; jump take-off speed unchanged, so jumps get higher. A repeat copy refreshes, never stacks or shortens.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class MoonGravityEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Duration, s (confirmed 12 → 45).")]
        [Min(0)] public float durationBase = 12, durationUpgraded = 45;
        [Tooltip("Gravity multiplier (board*: 25 %).")]
        [Min(0)] public float gravityMultiplier = .25f;

        public override void Apply(in ImpactContext c)
        {
            // TODO(MoonGravity): c.Motor.ApplyModifier(MotorModifier.Gravity, gravityMultiplier, c.Pick(durationBase, durationUpgraded)) + feedback.
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
