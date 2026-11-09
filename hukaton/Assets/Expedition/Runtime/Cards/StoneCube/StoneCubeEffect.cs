using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Каменный куб» (земля, ранг 3/5). A movable stone cube 1.5 m (upgraded 2 m), mass μ = 1, until the end of the outing: a step, cover, an obstacle;
    /// physical effects move it. Appears only where it does not intersect geometry. Rope may attach to it.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class StoneCubeEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Cube side, m (board: 1.5 → 2).")]
        [Min(0)] public float sizeBase = 1.5f, sizeUpgraded = 2;
        [Tooltip("Rigidbody mass = board μ (same on both levels).")]
        [Min(0)] public float mass = 1;

        public override void Apply(in ImpactContext c)
        {
            // TODO(StoneCube): check free space (Physics.CheckBox), SpawnForOuting a crooked cube prefab with a Rigidbody of this mass.
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Сторона";
            baseValue = sizeBase;
            upgradedValue = sizeUpgraded;
            unit = " м";
            return true;
        }
    }
}
