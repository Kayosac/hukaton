using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Лужа смолы» (земля, ранг 1/5*). Creates a tar puddle on the hit surface that lasts until the end of the outing and slows everyone on it by 50 %.
    /// Fire or Lightning ignite the whole puddle: 30 s of 1 damage unit per second to everyone in the fire, no slowdown,
    /// then it burns out. Rain washes it away (burning or not). Overlapping puddles do not stack.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class TarPuddleEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Puddle radius, m (board*: 2 → 3).")]
        [Min(0)] public float radiusBase = 2, radiusUpgraded = 3;
        [Tooltip("Movement multiplier on unlit tar (board*: −50 %).")]
        [Min(0)] public float speedMultiplier = .5f;
        [Tooltip("Burning time after ignition, s (confirmed; same on both levels).")]
        [Min(0)] public float burnSeconds = 30;
        [Tooltip("Damage units per second in the fire (board*: 1 = ½ heart for the player).")]
        [Min(0)] public float burnDamagePerSecond = 1;

        public override void Apply(in ImpactContext c)
        {
            // TODO(TarPuddle): spawn the puddle zone (CardZoneBehaviour, Kind TarPuddle, IIgnitable + IWashable) until the end of the outing;
            // slow via motor.ApplyModifier(MotorModifier.MoveSpeed, …) and WorldCreature.ApplyMoveSpeedScale, refreshed each tick.
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Радиус";
            baseValue = radiusBase;
            upgradedValue = radiusUpgraded;
            unit = " м";
            return true;
        }
    }
}
