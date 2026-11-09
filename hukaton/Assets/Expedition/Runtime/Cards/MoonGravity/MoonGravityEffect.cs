using Hakaton.Character;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Лунная гравитация» (тьма*, ранг 3/5*). At the card's first contact — anywhere, there is no zone — the player's
    /// gravity becomes 25 % for 12 s (upgraded 45 s). Only the player: cards in flight, creatures and objects keep the
    /// normal g. Speed at the start and at the end is kept (only the acceleration changes); the jump's take-off speed is
    /// unchanged, so jumps get higher and falls slower; landing damage still follows the actual impact speed. A repeat
    /// copy refreshes the time to the longer of the two (never multiplies the gravity, a shorter copy never cuts it) —
    /// <see cref="FirstPersonMotor.ApplyModifier"/>. A HUD badge shows the remaining time and blinks before the end.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class MoonGravityEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Duration, s (confirmed 12 → 45).")]
        [Min(0)] public float durationBase = 12, durationUpgraded = 45;
        [Tooltip("Gravity multiplier (board*: 25 %).")]
        [Min(0)] public float gravityMultiplier = .25f;

        [Header("Presentation")]
        [Tooltip("HUD badge with the remaining time (and the floating-dust aura while it lasts).")]
        public PlayerStatusBadge badge;
        [Tooltip("Burst around the player's feet when the gravity drops.")]
        public VfxBurst playerVfx;
        public Color playerTint = new Color(.78f, .74f, 1f);
        [Min(0)] public float playerVfxScale = 1;

        /// <summary>Duration of a base or an upgraded copy.</summary>
        public float Duration(bool upgraded) => upgraded ? durationUpgraded : durationBase;

        /// <summary>The rule itself (separate so it can be tested on a bare motor).</summary>
        public void ApplyTo(FirstPersonMotor motor, bool upgraded)
        {
            if (motor) motor.ApplyModifier(MotorModifier.Gravity, gravityMultiplier, Duration(upgraded));
        }

        public override void Apply(in ImpactContext c)
        {
            var motor = c.Motor;
            ApplyTo(motor, c.Upgraded);
            PlayerStatusBadge.Show(c.World, badge);
            c.World.Spawn(playerVfx, motor.transform.position + Vector3.up * .05f, Quaternion.identity, playerTint, playerVfxScale);
            if (c.World.feel) c.World.feel.PunchFov(-14);
            Feedback(c, true);
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
