using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Оживление дерева» (земля*, ранг 4/5*). A hit on a suitable tree turns that tree into an ally until the end of the outing or its death: follows the player
    /// and fights. Health 12 (upgraded 18), branch strike 2 within 2.5 m, cycle 2.5 s with a 0.8 s wind-up, μ = 3.
    /// Every copy awakens another tree. The look is a placeholder in its own prefab slot.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class TreeAwakeningEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Ally health (board*: 12 → 18).")]
        [Min(0)] public int healthBase = 12, healthUpgraded = 18;
        [Tooltip("Branch strike damage (board*).")]
        [Min(0)] public int damage = 2;
        [Tooltip("Strike reach, m.")]
        [Min(0)] public float reach = 2.5f;
        [Tooltip("Attack cycle and its wind-up, s.")]
        [Min(0)] public float attackCycle = 2.5f, windup = .8f;
        [Tooltip("Ally mass μ.")]
        [Min(0)] public float mass = 3;

        public override void Apply(in ImpactContext c)
        {
            // TODO(TreeAwakening): world.HideForOuting(the scene tree) + SpawnForOuting the ally (ICombatant, Faction.Player, world.Allies.Add);
            // creatures it hits turn on it via WorldCreature.TakeDamage(instigator).
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Здоровье";
            baseValue = healthBase;
            upgradedValue = healthUpgraded;
            unit = "";
            return true;
        }
    }
}
