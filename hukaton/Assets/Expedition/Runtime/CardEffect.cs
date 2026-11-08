using System.Collections.Generic;
using Hakaton.Decks;
using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
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
}
