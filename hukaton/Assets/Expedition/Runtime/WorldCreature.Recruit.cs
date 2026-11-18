using System;
using System.Linq;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>How a recruited creature (<see cref="WorldCreature.Recruit"/>) keeps to the player and picks its fights.</summary>
    [Serializable]
    public sealed class RecruitOrders
    {
        [Tooltip("Keeps about this far from the player, m.")]
        [Min(.5f)] public float followDistance = 2.6f;
        [Tooltip("Further than this from the player it runs to catch up, m (closer it walks).")]
        [Min(1)] public float catchUpDistance = 6;
        [Tooltip("Goes for hostile creatures within this distance of the player, m.")]
        [Min(1)] public float engageRadius = 9;
        [Tooltip("One that holds its ground (the machine-gun crew) fights what comes within this distance of its post, m.")]
        [Min(1)] public float holdRadius = 11;
        [Tooltip("Creatures more than this above or below it are on another floor and left alone, m.")]
        [Min(.5f)] public float floorHeight = 3.5f;
        [Tooltip("How often it looks round for a foe, s.")]
        [Min(.05f)] public float lookSeconds = .35f;
        [Tooltip("The colour a recruit takes (the drawing only darkens toward it) and how much.")]
        public Color tint = new Color(.45f, 1f, .55f);
        [Range(0, 1)] public float tintAmount = .75f;
    }

    /// <summary>
    /// «Вербовка» (<see cref="Cards.RecruitmentEffect"/>): a creature that went over to the player's side
    /// (<see cref="Faction.Player"/>) until it dies or the outing ends. It keeps a few metres behind the player, out of
    /// the way, goes for hostile creatures near the player and for anyone attacking the player's side, and never hurts
    /// the player or another ally — not with a blow, a shot, a burst or a puddle. One that holds its ground (the
    /// machine-gun crew) stays where it was recruited and fights whatever comes within its reach. A recruit is not
    /// drawn by the Lure, marked, or roused for its kin; «Раздор» still drags it into a brawl like any beast, and it
    /// comes back to the player's side when the brawl is over. Wild creatures it hits turn on it. A recruit left in a
    /// dungeon the player climbed out of waits there and follows again when the player comes back down (its dungeon
    /// sleeps with it). <see cref="ResetForOuting"/> ends it.
    /// </summary>
    public sealed partial class WorldCreature
    {
        /// <summary>Went over to the player's side (Вербовка) for this outing.</summary>
        public bool Recruited { get; private set; }
        /// <summary>A recruit that stays at its post instead of following the player.</summary>
        public bool HoldsGround { get; private set; }
        /// <summary>The creature a recruit is going for now, or null.</summary>
        public WorldCreature RecruitFoe => recruitFoe && recruitFoe.Alive ? recruitFoe : null;

        RecruitOrders recruitOrders;
        WorldCreature recruitFoe;
        float nextFoeLook, recruitSlot;
        Vector3 recruitPost;

        /// <summary>
        /// Takes the creature over to the player's side for the rest of the outing (<paramref name="holdGround"/>: it stays
        /// at its post). False when it is dead or already a recruit.
        /// </summary>
        public bool Recruit(RecruitOrders orders, bool holdGround)
        {
            if (!world || !Alive || dying || Recruited) return false;
            Recruited = true;
            HoldsGround = holdGround;
            recruitOrders = orders ?? new RecruitOrders();
            recruitFoe = null;
            nextFoeLook = 0;
            // Each recruit keeps its own place round the player: behind them, a little to one side.
            recruitSlot = UnityEngine.Random.Range(-75f, 75f);
            recruitPost = body ? body.position : transform.position;
            // Out of the wild squad it walked with: its mates no longer wait for it or follow its alarm.
            squad?.Remove(this);
            squad = null;
            CancelKinAlarm();
            attackTarget = null;
            provokedUntil = markedUntil = provokeTimer = 0;
            lured = false;
            investigateUntil = 0;
            if (!Brawling)
            {
                home = recruitPost;
                EndAttack();
                Enter(CreatureState.Idle);
            }
            return true;
        }

        void ResetRecruit()
        {
            Recruited = HoldsGround = false;
            recruitFoe = null;
            recruitOrders = null;
        }

        /// <summary>A recruit spares its own side: the player and every ally.</summary>
        bool Spares(ICombatant victim) => Recruited && victim != null && victim.Faction == Faction.Player;

        /// <summary>Decisions of a recruit (Update, instead of its temperament's).</summary>
        void ThinkRecruit()
        {
            var orders = recruitOrders;
            if (Time.time >= nextFoeLook)
            {
                nextFoeLook = Time.time + orders.lookSeconds * UnityEngine.Random.Range(.8f, 1.2f);
                recruitFoe = PickRecruitFoe(orders);
            }
            var foe = RecruitFoe;
            if (foe)
            {
                SetTarget(foe);
                // Keeps the hostile logic hunting and its home where the fight is: no running back to where it was born.
                provokedUntil = Mathf.Max(provokedUntil, Time.time + .5f);
                home = HoldsGround ? recruitPost : PlayerPosition;
                if (IsCalm || State == CreatureState.Threat || State == CreatureState.Return || State == CreatureState.Search
                    || State == CreatureState.Alert || State == CreatureState.Flee)
                    Enter(CreatureState.Chase);
                ThinkHostile();
                return;
            }
            // Nobody to fight: whatever it was doing ends, it never turns on the player instead.
            attackTarget = null;
            provokedUntil = 0;
            if (!IsCalm || State == CreatureState.Investigate)
            {
                EndAttack();
                Enter(CreatureState.Idle);
            }
            FollowPlayer(orders);
        }

        void FollowPlayer(RecruitOrders orders)
        {
            if (HoldsGround)
            {
                home = recruitPost;
                if (Flat(body.position - recruitPost).magnitude > 1.2f)
                {
                    if (State != CreatureState.Wander) Enter(CreatureState.Wander);
                    Go(recruitPost, wanderSpeed * 1.6f);
                }
                else
                {
                    if (State != CreatureState.Idle) Enter(CreatureState.Idle);
                    Halt();
                    Face(ToPlayer);
                }
                return;
            }
            Vector3 player = PlayerPosition;
            home = player;
            float distance = PlayerDistance;
            // Already on its way: it walks right up to its place; standing, it only sets off when the player is further.
            float start = State == CreatureState.Wander ? orders.followDistance : orders.followDistance + .8f;
            if (distance <= start)
            {
                if (State != CreatureState.Idle) Enter(CreatureState.Idle);
                Halt();
                Face(ToPlayer);
                return;
            }
            if (State != CreatureState.Wander) Enter(CreatureState.Wander);
            Vector3 back = world.motor ? -Flat(world.motor.transform.forward) : -ToPlayer;
            if (back.sqrMagnitude < .01f) back = -ToPlayer;
            Vector3 place = player + Quaternion.Euler(0, recruitSlot, 0) * back.normalized * orders.followDistance;
            float pace = distance > orders.catchUpDistance ? speed : Mathf.Max(wanderSpeed * 1.8f, speed * .6f);
            Go(place, pace);
        }

        /// <summary>
        /// The nearest wild creature worth fighting: one attacking the player's side first, then a hostile one
        /// (aggressive or territorial) near the player, or near the post of one that holds its ground. Not on another
        /// floor, not another recruit, not one hiding in an ambush.
        /// </summary>
        WorldCreature PickRecruitFoe(RecruitOrders orders)
        {
            var creatures = world.ActiveCreatures;
            if (creatures == null) return null;
            Vector3 self = body.position, anchor = HoldsGround ? recruitPost : PlayerPosition;
            float reach = HoldsGround ? orders.holdRadius : orders.engageRadius;
            WorldCreature best = null;
            float bestScore = float.MaxValue;
            foreach (var creature in creatures)
            {
                if (!creature || creature == this || !creature.isActiveAndEnabled || !creature.Alive || creature.Recruited) continue;
                if (Mathf.Abs(creature.Position.y - self.y) > orders.floorHeight) continue;
                var target = creature.Target;
                bool attacking = !creature.IsCalm && Combat.IsAlive(target) && target.Faction == Faction.Player;
                bool hostile = attacking || creature.temperament == Temperament.Aggressive || creature.temperament == Temperament.Territorial;
                if (!hostile || creature.Lurking && !attacking) continue;
                float toSelf = Flat(creature.Position - self).magnitude;
                float toAnchor = Flat(creature.Position - anchor).magnitude;
                if (toAnchor > reach && !(attacking && toSelf < reach)) continue;
                float score = toSelf - (attacking ? 4 : 0) - (creature == recruitFoe ? 1.5f : 0);
                if (score < bestScore) { bestScore = score; best = creature; }
            }
            return best;
        }

        /// <summary>A recruit's burst (the bubble larva): it hurts the wild creatures round it, never the player's side.</summary>
        void RecruitBurst(EnemyProjectileProfile profile, Vector3 at)
        {
            var creatures = world.ActiveCreatures;
            if (creatures == null || profile.splashRadius <= 0) return;
            foreach (var creature in creatures.ToArray())
            {
                if (!creature || creature == this || !creature.Alive || creature.Faction == Faction.Player) continue;
                Vector3 away = Flat(creature.Position - at);
                if (away.magnitude > profile.splashRadius || Mathf.Abs(creature.Position.y - at.y) > 2) continue;
                Vector3 impulse = (away.sqrMagnitude > .01f ? away.normalized : transform.forward) * profile.knockback + Vector3.up * profile.knockbackUp;
                creature.TakeDamage(new DamageInfo(profile.damage, impulse, creature.Centre, at, DamageKind.Hit, this));
            }
        }
    }
}
