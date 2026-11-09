using System.Collections;
using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions.Cards
{
    public enum TreeAllyState { Awakening, Follow, Wait, Engage, Attack, Dying }

    /// <summary>
    /// An awakened tree: an ally of the player (<see cref="Faction.Player"/>) until the end of the outing or its death.
    /// Walks after the player along the NavMesh (no teleport: with no path it waits at the last reachable point),
    /// helps against creatures that attack the player or the squad and against the one the player hurt, never hunts
    /// peaceful creatures. Branch strike: wind-up, one hit at most per strike and only on a real contact.
    /// Creatures turn on it when it is closer than their target or when it hits them. Its Rigidbody mass is the board's
    /// μ, so impulses push it like any body; tar slows it (<see cref="IMoveSpeedScalable"/>); rain wets it
    /// (<see cref="WetStatus"/> on <see cref="Root"/>) and lightning, burning tar and creatures hurt it through
    /// <see cref="TakeDamage"/>. Locomotion is copied from <see cref="WorldCreature"/>: the agent plans, the body moves.
    /// The look is a swappable child (<see cref="TreeAllyLook"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreeAlly : MonoBehaviour, ICombatant, IMoveSpeedScalable, IOutingSpawned
    {
        [Header("Authored parts")]
        public Rigidbody body;
        public NavMeshAgent navigator;
        [Tooltip("Swappable look: a child prefab with a TreeAllyLook (placeholder for now).")]
        public TreeAllyLook look;
        [Tooltip("Health pips above the crown (one pip per point).")]
        public CreatureOverhead overhead;
        [Tooltip("Dust from under the roots (world-space particle system).")]
        public ParticleSystem stepDust;
        [Min(0)] public int stepDustCount = 3;
        public Color dustColour = new Color(.62f, .52f, .4f);

        [Header("Fight (the card sets these from its asset on awakening)")]
        [Min(1)] public int maximumHealth = 12;
        [Min(0)] public int strikeDamage = 2;
        [Tooltip("Branch reach, m: wind-up starts within it and the strike only lands within it.")]
        [Min(.5f)] public float reach = 2.5f;
        [Tooltip("Whole attack cycle and its wind-up, s.")]
        [Min(.1f)] public float attackCycle = 2.5f;
        [Min(0)] public float windup = .8f;
        [Tooltip("How long after the wind-up the swinging branch can connect, s.")]
        [Min(.05f)] public float strikeWindow = .3f;
        [Tooltip("Push given by the branch (impulse units: Δv = I / μ of the target) and its upward part.")]
        [Min(0)] public float strikeImpulse = 3, strikeLift = 1.5f;
        [Tooltip("The target must be in front of the tree: minimum cosine between facing and the target.")]
        [Range(-1, 1)] public float strikeArc = .35f;

        [Header("Following")]
        [Tooltip("Stops this close to the player, m.")]
        [Min(.5f)] public float followDistance = 2.8f;
        [Tooltip("Farther than this it hurries, m.")]
        [Min(1)] public float catchUpDistance = 8;
        [Min(0)] public float walkSpeed = 3.2f, catchUpSpeed = 4.8f;
        [Min(1)] public float acceleration = 14;
        [Min(10)] public float turnRate = 260;
        [Tooltip("How far around the player a walkable point is searched for, m. None: the tree keeps its last destination.")]
        [Min(.5f)] public float playerNavSearch = 2.5f;

        [Header("Helping")]
        public TreeAllyTargeting.Rules targeting = TreeAllyTargeting.Rules.Default;
        [Tooltip("A creature that lost health counts as being fought by the squad for this long, s.")]
        [Min(0)] public float hurtMemorySeconds = 8;
        [Tooltip("An attacking creature closer to the tree than to its target by this margin (and within range) turns on the tree, m.")]
        [Min(0)] public float drawRange = 6, drawMargin = 1;
        [Tooltip("It goes back to the player when the player is closer than the tree by this margin and the tree has not hit it lately, m.")]
        [Min(0)] public float releaseMargin = 3;
        [Min(0)] public float hitMemorySeconds = 4;

        [Header("Presentation")]
        [Min(0)] public float awakenSeconds = .8f;
        [Min(0)] public float hurtSeconds = .3f;
        [Tooltip("The fallen tree lies this long before it sinks away, s.")]
        [Min(0)] public float corpseSeconds = 1.6f;
        [Min(.1f)] public float gaitFrequency = 1.5f;
        public VfxBurst awakenVfx, strikeVfx, deathVfx;
        public Color leafColour = new Color(.42f, .55f, .3f);
        [Header("Sounds")]
        public SfxCue awakenCue;
        public SfxCue windupCue, swingCue, hitCue, hurtCue, deathCue, stepCue;

        public int Health { get; private set; }
        public TreeAllyState State { get; private set; }
        public TreeAttackPhase AttackPhase => cycle.Phase;
        /// <summary>The creature the tree is fighting now, if any.</summary>
        public WorldCreature Foe => foe;
        /// <summary>The tree this ally replaced (hidden until the end of the outing).</summary>
        public AwakenableTree Origin { get; private set; }
        public ExpeditionWorld World => world;

        ExpeditionWorld world;
        TreeAttackCycle cycle;
        TimedModifier moveSpeedScale;
        WorldCreature foe;
        ICombatant strikeTarget;
        Vector3 desired, destination, strikeDirection;
        Quaternion facing;
        bool hasDestination, dying, swung;
        float stateTime, nextThink, nextPath, hurtUntil, gaitPhase, fade, deathProgress, awakenedAt;
        readonly Dictionary<WorldCreature, int> lastHealth = new Dictionary<WorldCreature, int>();
        readonly Dictionary<WorldCreature, float> hurtAt = new Dictionary<WorldCreature, float>();
        readonly Dictionary<WorldCreature, float> hitAt = new Dictionary<WorldCreature, float>();
        readonly List<FoeCandidate> candidates = new List<FoeCandidate>();
        readonly List<WorldCreature> candidateCreatures = new List<WorldCreature>();

        // ---------------- ICombatant ----------------

        public Faction Faction => Faction.Player;
        public bool Alive => Health > 0 && !dying;
        public Vector3 Position => body ? body.position : transform.position;
        public Vector3 Centre => Position + Vector3.up * Height * .5f;
        public Transform Root => transform;
        public Rigidbody Body => body;
        float Height => look ? look.Height : 2.6f;

        // ---------------- setup ----------------

        void Awake()
        {
            navigator.updatePosition = false;
            navigator.updateRotation = false;
            Health = maximumHealth;
            facing = transform.rotation;
            cycle = new TreeAttackCycle(attackCycle, windup, strikeWindow);
            moveSpeedScale.Clear();
        }

        public void OnSpawned(ExpeditionWorld owner) => world = owner;

        /// <summary>Numbers from the card (board: health 12 → 18, strike 2 within 2.5 m, cycle 2.5 s with a 0.8 s wind-up, μ 3).</summary>
        public void Configure(int health, int damage, float strikeReach, float cycleSeconds, float windupSeconds, float mass)
        {
            maximumHealth = Mathf.Max(1, health);
            Health = maximumHealth;
            strikeDamage = Mathf.Max(0, damage);
            reach = Mathf.Max(.5f, strikeReach);
            attackCycle = cycleSeconds;
            windup = windupSeconds;
            cycle = new TreeAttackCycle(attackCycle, windup, strikeWindow);
            if (mass > 0) body.mass = mass;
        }

        /// <summary>Stands up where the tree stood. <paramref name="position"/> is on the NavMesh (or the ground).</summary>
        public void Rise(AwakenableTree origin, Vector3 position)
        {
            Origin = origin;
            body.position = position;
            transform.position = position;
            navigator.enabled = true;
            if (navigator.isOnNavMesh) navigator.Warp(position);
            awakenedAt = Time.time;
            Enter(TreeAllyState.Awakening);
            if (world)
            {
                world.Spawn(awakenVfx, position + Vector3.up * .3f, Quaternion.identity, leafColour, 1.2f);
                world.Play(awakenCue, position);
                if (world.feel && Vector3.Distance(world.motor.transform.position, position) < 14) world.feel.AddTrauma(.22f);
            }
            KickDust(stepDustCount * 5, true);
            if (overhead) overhead.ShowHealth(Health, maximumHealth);
        }

        // ---------------- IMoveSpeedScalable ----------------

        public float MoveSpeedScale => moveSpeedScale.Current(Time.time, 1);
        public void ApplyMoveSpeedScale(float scale, float seconds) => moveSpeedScale.Apply(Mathf.Max(0, scale), seconds, Time.time);

        // ---------------- decisions ----------------

        bool Running => world && world.Running;
        Vector3 PlayerPosition => world.motor.transform.position;

        void Enter(TreeAllyState next)
        {
            State = next;
            stateTime = 0;
        }

        void Update()
        {
            if (!Running || !Alive) return;
            float now = Time.time;
            stateTime += Time.deltaTime;
            if (transform.position.y < world.settings.killHeight) { Kill(); return; }

            if (State == TreeAllyState.Awakening)
            {
                Halt();
                if (stateTime >= awakenSeconds) Enter(TreeAllyState.Follow);
                return;
            }

            if (now >= nextThink)
            {
                nextThink = now + .2f;
                TrackCreatures(now);
                ChooseFoe();
                DrawAttacks(now);
            }

            var phase = cycle.Advance(now);
            if (phase == TreeAttackPhase.Windup || phase == TreeAttackPhase.Strike)
            {
                ThinkAttack(now, phase);
                return;
            }
            if (State == TreeAllyState.Attack) Enter(foe ? TreeAllyState.Engage : TreeAllyState.Follow);

            if (foe && foe.Alive) ThinkEngage(now);
            else ThinkFollow();
        }

        void ThinkAttack(float now, TreeAttackPhase phase)
        {
            if (State != TreeAllyState.Attack) Enter(TreeAllyState.Attack);
            Halt();
            if (phase == TreeAttackPhase.Windup)
            {
                // Turns to the target during the wind-up; the direction locks when the branch comes down.
                if (Combat.IsAlive(strikeTarget)) Face(strikeTarget.Position - Position);
                swung = false;
                return;
            }
            BeginSwing();
        }

        /// <summary>The branch comes down: the direction locks here (a target that stepped aside is missed).</summary>
        void BeginSwing()
        {
            if (swung) return;
            swung = true;
            strikeDirection = Flat(transform.forward).normalized;
            facing = Quaternion.LookRotation(strikeDirection);
            world.Play(swingCue, Position);
        }

        void ThinkEngage(float now)
        {
            if (State != TreeAllyState.Engage) Enter(TreeAllyState.Engage);
            Vector3 to = Flat(foe.Position - Position);
            float distance = to.magnitude;
            if (cycle.Ready(now) && distance <= reach && CanSee(foe))
            {
                strikeTarget = foe;
                swung = false;
                cycle.Begin(now);
                world.Play(windupCue, Position);
                Enter(TreeAllyState.Attack);
                Halt();
                Face(to);
                return;
            }
            // Recovering: may close in but does not strike yet.
            if (distance > reach * .8f) Go(foe.Position, PlayerFar ? catchUpSpeed : walkSpeed);
            else { Halt(); Face(to); }
        }

        bool PlayerFar => Flat(PlayerPosition - Position).magnitude > catchUpDistance;

        void ThinkFollow()
        {
            Vector3 player = PlayerPosition;
            float distance = Flat(player - Position).magnitude;
            if (distance <= followDistance)
            {
                Halt();
                if (State != TreeAllyState.Follow) Enter(TreeAllyState.Follow);
                if (distance > .5f) Face(player - Position);
                return;
            }
            Go(player, distance > catchUpDistance ? catchUpSpeed : walkSpeed);
            // No walkable way on: it stands at the closest point it could reach until the player comes back.
            bool stuck = navigator.enabled && navigator.isOnNavMesh && !navigator.pathPending
                && (navigator.pathStatus == NavMeshPathStatus.PathPartial || navigator.pathStatus == NavMeshPathStatus.PathInvalid || !hasDestination)
                && navigator.remainingDistance < .6f;
            if (stuck) { Halt(); Face(player - Position); }
            var next = stuck ? TreeAllyState.Wait : TreeAllyState.Follow;
            if (State != next) Enter(next);
        }

        bool CanSee(WorldCreature creature)
        {
            Vector3 eye = Position + Vector3.up * 1.5f;
            Vector3 target = creature.Centre;
            Vector3 to = target - eye;
            float distance = to.magnitude;
            if (distance < .05f) return true;
            if (!Physics.Raycast(eye, to / distance, out var hit, distance, ~(1 << 2), QueryTriggerInteraction.Ignore)) return true;
            return hit.transform.IsChildOf(creature.transform) || hit.transform.IsChildOf(transform);
        }

        // ---------------- whom to fight ----------------

        static bool Attacking(CreatureState state) => state == CreatureState.Chase || state == CreatureState.Windup
            || state == CreatureState.Strike || state == CreatureState.Recover || state == CreatureState.Reposition
            || state == CreatureState.Search;

        static bool MidAttack(CreatureState state) => state == CreatureState.Windup || state == CreatureState.Strike;

        /// <summary>Notices which creatures lost health: the player or the squad is fighting them.</summary>
        void TrackCreatures(float now)
        {
            var creatures = world.creatures;
            if (creatures == null) return;
            foreach (var creature in creatures)
            {
                if (!creature) continue;
                int health = creature.Health;
                if (lastHealth.TryGetValue(creature, out int before) && health < before) hurtAt[creature] = now;
                lastHealth[creature] = health;
            }
        }

        void ChooseFoe()
        {
            candidates.Clear();
            candidateCreatures.Clear();
            var creatures = world.creatures;
            if (creatures == null) { foe = null; return; }
            Vector3 player = PlayerPosition, self = Position;
            float now = Time.time;
            foreach (var creature in creatures)
            {
                if (!creature || !creature.isActiveAndEnabled || !creature.Alive) continue;
                var target = creature.Target;
                bool attacking = Attacking(creature.State) && Combat.IsAlive(target) && target.Faction == Faction.Player;
                candidates.Add(new FoeCandidate
                {
                    DistanceToAlly = Flat(creature.Position - self).magnitude,
                    DistanceToPlayer = Flat(creature.Position - player).magnitude,
                    AttacksSquad = attacking,
                    AttacksThisAlly = attacking && ReferenceEquals(target, this),
                    RecentlyHurt = hurtAt.TryGetValue(creature, out float hurt) && now - hurt < hurtMemorySeconds,
                    Current = creature == foe,
                });
                candidateCreatures.Add(creature);
            }
            int index = TreeAllyTargeting.Choose(candidates, targeting, Flat(player - self).magnitude);
            foe = index >= 0 ? candidateCreatures[index] : null;
        }

        /// <summary>Attacking creatures nearby turn on the tree when it is clearly closer than their target.</summary>
        void DrawAttacks(float now)
        {
            Vector3 player = PlayerPosition, self = Position;
            for (int i = 0; i < candidateCreatures.Count; i++)
            {
                var creature = candidateCreatures[i];
                if (!Attacking(creature.State)) continue;
                var target = creature.Target;
                if (!Combat.IsAlive(target) || target.Faction != Faction.Player) continue;
                bool targetsThis = ReferenceEquals(target, this);
                Vector3 at = creature.Position;
                bool hitLately = hitAt.TryGetValue(creature, out float hit) && now - hit < hitMemorySeconds;
                var decision = TreeAllyTargeting.Taunt(targetsThis, MidAttack(creature.State), hitLately,
                    Flat(self - at).magnitude, Flat(target.Position - at).magnitude, Flat(player - at).magnitude,
                    drawRange, drawMargin, releaseMargin);
                if (decision == TauntDecision.Draw) creature.SetTarget(this);
                else if (decision == TauntDecision.Release) creature.SetTarget(null);
            }
        }

        // ---------------- movement ----------------

        void Go(Vector3 point, float speed)
        {
            if (!navigator.enabled || !navigator.isOnNavMesh)
            {
                desired = Flat(point - Position).normalized * speed;
                Face(desired);
                return;
            }
            navigator.speed = speed;
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + .3f;
                // Only walkable points: a player in the air or on an unwalkable ledge leaves the last destination as is.
                if (NavMesh.SamplePosition(point, out var hit, playerNavSearch, navigator.areaMask) && navigator.SetDestination(hit.position))
                {
                    destination = hit.position;
                    hasDestination = true;
                }
                else if (hasDestination) navigator.SetDestination(destination);
            }
            Vector3 steer = navigator.desiredVelocity;
            steer.y = 0;
            desired = steer.sqrMagnitude > .01f ? steer.normalized * speed : Vector3.zero;
            if (desired.sqrMagnitude > .01f) Face(desired);
        }

        void Halt() => desired = Vector3.zero;

        void Face(Vector3 direction)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > .01f) facing = Quaternion.LookRotation(direction);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void FixedUpdate()
        {
            if (!Running || dying) return;
            if (navigator.enabled && navigator.isOnNavMesh) navigator.nextPosition = body.position;
            float dt = Time.fixedDeltaTime;
            Vector3 velocity = body.linearVelocity;
            Vector3 planar = new Vector3(velocity.x, 0, velocity.z);
            planar = Vector3.MoveTowards(planar, desired * MoveSpeedScale, acceleration * dt);
            body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, facing, turnRate * dt));
            if (cycle.Advance(Time.time) == TreeAttackPhase.Strike) CheckStrikeContact();
        }

        void CheckStrikeContact()
        {
            BeginSwing();
            if (cycle.Landed || !Combat.IsAlive(strikeTarget)) return;
            Vector3 to = strikeTarget.Position - Position;
            Vector3 flat = Flat(to);
            if (flat.magnitude > reach || Mathf.Abs(to.y) > 2.2f) return;
            if (flat.sqrMagnitude > .01f && Vector3.Dot(strikeDirection, flat.normalized) < strikeArc) return;
            if (!cycle.TryLand(Time.time)) return;
            Vector3 point = strikeTarget.Centre;
            Vector3 push = strikeDirection * strikeImpulse + Vector3.up * strikeLift;
            if (strikeTarget.TakeDamage(new DamageInfo(strikeDamage, push, point, Position, DamageKind.Hit, this)))
            {
                if (strikeTarget is WorldCreature creature) hitAt[creature] = Time.time;
                world.Spawn(strikeVfx, point, Quaternion.LookRotation(strikeDirection), leafColour, 1);
                world.Play(hitCue, point);
                if (world.feel && Vector3.Distance(world.motor.transform.position, point) < 10) world.feel.AddTrauma(.12f);
            }
        }

        // ---------------- reactions ----------------

        /// <summary>
        /// A hit from a creature, burning tar, a chain of lightning. Damage stays (no healing); impulses push the
        /// body by its mass. A second awakening card on a live tree does nothing (the card handles that).
        /// </summary>
        public bool TakeDamage(in DamageInfo damage)
        {
            if (!Alive || !Running) return false;
            int amount = Mathf.Max(0, damage.Amount);
            Health = Mathf.Max(0, Health - amount);
            if (amount > 0)
            {
                hurtUntil = Time.time + hurtSeconds;
                world.Play(hurtCue, Position, 1, Random.Range(.8f, .95f));
                if (overhead) overhead.ShowHealth(Health, maximumHealth);
                KickDust(stepDustCount, true);
            }
            if (damage.Impulse.sqrMagnitude > 0) CardPhysics.Push(body, damage.Impulse);
            // Struck by a creature while idle: it becomes the foe at once.
            if (Health > 0 && damage.Instigator is WorldCreature attacker && attacker.Alive && !foe) foe = attacker;
            if (Health == 0) StartCoroutine(Die());
            return true;
        }

        void Kill()
        {
            if (!Alive) return;
            Health = 0;
            StartCoroutine(Die());
        }

        IEnumerator Die()
        {
            dying = true;
            cycle.Reset();
            foe = null;
            strikeTarget = null;
            Enter(TreeAllyState.Dying);
            if (overhead) overhead.Clear();
            if (navigator.enabled) navigator.enabled = false;
            body.isKinematic = true;
            // The fallen trunk no longer stops cards, creatures or the player.
            body.detectCollisions = false;
            world.Play(deathCue, Position);
            world.Spawn(deathVfx, Position + Vector3.up * 1.2f, Quaternion.identity, leafColour, 1.3f);
            if (world.feel && Vector3.Distance(world.motor.transform.position, Position) < 14) world.feel.AddTrauma(.2f);
            world.Announce("Оживлённое дерево погибло", 2.2f);
            float topple = .9f;
            float time = 0;
            bool landed = false;
            while (time < topple + corpseSeconds + .6f)
            {
                time += Time.deltaTime;
                deathProgress = Mathf.Clamp01(time / topple);
                fade = Mathf.Clamp01((time - topple - corpseSeconds) / .6f);
                if (!landed && time > topple * .45f) { landed = true; KickDust(stepDustCount * 4, true); }
                yield return null;
            }
            // Stays in the outing's objects (destroyed when it ends); no trophy, cannot be awakened again.
            gameObject.SetActive(false);
        }

        // ---------------- presentation ----------------

        void LateUpdate()
        {
            if (!look) return;
            float dt = Time.deltaTime;
            float now = Time.time;
            Vector3 velocity = body ? body.linearVelocity : Vector3.zero;
            float speed = dying ? 0 : new Vector2(velocity.x, velocity.z).magnitude;
            float previous = gaitPhase;
            gaitPhase += dt * gaitFrequency * Mathf.Clamp(speed / Mathf.Max(.5f, walkSpeed), .4f, 1.8f) * Mathf.PI * 2 * (speed > .3f ? 1 : 0);
            if (Mathf.FloorToInt(previous / Mathf.PI) != Mathf.FloorToInt(gaitPhase / Mathf.PI) && world)
            {
                if (speed > .8f && Vector3.Distance(world.motor.transform.position, Position) < 20) world.Play(stepCue, Position, .45f, .75f);
                if (speed > .5f) KickDust(stepDustCount, false);
            }
            var state = new TreeAllyLookState
            {
                Speed = speed,
                GaitPhase = gaitPhase,
                Heading = transform.forward,
                Hurt = hurtSeconds > 0 ? Mathf.Clamp01((hurtUntil - now) / hurtSeconds) : 0,
            };
            if (dying) { state.Pose = TreeAllyPose.Dying; state.Progress = deathProgress; state.Fade = fade; }
            else if (State == TreeAllyState.Awakening) { state.Pose = TreeAllyPose.Awakening; state.Progress = awakenSeconds > 0 ? Mathf.Clamp01((now - awakenedAt) / awakenSeconds) : 1; }
            else
            {
                switch (cycle.Phase)
                {
                    case TreeAttackPhase.Windup: state.Pose = TreeAllyPose.Windup; state.Progress = cycle.WindupProgress(now); break;
                    case TreeAttackPhase.Strike: state.Pose = TreeAllyPose.Strike; state.Progress = cycle.StrikeProgress(now); break;
                    case TreeAttackPhase.Recover: state.Pose = TreeAllyPose.Recover; state.Progress = cycle.RecoverProgress(now); break;
                    default: state.Pose = speed > .3f ? TreeAllyPose.Walk : TreeAllyPose.Idle; break;
                }
            }
            look.Present(state, dt);
        }

        void KickDust(int count, bool always)
        {
            if (!stepDust || count <= 0 || !world) return;
            if (!always && Vector3.Distance(world.motor.transform.position, Position) > 30) return;
            stepDust.Emit(new ParticleSystem.EmitParams { startColor = dustColour }, count);
        }
    }
}
