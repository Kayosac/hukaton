using System.Collections;
using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Feel;
using Hakaton.Loot;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// Timid flees, Territorial guards its home, Aggressive attacks on sight, Peaceful minds its own business
    /// and only fights back when hurt, calming down once nobody has hit it for a while.
    /// </summary>
    public enum Temperament { Timid, Territorial, Aggressive, Peaceful }
    public enum CreatureState { Idle, Wander, Graze, Alert, Flee, Threat, Chase, Search, Return, Windup, Strike, Recover, Reposition, Frozen, Stunned, Dying, Investigate, Aim, Shoot }

    /// <summary>
    /// A creature of the valley. Decisions run in Update (per temperament), locomotion in FixedUpdate on the
    /// Rigidbody (so cards and rocks can really shove it), and in LateUpdate the state picks a hand-drawn pose
    /// (<see cref="CreatureSpriteView"/>) that is squashed, hopped and tinted procedurally.
    /// Every attack is telegraphed: the aggression drawing, a crouch, sound and a red "!!" before the direction locks.
    /// </summary>
    public sealed partial class WorldCreature : MonoBehaviour, ICombatant
    {
        [Header("Identity")]
        public ExpeditionWorld world;
        public Temperament temperament;
        public string displayName;
        [Header("Authored parts")]
        public Rigidbody body;
        public NavMeshAgent navigator;
        public CreatureSpriteView view;
        public CreatureOverhead overhead;
        [Header("Size")]
        [Tooltip("Size relative to the art (the atlas is drawn at the manifest's physical height). Scales the drawing, the body capsule, the navigation agent and the marks above the head; the root stays at scale one. The capsule and agent are authored at size one and scaled when the game runs.")]
        [Min(.05f)] public float size = 1;
        [Tooltip("Every spawn rolls the size between these multiples of the default size above.")]
        public Vector2 spawnSizeRange = new Vector2(.9f, 1.2f);
        float spawnSize = 1;
        [Tooltip("Height of the stun stars above the top of the drawing and their size.")]
        public float stunStarsHeight = .35f, stunStarsScale = 1.2f;
        VfxBurst stunStars;
        [Tooltip("What drops on death. Each row is rolled on its own; the drops pop out of the body.")]
        public LootTable drops = new LootTable();
        [Tooltip("Optional authored alternatives for this creature's home territory.")]
        public Transform[] spawnPoints;
        [Header("Health and attack (board v0.1)")]
        public int maximumHealth = 8;
        public int attackHalfHearts = 2;
        public float windup = .7f, strikeSeconds = .45f, recovery = 1.1f;
        [Tooltip("Recovery after a missed strike. Negative uses the normal recovery.")]
        public float recoveryOnMiss = -1;
        [Tooltip("Strike is a dash (boar charge, wolf lunge) rather than a standing bite.")]
        public bool dash;
        [Tooltip("Camera shake and a burst of dust when the strike lands: a heavy ground slam. Zero for bites and lunges.")]
        [Min(0)] public float strikeShake;
        [Header("Senses")]
        public float sightRange = 15;
        [Tooltip("Within this distance the creature notices you even from behind.")]
        public float hearingRange = 4;
        public float leash = 23;
        [Header("Movement")]
        [Tooltip("Chase speed. The player walks at 4 and sprints at 6.5 m/s.")]
        public float speed = 3.5f;
        public float wanderSpeed = 1.3f, dashSpeed = 9.6f, territoryRadius = 6, turnRate = 360;
        [Header("Ranges")]
        [Tooltip("Distance at which the attack wind-up starts.")]
        public float attackRange = 2.4f;
        [Tooltip("Reach of the attack's contact check from the creature's snout.")]
        public float contactRange = 1.6f;
        [Header("Timid")]
        public float alertRange = 12, fleeRange = 7.5f, safeDistance = 15, dashSeconds = .55f, pauseSeconds = .55f;
        [Header("Territorial")]
        public float threatRange = 10, calmRange = 13, aggroRange = 6, provokeSeconds = 1.1f;
        [Header("Animation")]
        public float hopHeight = .08f, gaitFrequency = 2.2f;
        [Tooltip("How long the taking-damage drawing stays after a hit.")]
        [Min(0)] public float hurtSeconds = .35f;
        [Tooltip("The death drawing lies this long before it fades.")]
        [Min(0)] public float corpseSeconds = 1.4f;
        public Color hitTint = new Color(1f, .45f, .4f), frozenTint = new Color(.45f, .85f, 1f), burnTint = new Color(1f, .5f, .2f);
        [Tooltip("Main colour of the creature, for the death puff.")]
        public Color bodyColour = Color.white;
        [Tooltip("Dust kicked up from under the feet: a world-space particle system that only emits when the creature steps.")]
        public ParticleSystem stepDust;
        [Min(0)] public int stepDustCount = 2;
        public Color dustColour = new Color(.86f, .78f, .64f);
        [Header("Sounds")]
        public SfxCue alertCue;
        public SfxCue threatCue, windupCue, strikeCue, hurtCue, deathCue, stepCue;

        public int Health { get; private set; }
        public bool Alive => Health > 0 && !dying;
        public CreatureState State { get; private set; }

        Vector3 home, authoredHome, lastSeen, strikeDirection, desired, wanderTarget, fleeDirection;
        Quaternion homeRotation, authoredRotation, facing;
        float stateTime, nextSight, unseen, provokedUntil, frozenUntil, stunnedUntil, burnUntil, flashUntil, hurtUntil;
        float nextPath, nextThreatSound, provokeTimer, idleDuration, gaitPhase, crouch, nextDust;
        bool sees, hitThisStrike, dying;
        Vector3 faceTowards;
        SpringFloat squash;
        CreaturePose pose;
        // Extension points for cards (tmp/new-cards/API.md): slowdown, lure, attack target.
        TimedModifier moveSpeedScale;
        Vector3 noisePoint;
        float investigateUntil;
        ICombatant attackTarget;

        Vector3 PlayerPosition => world.motor.transform.position;
        Vector3 ToPlayer { get { var v = PlayerPosition - body.position; v.y = 0; return v; } }
        float PlayerDistance => ToPlayer.magnitude;
        // The hostile logic follows the attack target (the player unless an ally drew the attack);
        // fleeing, sounds and dust stay about the player.
        Vector3 TargetPosition => Target.Position;
        Vector3 ToTarget { get { var v = TargetPosition - body.position; v.y = 0; return v; } }
        float TargetDistance => ToTarget.magnitude;
        bool Provoked => Time.time < provokedUntil;
        /// <summary>Visible height of the drawing: eyes, marks and effects scale with the creature.</summary>
        float TopHeight => view ? view.height * Size : 1;
        /// <summary>The creature's size in the world: the size field times any scale of the root.</summary>
        public float Size => CurrentSize * transform.lossyScale.y;
        float CurrentSize => size * spawnSize;

        // The body as authored at size one, captured once so the size can change while playing.
        CapsuleCollider capsule;
        float capsuleRadius, capsuleHeight, agentRadius, agentHeight, appliedSize;
        Vector3 capsuleCentre, overheadPosition;
        bool authoredSizeCaptured;

        void ApplySize()
        {
            if (!authoredSizeCaptured)
            {
                authoredSizeCaptured = true;
                capsule = GetComponent<CapsuleCollider>();
                if (capsule) { capsuleRadius = capsule.radius; capsuleHeight = capsule.height; capsuleCentre = capsule.center; }
                if (navigator) { agentRadius = navigator.radius; agentHeight = navigator.height; }
                if (overhead) overheadPosition = overhead.transform.localPosition;
            }
            float s = appliedSize = CurrentSize;
            if (capsule) { capsule.radius = capsuleRadius * s; capsule.height = capsuleHeight * s; capsule.center = capsuleCentre * s; }
            if (navigator) { navigator.radius = agentRadius * s; navigator.height = agentHeight * s; }
            if (overhead) overhead.transform.localPosition = overheadPosition * s;
            if (view) view.Size = s;
        }

#if UNITY_EDITOR
        // In the editor the drawing shows the size at once; the capsule and agent keep their size-one values.
        void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (!this || !view) return;
                var drawing = new Vector3(size, size, 1);
                if (view.transform.localScale != drawing) view.transform.localScale = drawing;
            };
        }
#endif

        void Awake()
        {
            ApplySize();
            // Navigation only plans; the Rigidbody owns position and rotation.
            navigator.updatePosition = false;
            navigator.updateRotation = false;
            home = authoredHome = transform.position;
            homeRotation = authoredRotation = facing = transform.rotation;
            Health = maximumHealth;
            PrepareFlight(false);
        }

        public void ResetForOuting(System.Random random)
        {
            StopAllCoroutines();
            gameObject.SetActive(true);
            dying = false;
            Health = maximumHealth;
            var point = random != null && spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints[random.Next(spawnPoints.Length)] : null;
            home = SnapToGround(point ? point.position : authoredHome);
            homeRotation = facing = point ? point.rotation : authoredRotation;
            // A little different every time it appears: the outing's random keeps a seeded outing repeatable.
            float roll = random != null ? (float)random.NextDouble() : Random.value;
            spawnSize = Mathf.Lerp(Mathf.Min(spawnSizeRange.x, spawnSizeRange.y), Mathf.Max(spawnSizeRange.x, spawnSizeRange.y), roll);
            ApplySize();
            frozenUntil = stunnedUntil = burnUntil = flashUntil = hurtUntil = provokedUntil = 0;
            unseen = provokeTimer = 0;
            moveSpeedScale.Clear();
            attackTarget = null;
            investigateUntil = 0;
            ResetRanged();
            body.isKinematic = false;
            body.detectCollisions = true;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = home;
            body.rotation = homeRotation;
            transform.SetPositionAndRotation(home, homeRotation);
            navigator.enabled = true;
            if (navigator.isOnNavMesh) { navigator.Warp(home); navigator.ResetPath(); }
            PrepareFlight(true);
            squash.Snap(1);
            crouch = 0;
            if (stepDust) stepDust.Clear();
            if (overhead) overhead.Clear();
            Enter(CreatureState.Idle);
            pose = CreaturePose.Default;
            if (view) view.Present(pose, transform.forward, Vector3.zero, Vector2.one, 0);
            Tint(Color.white, 0);
        }

        /// <summary>
        /// Back after its part of the world was unloaded and loaded again in the same outing: same health, where it
        /// was left; its home stays where it first appeared.
        /// </summary>
        public void Resume(int health, Vector3 position)
        {
            ResetForOuting(null);
            Health = Mathf.Clamp(health, 1, maximumHealth);
            body.position = position;
            transform.position = position;
            if (navigator.isOnNavMesh) navigator.Warp(position);
        }

        // ---------------- decisions ----------------

        void Update()
        {
            if (!world || !world.Running || !Alive) return;
            float dt = Time.deltaTime;
            stateTime += dt;
            Perceive(dt);

            if (Time.time < frozenUntil) { if (State != CreatureState.Frozen) Enter(CreatureState.Frozen); Halt(); return; }
            if (State == CreatureState.Frozen) Recover(.35f);
            if (Time.time < stunnedUntil) { if (State != CreatureState.Stunned) Enter(CreatureState.Stunned); Halt(); return; }
            if (State == CreatureState.Stunned) Recover(.2f);

            switch (temperament)
            {
                case Temperament.Timid: ThinkTimid(); break;
                case Temperament.Territorial: ThinkTerritorial(); break;
                case Temperament.Peaceful: ThinkPeaceful(); break;
                default: ThinkAggressive(); break;
            }
            UpdateOverhead();
        }

        void Perceive(float dt)
        {
            if (Time.time >= nextSight)
            {
                nextSight = Time.time + .12f;
                sees = CanSeeTarget();
            }
            if (sees) { lastSeen = TargetPosition; unseen = 0; }
            else unseen += dt;
        }

        bool CanSeeTarget()
        {
            // Eyes at the creature's own height: a small one loses you behind a low rock.
            Vector3 eye = body.position + Vector3.up * Mathf.Max(.15f, TopHeight * .8f);
            var seen = Target;
            bool player = ReferenceEquals(seen, world.Player);
            Vector3 target = TargetPosition + Vector3.up * 1.2f;
            Vector3 to = target - eye;
            float distance = to.magnitude;
            if (distance > sightRange) return false;
            bool calm = IsCalm;
            bool loud = player && world.motor.IsSprinting && distance < hearingRange * 2;
            if (calm && distance > hearingRange && !loud && Vector3.Dot(transform.forward, to / distance) < -.25f) return false;
            // Card zones (fog) hide the target.
            if (world.IsVisionBlocked(eye, target)) return false;
            // The player rig is on Ignore Raycast: anything hit means the view is blocked. An ally may be hit itself.
            if (!Physics.Raycast(eye, to / distance, out var hit, distance - .3f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return true;
            return !player && hit.transform.IsChildOf(seen.Root);
        }

        void Enter(CreatureState next)
        {
            State = next;
            stateTime = 0;
            switch (next)
            {
                case CreatureState.Idle: idleDuration = Random.Range(1.2f, 2.8f); Halt(); break;
                case CreatureState.Graze: idleDuration = Random.Range(1.5f, 3.2f); Halt(); break;
                case CreatureState.Wander: wanderTarget = RandomPointNear(home, territoryRadius); break;
                case CreatureState.Investigate: squash.Kick(-2); break;
                case CreatureState.Alert: world.Play(alertCue, body.position); squash.Kick(-3); break;
                case CreatureState.Threat: nextThreatSound = 0; provokeTimer = 0; break;
                case CreatureState.Flee: ChooseFleeDirection(); world.Play(alertCue, body.position, 1, 1.15f); KickDust(stepDustCount * 2); break;
                case CreatureState.Windup:
                    world.Play(windupCue, body.position);
                    squash.Kick(-4);
                    break;
                case CreatureState.Aim:
                    world.Play(windupCue, body.position);
                    squash.Kick(-3);
                    break;
                case CreatureState.Shoot:
                    shotsFired = 0;
                    break;
                case CreatureState.Recover:
                    shoving = false;
                    break;
                case CreatureState.Strike:
                    Vector3 to = ToTarget;
                    strikeDirection = to.sqrMagnitude > .01f ? to.normalized : transform.forward;
                    facing = Quaternion.LookRotation(strikeDirection);
                    hitThisStrike = false;
                    world.Play(strikeCue, body.position);
                    squash.Kick(6);
                    if (dash) KickDust(stepDustCount * 3);
                    if (strikeShake > 0)
                    {
                        KickDust(stepDustCount * 4);
                        if (world.feel && PlayerDistance < 16) world.feel.AddTrauma(strikeShake);
                    }
                    break;
            }
        }

        void ThinkIdle(bool noticed)
        {
            switch (State)
            {
                case CreatureState.Idle:
                    if (stateTime > idleDuration) Enter(Random.value < .55f ? CreatureState.Wander : CreatureState.Graze);
                    break;
                case CreatureState.Graze:
                    if (stateTime > idleDuration) Enter(CreatureState.Idle);
                    break;
                case CreatureState.Wander:
                    Go(wanderTarget, wanderSpeed);
                    if (Flat(body.position - wanderTarget).magnitude < .8f || stateTime > 7) Enter(CreatureState.Idle);
                    break;
                case CreatureState.Investigate:
                    // Lure: walk over to the noise, then stand and look until it stops.
                    if (Flat(body.position - noisePoint).magnitude > 1.2f) Go(noisePoint, wanderSpeed * 1.6f);
                    else { Halt(); Face(noisePoint - body.position); }
                    if (Time.time >= investigateUntil) Enter(CreatureState.Idle);
                    break;
            }
        }

        bool IsCalm => State == CreatureState.Idle || State == CreatureState.Wander || State == CreatureState.Graze
            || State == CreatureState.Investigate;

        void ThinkTimid()
        {
            float distance = PlayerDistance;
            if (IsCalm)
            {
                if (Provoked) { Enter(CreatureState.Flee); return; }
                if (sees && distance < alertRange) { Enter(CreatureState.Alert); return; }
                ThinkIdle(false);
                return;
            }
            switch (State)
            {
                case CreatureState.Alert:
                    Halt();
                    Face(ToPlayer);
                    if (Provoked || (sees && distance < fleeRange) || world.motor.IsSprinting && distance < fleeRange * 1.4f) Enter(CreatureState.Flee);
                    else if ((!sees || distance > alertRange + 3) && stateTime > 2) Enter(CreatureState.Idle);
                    break;
                case CreatureState.Flee:
                    float cycle = dashSeconds + pauseSeconds;
                    float phase = stateTime % cycle;
                    if (phase < dashSeconds)
                    {
                        if (phase < Time.deltaTime * 1.5f && stateTime > .1f) { ChooseFleeDirection(); KickDust(stepDustCount * 2); }
                        desired = fleeDirection * dashSpeed;
                        Face(fleeDirection);
                    }
                    else
                    {
                        // Freeze for a beat: the window the hunter aims for.
                        Halt();
                        if (distance > safeDistance && !Provoked) { Enter(CreatureState.Alert); return; }
                    }
                    if (stateTime > 9) Enter(CreatureState.Alert);
                    break;
                default:
                    Enter(CreatureState.Idle);
                    break;
            }
        }

        void ThinkTerritorial()
        {
            float distance = TargetDistance;
            if (IsCalm)
            {
                if (Provoked) { Enter(CreatureState.Chase); return; }
                if (sees && distance < threatRange && Flat(TargetPosition - home).magnitude < leash) { Enter(CreatureState.Threat); return; }
                ThinkIdle(false);
                return;
            }
            switch (State)
            {
                case CreatureState.Threat:
                    Halt();
                    Face(ToTarget);
                    if (Time.time >= nextThreatSound) { nextThreatSound = Time.time + 1.3f; world.Play(threatCue, body.position); squash.Kick(-2.5f); }
                    if (distance < aggroRange) provokeTimer += Time.deltaTime;
                    else provokeTimer = Mathf.Max(0, provokeTimer - Time.deltaTime);
                    if (Provoked || provokeTimer > provokeSeconds) Enter(CreatureState.Chase);
                    else if (distance > calmRange || (!sees && stateTime > 2)) Enter(CreatureState.Idle);
                    break;
                default:
                    ThinkHostile();
                    break;
            }
        }

        void ThinkAggressive()
        {
            if (IsCalm)
            {
                if (Provoked || (sees && TargetDistance < sightRange)) { Aggro(); return; }
                ThinkIdle(false);
                return;
            }
            ThinkHostile();
        }

        /// <summary>Ignores the player until hurt (or frozen, knocked down), then hunts like an aggressive one.</summary>
        void ThinkPeaceful()
        {
            if (IsCalm)
            {
                if (Provoked) { Aggro(); return; }
                ThinkIdle(false);
                return;
            }
            ThinkHostile();
        }

        void Aggro()
        {
            world.Play(threatCue, body.position);
            squash.Kick(-3);
            Enter(CreatureState.Chase);
        }

        /// <summary>Chase → wind-up → strike → recover, with search and return when the trail goes cold.</summary>
        void ThinkHostile()
        {
            float distance = TargetDistance;
            float fromHome = Flat(body.position - home).magnitude;
            if (HasRangedAttack && ThinkRanged()) return;
            switch (State)
            {
                case CreatureState.Chase:
                    if (fromHome > leash && !Provoked) { Enter(CreatureState.Return); break; }
                    // Peaceful: the grudge lasts as long as the provocation; then it goes home.
                    if (temperament == Temperament.Peaceful && !Provoked) { Enter(CreatureState.Return); break; }
                    if (!sees) { Enter(CreatureState.Search); break; }
                    if (HasRangedAttack) { ChaseWithRanged(distance); break; }
                    Go(TargetPosition, speed);
                    if (distance <= attackRange) Enter(CreatureState.Windup);
                    break;
                case CreatureState.Search:
                    Go(lastSeen, speed * .8f);
                    if (sees) Enter(CreatureState.Chase);
                    else if (Flat(body.position - lastSeen).magnitude < 1.2f || stateTime > 4) Enter(CreatureState.Return);
                    break;
                case CreatureState.Return:
                    Go(home, wanderSpeed * 1.6f);
                    if (sees && fromHome < leash * .7f && (temperament == Temperament.Aggressive || Provoked)) Enter(CreatureState.Chase);
                    else if (fromHome < 1.2f) Enter(CreatureState.Idle);
                    break;
                case CreatureState.Windup:
                    Halt();
                    Face(ToTarget);
                    if (stateTime >= windup) Enter(CreatureState.Strike);
                    break;
                case CreatureState.Strike:
                    desired = dash ? strikeDirection * dashSpeed : Vector3.zero;
                    if (dash && world.feel && distance < 9) world.feel.Sustain(.1f);
                    if (stateTime >= strikeSeconds)
                    {
                        Enter(CreatureState.Recover);
                        idleDuration = !hitThisStrike && recoveryOnMiss >= 0 ? recoveryOnMiss : recovery;
                    }
                    break;
                case CreatureState.Recover:
                    Halt();
                    if (stateTime < idleDuration) break;
                    // A charger never re-charges from point blank: it trots off to its run-up distance first.
                    if (dash && sees && distance < attackRange * .55f) Enter(CreatureState.Reposition);
                    else Enter(sees ? CreatureState.Chase : CreatureState.Search);
                    break;
                case CreatureState.Reposition:
                    Vector3 away = -ToTarget.normalized;
                    desired = away * speed;
                    Face(away);
                    if (distance >= attackRange * .85f || stateTime > 1.8f) Enter(CreatureState.Windup);
                    break;
                default:
                    Enter(CreatureState.Chase);
                    break;
            }
        }

        void Recover(float pause)
        {
            bool hostile = temperament != Temperament.Timid && (Provoked || State == CreatureState.Frozen);
            if (temperament == Temperament.Timid) Enter(CreatureState.Flee);
            else if (hostile || temperament == Temperament.Aggressive) { Enter(CreatureState.Recover); idleDuration = pause; }
            else Enter(CreatureState.Idle);
        }

        void UpdateOverhead()
        {
            if (!overhead) return;
            // Noticing is drawn into the fear and aggression poses; the mark above only warns of the attack itself.
            overhead.SetAlert(State == CreatureState.Windup || State == CreatureState.Strike || State == CreatureState.Aim || State == CreatureState.Shoot ? 2 : 0);
        }

        // ---------------- movement ----------------

        void Go(Vector3 destination, float moveSpeed)
        {
            if (flying) { Fly(destination, moveSpeed); return; }
            if (!navigator.enabled || !navigator.isOnNavMesh) { desired = Flat(destination - body.position).normalized * moveSpeed; return; }
            navigator.speed = moveSpeed;
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + .25f;
                if (NavMesh.SamplePosition(destination, out var hit, 4, NavMesh.AllAreas)) navigator.SetDestination(hit.position);
            }
            Vector3 steer = navigator.desiredVelocity;
            steer.y = 0;
            desired = steer.sqrMagnitude > .01f ? steer.normalized * moveSpeed : Vector3.zero;
            if (desired.sqrMagnitude > .01f) Face(desired);
        }

        void Halt() => desired = Vector3.zero;

        void Face(Vector3 direction)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > .01f) facing = Quaternion.LookRotation(direction);
        }

        void ChooseFleeDirection()
        {
            Vector3 away = -ToPlayer;
            away = away.sqrMagnitude > .01f ? away.normalized : transform.forward;
            float best = float.MinValue;
            Vector3 chosen = away;
            for (int i = 0; i < 8; i++)
            {
                Vector3 candidate = Quaternion.Euler(0, Random.Range(-70f, 70f), 0) * away;
                Vector3 end = body.position + candidate * dashSpeed * dashSeconds;
                float score = Vector3.Dot(candidate, away) * 2;
                if (navigator.isOnNavMesh && NavMesh.Raycast(body.position, end, out var hit, NavMesh.AllAreas))
                    score -= (1 - hit.distance / (dashSpeed * dashSeconds)) * 4;
                // Stay in its own area: past the leash, prefer heading home.
                if (Flat(end - home).magnitude > leash) score -= 3;
                if (score > best) { best = score; chosen = candidate; }
            }
            fleeDirection = chosen;
        }

        Vector3 RandomPointNear(Vector3 centre, float radius)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                if (NavMesh.SamplePosition(centre + new Vector3(offset.x, 0, offset.y), out var hit, 2, NavMesh.AllAreas)) return hit.position;
            }
            return centre;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        static readonly RaycastHit[] groundHits = new RaycastHit[8];

        /// <summary>Stands an authored point on the ground below or above it: a small body started inside the terrain falls through.</summary>
        Vector3 SnapToGround(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 3, Vector3.down, groundHits, 8, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody == body || hit.rigidbody && !hit.rigidbody.isKinematic) continue;
                if (hit.distance < best) { best = hit.distance; point.y = hit.point.y; }
            }
            return point;
        }

        void FixedUpdate()
        {
            if (!world || !world.Running || dying) return;
            if (navigator.enabled && navigator.isOnNavMesh) navigator.nextPosition = body.position;
            float dt = Time.fixedDeltaTime;
            bool controlled = State == CreatureState.Frozen || State == CreatureState.Stunned;
            Vector3 velocity = body.linearVelocity;
            Vector3 planar = new Vector3(velocity.x, 0, velocity.z);
            // While frozen or knocked about, physics keeps the momentum: cards and rocks still push it around.
            float acceleration = controlled ? 3 : State == CreatureState.Strike && dash ? 90 : State == CreatureState.Flee ? 45 : 18;
            Vector3 target = controlled ? Vector3.zero : desired * MoveSpeedScale;
            planar = Vector3.MoveTowards(planar, target, acceleration * dt);
            body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
            if (!controlled) body.MoveRotation(Quaternion.RotateTowards(body.rotation, facing, turnRate * dt * (State == CreatureState.Windup ? 1.6f : 1)));
            if (flying) FlyVertical(dt, controlled);
            if (State == CreatureState.Strike) CheckStrikeContact();
        }

        void CheckStrikeContact()
        {
            if (hitThisStrike) return;
            float size = Size;
            // A flyer dives in with its whole body: the reach is measured from the middle of the drawing.
            Vector3 snout = flying ? Centre + transform.forward * Mathf.Max(.3f, TopHeight * .5f) : body.position + (transform.forward * .65f + Vector3.up * .6f) * size;
            var victim = Target;
            Vector3 aim = victim.Position + Vector3.up * .9f;
            Vector3 to = aim - snout;
            if (to.magnitude > contactRange || Vector3.Dot(strikeDirection, Flat(to).normalized) < .1f) return;
            hitThisStrike = true;
            Vector3 knockback = (strikeDirection * (dash ? 7 : 4) + Vector3.up * 2.5f);
            victim.TakeDamage(new DamageInfo(StrikeDamage, knockback, aim, body.position, DamageKind.Hit, this));
        }

        // ---------------- reactions ----------------

        public void Damage(int amount, Vector3 impulse, Vector3 point, float burnSeconds)
        {
            if (!Alive) return;
            Health = Mathf.Max(0, Health - amount);
            if (amount > 0)
            {
                flashUntil = Time.time + .1f;
                hurtUntil = Time.time + hurtSeconds;
                squash.Kick(-6);
                world.Play(hurtCue, body.position, 1, Random.Range(.95f, 1.15f));
                if (overhead) overhead.ShowHealth(Health, maximumHealth);
            }
            if (impulse.sqrMagnitude > 0) CardPhysics.Push(body, impulse);
            if (burnSeconds > 0) burnUntil = Time.time + burnSeconds;
            provokedUntil = Time.time + 8;
            lastSeen = PlayerPosition;
            if (Health == 0) StartCoroutine(Die(impulse));
            else if (IsCalm || State == CreatureState.Alert || State == CreatureState.Threat)
                Enter(temperament == Temperament.Timid ? CreatureState.Flee : CreatureState.Chase);
        }

        /// <summary>Knocked off its feet: interrupts any attack (board: «сбивание с ног прерывает атаку»).</summary>
        public void Stagger(float seconds, Vector3 from)
        {
            if (!Alive) return;
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
            provokedUntil = Time.time + 8;
            squash.Kick(-5);
            // Stars circle the head for as long as the creature stays knocked out.
            if (!stunStars) stunStars = world.Spawn(world.vfx ? world.vfx.stunned : null, transform.position, Quaternion.identity, Color.white, stunStarsScale);
            if (stunStars)
            {
                stunStars.Follow(transform, Vector3.up * (TopHeight + stunStarsHeight));
                stunStars.HoldFor(stunnedUntil - Time.time);
            }
        }

        /// <summary>Full freeze: interrupts the attack; physics still applies.</summary>
        public void Freeze(float seconds)
        {
            if (!Alive) return;
            frozenUntil = Time.time + seconds;
            provokedUntil = Time.time + 8 + seconds;
            Enter(CreatureState.Frozen);
        }

        // ---------------- card hooks (tmp/new-cards/API.md) ----------------

        /// <summary>Movement multiplier from cards (tar ×0.5); 1 normally. Several sources do not multiply.</summary>
        public float MoveSpeedScale => moveSpeedScale.Current(Time.time, 1);

        /// <summary>Slows (or speeds) the creature for a while. Refresh it every tick from a zone it stands in.</summary>
        public void ApplyMoveSpeedScale(float scale, float seconds) => moveSpeedScale.Apply(Mathf.Max(0, scale), seconds, Time.time);

        /// <summary>Received-impulse multiplier on this body (Lightness ×1.5); 1 normally.</summary>
        public float ImpulseScale => BodyModifierStatus.ImpulseScaleOf(body);

        /// <summary>Lightness on the creature: impulses ×multiplier and falling limited to fallSpeedLimit m/s.</summary>
        public void ApplyLightness(float multiplier, float fallSpeedLimit, float seconds)
        {
            var status = BodyModifierStatus.On(body, world);
            status.ApplyImpulseScale(multiplier, seconds);
            status.ApplyFallSpeedLimit(fallSpeedLimit, seconds);
        }

        /// <summary>The point lies in the creature's territory: within <see cref="leash"/> of its home.</summary>
        public bool InTerritory(Vector3 point) => Flat(point - home).magnitude <= leash;

        /// <summary>
        /// Lure: a calm creature (idle, wandering, grazing) walks over to a noise at <paramref name="point"/> and stays
        /// for <paramref name="seconds"/>. Ignored when it is alert, fleeing or attacking, or the point is outside its
        /// territory. Seeing the player still interrupts it as usual. Returns true when it goes.
        /// </summary>
        public bool Investigate(Vector3 point, float seconds)
        {
            if (!world || !world.Running || !Alive || !IsCalm || seconds <= 0 || !InTerritory(point)) return false;
            noisePoint = point;
            investigateUntil = Time.time + seconds;
            if (State != CreatureState.Investigate) Enter(CreatureState.Investigate);
            return true;
        }

        public bool Investigating => State == CreatureState.Investigate;

        /// <summary>Who the creature fights: the player, unless <see cref="SetTarget"/> chose another live fighter.</summary>
        public ICombatant Target => Combat.IsAlive(attackTarget) ? attackTarget : world.Player;

        /// <summary>Makes the creature fight this fighter (an ally); null goes back to the player.</summary>
        public void SetTarget(ICombatant fighter) => attackTarget = ReferenceEquals(fighter, this) ? null : fighter;

        // ICombatant
        public Faction Faction => Faction.Wild;
        public Vector3 Position => body ? body.position : transform.position;
        public Vector3 Centre => Position + Vector3.up * TopHeight * .5f;
        public Transform Root => transform;

        /// <summary>
        /// A hit from a card effect or a fighter. A hit from a fighter of the other side that is not the player
        /// (an ally) turns the creature against it.
        /// </summary>
        public bool TakeDamage(in DamageInfo damage)
        {
            if (!Alive) return false;
            var by = damage.Instigator;
            bool fromAlly = Combat.IsAlive(by) && !ReferenceEquals(by, world.Player) && by.Faction != Faction;
            Damage(damage.Amount, damage.Impulse, damage.Point, damage.Kind == DamageKind.Fire ? 1.6f : 0);
            if (fromAlly && Alive) { SetTarget(by); lastSeen = by.Position; }
            return true;
        }

        IEnumerator Die(Vector3 impulse)
        {
            if (stunStars) stunStars.Release(.2f);
            dying = true;
            State = CreatureState.Dying;
            if (overhead) overhead.Clear();
            navigator.enabled = false;
            world.Play(deathCue, body.position);
            if (world.time) world.time.HitStop(.09f);
            body.isKinematic = true;
            // The body no longer stops cards or the player.
            body.detectCollisions = false;
            Vector3 start = body.position;
            Vector3 drift = Flat(impulse).normalized * Mathf.Min(.8f, TopHeight * .5f);
            Vector3 heading = transform.forward;
            float hop = Mathf.Min(.45f, TopHeight * .4f);
            // A flyer drops out of the air first.
            float fall = FallHeight(start);
            float fallSeconds = Mathf.Max(.3f, Mathf.Sqrt(2 * fall / 14));
            // Knocked over: a short hop already in the death drawing, then a landing squash.
            float time = 0;
            while (time < fallSeconds)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / fallSeconds);
                transform.position = start + drift * k + Vector3.down * fall * k * k;
                if (view) view.Present(CreaturePose.Death, heading, Vector3.up * Mathf.Sin(k * Mathf.PI) * hop, new Vector2(.92f, 1.08f), 0);
                Tint(hitTint, 1 - k);
                yield return null;
            }
            KickDust(stepDustCount * 3);
            world.Spawn(world.vfx ? world.vfx.creatureDeath : null, transform.position + Vector3.up * TopHeight * .4f, Quaternion.identity, bodyColour, Mathf.Clamp(TopHeight, .5f, 1.2f));
            if (world.feel) world.feel.AddTrauma(.18f);
            DropLoot();
            // The body lies in its death drawing for a moment, then fades away.
            time = 0;
            while (time < corpseSeconds + .45f)
            {
                time += Time.deltaTime;
                float settle = Ease.OutBack(Mathf.Clamp01(time / .25f));
                if (view) view.Present(CreaturePose.Death, heading, Vector3.zero, Vector2.LerpUnclamped(new Vector2(1.18f, .78f), Vector2.one, settle), 0);
                float fade = Mathf.Clamp01((time - corpseSeconds) / .45f);
                if (view) view.Tint(new Color(1, 1, 1, 1 - fade));
                yield return null;
            }
            gameObject.SetActive(false);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!world || !world.Running || !Alive) return;
            // Bumping into the player is not a crash; only the attack hurts, and only the player.
            if (collision.collider == world.motor.characterController || collision.collider.GetComponentInParent<CardProjectile>()) return;
            float normalSpeed = 0;
            for (int i = 0; i < collision.contactCount; i++)
                normalSpeed = Mathf.Max(normalSpeed, ExpeditionEffectRules.NormalSpeed(collision.relativeVelocity, collision.GetContact(i).normal));
            int damage = ExpeditionEffectRules.ImpactDamage(normalSpeed, world.settings.collisionDamageSpeed, world.settings.collisionDamageStep);
            bool crashed = State == CreatureState.Strike && dash && normalSpeed > 4 && !collision.rigidbody;
            if (damage > 0)
            {
                world.Spawn(world.vfx ? world.vfx.dustPuff : null, collision.GetContact(0).point, Quaternion.identity, new Color(.8f, .72f, .6f), 1);
                Damage(damage, Vector3.zero, collision.GetContact(0).point, 0);
            }
            if (crashed && Alive)
            {
                // Charging into rock: dazed — the opening the board's "use the predator's dash" example describes.
                Stagger(1.3f, collision.GetContact(0).point);
                if (world.feel && PlayerDistance < 12) world.feel.AddTrauma(.25f);
            }
        }

        // ---------------- presentation ----------------

        /// <summary>Which drawing the current state reads as.</summary>
        CreaturePose PoseForState()
        {
            if (Time.time < hurtUntil || State == CreatureState.Stunned) return CreaturePose.Hurt;
            switch (State)
            {
                case CreatureState.Alert:
                case CreatureState.Threat:
                case CreatureState.Chase:
                case CreatureState.Windup:
                case CreatureState.Aim:
                    return CreaturePose.Alarm;
                case CreatureState.Shoot:
                    return stateTime < ShotPoseSeconds ? CreaturePose.Attack : CreaturePose.Alarm;
                case CreatureState.Flee:
                    // Runs off in its ordinary drawing (seen from behind), startles again on every stop.
                    return stateTime % (dashSeconds + pauseSeconds) < dashSeconds ? CreaturePose.Default : CreaturePose.Alarm;
                case CreatureState.Strike:
                    return CreaturePose.Attack;
                default:
                    return CreaturePose.Default;
            }
        }

        void LateUpdate()
        {
            if (appliedSize != CurrentSize) ApplySize();
            if (flying) UpdateShadow();
            if (!world || dying || !view) return;
            float dt = Time.deltaTime;
            Vector3 velocity = body.linearVelocity;
            float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            bool frozen = State == CreatureState.Frozen;
            if (!frozen)
            {
                float previous = gaitPhase;
                gaitPhase += dt * gaitFrequency * Mathf.Clamp(planarSpeed / Mathf.Max(.5f, speed), .3f, 2.4f) * Mathf.PI * 2 * (planarSpeed > .3f ? 1 : 0);
                if (Mathf.FloorToInt(previous / Mathf.PI) != Mathf.FloorToInt(gaitPhase / Mathf.PI))
                {
                    squash.Kick(-1.2f);
                    if (planarSpeed > 1 && PlayerDistance < 18) world.Play(stepCue, body.position, .35f);
                    if (planarSpeed > .5f) KickDust(stepDustCount);
                }
            }
            float sq = squash.Step(1, 7, .35f, dt);
            float targetCrouch = State == CreatureState.Windup ? 1 : State == CreatureState.Aim ? .6f : State == CreatureState.Threat ? .45f : 0;
            crouch = Mathf.MoveTowards(crouch, targetCrouch, dt * (State == CreatureState.Windup ? 3 : 5));
            // Frozen: the drawing and its squash stay exactly as the ice caught them.
            if (!frozen)
            {
                pose = PoseForState();
                float hopScale = State == CreatureState.Flee ? 2.6f : 1;
                float hop = flying ? 0 : Mathf.Abs(Mathf.Sin(gaitPhase)) * hopHeight * hopScale * Mathf.Clamp01(planarSpeed / 1.2f);
                Vector3 tremble = State == CreatureState.Windup ? Random.insideUnitSphere * .02f * TopHeight * crouch : Vector3.zero;
                float stretch = State == CreatureState.Strike ? 1.12f : 1;
                float roll = State == CreatureState.Stunned ? Mathf.Sin(Time.time * 22) * 10 : 0;
                view.Present(pose, transform.forward, Vector3.up * hop + tremble + FlightSway(),
                    new Vector2(stretch / Mathf.Sqrt(sq) * (1 + crouch * .06f), sq * (1 - crouch * .16f)), roll);
            }
            // Colour: red flash on hit, icy while frozen, flickering orange while burning.
            if (Time.time < flashUntil) Tint(hitTint, 1);
            else if (frozen) Tint(frozenTint, .65f);
            else if (Time.time < burnUntil) Tint(burnTint, .45f + Mathf.Sin(Time.time * 30) * .15f);
            else Tint(Color.white, 0);
        }

        /// <summary>A puff of dust from under the feet; throttled so a fast gait does not turn into a smoke screen.</summary>
        void KickDust(int count)
        {
            // No dust from under a flyer; it only kicks some up when it hits the ground dead.
            if (!stepDust || count <= 0 || flying && Alive || Time.time < nextDust || PlayerDistance > 30) return;
            nextDust = Time.time + .08f;
            stepDust.Emit(new ParticleSystem.EmitParams { startColor = dustColour }, count);
        }

        void Tint(Color colour, float amount)
        {
            // The hand-drawn shader multiplies by the renderer colour, so tints only darken toward a hue.
            if (view) view.Tint(Color.Lerp(Color.white, new Color(colour.r, colour.g, colour.b, 1), amount));
        }

        static readonly List<LootItem> dropped = new List<LootItem>();

        /// <summary>Rolls <see cref="drops"/>; every copy pops out of the body on its own arc.</summary>
        void DropLoot()
        {
            dropped.Clear();
            drops.Roll(dropped);
            if (dropped.Count == 0) return;
            Vector3 from = transform.position + Vector3.up * Mathf.Max(.4f, TopHeight * .5f);
            foreach (var item in dropped)
            {
                var drop = world.SpawnLoot(item, from);
                if (drop) drop.Pop(Vector3.up * 5.5f + Random.insideUnitSphere * (dropped.Count > 1 ? 2.2f : 1.5f));
            }
            world.Play(world.cues ? world.cues.trophyDrop : null, from);
            world.Announce(displayName + (dropped.Count > 1 ? ": трофеи ждут" : ": трофей ждёт"), 2.2f);
        }
    }
}
