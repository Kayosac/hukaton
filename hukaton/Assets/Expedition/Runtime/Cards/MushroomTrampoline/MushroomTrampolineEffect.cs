using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Гриб-батут» (земля, ранг 3/5). A mushroom with a 1.6 m cap for 15 s on an upper surface. Landing on the cap from above: no fall damage, the body is
    /// thrown up: h_eq = v²/2g, h = clamp(k·h_eq, 1.5, 10), v_up = √(2gh), g = 9.81 in design units, k = 0.8 (upgraded 1).
    /// Horizontal speed is kept. Only the player bounces in this version.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// STUB from the foundation pass: only the impact feedback so far.
    /// </summary>
    public sealed class MushroomTrampolineEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Mushroom lifetime, s (same on both levels).")]
        [Min(0)] public float lifetimeSeconds = 15;
        [Tooltip("Share of the equivalent fall height returned (board: 80 % → 100 %).")]
        [Min(0)] public float heightReturnBase = .8f, heightReturnUpgraded = 1;
        [Tooltip("Bounce height limits over the cap, m (design units).")]
        [Min(0)] public float minBounceHeight = 1.5f, maxBounceHeight = 10;
        [Tooltip("Cap diameter, m.")]
        [Min(0)] public float capDiameter = 1.6f;

        public override void Apply(in ImpactContext c)
        {
            // TODO(MushroomTrampoline): SpawnForOuting the mushroom; its cap implements ILandingSurface (PlayerVitals asks it on landing): convert
            // with motor.ToReferenceSpeed / ReferenceScale and relaunch via motor.SetVerticalSpeed.
            Feedback(c, false);
            c.World.Announce(c.Definition.displayName + ": эффект в разработке", 1.5f);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Возврат высоты";
            baseValue = heightReturnBase * 100;
            upgradedValue = heightReturnUpgraded * 100;
            unit = " %";
            return true;
        }
    }
}
