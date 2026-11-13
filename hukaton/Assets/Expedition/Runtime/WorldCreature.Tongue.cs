using System;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>A sticky tongue (giant vile toad): from a few metres it yanks the player to itself, then slams him.</summary>
    [Serializable]
    public sealed class TongueSettings
    {
        [Tooltip("Shoots out its tongue at a target it sees between the min and the max range and pulls it in; the next blow of the attack lands on it.")]
        public bool enabled;
        [Tooltip("Reach of the tongue, metres: from and to.")]
        [Min(0)] public float minRange = 2.5f, maxRange = 6;
        [Tooltip("Wind-up before the tongue shoots out, seconds (the throat swells).")]
        [Min(.05f)] public float windup = .6f;
        [Tooltip("How hard the tongue pulls the player in (impulse units, as the knock-backs; up to lift him off the ground).")]
        [Min(0)] public float pull = 9, pullUp = 3;
        [Tooltip("Seconds before it can use the tongue again.")]
        [Min(0)] public float cooldown = 4;
        [Tooltip("Wind-up of the slam that follows the pull, seconds.")]
        [Min(.05f)] public float slamWindup = .5f;
    }

    public sealed partial class WorldCreature
    {
        [Header("Tongue")]
        public TongueSettings tongue = new TongueSettings();

        bool tonguing;
        float nextTongue;

        /// <summary>Its tongue is out (the strike of a tongue attack).</summary>
        public bool Tonguing => tonguing && State == CreatureState.Strike;

        /// <summary>
        /// Chasing: a target it sees within the tongue's reach (and the tongue ready) gets the tongue; the slam follows as
        /// the next blow. True when it starts the tongue attack.
        /// </summary>
        bool TryTongue()
        {
            if (!tongue.enabled || State != CreatureState.Chase || !sees || Time.time < nextTongue) return false;
            float distance = TargetDistance;
            if (distance < tongue.minRange || distance > tongue.maxRange) return false;
            nextTongue = Time.time + tongue.cooldown;
            tongueArmed = true;
            Enter(CreatureState.Windup);
            return true;
        }

        bool tongueArmed;

        /// <summary>The tongue lashes out: the player it reaches is yanked in, ready for the slam.</summary>
        void LashTongue()
        {
            var target = Target;
            if (!Combat.IsAlive(target) || !ReferenceEquals(target, world.Player)) return;
            if (StrikeShielded(target, target.Position + Vector3.up * .9f)) return;
            Vector3 back = body.position - target.Position;
            back.y = 0;
            if (back.magnitude > tongue.maxRange + 1) return;
            world.motor.ApplyNormalizedImpulse(back.normalized * tongue.pull + Vector3.up * tongue.pullUp);
            world.Play(strikeCue, target.Position, .8f, .7f);
        }
    }
}
