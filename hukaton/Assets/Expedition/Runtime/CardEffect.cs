using System.Collections.Generic;
using Hakaton.Character;
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
        /// <summary>Where the card left the hand (the branch bridge grows back toward it). The player's feet when unknown.</summary>
        public readonly Vector3 ThrowOrigin;

        public ImpactContext(ExpeditionWorld world, CardCopy copy, CardDefinition definition, Collider target,
            Vector3 point, Vector3 normal, Vector3 velocity, Vector3? throwOrigin = null)
        {
            World = world; Copy = copy; Definition = definition; Target = target;
            Point = point; Normal = normal; Velocity = velocity;
            Creature = target ? target.GetComponentInParent<WorldCreature>() : null;
            Obstacle = target ? target.GetComponentInParent<WorldObstacle>() : null;
            ThrowOrigin = throwOrigin ?? (world && world.motor ? world.motor.transform.position : point);
        }

        // ---------- card parameters ----------

        /// <summary>The copy's level: 1, 2 or 3 (I / II / III).</summary>
        public int Level => Copy != null ? Copy.Level : 1;
        /// <summary>A card parameter at this copy's level: <c>c.At(duration)</c>.</summary>
        public float At(LevelFloat value) => value.At(Level);
        /// <inheritdoc cref="At(LevelFloat)"/>
        public int At(LevelInt value) => value.At(Level);

        // ---------- the player and the world ----------

        public FirstPersonMotor Motor => World.motor;
        public PlayerVitals Vitals => World.vitals;
        public CardHandView Hand => World.hand;
        /// <summary>The player as a fighter (for chain lightning, burning tar).</summary>
        public ICombatant Player => World.Player;

        // ---------- what was hit ----------

        /// <summary>The Rigidbody that was hit (creature, prop, cube, loot), if any.</summary>
        public Rigidbody Body => Target ? Target.attachedRigidbody : null;
        /// <summary>The fighter hit directly (creature, ally), if any.</summary>
        public ICombatant HitCombatant => Combat.Of(Target);
        /// <summary>A component of type T on the hit collider or its parents; null when nothing was hit.</summary>
        public T TargetAs<T>() where T : class => Target ? Target.GetComponentInParent<T>() : null;
        /// <summary>Card zones containing the impact point (the puddle the card landed in). Appends; returns the count.</summary>
        public int ZonesAtPoint(List<ICardZone> results, CardZoneKind? kind = null) => World.Zones.Containing(Point, results, kind);

        /// <summary>Damage, freeze seconds or half hearts of healing of the simple cards at this level.</summary>
        public int Power => Definition.power.At(Level);
        public float Radius => Definition.effectRadius;
        public float Impulse => Definition.impulse.At(Level);
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

        /// <summary>Effect, sound, shake and hit-stop of a landing; returns the spawned effect (null when none is assigned).</summary>
        protected VfxBurst Feedback(in ImpactContext c, bool meaningful, float scale = 1)
        {
            var world = c.World;
            var burst = world.Spawn(impactVfx, c.Point + c.Normal * .05f, Quaternion.LookRotation(c.Normal), c.Accent, vfxScale * scale);
            world.Play(meaningful || !missSound ? impactSound : missSound, c.Point);
            float distance = Vector3.Distance(world.viewCamera.transform.position, c.Point);
            float proximity = Mathf.Clamp01(1.4f - distance / 16f);
            if (world.feel) world.feel.AddTrauma((meaningful ? hitShake : shake) * proximity);
            if (meaningful && hitStop > 0 && world.time) world.time.HitStop(hitStop);
            return burst;
        }
    }
}
