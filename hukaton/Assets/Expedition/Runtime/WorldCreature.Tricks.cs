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

    /// <summary>A last-moment dodge of a thrown card: a hop aside (the mantis) or a blink away in a puff (the clown).</summary>
    [Serializable]
    public sealed class DodgeSettings
    {
        [Tooltip("In a fight, while not winding up or striking, it may dodge a card thrown at it at the last moment.")]
        public bool enabled;
        [Tooltip("The chance to dodge each card that would hit it.")]
        [Range(0, 1)] public float chance = .35f;
        [Tooltip("It reacts this many seconds before the card would reach it.")]
        [Min(.05f)] public float warning = .25f;
        [Tooltip("Blink: vanishes in its puff (Blink settings) and reappears a few metres aside. Off: hops aside.")]
        public bool blink;
        [Tooltip("The hop aside: speed, m/s.")]
        [Min(0)] public float hopSpeed = 11;
        [Tooltip("The blink: it reappears this far aside, metres.")]
        [Min(1)] public float blinkDistance = 4.5f;
        [Tooltip("Seconds after a dodge before the next one.")]
        [Min(0)] public float cooldown = 1.2f;
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
        [Tooltip("Its drawing while it hides in the shell. Empty: the usual drawing, flattened and greyed.")]
        public Sprite shellPicture;

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
        [Tooltip("Its drawing while it rams (riding straight at the camera). Empty: the attack pose.")]
        public Sprite ramPicture;

        [Header("Blink")]
        public BlinkSettings blink = new BlinkSettings();

        [Header("Dodge")]
        public DodgeSettings dodge = new DodgeSettings();

        [Header("Burrow nerve")]
        [Tooltip("A burrower dives when the player it sees comes this close: each picks its own distance between these two, metres (a bold one waits till you are right by it).")]
        public Vector2 burrowDistance = new Vector2(2, 9);
        [Tooltip("Under the ground, when the player stands still this long (seconds, picked between these two) it peeks out to see whether he has gone.")]
        public Vector2 peekAfter = new Vector2(1.8f, 3.2f);
        [Tooltip("How long a peek lasts, seconds; in it the head is out and it can be hit.")]
        [Min(.2f)] public float peekSeconds = 1.3f;

        [Header("Smoke")]
        [Tooltip("The beekeeper: while it lives and is not angry, bees within the radius below stay calm. Hurt it and they all go for the player.")]
        public bool smoker;
        [Min(0)] public float smokeRadius = 14;
        [Tooltip("A bee: calm while a beekeeper's smoke is near (see Smoker).")]
        public bool calmedBySmoke;

        static readonly List<WorldCreature> smokers = new List<WorldCreature>();

        bool spawnedHere, burrowed, ramming, blinking, blinkStrike, dodgeBlink, peeking;
        float shellUntil, shellShape, clearFor, nextBlink, blinkAt, swingUntil, nextMound, nextDodge, burrowNerve, stillFor, peekWait, peekUntil;
        Vector3 dodgeSide;
        readonly HashSet<CardProjectile> weighedCards = new HashSet<CardProjectile>();

        /// <summary>In its shell: nothing hurts it.</summary>
        public bool InShell => Time.time < shellUntil;
        /// <summary>Burrowed under the ground: nothing reaches it.</summary>
        public bool Burrowed => burrowed;
        /// <summary>Gone in a puff, about to reappear by its target.</summary>
        public bool Blinking => blinking;
        /// <summary>A burrower peeking out of the ground to see whether the player has gone.</summary>
        public bool Peeking => peeking;

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
            blinkStrike = dodgeBlink = peeking = false;
            nextDodge = stillFor = 0;
            weighedCards.Clear();
            burrowNerve = Random.Range(Mathf.Min(burrowDistance.x, burrowDistance.y), Mathf.Max(burrowDistance.x, burrowDistance.y));
            peekWait = Random.Range(Mathf.Min(peekAfter.x, peekAfter.y), Mathf.Max(peekAfter.x, peekAfter.y));
            if (burrowed) { burrowed = false; SetBurrowed(false); }
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

        /// <summary>A drawing of its own for the state it is in (in the shell, riding a ram), or null for the poses.</summary>
        Sprite SpecialPicture => InShell && shellPicture ? shellPicture
            : State == CreatureState.Strike && ramming && ramPicture ? ramPicture : null;

        /// <summary>The drawing flattens into the shell and back: x and y scale (not with a shell drawing of its own).</summary>
        Vector2 ShellScale(float dt)
        {
            if (shellSeconds <= 0 || shellPicture) return Vector2.one;
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
            float dt = Time.deltaTime;
            // Each has its own nerve: a timid one dives as soon as it sees the player, a bold one only when he is right by it.
            bool near = Provoked || (sees || distance < hearingRange) && distance < Mathf.Min(burrowNerve, alertRange);
            if (!burrowed)
            {
                if (!near) return false;
                if (State != CreatureState.Idle) Enter(CreatureState.Idle);
                Halt();
                world.Play(alertCue, body.position, 1, 1.15f);
                world.Spawn(world.vfx ? world.vfx.dustPuff : null, body.position, Quaternion.identity, dustColour, Mathf.Clamp(TopHeight, .5f, 1));
                burrowed = true;
                peeking = false;
                clearFor = stillFor = 0;
                return true;
            }
            if (State != CreatureState.Idle) Enter(CreatureState.Idle);
            Halt();
            // A little dust now and then shows where it sits.
            if (sunk >= .95f && Time.time >= nextMound) { nextMound = Time.time + 1.3f; KickDust(1); }
            bool still = !world.motor || Flat(world.motor.Velocity).sqrMagnitude < .25f;
            if (peeking)
            {
                // Peeking: the player moves close by — back under at once; time's up — under again, or out if he has gone.
                if (!still && distance < burrowNerve + 1.5f || Provoked) { peeking = false; stillFor = 0; world.Play(alertCue, body.position, .8f, 1.3f); }
                else if (Time.time >= peekUntil)
                {
                    peeking = false;
                    stillFor = 0;
                    peekWait = Random.Range(Mathf.Min(peekAfter.x, peekAfter.y), Mathf.Max(peekAfter.x, peekAfter.y));
                }
                return true;
            }
            // The player stands still for a while: it peeks out to see whether he has gone.
            stillFor = still ? stillFor + dt : 0;
            if (stillFor >= peekWait && sunk >= .95f)
            {
                peeking = true;
                peekUntil = Time.time + peekSeconds;
                KickDust(stepDustCount);
                return true;
            }
            if (distance > fleeRange && !Provoked) clearFor += dt;
            else clearFor = 0;
            if (clearFor >= emergeSeconds)
            {
                burrowed = false;
                world.Spawn(world.vfx ? world.vfx.dustPuff : null, body.position, Quaternion.identity, dustColour, Mathf.Clamp(TopHeight, .5f, 1));
            }
            return true;
        }

        /// <summary>How deep a burrower wants to be: all the way under, its head out while it peeks, or up.</summary>
        float BurrowDepth => !burrowed ? 0 : peeking ? .45f : 1;

        /// <summary>
        /// Under the ground the body has no collider (nothing to hit that cannot be seen) and hangs where it is;
        /// out again, it is solid and falls as usual.
        /// </summary>
        void SetBurrowed(bool under)
        {
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

        // ---------------- dodge ----------------

        /// <summary>
        /// In a fight and not mid-attack: a card about to hit it may be dodged — one roll per card, at the last moment.
        /// </summary>
        void ThinkDodge()
        {
            if (!dodge.enabled || dying || blinking || Time.time < nextDodge || !world) return;
            if (IsCalm && !Provoked) return;
            if (State == CreatureState.Windup || State == CreatureState.Strike || State == CreatureState.Aim || State == CreatureState.Shoot) return;
            var cards = CardProjectile.Active;
            if (cards.Count == 0) { if (weighedCards.Count > 0) weighedCards.Clear(); return; }
            Vector3 centre = Centre;
            float reach = CapsuleRadius + .55f;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (!card || card.Resolved) continue;
                Vector3 v = card.Velocity;
                float speed2 = v.sqrMagnitude;
                if (speed2 < 1) continue;
                Vector3 p = card.transform.position;
                float t = Vector3.Dot(centre - p, v) / speed2;
                if (t < 0 || t > dodge.warning) continue;
                if ((p + v * t - centre).magnitude > reach) continue;
                if (!weighedCards.Add(card)) continue;
                if (Random.value >= dodge.chance) continue;
                DodgeCard(v);
                return;
            }
        }

        /// <summary>Dodges a card flying with <paramref name="cardVelocity"/>: a hop aside or a blink away (Dodge settings).</summary>
        public void DodgeCard(Vector3 cardVelocity)
        {
            if (!dodge.enabled || !world || dying) return;
            nextDodge = Time.time + dodge.cooldown;
            Vector3 side = Vector3.Cross(Vector3.up, cardVelocity);
            side = side.sqrMagnitude > .01f ? side.normalized : transform.right;
            if (Random.value < .5f) side = -side;
            // Prefer the side with ground to land on.
            if (!NavMesh.SamplePosition(body.position + side * 3, out _, 1.5f, NavMesh.AllAreas)) side = -side;
            dodgeSide = side;
            if (dodge.blink)
            {
                dodgeBlink = true;
                blinking = true;
                blinkAt = Time.time + Mathf.Max(.2f, blink.vanishSeconds * .7f);
                nextBlink = Mathf.Max(nextBlink, Time.time + 1.5f);
                world.Spawn(blink.puff, Centre, Quaternion.identity, Color.white, blink.puffScale * Mathf.Clamp(TopHeight, .6f, 1.4f));
                world.Play(threatCue, body.position, 1, 1.25f);
                SetHidden(true);
                Halt();
                if (State != CreatureState.Chase && State != CreatureState.Search) Enter(CreatureState.Chase);
                return;
            }
            // A hop aside, low and quick.
            body.linearVelocity = side * dodge.hopSpeed + Vector3.up * 2.5f;
            squash.Kick(5);
            KickDust(stepDustCount * 3);
            world.Play(alertCue, body.position, .9f, 1.2f);
        }

        // ---------------- blink ----------------

        /// <summary>
        /// The blink in a fight: gone in a puff, back beside or behind the target diving at it. True while it is gone
        /// (it does nothing else then).
        /// </summary>
        bool TryBlink()
        {
            if (!blink.enabled && !dodgeBlink) return false;
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
            if (dodgeBlink)
            {
                // Dodged a card: back a few metres aside, and the fight goes on.
                dodgeBlink = false;
                Vector3 to = body.position + dodgeSide * dodge.blinkDistance;
                Vector3 land = NavMesh.SamplePosition(to, out var near, 2.5f, NavMesh.AllAreas) ? near.position : body.position;
                Vector3 air = land + Vector3.up * (flying ? cruiseHeight : .15f);
                body.position = air;
                transform.position = air;
                body.linearVelocity = Vector3.zero;
                if (navigator.isOnNavMesh) navigator.Warp(land);
                Face(target - air);
                SetHidden(false);
                world.Spawn(blink.puff, Centre, Quaternion.identity, Color.white, blink.puffScale * Mathf.Clamp(TopHeight, .6f, 1.4f));
                world.Play(alertCue, body.position, 1, 1.3f);
                Enter(CreatureState.Chase);
                return;
            }
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
