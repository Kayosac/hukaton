using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    public sealed class ImpulseEffect : CardEffect, ICardArea
    {
        [Tooltip("Off its feet this long, s: it flies and lands before it walks again.")]
        [Range(0, 3)] public float creatureStagger = 1.3f;
        [Tooltip("A creature is hurled at the card's impulse times this, m/s, whatever it weighs (at most 20).")]
        [Min(0)] public float creatureFling = 1.4f;
        [Range(0, 2)] public float creatureLift = .7f;

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
                if (!body || !seen.Add(body) || !PropPhysics.Ready(body)) continue;
                var push = ExpeditionEffectRules.RadialImpulse(c.Point, body.worldCenterOfMass, impulse);
                // Creatures are hurled whatever they weigh, high and off their feet; loose things fly spinning.
                if (body.TryGetComponent(out WorldCreature creature))
                {
                    var away = Vector3.ProjectOnPlane(body.worldCenterOfMass - c.Point, Vector3.up);
                    var direction = ((away.sqrMagnitude > .0001f ? away.normalized : Random.insideUnitSphere) + Vector3.up * creatureLift).normalized;
                    if (!creature.Blow(direction * Mathf.Min(impulse * creatureFling, 20), creatureStagger, c.Point)) CardPhysics.Push(body, push);
                    meaningful = true;
                    continue;
                }
                if (!PropPhysics.Shove(body, push, 2.4f, 14, .5f)) CardPhysics.Push(body, push);
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
