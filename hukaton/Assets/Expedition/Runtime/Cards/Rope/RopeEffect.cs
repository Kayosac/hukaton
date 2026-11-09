using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Верёвка» (земля*, ранг 3/5*). A rope hangs down from the point of impact: 5 m (upgraded 10 m), until the end of the outing. Climb (2 m/s) and
    /// swing as a pendulum; on release the real tangential speed is kept. Attaches to static surfaces and movable
    /// non-living objects (follows them, the hanging player's weight acts on them); not to creatures.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class RopeEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Rope length, m (confirmed 5 → 10).")]
        [Min(0)] public float lengthBase = 5, lengthUpgraded = 10;
        [Tooltip("Climbing speed, m/s (board*).")]
        [Min(0)] public float climbSpeed = 2;

        public override void Apply(in ImpactContext c)
        {
            // TODO(Rope): SpawnForOuting the rope (parent: the hit Rigidbody's transform if movable); grabbing uses
            // motor.TryClaimExternalControl / MoveExternally / ReleaseExternalControl (IExternalMotorControl).
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Длина";
            baseValue = lengthBase;
            upgradedValue = lengthUpgraded;
            unit = " м";
            return true;
        }
    }
}
