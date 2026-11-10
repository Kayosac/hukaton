using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Верёвка» (земля*, ранг 3/5*). A rope hangs down from the point of impact: 5 m (upgraded 10 m), until the end of the outing, reusable.
    /// Climb (2 m/s) and swing as a pendulum; on release the real tangential speed is kept. Attaches to static surfaces
    /// and movable non-living objects (follows them, the hanging player's weight acts on them); not to creatures or allies.
    /// Everything about the rope itself: <see cref="RopeLine"/>.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class RopeEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Rope length, m (confirmed 5 → 10).")]
        public LevelFloat length = new LevelFloat(5, 7.5f, 10);
        [Tooltip("Climbing speed, m/s (board*).")]
        [Min(0)] public float climbSpeed = 2;

        [Header("Prefab")]
        public RopeLine ropePrefab;
        public string creatureNotice = "Верёвка не крепится к существам";

        public override void Apply(in ImpactContext c)
        {
            var target = c.Target;
            int water = LayerMask.NameToLayer("Water");
            bool living = c.Creature || c.HitCombatant != null;
            if (!ropePrefab || !target || target.isTrigger || living || (water >= 0 && target.gameObject.layer == water))
            {
                if (!ropePrefab) Debug.LogWarning("RopeEffect: no rope prefab assigned.", this);
                Feedback(c, false, .6f);
                if (living && !string.IsNullOrEmpty(creatureNotice)) c.World.Announce(creatureNotice, 1.4f);
                return;
            }
            var rope = c.World.SpawnForOuting(ropePrefab, c.Point, Quaternion.identity);
            if (!rope) return;
            rope.Attach(target, c.Point, c.Normal, c.At(length), climbSpeed);
            Feedback(c, true);
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Длина";
            values = length;
            unit = " м";
            return true;
        }
    }
}
