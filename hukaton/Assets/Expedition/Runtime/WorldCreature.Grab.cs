using System;
using UnityEngine;
using Hakaton.Character;

namespace Hakaton.Expeditions
{
    /// <summary>How a creature that grabs (swamp mutant) holds the player.</summary>
    [Serializable]
    public sealed class GrabSettings
    {
        [Tooltip("Its strike grabs the player: no knock-back, the player cannot walk while held and is squeezed. " +
                 "The hold breaks when it is hurt (a card), frozen or knocked down, or when the player gets away (a jump, a push).")]
        public bool enabled;
        [Tooltip("How long it holds, seconds.")]
        [Min(.1f)] public float holdSeconds = 1.5f;
        [Tooltip("Damage of every squeeze while it holds: ½ heart per unit.")]
        [Min(0)] public int squeezeHalfHearts = 1;
        [Tooltip("Seconds between squeezes.")]
        [Min(.1f)] public float squeezeInterval = .75f;
        [Tooltip("The player slips out once further than the strike's contact range plus this, metres.")]
        [Min(0)] public float slip = .8f;
    }

    public sealed partial class WorldCreature
    {
        [Header("Grab")]
        public GrabSettings grab = new GrabSettings();

        bool holding;
        float holdUntil, nextSqueeze;
        int healthAtGrab;

        /// <summary>It holds the player in its grip.</summary>
        public bool Holding => holding;

        /// <summary>The strike landed on the player: it grabs.</summary>
        void StartHold()
        {
            holding = true;
            holdUntil = Time.time + grab.holdSeconds;
            nextSqueeze = Time.time + grab.squeezeInterval;
            healthAtGrab = Health;
        }

        /// <summary>
        /// While it holds: stands, faces the player, keeps him from walking and squeezes him now and then. False once the
        /// hold is over (time is up, it was hurt, the player got away); then it recovers as after any strike.
        /// </summary>
        bool ThinkHold()
        {
            if (!holding) return false;
            var target = Target;
            bool broken = Health < healthAtGrab || !Combat.IsAlive(target) || TargetDistance > contactRange + grab.slip;
            // The last squeeze falls on the hold's last moment: it still lands before the grip opens.
            if (!broken && Time.time >= nextSqueeze && nextSqueeze <= holdUntil + .001f) Squeeze(target);
            if (broken || Time.time >= holdUntil)
            {
                holding = false;
                if (State == CreatureState.Recover) stateTime = 0;
                return false;
            }
            Halt();
            Face(ToTarget);
            // Refreshed every frame: the grip lets go by itself a moment after the creature stops holding, whatever stops it.
            if (ReferenceEquals(target, world.Player)) world.motor.ApplyModifier(MotorModifier.MoveSpeed, 0, .2f);
            return true;
        }

        /// <summary>One squeeze of the grip: it lands even inside the player's protection after the previous one.</summary>
        void Squeeze(ICombatant target)
        {
            nextSqueeze += grab.squeezeInterval;
            squash.Kick(-2.5f);
            world.Play(strikeCue, body.position, .7f);
            if (grab.squeezeHalfHearts > 0)
                target.TakeDamage(new DamageInfo(ScaledDamage(grab.squeezeHalfHearts, DamageScale), Vector3.zero,
                    target.Position + Vector3.up * .9f, body.position, DamageKind.Hit, this, true));
        }
    }
}
