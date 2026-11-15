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

        public override float BodyNudge => 7;
        public override float BodyFling => 2.2f;
        public override float BodyLift => .7f;
        public override float BodySpin => 10;

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
            // A heavy blow sends a shock wave: everything round the hit jumps and scatters; upgraded, it breaks what is near.
            float boost = PropPhysics.LevelBoost(c.Level);
            PropPhysics.Burst(c.Point, 2.2f * Mathf.Sqrt(boost), 3.5f * boost, .9f, c.Body, 2.4f, 12, c.Blow);
            Feedback(c, meaningful);
        }
    }
}
