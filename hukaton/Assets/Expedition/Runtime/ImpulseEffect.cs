using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class ImpulseEffect : CardEffect, ICardArea
    {
        [Range(0, 2)] public float creatureStagger = .55f;

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            float impulse = c.Impulse, radius = c.Radius;
            bool meaningful = false;
            if (c.PlayerWithin(radius))
            {
                // Normalized mass 1: Δv = I, scaled so the launch height matches the design at any gravity.
                Vector3 push = ExpeditionEffectRules.RadialImpulse(c.Point, c.PlayerCentre, impulse);
                world.motor.ApplyNormalizedImpulse(push);
                world.OnPlayerLaunched(push);
                meaningful = true;
            }
            var seen = new HashSet<Rigidbody>();
            foreach (var hit in Physics.OverlapSphere(c.Point, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.attachedRigidbody;
                if (!body || body.isKinematic || !seen.Add(body)) continue;
                // ForceMode.Impulse: Δv = I / mass. Creature masses act as the board's μ (mushroom 0.3, goblin dog 1.5).
                // Through CardPhysics so Lightness on the body multiplies it.
                CardPhysics.Push(body, ExpeditionEffectRules.RadialImpulse(c.Point, body.worldCenterOfMass, impulse));
                var creature = body.GetComponent<WorldCreature>();
                if (creature) { creature.Stagger(creatureStagger, c.Point); meaningful = true; }
            }
            Feedback(c, meaningful);
        }

        bool ICardArea.TryGetArea(CardDefinition definition, int level, Collider target, out float radius)
        {
            radius = definition.effectRadius;
            return true;
        }

        bool ICardArea.LeavesOutlinedZone => false;
    }
}
