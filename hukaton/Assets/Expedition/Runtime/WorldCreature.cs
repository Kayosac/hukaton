using System.Collections;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions
{
    public enum Temperament { Timid, Territorial, Aggressive }
    public enum CreatureState { Idle, Wander, Graze, Alert, Flee, Threat, Chase, Search, Return, Windup, Strike, Recover, Reposition, Frozen, Stunned, Dying }

    /// <summary>
    /// A creature of the valley. Decisions run in Update (per temperament), locomotion in FixedUpdate on the
    /// Rigidbody (so cards and rocks can really shove it), and all animation is procedural in LateUpdate.
    /// Every attack is telegraphed: posture, sound and a red "!!" before the direction locks.
    /// </summary>
    public sealed class WorldCreature : MonoBehaviour
    {
        [Header("Identity")]
        public ExpeditionWorld world;
        public Temperament temperament;
        public string displayName;
        [Header("Authored parts")]
        public Rigidbody body;
        public NavMeshAgent navigator;
        public Transform visual, head;
        public Renderer[] skin;
        public CreatureOverhead overhead;
        [Tooltip("Height of the stun stars above the head and their size.")]
        public float stunStarsHeight = .35f, stunStarsScale = .8f;
        VfxBurst stunStars;
        public WorldLoot trophyPrefab;
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
        public Color frozenTint = new Color(.45f, .85f, 1f), burnTint = new Color(1f, .5f, .2f);
        [Header("Sounds")]
        public SfxCue alertCue;
        public SfxCue threatCue, windupCue, strikeCue, hurtCue, deathCue, stepCue;

        public int Health { get; private set; }
        public bool Alive => Health > 0 && !dying;
        public CreatureState State { get; private set; }

        Vector3 home, authoredHome, lastSeen, strikeDirection, desired, wanderTarget, fleeDirection;
        Quaternion homeRotation, authoredRotation, facing;
        float stateTime, nextSight, unseen, provokedUntil, frozenUntil, stunnedUntil, burnUntil, flashUntil;
        float nextPath, nextThreatSound, provokeTimer, idleDuration, gaitPhase, crouch;
        bool sees, hitThisStrike, dying;
        Vector3 faceTowards;
        SpringFloat squash;
        MaterialPropertyBlock block;
        Color[] baseColours;
        static readonly int BaseColour = Shader.PropertyToID("_BaseColor");

        Vector3 PlayerPosition => world.motor.transform.position;
        Vector3 ToPlayer { get { var v = PlayerPosition - body.position; v.y = 0; return v; } }
        float PlayerDistance => ToPlayer.magnitude;
        bool Provoked => Time.time < provokedUntil;

        void Awake()
        {
            // Navigation only plans; the Rigidbody owns position and rotation.
            navigator.updatePosition = false;
            navigator.updateRotation = false;
            home = authoredHome = transform.position;
            homeRotation = authoredRotation = facing = transform.rotation;
            block = new MaterialPropertyBlock();
            baseColours = System.Array.ConvertAll(skin, r => r.sharedMaterial.HasProperty(BaseColour) ? r.sharedMaterial.GetColor(BaseColour) : Color.white);
            Health = maximumHealth;
        }

        public void ResetForOuting(System.Random random)
        {
            StopAllCoroutines();
            gameObject.SetActive(true);
            dying = false;
            Health = maximumHealth;
            var point = random != null && spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints[random.Next(spawnPoints.Length)] : null;
            home = point ? point.position : authoredHome;
            homeRotation = facing = point ? point.rotation : authoredRotation;
            frozenUntil = stunnedUntil = burnUntil = flashUntil = provokedUntil = 0;
            unseen = provokeTimer = 0;
            body.isKinematic = false;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = home;
            body.rotation = homeRotation;
            transform.SetPositionAndRotation(home, homeRotation);
            navigator.enabled = true;
            if (navigator.isOnNavMesh) { navigator.Warp(home); navigator.ResetPath(); }
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            squash.Snap(1);
            if (overhead) overhead.Clear();
            Enter(CreatureState.Idle);
            Tint(Color.white, 0);
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
                default: ThinkAggressive(); break;
            }
            UpdateOverhead();
        }

        void Perceive(float dt)
        {
            if (Time.time >= nextSight)
            {
                nextSight = Time.time + .12f;
                sees = CanSeePlayer();
            }
            if (sees) { lastSeen = PlayerPosition; unseen = 0; }
            else unseen += dt;
        }

        bool CanSeePlayer()
        {
            Vector3 eye = body.position + Vector3.up * .9f;
            Vector3 target = PlayerPosition + Vector3.up * 1.2f;
            Vector3 to = target - eye;
            float distance = to.magnitude;
            if (distance > sightRange) return false;
            bool calm = State == CreatureState.Idle || State == CreatureState.Wander || State == CreatureState.Graze;
            bool loud = world.motor.IsSprinting && distance < hearingRange * 2;
            if (calm && distance > hearingRange && !loud && Vector3.Dot(transform.forward, to / distance) < -.25f) return false;
            // The player rig is on Ignore Raycast: anything hit means the view is blocked.
            return !Physics.Raycast(eye, to / distance, distance - .3f, ~(1 << 2), QueryTriggerInteraction.Ignore);
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
                case CreatureState.Alert: world.Play(alertCue, body.position); squash.Kick(-3); break;
                case CreatureState.Threat: nextThreatSound = 0; provokeTimer = 0; break;
                case CreatureState.Flee: ChooseFleeDirection(); world.Play(alertCue, body.position, 1, 1.15f); break;
                case CreatureState.Windup:
                    world.Play(windupCue, body.position);
                    squash.Kick(-4);
                    break;
                case CreatureState.Strike:
                    Vector3 to = ToPlayer;
                    strikeDirection = to.sqrMagnitude > .01f ? to.normalized : transform.forward;
                    facing = Quaternion.LookRotation(strikeDirection);
                    hitThisStrike = false;
                    world.Play(strikeCue, body.position);
                    squash.Kick(6);
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
            }
        }

        bool IsCalm => State == CreatureState.Idle || State == CreatureState.Wander || State == CreatureState.Graze;

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
                        if (phase < Time.deltaTime * 1.5f && stateTime > .1f) ChooseFleeDirection();
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
            float distance = PlayerDistance;
            if (IsCalm)
            {
                if (Provoked) { Enter(CreatureState.Chase); return; }
                if (sees && distance < threatRange && Flat(PlayerPosition - home).magnitude < leash) { Enter(CreatureState.Threat); return; }
                ThinkIdle(false);
                return;
            }
            switch (State)
            {
                case CreatureState.Threat:
                    Halt();
                    Face(ToPlayer);
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
                if (Provoked || (sees && PlayerDistance < sightRange)) { Aggro(); return; }
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
            float distance = PlayerDistance;
            float fromHome = Flat(body.position - home).magnitude;
            switch (State)
            {
                case CreatureState.Chase:
                    if (fromHome > leash && !Provoked) { Enter(CreatureState.Return); break; }
                    if (!sees) { Enter(CreatureState.Search); break; }
                    Go(PlayerPosition, speed);
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
                    Face(ToPlayer);
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
                    Vector3 away = -ToPlayer.normalized;
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
            int level = State switch
            {
                CreatureState.Windup or CreatureState.Strike => 2,
                CreatureState.Alert or CreatureState.Threat => 1,
                CreatureState.Chase when stateTime < 1.2f => temperament == Temperament.Aggressive ? 2 : 1,
                _ => 0
            };
            overhead.SetAlert(level);
        }

        // ---------------- movement ----------------

        void Go(Vector3 destination, float moveSpeed)
        {
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
            Vector3 target = controlled ? Vector3.zero : desired;
            planar = Vector3.MoveTowards(planar, target, acceleration * dt);
            body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
            if (!controlled) body.MoveRotation(Quaternion.RotateTowards(body.rotation, facing, turnRate * dt * (State == CreatureState.Windup ? 1.6f : 1)));
            if (State == CreatureState.Strike) CheckStrikeContact();
        }

        void CheckStrikeContact()
        {
            if (hitThisStrike) return;
            Vector3 snout = body.position + transform.forward * .65f + Vector3.up * .6f;
            Vector3 player = PlayerPosition + Vector3.up * .9f;
            Vector3 to = player - snout;
            if (to.magnitude > contactRange || Vector3.Dot(strikeDirection, Flat(to).normalized) < .1f) return;
            hitThisStrike = true;
            Vector3 knockback = (strikeDirection * (dash ? 7 : 4) + Vector3.up * 2.5f);
            world.vitals.Hurt(attackHalfHearts, knockback, body.position);
        }

        // ---------------- reactions ----------------

        public void Damage(int amount, Vector3 impulse, Vector3 point, float burnSeconds)
        {
            if (!Alive) return;
            Health = Mathf.Max(0, Health - amount);
            if (amount > 0)
            {
                flashUntil = Time.time + .1f;
                squash.Kick(-6);
                world.Play(hurtCue, body.position, 1, Random.Range(.95f, 1.15f));
                if (overhead) overhead.ShowHealth(Health, maximumHealth);
            }
            if (impulse.sqrMagnitude > 0) body.AddForce(impulse, ForceMode.Impulse);
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
                stunStars.Follow(head ? head : transform, Vector3.up * stunStarsHeight);
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
            Vector3 start = body.position;
            Vector3 drift = Flat(impulse).normalized * 1.2f;
            float time = 0;
            while (time < .38f)
            {
                time += Time.deltaTime;
                float k = time / .38f;
                transform.position = start + drift * k + Vector3.up * Mathf.Sin(k * Mathf.PI) * .9f;
                visual.localRotation = Quaternion.Euler(k * 200, 0, k * 120);
                visual.localScale = Vector3.one * Mathf.Lerp(1, .7f, k);
                Tint(Color.white, 1 - k);
                yield return null;
            }
            world.Spawn(world.vfx ? world.vfx.creatureDeath : null, transform.position + Vector3.up * .5f, Quaternion.identity, baseColours.Length > 0 ? baseColours[0] : Color.white, 1.2f);
            if (world.feel) world.feel.AddTrauma(.18f);
            if (trophyPrefab)
            {
                var trophy = Instantiate(trophyPrefab, transform.position + Vector3.up * .6f, Quaternion.identity, world.spawnedObjects);
                trophy.world = world;
                trophy.Pop(Vector3.up * 5.5f + Random.insideUnitSphere * 1.5f);
                world.Play(world.cues ? world.cues.trophyDrop : null, trophy.transform.position);
            }
            world.Announce(displayName + ": трофей ждёт", 2.2f);
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

        // ---------------- procedural animation ----------------

        void LateUpdate()
        {
            if (!world || dying || !visual) return;
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
                }
            }
            float sq = squash.Step(1, 7, .35f, dt);
            float targetCrouch = State == CreatureState.Windup ? 1 : State == CreatureState.Threat ? .45f : 0;
            crouch = Mathf.MoveTowards(crouch, targetCrouch, dt * (State == CreatureState.Windup ? 3 : 5));
            float hopScale = State == CreatureState.Flee ? 2.6f : 1;
            float hop = frozen ? 0 : Mathf.Abs(Mathf.Sin(gaitPhase)) * hopHeight * hopScale * Mathf.Clamp01(planarSpeed / 1.2f);
            Vector3 tremble = State == CreatureState.Windup ? Random.insideUnitSphere * .025f * crouch : Vector3.zero;
            float stretch = State == CreatureState.Strike ? 1.18f : 1;
            float stunRoll = State == CreatureState.Stunned ? Mathf.Sin(Time.time * 22) * 14 : 0;
            if (!frozen)
            {
                visual.localPosition = Vector3.up * hop + tremble + Vector3.down * crouch * .08f;
                visual.localScale = new Vector3(1 / Mathf.Sqrt(sq), sq * (1 - crouch * .16f), stretch * (1 + crouch * .06f));
                visual.localRotation = Quaternion.Euler(-crouch * 8 + (State == CreatureState.Strike ? 6 : 0), 0, stunRoll);
            }
            if (head && !frozen)
            {
                float pitch = State switch
                {
                    CreatureState.Graze => 38,
                    CreatureState.Alert => -14,
                    CreatureState.Threat => 16 + Mathf.Sin(Time.time * 7) * 4,
                    CreatureState.Windup => 22,
                    _ => Mathf.Sin(Time.time * 1.3f + home.x) * 4
                };
                float yaw = 0;
                if (State == CreatureState.Alert || State == CreatureState.Idle && sees)
                {
                    Vector3 local = transform.InverseTransformDirection(ToPlayer);
                    yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -45, 45);
                }
                head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitch, yaw, 0), Ease.Damp(10, dt));
            }
            // Colour: white flash on hit, icy while frozen, flickering orange while burning.
            if (Time.time < flashUntil) Tint(Color.white, 1);
            else if (frozen) Tint(frozenTint, .65f);
            else if (Time.time < burnUntil) Tint(burnTint, .45f + Mathf.Sin(Time.time * 30) * .15f);
            else Tint(Color.white, 0);
        }

        void Tint(Color colour, float amount)
        {
            if (skin == null || block == null) return;
            for (int i = 0; i < skin.Length; i++)
            {
                if (!skin[i]) continue;
                Color baseColour = i < baseColours.Length ? baseColours[i] : Color.white;
                Color result = colour == Color.white && amount > .99f ? Color.white * 1.6f : Color.Lerp(baseColour, colour, amount);
                skin[i].GetPropertyBlock(block);
                block.SetColor(BaseColour, result);
                skin[i].SetPropertyBlock(block);
            }
        }
    }
}
