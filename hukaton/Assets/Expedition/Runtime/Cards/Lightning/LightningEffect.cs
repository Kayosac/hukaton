using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Разряд молнии» (воздух*, ранг 2/5). A lightning strike on the target hit directly (no area): 3 damage (upgraded 4). A wet target takes double and passes
    /// the strike to the nearest unhit wet fighter within 4 m in line of sight, up to 3 targets, either side. Hitting tar ignites it.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class LightningEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Direct damage units (board*: 3 → 4).")]
        [Min(0)] public int damageBase = 3, damageUpgraded = 4;
        [Tooltip("Damage multiplier for a wet target (confirmed ×2).")]
        [Min(0)] public float wetDamageMultiplier = 2;
        [Tooltip("Targets in the chain including the first (board*).")]
        [Min(0)] public int chainTargets = 3;
        [Tooltip("Longest jump between wet targets, m (board*).")]
        [Min(0)] public float chainRange = 4;
        [Tooltip("Tar catches fire when the strike lands this close to it, m.")]
        [Min(0)] public float igniteReach = .4f;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Lightning): c.HitCombatant → TakeDamage(new DamageInfo(…, DamageKind.Lightning)); WetStatus.IsWet(target.Root) → ×2 and chain
            // through world.Combatants; world.Ignite(c.Target, c.Point, igniteReach, IgnitionSource.Lightning).
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Урон";
            baseValue = damageBase;
            upgradedValue = damageUpgraded;
            unit = "";
            return true;
        }
    }
}
