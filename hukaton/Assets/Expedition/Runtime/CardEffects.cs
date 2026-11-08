using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Everything an effect needs to know about one card hitting something.</summary>
    public readonly struct ImpactContext
    {
        public readonly ExpeditionWorld World;
        public readonly CardCopy Copy;
        public readonly CardDefinition Definition;
        public readonly Collider Target;
        public readonly Vector3 Point, Normal, Velocity;
        public readonly WorldCreature Creature;
        public readonly WorldObstacle Obstacle;

        public ImpactContext(ExpeditionWorld world, CardCopy copy, CardDefinition definition, Collider target,
            Vector3 point, Vector3 normal, Vector3 velocity)
        {
            World = world; Copy = copy; Definition = definition; Target = target;
            Point = point; Normal = normal; Velocity = velocity;
            Creature = target ? target.GetComponentInParent<WorldCreature>() : null;
            Obstacle = target ? target.GetComponentInParent<WorldObstacle>() : null;
        }

        public int Power => Copy.Power;
        public float Radius => Definition.effectRadius;
        public float Impulse => Definition.ImpulseAtLevel(Copy.Level);
        public Color Accent => Definition.accent;
        public Vector3 Direction => Velocity.sqrMagnitude > .001f ? Velocity.normalized : -Normal;
        public Vector3 PlayerCentre => World.motor.transform.TransformPoint(World.motor.characterController.center);
        public bool PlayerWithin(float radius) => Vector3.Distance(PlayerCentre, Point) <= radius + World.motor.characterController.radius;
    }

    /// <summary>
    /// What a card does when it lands. One asset per card type; all numbers come from the card definition
    /// (board preset v0.1). Feedback (effect, sound, shake, hit-stop) is tuned per asset.
    /// </summary>
    public abstract class CardEffect : ScriptableObject
    {
        [Header("Feedback")]
        public VfxBurst impactVfx;
        [Min(0)] public float vfxScale = 1;
        public SfxCue impactSound;
        [Tooltip("Played instead when the card hits nothing it can affect.")]
        public SfxCue missSound;
        [Range(0, 1)] public float shake = .12f;
        [Tooltip("Shake when the effect actually lands on a creature or the player.")]
        [Range(0, 1)] public float hitShake = .3f;
        [Min(0)] public float hitStop;

        public abstract void Apply(in ImpactContext context);

        protected void Feedback(in ImpactContext c, bool meaningful, float scale = 1)
        {
            var world = c.World;
            world.Spawn(impactVfx, c.Point + c.Normal * .05f, Quaternion.LookRotation(c.Normal), c.Accent, vfxScale * scale);
            world.Play(meaningful || !missSound ? impactSound : missSound, c.Point);
            float distance = Vector3.Distance(world.viewCamera.transform.position, c.Point);
            float proximity = Mathf.Clamp01(1.4f - distance / 16f);
            if (world.feel) world.feel.AddTrauma((meaningful ? hitShake : shake) * proximity);
            if (meaningful && hitStop > 0 && world.time) world.time.HitStop(hitStop);
        }
    }

    /// <summary>Exploration · Impulse: radial push from the point of impact. Under your feet it launches you.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Cards/Impulse")]
    public sealed class ImpulseEffect : CardEffect
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
                // ForceMode.Impulse: Δv = I / mass. Creature masses are the board's μ (hare 0.5, boar 2, wolf 1).
                body.AddForce(ExpeditionEffectRules.RadialImpulse(c.Point, body.worldCenterOfMass, impulse), ForceMode.Impulse);
                var creature = body.GetComponent<WorldCreature>();
                if (creature) { creature.Stagger(creatureStagger, c.Point); meaningful = true; }
            }
            Feedback(c, meaningful);
        }
    }

    /// <summary>Exploration · Ice: freezes water until the outing ends; freezes a creature for Power seconds.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Cards/Freeze")]
    public sealed class FreezeEffect : CardEffect
    {
        public override void Apply(in ImpactContext c)
        {
            bool meaningful = false;
            if (c.Creature) { c.Creature.Freeze(c.Power); meaningful = true; }
            var water = c.Obstacle ? c.Obstacle : c.World.ObstacleNear(c.Point, c.Radius, ObstacleKind.Lake);
            if (water && water.React(CardAction.Freeze, c.Point)) meaningful = true;
            Feedback(c, meaningful);
        }
    }

    /// <summary>Exploration · Fire: burns thickets open for the outing; one-off damage to a creature (test variant of v0.1).</summary>
    [CreateAssetMenu(menuName = "Hakaton/Cards/Fire")]
    public sealed class FireEffect : CardEffect
    {
        [Min(0)] public float burnVisualSeconds = 1.6f;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = false;
            if (c.Creature) { c.Creature.Damage(c.Power, c.Direction * 1.5f, c.Point, burnVisualSeconds); meaningful = true; }
            var thicket = c.Obstacle ? c.Obstacle : c.World.ObstacleNear(c.Point, c.Radius, ObstacleKind.Thorns);
            if (thicket && thicket.React(CardAction.Fire, c.Point)) meaningful = true;
            Feedback(c, meaningful);
        }
    }

    /// <summary>Combat · Strike: plain direct damage. The most damage per card.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Cards/Strike")]
    public sealed class StrikeEffect : CardEffect
    {
        [Tooltip("Small shove along the throw so hits read physically.")]
        [Min(0)] public float nudge = 1.2f;

        public override void Apply(in ImpactContext c)
        {
            bool meaningful = c.Creature && c.Creature.Alive;
            if (meaningful) c.Creature.Damage(c.Power, Flat(c.Direction) * nudge, c.Point, 0);
            Feedback(c, meaningful);
        }

        internal static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0;
            return direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
        }
    }

    /// <summary>Combat · Heavy strike: less damage, big knockback along the throw (slam them into rocks or off ledges).</summary>
    [CreateAssetMenu(menuName = "Hakaton/Cards/Heavy strike")]
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
            Feedback(c, meaningful);
        }
    }

    /// <summary>Combat · Heal: a splash; restores Power half-hearts once if you stand in it.</summary>
    [CreateAssetMenu(menuName = "Hakaton/Cards/Heal")]
    public sealed class HealEffect : CardEffect
    {
        public override void Apply(in ImpactContext c)
        {
            bool meaningful = c.PlayerWithin(c.Radius) && c.World.vitals.Heal(c.Power);
            Feedback(c, meaningful);
        }
    }
}
