using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>Who hurt a brawling creature.</summary>
    public enum BrawlHurtSource
    {
        /// <summary>The player's cards and hazards, or the player's allies.</summary>
        Player,
        /// <summary>Another creature: a strike or a shot.</summary>
        Creature,
        /// <summary>Anything else: crashing into the ground or a rock.</summary>
        Other,
    }

    /// <summary>
    /// Watches the creatures of a beast brawl (<see cref="Events.BeastBrawl"/>). Ordinary creatures have no referee and
    /// never call it.
    /// </summary>
    public interface IBrawlReferee
    {
        /// <summary>The creature took a hit (it may have died of it).</summary>
        void CreatureHurt(WorldCreature creature, BrawlHurtSource source, ICombatant by, Vector3 impulse);

        /// <summary>The creature bumped into another creature.</summary>
        void CreatureBumped(WorldCreature creature, WorldCreature other);
    }

    /// <summary>
    /// Brawl mode: only for the creatures of a beast brawl event. While it lasts the creature fights the rival the
    /// referee gives it, whatever its temperament, inside the event's circle, and pays the player no attention. Its
    /// strikes and shots are the usual ones (<see cref="TakeDamage"/>, <see cref="EnemyProjectile"/>). Before the fight
    /// it postures at the rival: the aggression drawing, the red "!!" and its threat sound.
    /// </summary>
    public sealed partial class WorldCreature
    {
        /// <summary>The brawl this creature belongs to, or null for an ordinary creature.</summary>
        public IBrawlReferee Referee { get; private set; }
        /// <summary>Fighting (or posturing at) another creature for a brawl.</summary>
        public bool Brawling { get; private set; }
        /// <summary>Posturing: stands its ground facing the rival, not attacking yet.</summary>
        public bool Posturing => Brawling && brawlHold;

        bool brawlHold;
        Vector3 arenaCentre;
        float arenaRadius = 7, nextPosture;
        // Ranged distances as authored: the brawl keeps the shooter inside the circle and puts them back afterwards.
        bool brawlRangedSaved;
        float savedKeepDistance, savedRetreatDistance, savedRange, savedMinRange;
        // Hit bookkeeping for the referee: who is dealing the hit being applied right now.
        ICombatant hurtBy;
        bool hurtByCrash;

        /// <summary>Makes this creature part of a brawl: the referee hears about its hits and bumps. It behaves as usual until told to fight.</summary>
        public void JoinBrawl(IBrawlReferee referee) => Referee = referee;

        /// <summary>Stands facing <paramref name="rival"/> in the aggression drawing with the red "!!", growling now and then.</summary>
        public void Posture(ICombatant rival, Vector3 centre, float radius)
        {
            bool already = Posturing;
            EnterBrawl(rival, centre, radius);
            brawlHold = true;
            if (already) return;
            nextPosture = 0;
            Enter(CreatureState.Threat);
        }

        /// <summary>Fights <paramref name="rival"/> (a creature of the other pack) inside the circle; the referee changes the rival as they fall.</summary>
        public void Fight(ICombatant rival, Vector3 centre, float radius)
        {
            bool wasHolding = Brawling && brawlHold;
            EnterBrawl(rival, centre, radius);
            brawlHold = false;
            if (wasHolding || IsCalm || State == CreatureState.Threat) Aggro();
        }

        /// <summary>Leaves the brawl and goes for the player, as if the player had just hurt it.</summary>
        public void FightPlayer()
        {
            if (!Alive) return;
            LeaveBrawl();
            provokedUntil = Time.time + 8;
            lastSeen = PlayerPosition;
            Aggro();
        }

        /// <summary>The brawl is over for this creature: it goes back to its ordinary behaviour (wounds and all).</summary>
        public void LeaveBrawl()
        {
            bool was = Brawling;
            Brawling = brawlHold = false;
            attackTarget = null;
            RestoreBrawlRanged();
            if (!was || !Alive) return;
            provokedUntil = 0;
            if (State != CreatureState.Frozen && State != CreatureState.Stunned) Enter(CreatureState.Return);
        }

        void EnterBrawl(ICombatant rival, Vector3 centre, float radius)
        {
            Brawling = true;
            arenaCentre = centre;
            arenaRadius = Mathf.Max(1, radius);
            SetTarget(rival);
            if (HasRangedAttack && !brawlRangedSaved)
            {
                brawlRangedSaved = true;
                savedKeepDistance = ranged.keepDistance;
                savedRetreatDistance = ranged.retreatDistance;
                savedRange = ranged.range;
                savedMinRange = ranged.minRange;
                // The whole fight fits in the circle: a shooter keeps its distance well inside it.
                float across = arenaRadius * .5f;
                ranged.keepDistance = Mathf.Min(ranged.keepDistance, across);
                ranged.retreatDistance = Mathf.Min(ranged.retreatDistance, across * .6f);
                ranged.range = Mathf.Min(ranged.range, arenaRadius * 1.6f);
                ranged.minRange = Mathf.Min(ranged.minRange, across * .5f);
            }
        }

        void RestoreBrawlRanged()
        {
            if (!brawlRangedSaved) return;
            brawlRangedSaved = false;
            ranged.keepDistance = savedKeepDistance;
            ranged.retreatDistance = savedRetreatDistance;
            ranged.range = savedRange;
            ranged.minRange = savedMinRange;
        }

        /// <summary>A fresh spawn is no part of any brawl until a referee takes it again.</summary>
        void ResetBrawl()
        {
            RestoreBrawlRanged();
            Referee = null;
            Brawling = brawlHold = false;
            hurtBy = null;
            hurtByCrash = false;
        }

        float FromArenaCentre => Flat(body.position - arenaCentre).magnitude;

        /// <summary>Decisions while brawling, instead of the temperament's.</summary>
        void ThinkBrawl()
        {
            if (brawlHold)
            {
                if (State != CreatureState.Threat) Enter(CreatureState.Threat);
                Halt();
                Face(ToTarget);
                if (Time.time >= nextPosture)
                {
                    nextPosture = Time.time + Random.Range(1.1f, 1.9f);
                    world.Play(threatCue, body.position, 1, Random.Range(.92f, 1.1f));
                    squash.Kick(-2.5f);
                }
                return;
            }
            // Keeps the hostile logic hunting; whoever is left goes calm once the referee lets go.
            provokedUntil = Mathf.Max(provokedUntil, Time.time + .5f);
            switch (State)
            {
                case CreatureState.Idle:
                case CreatureState.Wander:
                case CreatureState.Graze:
                case CreatureState.Investigate:
                case CreatureState.Alert:
                case CreatureState.Threat:
                case CreatureState.Flee:
                case CreatureState.Search:
                case CreatureState.Return:
                    Enter(CreatureState.Chase);
                    break;
                case CreatureState.Reposition:
                    // A charger's run-up never takes it out of the circle.
                    if (FromArenaCentre > arenaRadius - 1.5f) Enter(CreatureState.Windup);
                    break;
            }
            ThinkHostile();
            KeepDesiredInArena();
        }

        /// <summary>Near the edge of the circle the creature does not walk, charge or back off any further out.</summary>
        void KeepDesiredInArena()
        {
            Vector3 outward = Flat(body.position - arenaCentre);
            float distance = outward.magnitude;
            if (distance < arenaRadius - 1.2f || distance < .01f) return;
            outward /= distance;
            float away = Vector3.Dot(desired, outward);
            if (away > 0) desired -= outward * away;
        }

        /// <summary>FixedUpdate: a knock toward the edge stops at the edge of the circle.</summary>
        void ContainInArena()
        {
            Vector3 outward = Flat(body.position - arenaCentre);
            float distance = outward.magnitude;
            if (distance < arenaRadius - .4f || distance < .01f) return;
            outward /= distance;
            Vector3 velocity = body.linearVelocity;
            float away = Vector3.Dot(Flat(velocity), outward);
            if (away > 0) body.linearVelocity = velocity - outward * away;
            if (distance > arenaRadius - .1f)
            {
                // Already over the line (a big knock): ease back inside.
                Vector3 back = -outward * Mathf.Min(3, (distance - arenaRadius + .4f) * 6);
                body.linearVelocity = new Vector3(back.x, body.linearVelocity.y, back.z);
            }
        }

        void ReportHurt(int amount, Vector3 impulse)
        {
            if (Referee == null) return;
            var source = hurtByCrash ? BrawlHurtSource.Other
                : hurtBy == null ? BrawlHurtSource.Player
                : hurtBy.Faction == Faction.Player ? BrawlHurtSource.Player
                : BrawlHurtSource.Creature;
            if (amount <= 0 && source != BrawlHurtSource.Player) return;
            Referee.CreatureHurt(this, source, hurtBy, impulse);
        }

        void ReportBump(Collision collision)
        {
            if (Referee == null || !collision.rigidbody) return;
            var other = collision.rigidbody.GetComponent<WorldCreature>();
            if (other && other != this) Referee.CreatureBumped(this, other);
        }
    }
}
