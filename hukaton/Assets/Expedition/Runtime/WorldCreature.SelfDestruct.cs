using System;
using UnityEngine;

namespace Hakaton.Expeditions
{
    [Serializable]
    public sealed class SelfDestructSettings
    {
        [Tooltip("Instead of a normal strike, the creature bursts when its wind-up ends and leaves the configured puddle.")]
        public bool enabled;
        [Tooltip("Direct hit uses damage and splashRadius; the puddle uses this same profile. Fire cue and impact provide the burst feedback.")]
        public EnemyProjectileProfile explosion;
    }

    public sealed partial class WorldCreature
    {
        [Header("Self destruct")]
        public SelfDestructSettings selfDestruct = new SelfDestructSettings();

        bool ThinkSelfDestruct()
        {
            if (!selfDestruct.enabled || !selfDestruct.explosion || State != CreatureState.Windup) return false;
            Halt();
            Face(ToTarget);
            if (stateTime >= windup) Explode();
            return true;
        }

        void Explode()
        {
            var profile = selfDestruct.explosion;
            Vector3 at = body.position;
            if (profile.splashRadius > 0 && PlayerDistance <= profile.splashRadius && Mathf.Abs(PlayerPosition.y - at.y) <= 2)
            {
                var player = world.Player;
                if (Combat.IsAlive(player))
                {
                    Vector3 away = Flat(PlayerPosition - at);
                    Vector3 impulse = (away.sqrMagnitude > .01f ? away.normalized : transform.forward) * profile.knockback
                        + Vector3.up * profile.knockbackUp;
                    player.TakeDamage(new DamageInfo(profile.damage, impulse, player.Centre, at, DamageKind.Hit, this));
                }
            }
            EnemyPuddle.Spawn(world, at, profile, this);
            world.Spawn(profile.impact, at + Vector3.up * .2f, Quaternion.identity, profile.impactTint, profile.effectScale);
            world.Play(profile.fireCue, at);
            Damage(maximumHealth, Vector3.zero, Centre, 0, DamageKind.Other);
        }
    }
}
