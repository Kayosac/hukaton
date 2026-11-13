using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// «Лечение»: healing splashes around the impact. The player within the card's radius gets its power in half
    /// hearts (2, upgraded 3). Every ally within the radius or hit directly (<see cref="IHealable"/>, the awakened tree;
    /// owner, 09.11 evening) gets the same number in its own health units (1 unit = ½ heart), up to its maximum, with
    /// the healing poof and sound on it.
    /// </summary>
    public sealed class HealEffect : CardEffect, ICardArea
    {
        [Tooltip("Size of the healing poof on a healed ally (× vfxScale).")]
        [Min(0)] public float allyVfxScale = 1.3f;

        static readonly List<IHealable> Reached = new List<IHealable>();

        public override void Apply(in ImpactContext c)
        {
            bool reached = c.PlayerWithin(c.Radius);
            bool meaningful = reached && c.World.vitals.Heal(c.Power);
            // «Лечение» cures the debuffs it can (Poison), even at full hearts.
            if (reached && c.World.statuses && c.World.statuses.Count > 0)
            {
                int before = c.World.statuses.Count;
                c.World.statuses.Healed();
                if (c.World.statuses.Count < before) meaningful = true;
            }
            if (HealAllies(c)) meaningful = true;
            Feedback(c, meaningful);
        }

        bool HealAllies(in ImpactContext c)
        {
            var world = c.World;
            var direct = c.TargetAs<IHealable>();
            Reached.Clear();
            foreach (var fighter in world.Allies.All)
                if (fighter is IHealable ally && ally.Alive && (ReferenceEquals(ally, direct) || ally.WithinHealReach(c.Point, c.Radius)))
                    Reached.Add(ally);
            bool healed = false;
            foreach (var ally in Reached)
            {
                if (ally.Heal(c.Power) <= 0) continue;
                healed = true;
                world.Spawn(impactVfx, ally.Centre, Quaternion.identity, c.Accent, vfxScale * allyVfxScale);
                world.Play(world.cues ? world.cues.playerHeal : null, ally.Centre);
            }
            Reached.Clear();
            return healed;
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = definition.effectRadius;
            return true;
        }

        bool ICardArea.LeavesOutlinedZone => false;
    }
}
