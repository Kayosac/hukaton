using System;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Hakaton.Expeditions
{
    /// <summary>What a creature that can shoot does when the target comes up close.</summary>
    public enum CloseCombat
    {
        [Tooltip("Its usual melee attack (wind-up, strike) once the target is within the attack range.")]
        Strike,
        [Tooltip("A pure shooter: up close it backs off; only when it cannot get away does it shove with a weak hit.")]
        Shove,
        [Tooltip("Never fights up close: it only shoots and backs off.")]
        None,
    }

    /// <summary>
    /// Ranged attack of a creature: when it shoots, how many at once, and how it keeps its distance. Empty
    /// <see cref="projectile"/> means it has none. What flies is set in the projectile asset.
    /// </summary>
    [Serializable]
    public sealed class RangedAttack
    {
        [Tooltip("What it shoots. Empty: no ranged attack, the creature fights as before.")]
        public EnemyProjectileProfile projectile;

        [Header("When it shoots")]
        [Tooltip("Shoots at a target it sees closer than this, metres.")]
        [Min(1)] public float range = 12;
        [Tooltip("Closer than this it does not shoot: it strikes (Close combat: Strike) or backs off.")]
        [Min(0)] public float minRange = 3.5f;
        [Tooltip("Aiming before the shot, seconds: the pose, the sound and the red marks warn of it. The direction locks at its end.")]
        [Min(.05f)] public float aimSeconds = .6f;
        [Tooltip("Seconds from one shot to the next.")]
        [Min(0)] public float cooldown = 2.2f;
        [Tooltip("Stands still after the shot, seconds.")]
        [Min(0)] public float recovery = .5f;

        [Header("Volley")]
        [Tooltip("Projectiles in one shot, fanned out side by side.")]
        [Min(1)] public int projectiles = 1;
        [Tooltip("Angle between the outermost projectiles of a fan, degrees.")]
        [Range(0, 120)] public float spreadAngle = 24;
        [Tooltip("Shots in a row at the same aim (a burst).")]
        [Min(1)] public int burst = 1;
        [Tooltip("Seconds between the shots of a burst.")]
        [Min(.02f)] public float burstInterval = .2f;
        [Tooltip("Where the shots leave from. Empty: from the front of the drawing at the height below.")]
        public Transform muzzle;
        [Tooltip("Without a muzzle: height of the launch point as a share of the drawing's height.")]
        [Range(0, 1.5f)] public float muzzleHeight = .6f;

        [Header("Keeping the distance")]
        [Tooltip("Distance it tries to keep from the target between shots, metres. 0: keeps coming closer (and strikes up close).")]
        [Min(0)] public float keepDistance = 8;
        [Tooltip("Backs off when the target comes closer than this, metres. 0: never backs off.")]
        [Min(0)] public float retreatDistance = 4;
        [Tooltip("Speed of backing off, as a share of its running speed.")]
        [Min(.1f)] public float retreatSpeed = .8f;
        [Tooltip("Circles round the target while waiting for the next shot, m/s. 0: stands and watches.")]
        [Min(0)] public float circleSpeed;

        [Header("Up close")]
        public CloseCombat closeCombat = CloseCombat.Strike;
        [Tooltip("Close combat Shove: damage of the shove (½ heart per unit to the player).")]
        [Min(0)] public int shoveDamage = 1;

        public bool Enabled => projectile && projectile.prefab;
    }

    public sealed partial class WorldCreature
    {
        [Header("Ranged attack")]
        public RangedAttack ranged = new RangedAttack();

        /// <summary>The creature can shoot.</summary>
        public bool HasRangedAttack => ranged != null && ranged.Enabled;

        float nextShot, cornered, circleUntil, circleSign = 1;
        int shotsFired;
        bool shoving;
        Vector3 aimPoint;

        void ResetRanged()
        {
            nextShot = 0;
            shotsFired = 0;
            shoving = false;
            cornered = 0;
            circleUntil = 0;
        }

        /// <summary>Chase with a ranged attack: shoots from afar, strikes up close, keeps its distance in between.</summary>
        void ChaseWithRanged(float distance)
        {
            var r = ranged;
            bool close = distance <= attackRange;
            if (close && r.closeCombat == CloseCombat.Strike) { shoving = false; Enter(CreatureState.Windup); return; }
            if (close && r.closeCombat == CloseCombat.Shove && (cornered > .6f || r.retreatDistance <= 0)) { shoving = true; Enter(CreatureState.Windup); return; }

            if (sees && distance <= r.range && distance >= r.minRange && Time.time >= nextShot)
            {
                if (ShotIsClear()) { Enter(CreatureState.Aim); return; }
                // Something in the way (a rock, another creature): walk round to get a clear shot.
                Go(TargetPosition, speed);
                return;
            }
            if (!sees || distance > r.range) { cornered = 0; Go(TargetPosition, speed); return; }
            if (r.retreatDistance > 0 && distance < r.retreatDistance) { BackOff(); return; }
            cornered = 0;
            if (r.keepDistance <= 0 || distance > r.keepDistance + .75f) { Go(TargetPosition, speed); return; }
            // At its distance: watches, or circles round, until it can shoot again.
            if (r.circleSpeed > 0) Circle(r.circleSpeed);
            else Halt();
            Face(ToTarget);
        }

        void BackOff()
        {
            Vector3 away = -ToTarget;
            away = away.sqrMagnitude > .01f ? away.normalized : -transform.forward;
            Vector3 best = Vector3.zero;
            foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f })
            {
                Vector3 candidate = Quaternion.Euler(0, angle, 0) * away;
                if (CanMove(candidate, 2.5f)) { best = candidate; break; }
            }
            float planarSpeed = Flat(body.linearVelocity).magnitude;
            if (best == Vector3.zero || stateTime > .3f && planarSpeed < .35f) cornered += Time.deltaTime;
            else cornered = Mathf.Max(0, cornered - Time.deltaTime * .5f);
            if (best == Vector3.zero) Halt();
            else Go(body.position + best * 3, speed * ranged.retreatSpeed);
            // Backs away facing the target.
            Face(ToTarget);
        }

        void Circle(float circleSpeed)
        {
            if (Time.time >= circleUntil) { circleUntil = Time.time + Random.Range(1.8f, 3.4f); if (Random.value < .5f) circleSign = -circleSign; }
            Vector3 side = Vector3.Cross(Vector3.up, ToTarget.normalized) * circleSign;
            if (!CanMove(side, 1.5f)) { circleSign = -circleSign; side = -side; }
            Go(body.position + side * 2, circleSpeed);
        }

        /// <summary>Room to move this way: the NavMesh for walkers, the air for flyers.</summary>
        bool CanMove(Vector3 direction, float distance)
        {
            if (flying)
            {
                float radius = capsule ? capsule.radius : .25f;
                return !Physics.SphereCast(Centre, radius, direction, out var hit, distance, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    || hit.rigidbody == body;
            }
            if (!navigator.enabled || !navigator.isOnNavMesh) return true;
            return !NavMesh.Raycast(body.position, body.position + direction * distance, out _, NavMesh.AllAreas);
        }

        Vector3 MuzzlePoint()
        {
            if (ranged.muzzle) return ranged.muzzle.position;
            float reach = (capsule ? capsule.radius * Size : .3f) + .15f;
            return body.position + Vector3.up * TopHeight * ranged.muzzleHeight + transform.forward * reach;
        }

        Vector3 AimPointOf(ICombatant target)
            => ranged.projectile.aimAt == EnemyProjectileProfile.AimPoint.Feet ? target.Position + Vector3.up * .05f : target.Centre;

        bool ShotIsClear()
        {
            var p = ranged.projectile;
            var target = Target;
            Vector3 from = body.position + Vector3.up * TopHeight * ranged.muzzleHeight;
            if (ranged.muzzle) from = ranged.muzzle.position;
            bool player = ReferenceEquals(target, world.Player);
            return EnemyProjectileFlight.IsPathClear(from, AimPointOf(target), p.speed, p.FlightArc, p.radius, transform, player ? null : target.Root);
        }

        /// <summary>Aim → Shoot → Recover. Returns false for the states it does not handle.</summary>
        bool ThinkRanged()
        {
            var r = ranged;
            switch (State)
            {
                case CreatureState.Aim:
                    Halt();
                    Face(ToTarget);
                    if (stateTime >= r.aimSeconds)
                    {
                        // The direction locks here: keep moving and the shot misses.
                        aimPoint = AimPointOf(Target);
                        Enter(CreatureState.Shoot);
                    }
                    return true;
                case CreatureState.Shoot:
                    Halt();
                    while (shotsFired < r.burst && stateTime >= shotsFired * r.burstInterval) FireVolley();
                    if (shotsFired >= r.burst && stateTime >= (r.burst - 1) * r.burstInterval + ShotPoseSeconds)
                    {
                        nextShot = Time.time + r.cooldown;
                        Enter(CreatureState.Recover);
                        idleDuration = r.recovery;
                    }
                    return true;
            }
            return false;
        }

        const float ShotPoseSeconds = .3f;

        void FireVolley()
        {
            var r = ranged;
            var p = r.projectile;
            shotsFired++;
            Vector3 from = MuzzlePoint();
            Vector3 toAim = aimPoint - from;
            int count = Mathf.Max(1, r.projectiles);
            var target = Target;
            for (int i = 0; i < count; i++)
            {
                float angle = count > 1 ? Mathf.Lerp(-r.spreadAngle * .5f, r.spreadAngle * .5f, i / (float)(count - 1)) : 0;
                Vector3 to = from + Quaternion.Euler(0, angle, 0) * toAim;
                EnemyProjectileFlight.Launch(from, to, p.speed, p.FlightArc, out var velocity, out float gravity);
                var shot = world.SpawnForOuting(p.prefab, from, Quaternion.LookRotation(velocity.sqrMagnitude > 1e-4f ? velocity : transform.forward));
                if (shot) shot.Launch(world, p, this, target, velocity, gravity);
            }
            Vector3 flat = Flat(toAim);
            world.Spawn(p.muzzleFlash, from, Quaternion.LookRotation(flat.sqrMagnitude > .01f ? flat : transform.forward), p.impactTint, p.effectScale);
            world.Play(p.fireCue, from, 1, Random.Range(.95f, 1.08f));
            squash.Kick(5);
        }

        /// <summary>Damage of the current melee strike: a shooter's shove is weaker than a real strike.</summary>
        int StrikeDamage => shoving && HasRangedAttack ? ranged.shoveDamage : attackHalfHearts;
    }
}
