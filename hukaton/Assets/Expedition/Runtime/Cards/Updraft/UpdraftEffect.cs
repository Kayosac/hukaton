using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Восходящий поток» (воздух, ранг 4/5). A vertical air column at the point of impact: radius 1.5 m, height 6 m (upgraded 8 m), 12 s. Lifts the player,
    /// creatures and objects with μ ≤ 2 at up to 4 m/s; one can step out sideways.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class UpdraftEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Column height, m (board: 6 → 8).")]
        [Min(0)] public float heightBase = 6, heightUpgraded = 8;
        [Tooltip("Column radius, m.")]
        [Min(0)] public float radius = 1.5f;
        [Tooltip("Duration, s.")]
        [Min(0)] public float durationSeconds = 12;
        [Tooltip("Upward speed limit, m/s.")]
        [Min(0)] public float riseSpeed = 4;
        [Tooltip("Heaviest body it lifts (μ).")]
        [Min(0)] public float maxMass = 2;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Updraft): spawn an updraft zone (Kind Updraft); lift the player with motor.SetVerticalSpeed and bodies with CardPhysics/velocity.
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Высота";
            baseValue = heightBase;
            upgradedValue = heightUpgraded;
            unit = " м";
            return true;
        }
    }
}
