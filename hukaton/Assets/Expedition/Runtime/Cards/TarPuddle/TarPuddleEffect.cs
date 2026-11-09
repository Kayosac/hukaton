using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Лужа смолы» (земля, ранг 1/5*). Leaves a tar puddle (<see cref="TarPuddleZone"/>) on the ground at the impact
    /// (or straight below it, when the card hit a wall or a creature) that lasts until the end of the outing and slows
    /// everyone on it by 50 %. Fire or Lightning ignite the whole puddle: 30 s of 1 damage unit per second to everyone
    /// in the fire, no slowdown, then it burns out. Rain washes it away (burning or not). Overlapping puddles do not stack.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
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

        [Header("Placement")]
        public TarPuddleZone puddlePrefab;
        [Tooltip("Steepest ground the tar settles on, degrees; steeper hits drip down to the ground below.")]
        [Range(0, 90)] public float maxSlope = 40;
        [Tooltip("How far below a wall or creature hit the tar still finds the ground, m.")]
        [Min(0)] public float dropDistance = 8;

        public override void Apply(in ImpactContext c)
        {
            if (!puddlePrefab || !CardGround.TryFind(c.Target, c.Point, c.Normal, maxSlope, dropDistance, out var point, out var normal))
            {
                Feedback(c, false);
                return;
            }
            var rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Random.Range(0, 360f), 0);
            var puddle = c.World.SpawnForOuting(puddlePrefab, point, rotation);
            if (puddle) puddle.Setup(c.Pick(radiusBase, radiusUpgraded), speedMultiplier, burnSeconds, burnDamagePerSecond);
            Feedback(c, puddle);
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
