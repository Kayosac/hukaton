using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class HeavyStrikeEffect : CardEffect
    {
        [Range(0, 1)] public float lift = .3f;
        [Range(0, 2)] public float stagger = .7f;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = c.Creature && c.Creature.Alive;
            if (meaningful)
            {
                // Along the throw, never into the ground (the old version pushed along the contact normal).
                Vector3 direction = (StrikeEffect.Flat(c.Direction) + Vector3.up * lift).normalized;
                c.Creature.Damage(c.Power, direction * c.Impulse, c.Point, 0);
                c.Creature.Stagger(stagger, c.Point);
            }
            else if (c.TargetAs<ICardCreature>() is ICardCreature other && other.Alive)
                meaningful = other.TakeDamage(new DamageInfo(c.Power, (StrikeEffect.Flat(c.Direction) + Vector3.up * lift).normalized * c.Impulse,
                    c.Point, c.ThrowOrigin));
            Feedback(c, meaningful);
        }
    }
}
