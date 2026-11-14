using System;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Hakaton.Expeditions
{
    /// <summary>Creatures that come out of a hurt one: the budding troll's buds, the bee mutant's swarms.</summary>
    [Serializable]
    public sealed class OffspringSettings
    {
        [Tooltip("As it is hurt, new creatures come out of it and go straight for whoever it fights.")]
        public bool enabled;
        [Tooltip("What comes out (a small troll, a bee swarm).")]
        public WorldCreature prefab;
        [Tooltip("Shares of its health at which they come out: 0.5 and 0.25 — once at half health, once at a quarter.")]
        public float[] atHealth = { .5f, .25f };
        [Tooltip("How many come out each time.")]
        [Min(1)] public int count = 1;
        [Tooltip("They appear this far from it, metres.")]
        [Min(0)] public float scatter = 1.6f;
        [Tooltip("Optional: played at the parent each time one comes out (flesh tearing for a bud).")]
        public SfxCue sound;
    }

    /// <summary>A flyer that vanishes in a puff and reappears beside or behind its target to dive at once (the clown).</summary>
    [Serializable]
    public sealed class BlinkSettings
    {
        [Tooltip("In a fight it vanishes in a puff of smoke with a laugh and reappears beside or behind the target, diving at once.")]
        public bool enabled;
        [Tooltip("Blinks only when the target it sees is this far away, metres: from and to.")]
        [Min(0)] public float minRange = 4, maxRange = 14;
        [Tooltip("Seconds from one blink to the next.")]
        [Min(0)] public float cooldown = 4.5f;
        [Tooltip("How long it is gone, seconds.")]
        [Min(.05f)] public float vanishSeconds = .45f;
        [Tooltip("It reappears this far from the target, metres.")]
        [Min(1)] public float appearDistance = 2.6f;
        [Tooltip("It chases in plain sight this long before it may blink, seconds.")]
        [Min(0)] public float showSeconds = 1;
        [Tooltip("The wind-up of the dive after it reappears, seconds: short, but still a warning.")]
        [Min(.05f)] public float windup = .45f;
        [Tooltip("The puff it vanishes and reappears in, and its size per metre of the creature's height.")]
        public VfxBurst puff;
        [Min(.01f)] public float puffScale = 1;
    }

    /// <summary>
    /// The small tricks of the forest's creatures: buds and summoned swarms (<see cref="offspring"/>), burrowing and the
    /// shell (<see cref="burrows"/>, <see cref="shellSeconds"/>), a bat that knocks cards away (<see cref="deflectCards"/>),
    /// a drunken zigzag (<see cref="staggerAngle"/>), a ram that runs through its target (<see cref="ramThrough"/>),
    /// a blink (<see cref="blink"/>) and the beekeeper's smoker that keeps the bees calm (<see cref="smoker"/>).
    /// </summary>
    public sealed partial class WorldCreature
    {
        [Header("Offspring")]
        public OffspringSettings offspring = new OffspringSettings();

        [Header("Shelter")]
        [Tooltip("Timid: instead of running off it burrows into the ground when it notices the player, and comes out once he has gone. Under the ground nothing reaches it.")]
        public bool burrows;
        [Tooltip("It comes out once the player has been further than the flee range for this long, seconds.")]
        [Min(0)] public float emergeSeconds = 2.5f;
        [Tooltip("A hit sends it into its shell for this long, seconds: nothing hurts it there and it does not move. 0: no shell.")]
        [Min(0)] public float shellSeconds;

        [Header("Batter")]
        [Tooltip("In a fight it knocks this share of the cards thrown at it away with its bat: they do nothing.")]
        [Range(0, 1)] public float deflectCards;
        [Tooltip("The burst where a knocked-away card meets the bat.")]
        public VfxBurst deflectVfx;

        [Header("Stagger")]
        [Tooltip("Walks in a drunken zigzag: its way swings this many degrees to each side. 0: straight.")]
        [Range(0, 80)] public float staggerAngle;
        [Tooltip("Swings of the zigzag per second.")]
        [Min(.05f)] public float staggerFrequency = .8f;

        [Header("Ram")]
        [Tooltip("Its dash runs through the target instead of stopping at it: it rams, goes on past, turns round and rams again.")]
        public bool ramThrough;

        [Header("Blink")]
        public BlinkSettings blink = new BlinkSettings();

        [Header("Smoke")]
        [Tooltip("The beekeeper: while it lives and is not angry, bees within the radius below stay calm. Hurt it and they all go for the player.")]
        public bool smoker;
        [Min(0)] public float smokeRadius = 14;
        [Tooltip("A bee: calm while a beekeeper's smoke is near (see Smoker).")]
        public bool calmedBySmoke;

        static readonly List<WorldCreature> smokers = new List<WorldCreature>();

        bool spawnedHere, burrowed, ramming, blinking, blinkStrike;
        float shellUntil, shellShape, clearFor, nextBlink, blinkAt, swingUntil, nextMound;

        /// <summary>In its shell: nothing hurts it.</summary>
        public bool InShell => Time.time < shellUntil;
        /// <summary>Burrowed under the ground: nothing reaches it.</summary>
        public bool Burrowed => burrowed;
        /// <summary>Gone in a puff, about to reappear by its target.</summary>
        public bool Blinking => blinking;

        void OnEnable() { if (smoker && !smokers.Contains(this)) smokers.Add(this); }

        void OnDestroy()
        {
            smokers.Remove(this);
            if (spawnedHere && world) world.creatures.Remove(this);
        }

        void ResetTricks()
        {
            shellUntil = 0;
            shellShape = 0;
            clearFor = 0;
            nextBlink = blinkAt = swingUntil = 0;
            blinkStrike = false;
            if (burrowed) SetBurrowed(false);
            if (blinking) { blinking = false; SetHidden(false); }
            RamThrough(false);
        }

        // ---------------- offspring ----------------

        /// <summary>Health went from <paramref name="before"/> to what it is now: a line of <see cref="OffspringSettings.atHealth"/> crossed lets some out.</summary>
        void ReleaseOffspring(int before)
        {
            if (!offspring.enabled || !offspring.prefab || Health <= 0 || offspring.atHealth == null || !world) return;
            foreach (float share in offspring.atHealth)
            {
                float line = share * maximumHealth;
                if (before > line && Health <= line)
                    for (int i = 0; i < offspring.count; i++) SpawnOffspring();
            }
        }

        void SpawnOffspring()
        {
            float a = Random.value * Mathf.PI * 2;
            Vector3 at = body.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * offspring.scatter;
            if (NavMesh.SamplePosition(at, out var hit, 2.5f, NavMesh.AllAreas)) at = hit.position + Vector3.up * .15f;
            else at = body.position;
            var child = Instantiate(offspring.prefab, at, transform.rotation, transform.parent);
            child.world = world;
            child.spawnPoints = null;
            child.spawnedHere = true;
            child.ResetForOuting(null);
            world.creatures.Add(child);
            child.JoinFight(Target);
            world.Spawn(world.vfx ? world.vfx.dustPuff : null, at, Quaternion.identity, bodyColour, Mathf.Clamp(child.TopHeight, .5f, 1.2f));
            world.Play(offspring.sound, body.position);
        }

        /// <summary>Goes for <paramref name="target"/> at once: a bud or a swarm called in, a bee roused by the beekeeper.</summary>
        public void JoinFight(ICombatant target)
        {
            if (!world || !Alive || dying) return;
            if (Combat.IsAlive(target) && !ReferenceEquals(target, world.Player)) SetTarget(target);
            provokedUntil = Mathf.Max(provokedUntil, Time.time + 8);
            lastSeen = TargetPosition;
            if (temperament == Temperament.Timid) return;
            if (IsCalm || State == CreatureState.Alert || State == CreatureState.Threat)
            {
                world.Play(threatCue, body.position);
                squash.Kick(-3);
                Enter(CreatureState.Chase);
            }
        }

        // ---------------- shell ----------------

        /// <summary>In its shell it stays put; true while it is in.</summary>
        bool ThinkShell()
        {
            if (!InShell) return false;
            Halt();
            return true;
        }

        /// <summary>A hit that landed sends it into its shell.</summary>
        void ShellUp()
        {
            if (shellSeconds <= 0 || Health <= 0) return;
            shellUntil = Time.time + shellSeconds;
            Halt();
        }

        /// <summary>A hit on the shell: a knock and a spark, nothing more.</summary>
        void ShellKnock(Vector3 point)
        {
            squash.Kick(-2);
            world.Play(hurtCue, body.position, .6f, 1.6f);
            world.Spawn(deflectVfx, point, Quaternion.identity, Color.white, .5f);
        }

        /// <summary>The drawing flattens into the shell and back: x and y scale.</summary>
        Vector2 ShellScale(float dt)
        {
            if (shellSeconds <= 0) return Vector2.one;
            shellShape = Mathf.MoveTowards(shellShape, InShell ? 1 : 0, dt * 7);
            return new Vector2(1 + .2f * shellShape, 1 - .5f * shellShape);
        }

        // ---------------- burrow ----------------

        /// <summary>
        /// A timid burrower: noticing the player it dives into the ground and stays there until he has been away for a
        /// while. False when it is no burrower or is out and unbothered.
        /// </summary>
        bool ThinkBurrow()
        {
            if (!burrows) return false;
            float distance = PlayerDistance;
            if (!burrowed)
            {
                if (!Provoked && !(sees && distance < alertRange)) return false;
                if (State != CreatureState.Idle) Enter(CreatureState.Idle);
                Halt();
                world.Play(alertCue, body.position, 1, 1.15f);
                world.Spawn(world.vfx ? world.vfx.dustPuff : null, body.position, Quaternion.identity, dustColour, Mathf.Clamp(TopHeight, .5f, 1));
                burrowed = true;
                clearFor = 0;
                return true;
            }
            if (State != CreatureState.Idle) Enter(CreatureState.Idle);
            Halt();
            // A little dust now and then shows where it sits.
            if (sunk >= .95f && Time.time >= nextMound) { nextMound = Time.time + 1.3f; KickDust(1); }
            if (distance > fleeRange && !Provoked) clearFor += Time.deltaTime;
            else clearFor = 0;
            if (clearFor >= emergeSeconds)
            {
                burrowed = false;
                world.Spawn(world.vfx ? world.vfx.dustPuff : null, body.position, Quaternion.identity, dustColour, Mathf.Clamp(TopHeight, .5f, 1));
            }
            return true;
        }

        /// <summary>
        /// Under the ground the body has no collider (nothing to hit that cannot be seen) and hangs where it is;
        /// out again, it is solid and falls as usual.
        /// </summary>
        void SetBurrowed(bool under)
        {
            if (!under) burrowed = false;
            if (capsule) capsule.enabled = !under;
            if (body)
            {
                body.useGravity = !under && !flying;
                if (under && !body.isKinematic) body.linearVelocity = Vector3.zero;
            }
        }

        // ---------------- batter ----------------

        /// <summary>
        /// A card is about to hit it: in a fight, a batter knocks some away with its bat. True when it did — the card
        /// does nothing.
        /// </summary>
        public bool TryDeflectCard(Vector3 point)
        {
            if (deflectCards <= 0 || !world || !Alive || dying) return false;
            if (IsCalm && !Provoked) return false;
            if (State == CreatureState.Frozen || State == CreatureState.Stunned || Random.value >= deflectCards) return false;
            swingUntil = Time.time + .3f;
            Face(point - body.position);
            squash.Kick(5);
            world.Play(strikeCue, body.position, 1, 1.25f);
            world.Spawn(deflectVfx, point, Quaternion.identity, Color.white, .8f);
            return true;
        }

        // ---------------- stagger ----------------

        /// <summary>The drunken zigzag: the way it walks swung to one side or the other.</summary>
        Vector3 Stagger(Vector3 velocity)
        {
            if (staggerAngle <= 0 || velocity.sqrMagnitude < .01f) return velocity;
            float phase = (GetInstanceID() & 63) * .37f;
            return Quaternion.Euler(0, Mathf.Sin((Time.time * staggerFrequency + phase) * Mathf.PI * 2) * staggerAngle, 0) * velocity;
        }

        // ---------------- ram ----------------

        /// <summary>During a ram the body passes through the player: it hits him and runs on.</summary>
        void RamThrough(bool on)
        {
            if (on == ramming) return;
            ramming = on;
            if (!capsule || !capsule.enabled || !world || !world.motor || !world.motor.characterController) return;
            Physics.IgnoreCollision(capsule, world.motor.characterController, on);
        }

        /// <summary>A ram throws the target aside, off its line, so it runs on past.</summary>
        Vector3 RamKnock(Vector3 victim)
        {
            Vector3 side = Vector3.Cross(Vector3.up, strikeDirection);
            if (Vector3.Dot(Flat(victim - body.position), side) < 0) side = -side;
            return side * 5 + strikeDirection * 1.5f + Vector3.up * 2.5f;
        }

        // ---------------- blink ----------------

        /// <summary>
        /// The blink in a fight: gone in a puff, back beside or behind the target diving at it. True while it is gone
        /// (it does nothing else then).
        /// </summary>
        bool TryBlink()
        {
            if (!blink.enabled) return false;
            if (blinking)
            {
                Halt();
                if (Time.time >= blinkAt) Reappear();
                return blinking;
            }
            // It shows itself first: a second of the chase before the first trick.
            if (State != CreatureState.Chase || stateTime < blink.showSeconds || !sees || Time.time < nextBlink) return false;
            float distance = TargetDistance;
            if (distance < blink.minRange || distance > blink.maxRange) return false;
            blinking = true;
            blinkAt = Time.time + blink.vanishSeconds;
            nextBlink = blinkAt + blink.cooldown;
            world.Spawn(blink.puff, Centre, Quaternion.identity, Color.white, blink.puffScale * Mathf.Clamp(TopHeight, .6f, 1.4f));
            world.Play(threatCue, body.position, 1, 1.1f);
            SetHidden(true);
            Halt();
            return true;
        }

        void Reappear()
        {
            blinking = false;
            Vector3 target = TargetPosition;
            // Beside or behind the target as it looks: the player's view, or the way from this creature for anyone else.
            Vector3 look = ReferenceEquals(Target, world.Player) && world.motor ? Flat(world.motor.transform.forward) : Flat(target - body.position);
            look = look.sqrMagnitude > .01f ? look.normalized : Vector3.forward;
            float side = Random.value < .5f ? 1 : -1;
            float[] turns = { 180, 125 * side, -125 * side, 90 * side, -90 * side };
            Vector3 spot = body.position;
            foreach (float turn in turns)
            {
                Vector3 probe = target + Quaternion.Euler(0, turn, 0) * look * blink.appearDistance;
                if (!NavMesh.SamplePosition(probe, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                Vector3 eye = target + Vector3.up * 1.2f, at = hit.position + Vector3.up * 1.2f;
                if (Physics.Linecast(eye, at, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                spot = hit.position;
                break;
            }
            Vector3 airborne = spot + Vector3.up * (flying ? cruiseHeight : .15f);
            body.position = airborne;
            transform.position = airborne;
            body.linearVelocity = Vector3.zero;
            if (navigator.isOnNavMesh) navigator.Warp(spot);
            Face(target - airborne);
            body.rotation = facing;
            transform.rotation = facing;
            SetHidden(false);
            world.Spawn(blink.puff, Centre, Quaternion.identity, Color.white, blink.puffScale * Mathf.Clamp(TopHeight, .6f, 1.4f));
            world.Play(alertCue, body.position, 1, 1.2f);
            blinkStrike = true;
            Enter(CreatureState.Windup);
        }

        /// <summary>Gone in the puff: nothing to see and nothing to hit.</summary>
        void SetHidden(bool hidden)
        {
            if (view) view.gameObject.SetActive(!hidden);
            if (capsule) capsule.enabled = !hidden;
            if (overhead) overhead.gameObject.SetActive(!hidden);
            if (hidden && flightShadow) flightShadow.gameObject.SetActive(false);
        }

        // ---------------- smoke ----------------

        /// <summary>A bee near a calm, living beekeeper: it leaves the player be.</summary>
        bool Smoked
        {
            get
            {
                if (!calmedBySmoke || Provoked) return false;
                foreach (var keeper in smokers)
                    if (keeper && keeper != this && keeper.world == world && keeper.Alive && !keeper.Provoked
                        && Flat(keeper.Position - Position).magnitude <= keeper.smokeRadius)
                        return true;
                return false;
            }
        }

        /// <summary>The beekeeper was hurt by the player: every bee round it goes for him.</summary>
        void RouseBees()
        {
            if (!smoker || !world) return;
            foreach (var bee in world.ActiveCreatures)
                if (bee && bee != this && bee.calmedBySmoke && bee.Alive && Flat(bee.Position - Position).magnitude <= smokeRadius * 1.3f)
                    bee.JoinFight(world.Player);
        }
    }
}
