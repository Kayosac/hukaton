using System.Collections.Generic;
using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Притяжение» (тьма, ранг 2/5). One impulse toward the point of impact, I = 6 (upgraded 8), radius 3 m, on every
    /// physical body: the player, creatures, allies, objects and loot. Δv = I / μ (<see cref="CardPhysics.Push"/>, so
    /// Lightness multiplies it; the player through the motor's normalized impulse). A pull, not a hold; pulled loot is
    /// not picked up.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class AttractionEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Impulse I toward the centre (board: 6 → 8).")]
        [Min(0)] public float impulseBase = 6, impulseUpgraded = 8;
        [Tooltip("Radius, m (same on both levels).")]
        [Min(0)] public float radius = 3;

        [Header("Feel")]
        [Tooltip("Upward share of the pull so bodies on the ground slide instead of sticking (0 = flat).")]
        [Range(0, 1)] public float lift = .2f;
        [Tooltip("A pulled creature stumbles this long (its legs do not fight the pull), s.")]
        [Range(0, 2)] public float creatureStagger = .45f;

        static readonly Collider[] Overlaps = new Collider[64];

        public override void Apply(in ImpactContext c)
        {
            var world = c.World;
            float impulse = c.Pick(impulseBase, impulseUpgraded);
            Vector3 centre = c.Point;
            bool meaningful = false;
            if (c.PlayerWithin(radius) && Toward(c.PlayerCentre, centre, out var playerPull))
            {
                world.motor.ApplyNormalizedImpulse(playerPull * impulse);
                meaningful = true;
            }
            var seen = new HashSet<Rigidbody>();
            int count = Physics.OverlapSphereNonAlloc(centre, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var body = Overlaps[i] ? Overlaps[i].attachedRigidbody : null;
                if (!body || !seen.Add(body)) continue;
                var loot = body.GetComponent<WorldLoot>();
                if (loot && loot.Available && body.isKinematic)
                {
                    // Finds rest kinematic in the valley: let this one move until the outing ends.
                    body.isKinematic = false;
                    world.RestoreAtOutingEnd(() => { if (body) body.isKinematic = true; });
                }
                if (body.isKinematic || !Toward(body.worldCenterOfMass, centre, out var pull)) continue;
                var creature = body.GetComponent<WorldCreature>();
                if (creature && creatureStagger > 0) creature.Stagger(creatureStagger, centre);
                CardPhysics.Push(body, pull * impulse);
                meaningful = true;
            }
            Feedback(c, meaningful);
        }

        bool Toward(Vector3 from, Vector3 to, out Vector3 direction)
        {
            direction = to - from;
            if (direction.sqrMagnitude < .0004f) return false;
            direction = (direction.normalized + Vector3.up * lift).normalized;
            return true;
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Импульс";
            baseValue = impulseBase;
            upgradedValue = impulseUpgraded;
            unit = "";
            return true;
        }
    }
}
