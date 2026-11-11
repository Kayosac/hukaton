using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A chest that bites. It sits on a <see cref="TreasureChest"/> (same prefab, same tier, same marker) and looks like
    /// one; only close up (<see cref="tellDistance"/>) the lid twitches now and then and it breathes.
    /// E without ice: a warning like every creature attack (red «!!», the lid gapes, the body jerks, a growl), the bite
    /// (half hearts and a shove), then it hops away with coins rattling inside and, after a while, plays chest again
    /// nearby — this time on guard, so the next E ends the same way. Killed by cards it pours out more than a plain
    /// chest of its tier; frozen by Ice it can be opened with E for the plain payout. It never leaves the circle of
    /// <see cref="leash"/> around where it appeared, and stands on uneven ground by itself.
    /// Its state can be taken and given back (<see cref="Capture"/> / <see cref="Restore"/>) for a world that unloads.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TreasureChest))]
    public sealed class ChestMimic : MonoBehaviour, ICardCreature, IChestOccupant
    {
        /// <summary>What survives an unload: shut and unsuspecting, shut and on guard, killed, opened while frozen.</summary>
        public enum State { Closed, Alert, Dead, Opened }

        /// <summary>A saved mimic: its state, where it stands and where it appeared (the centre of its circle).</summary>
        [System.Serializable]
        public struct Snapshot
        {
            public State state;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 home;
        }

        enum Mode { Disguised, Warning, Biting, Fleeing, Recovering, Dead, Opened }

        [Header("Parts")]
        public TreasureChest chest;
        [Tooltip("Hangs out of the mouth when it gapes and when it dies; pivot at the back of the tongue.")]
        public Transform tongue;
        [Tooltip("Red «!!» before the bite and health pips after a hit (the creatures' Overhead).")]
        public CreatureOverhead overhead;
        [Tooltip("Turn ice-blue while frozen.")]
        public Renderer[] tinted = new Renderer[0];
        [Tooltip("Body height for the aim point (Centre) and the overhead, m.")]
        [Min(.1f)] public float height = .5f;

        [Header("Tells (only close up)")]
        [Min(0)] public float tellDistance = 6;
        [Tooltip("Seconds between lid twitches: from and to. On guard it twitches twice as often.")]
        public Vector2 twitchEvery = new Vector2(2.5f, 6f);
        [Min(0)] public float twitchAngle = 8;
        [Tooltip("Quiet breathing loop heard within the tell distance.")]
        public SfxCue breath;
        [Range(0, 1)] public float breathVolume = .7f;

        [Header("Bite")]
        [Tooltip("The warning before the bite, seconds (the player can step back).")]
        [Min(.1f)] public float warnSeconds = .6f;
        [Tooltip("How wide the mouth gapes in the warning, degrees.")]
        [Range(20, 110)] public float gapeAngle = 75;
        [Tooltip("Damage of the bite in ½ hearts.")]
        [Min(1)] public int biteHalfHearts = 2;
        [Tooltip("The bite lands when the player's body is this close to the mouth, m.")]
        [Min(.5f)] public float biteReach = 2.6f;
        [Min(0)] public float biteKnockback = 6;
        [Tooltip("How far it lunges at the player, m.")]
        [Min(0)] public float lunge = .6f;
        public SfxCue warnCue, biteCue;
        public VfxBurst biteBurst;
        [Min(.01f)] public float biteBurstScale = .5f;

        [Header("Running off")]
        [Tooltip("Never farther from where it appeared, m (fight and flight included).")]
        [Min(1)] public float leash = 7;
        [Min(.5f)] public float fleeSpeed = 4.5f;
        [Min(.5f)] public float hopsPerSecond = 3;
        [Min(0)] public float hopHeight = .3f;
        [Tooltip("Seconds after running off before it plays chest again: from and to.")]
        public Vector2 hideAfter = new Vector2(10, 15);
        [Tooltip("Running off again when the player comes this close while it is still catching its breath, m.")]
        [Min(0)] public float spookDistance = 3.5f;
        public SfxCue hopCue, rattleCue;

        [Header("Body")]
        [Min(1)] public int maxHealth = 8;
        public SfxCue hurtCue, deathCue;
        public VfxBurst deathBurst;
        [Min(.01f)] public float deathBurstScale = .8f;
        [Tooltip("Killed: the tier's coins times this.")]
        [Min(1)] public float killCoinMultiplier = 1.8f;
        [Tooltip("Killed: the tier's bonus finds are rolled this many times.")]
        [Min(1)] public int killBonusRolls = 2;

        [Header("Frozen")]
        [ColorUsage(false, true)] public Color frozenTint = new Color(.75f, 1.25f, 1.9f);
        public VfxBurst freezeBurst;
        [Min(.01f)] public float freezeBurstScale = .6f;

        public int Health { get; private set; }
        public bool Frozen => Time.time < frozenUntil && mode != Mode.Dead && mode != Mode.Opened;
        public State Current => mode == Mode.Dead ? State.Dead : mode == Mode.Opened ? State.Opened : alert ? State.Alert : State.Closed;
        /// <summary>Where it appeared: the centre of its circle.</summary>
        public Vector3 Home => home;
        /// <summary>Bites landed on the player (tests, debug).</summary>
        public int Bites { get; private set; }
        public bool Busy => mode == Mode.Warning || mode == Mode.Biting || mode == Mode.Fleeing || mode == Mode.Recovering;

        // ICombatant
        public Faction Faction => Faction.Wild;
        public bool Alive => mode != Mode.Dead && mode != Mode.Opened;
        public Vector3 Position => transform.position;
        public Vector3 Centre => transform.position + transform.up * height * .5f;
        public Transform Root => transform;

        ExpeditionWorld World => chest ? chest.world : null;
        Transform Lid => chest && chest.lid ? chest.lid.Lid : null;

        Mode mode;
        bool alert, homed, posed;
        Vector3 home;
        Quaternion lidClosed, tongueRest;
        float lidAngle, lidTarget, lidSpeed = 300;
        float nextTwitch, frozenUntil = -1, recoverAt, tilt;
        bool registered;
        MaterialPropertyBlock block;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly RaycastHit[] Hits = new RaycastHit[12];

        void Reset() => chest = GetComponent<TreasureChest>();

        void Awake()
        {
            if (!chest) chest = GetComponent<TreasureChest>();
            Health = maxHealth;
            Pose();
        }

        /// <summary>Records the closed lid and resting tongue once (the authored pose).</summary>
        void Pose()
        {
            if (posed) return;
            posed = true;
            if (Lid) lidClosed = Lid.localRotation;
            if (tongue) tongueRest = tongue.localRotation;
        }

        void Start()
        {
            if (!homed) { home = transform.position; homed = true; }
            SnapToGround();
            ScheduleTwitch();
            // The spawner sets the chest's world after instantiation (after OnEnable).
            Register(true);
        }

        void OnEnable() { Register(true); }

        void OnDisable()
        {
            Register(false);
            var world = World;
            if (world && world.sfx) world.sfx.StopLoop(this);
        }

        void Register(bool on)
        {
            var world = World;
            if (!world) return;
            if (on && !registered && Alive) { world.Wild.Add(this); registered = true; }
            else if (!on && registered) { world.Wild.Remove(this); registered = false; }
        }

        // ---------------- chest occupant ----------------

        public bool CanBeOpened => Alive && (Frozen || mode == Mode.Disguised);

        public bool InterceptOpen()
        {
            if (!Alive) return true;
            if (Frozen)
            {
                // Iced over: it cannot bite; the chest opens as a plain one and the mimic is spent.
                float thaw = frozenUntil - Time.time;
                End(Mode.Opened);
                StartCoroutine(ThawLater(thaw));
                return false;
            }
            if (mode != Mode.Disguised) return true;
            StartCoroutine(Bite());
            return true;
        }

        IEnumerator ThawLater(float seconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0, seconds));
            Tint(false);
        }

        public void OnChestReset()
        {
            StopAllCoroutines();
            Pose();
            mode = Mode.Disguised;
            alert = false;
            Health = maxHealth;
            frozenUntil = -1;
            tilt = 0;
            SetLid(0, true);
            SetTongue(0);
            Tint(false);
            if (overhead) overhead.Clear();
            ScheduleTwitch();
            Register(true);
        }

        public void OnChestEmptied()
        {
            // A world that only remembers "opened" gives it back opened and spent.
            if (mode != Mode.Dead) End(Mode.Opened);
        }

        // ---------------- saving ----------------

        public Snapshot Capture() => new Snapshot { state = Current, position = transform.position, rotation = transform.rotation, home = homed ? home : transform.position };

        /// <summary>Puts the mimic back as it was left: shut, on guard (where it last hid), killed or opened.</summary>
        public void Restore(Snapshot snapshot)
        {
            Pose();
            StopAllCoroutines();
            home = snapshot.home;
            homed = true;
            transform.SetPositionAndRotation(snapshot.position, snapshot.rotation);
            SnapToGround();
            switch (snapshot.state)
            {
                case State.Closed:
                case State.Alert:
                    chest.ResetForOuting(); // calls OnChestReset; the chest puts back its spawn pose, so place it again
                    alert = snapshot.state == State.Alert;
                    transform.SetPositionAndRotation(snapshot.position, snapshot.rotation);
                    SnapToGround();
                    break;
                case State.Opened:
                    chest.SetEmpty(); // calls OnChestEmptied
                    break;
                case State.Dead:
                    mode = Mode.Dead;
                    chest.SetEmpty();
                    DeadPose();
                    break;
            }
        }

        // ---------------- fighting ----------------

        public bool TakeDamage(in DamageInfo damage)
        {
            var world = World;
            if (!Alive || !world || !world.Running || damage.Amount <= 0) return false;
            Health = Mathf.Max(0, Health - damage.Amount);
            if (overhead) overhead.ShowHealth(Health, maxHealth);
            world.Play(hurtCue, Centre);
            StartCoroutine(Flinch());
            if (Health == 0) { Die(); return true; }
            if (Frozen) return true;
            // A hit wakes it: it bites when the player stands close, otherwise it runs.
            if (mode == Mode.Disguised)
            {
                alert = true;
                if (PlayerDistance() <= biteReach + .4f) StartCoroutine(Bite());
                else StartCoroutine(Flee());
            }
            else if (mode == Mode.Recovering) StartCoroutine(Flee());
            return true;
        }

        public void Freeze(float seconds)
        {
            var world = World;
            if (!Alive || seconds <= 0) return;
            bool was = Frozen;
            frozenUntil = Mathf.Max(frozenUntil, Time.time + seconds);
            if (was) return;
            // Caught mid-move: everything stops where it is, mouth shut.
            StopAllCoroutines();
            if (mode != Mode.Disguised) { mode = Mode.Recovering; recoverAt = Mathf.Max(recoverAt, frozenUntil + 1); }
            if (overhead) overhead.SetAlert(0);
            SetLid(0, true);
            SetTongue(0);
            Tint(true);
            if (world) world.Spawn(freezeBurst, Centre, Quaternion.identity, frozenTint, freezeBurstScale);
            if (world && world.sfx) world.sfx.StopLoop(this);
        }

        void Die()
        {
            var world = World;
            StopAllCoroutines();
            End(Mode.Dead);
            Tint(false);
            if (overhead) overhead.Clear();
            world.Play(deathCue, Centre);
            world.Spawn(deathBurst, Centre, Quaternion.identity, Color.white, deathBurstScale);
            StartCoroutine(Dying());
        }

        IEnumerator Dying()
        {
            // The lid flops open, the tongue lolls out, the box keels over a little; then the contents pour out.
            float time = 0;
            while (time < .35f)
            {
                time += Time.deltaTime;
                float k = Ease.OutBack(Mathf.Clamp01(time / .35f));
                lidAngle = Mathf.LerpUnclamped(lidAngle, 100, k);
                ApplyLid();
                SetTongue(k);
                tilt = Mathf.LerpUnclamped(0, 11, k);
                ApplyTilt();
                yield return null;
            }
            chest.Spill(killCoinMultiplier, killBonusRolls);
        }

        void DeadPose()
        {
            lidAngle = lidTarget = 100;
            ApplyLid();
            SetTongue(1);
            tilt = 11;
            ApplyTilt();
        }

        void End(Mode final)
        {
            mode = final;
            frozenUntil = -1;
            Register(false);
            var world = World;
            if (world && world.sfx) world.sfx.StopLoop(this);
            if (overhead) overhead.SetAlert(0);
        }

        /// <summary>Warning (like every creature attack), the bite, then running off.</summary>
        IEnumerator Bite()
        {
            var world = World;
            mode = Mode.Warning;
            alert = true;
            if (world.sfx) world.sfx.StopLoop(this);
            if (overhead) overhead.SetAlert(2);
            world.Play(warnCue, Centre);
            Quaternion start = transform.rotation;
            float time = 0;
            while (time < warnSeconds)
            {
                time += Time.deltaTime;
                float k = time / warnSeconds;
                // Turns its mouth (the front, -z) to the player and gapes, shaking.
                Vector3 to = Flat(PlayerFeet() - transform.position);
                if (to.sqrMagnitude > .01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(-to), Time.deltaTime * 10);
                lidAngle = Mathf.Lerp(0, gapeAngle, Ease.OutBack(Mathf.Clamp01(k * 1.6f)));
                ApplyLid();
                SetTongue(Mathf.Clamp01(k * 1.4f) * .6f);
                float shake = Mathf.Sin(time * 60) * 5 * (.4f + k);
                transform.rotation *= Quaternion.Euler(0, 0, shake * Time.deltaTime * 6);
                yield return null;
            }

            mode = Mode.Biting;
            if (overhead) overhead.SetAlert(0);
            Vector3 toPlayer = Flat(PlayerFeet() - transform.position);
            float reach = Mathf.Min(lunge, Mathf.Max(0, toPlayer.magnitude - .7f));
            Vector3 from = transform.position, to2 = from + (toPlayer.sqrMagnitude > .01f ? toPlayer.normalized : -transform.forward) * reach;
            to2 = Leashed(to2);
            time = 0;
            bool snapped = false;
            while (time < .12f)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / .12f);
                transform.position = Vector3.Lerp(from, to2, Ease.OutCubic(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * .15f;
                lidAngle = Mathf.Lerp(gapeAngle, 0, k * k);
                ApplyLid();
                SetTongue((1 - k) * .6f);
                if (!snapped && k > .6f) { snapped = true; Snap(); }
                yield return null;
            }
            SnapToGround();
            yield return new WaitForSeconds(.25f);
            StartCoroutine(Flee());
        }

        /// <summary>The jaws shut: the bite lands when the player is still within reach of the mouth.</summary>
        void Snap()
        {
            var world = World;
            world.Play(biteCue, Centre);
            if (world.vitals == null) return;
            Vector3 mouth = Centre - transform.forward * .3f;
            if (Vector3.Distance(mouth, world.vitals.Centre) > biteReach) return;
            Vector3 push = Flat(world.vitals.Centre - transform.position);
            push = (push.sqrMagnitude > .01f ? push.normalized : -transform.forward) * biteKnockback + Vector3.up * 2.5f;
            if (world.vitals.TakeDamage(new DamageInfo(biteHalfHearts, push, world.vitals.Centre, transform.position, DamageKind.Hit, this)))
            {
                Bites++;
                var eye = world.viewCamera ? world.viewCamera.transform : null;
                Vector3 at = eye ? eye.position + eye.forward * .6f - eye.up * .15f : world.vitals.Centre;
                world.Spawn(biteBurst, at, eye ? eye.rotation : Quaternion.identity, Color.white, biteBurstScale);
            }
        }

        /// <summary>Hops to a spot inside its circle away from the player, coins rattling, then catches its breath.</summary>
        IEnumerator Flee()
        {
            var world = World;
            mode = Mode.Fleeing;
            if (world.sfx) world.sfx.StopLoop(this);
            if (recoverAt < Time.time + 4) recoverAt = Time.time + Random.Range(hideAfter.x, hideAfter.y);
            Vector3 target = FleeSpot();
            float hopSeconds = 1 / hopsPerSecond, stride = fleeSpeed * hopSeconds;
            int guard = 0;
            while (Flat(target - transform.position).magnitude > .25f && guard++ < 60)
            {
                Vector3 start = transform.position;
                Vector3 step = Flat(target - start);
                step = step.magnitude > stride ? step.normalized * stride : step;
                Vector3 end = Ground(Leashed(start + step));
                Quaternion face = step.sqrMagnitude > .001f ? Quaternion.LookRotation(-step.normalized) : transform.rotation;
                float chatter = Random.Range(8, 22);
                world.Play(rattleCue, Centre, .8f, Random.Range(.92f, 1.12f));
                float time = 0;
                while (time < hopSeconds)
                {
                    time += Time.deltaTime;
                    float k = Mathf.Clamp01(time / hopSeconds);
                    transform.position = Vector3.Lerp(start, end, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * hopHeight;
                    transform.rotation = Quaternion.Slerp(transform.rotation, face, Time.deltaTime * 12);
                    lidAngle = Mathf.Sin(k * Mathf.PI) * chatter;
                    ApplyLid();
                    yield return null;
                }
                world.Play(hopCue, transform.position, .7f);
            }
            SetLid(0, true);
            SnapToGround();
            mode = Mode.Recovering;
        }

        void Update()
        {
            var world = World;
            if (!world || !Alive) return;
            if (frozenUntil >= 0 && Time.time >= frozenUntil) { frozenUntil = -1; Tint(false); }
            if (!world.Running || Frozen) return;

            switch (mode)
            {
                case Mode.Disguised:
                    Tells(world);
                    break;
                case Mode.Recovering:
                    if (PlayerDistance() < spookDistance) { StartCoroutine(Flee()); break; }
                    // Panting with its mouth a crack open, the coins still inside.
                    lidAngle = Mathf.Abs(Mathf.Sin(Time.time * 7)) * 6;
                    ApplyLid();
                    Breathe(world, 1.3f);
                    if (Time.time >= recoverAt)
                    {
                        mode = Mode.Disguised;
                        alert = true;
                        SetLid(0, true);
                        ScheduleTwitch();
                    }
                    break;
            }
        }

        /// <summary>Close up: breathing and a lid twitch now and then. From afar it is just a chest.</summary>
        void Tells(ExpeditionWorld world)
        {
            float distance = PlayerDistance();
            if (distance > tellDistance)
            {
                if (world.sfx) world.sfx.StopLoop(this);
                if (lidAngle != 0) { lidAngle = Mathf.MoveTowards(lidAngle, 0, Time.deltaTime * lidSpeed); ApplyLid(); }
                return;
            }
            Breathe(world, 1);
            if (Time.time >= nextTwitch)
            {
                lidTarget = twitchAngle * Random.Range(.6f, 1.2f);
                ScheduleTwitch();
            }
            lidAngle = Mathf.MoveTowards(lidAngle, lidTarget, Time.deltaTime * lidSpeed);
            if (Mathf.Approximately(lidAngle, lidTarget)) lidTarget = 0;
            ApplyLid();
        }

        void Breathe(ExpeditionWorld world, float loudness)
        {
            if (!world.sfx || !breath) return;
            float distance = PlayerDistance();
            float fade = Mathf.Clamp01(1 - distance / Mathf.Max(.1f, tellDistance));
            world.sfx.Loop(this, breath, Centre, breathVolume * loudness * fade);
        }

        void ScheduleTwitch()
        {
            float span = Random.Range(twitchEvery.x, twitchEvery.y);
            nextTwitch = Time.time + (alert ? span * .5f : span);
        }

        IEnumerator Flinch()
        {
            float time = 0;
            while (time < .18f)
            {
                time += Time.deltaTime;
                float k = 1 - time / .18f;
                if (mode == Mode.Disguised || mode == Mode.Recovering)
                {
                    lidAngle = Mathf.Max(lidAngle, 18 * k);
                    ApplyLid();
                }
                yield return null;
            }
        }

        // ---------------- pose helpers ----------------

        void SetLid(float angle, bool instant)
        {
            lidAngle = lidTarget = angle;
            if (instant) ApplyLid();
        }

        void ApplyLid()
        {
            var lid = Lid;
            if (!lid || (chest && chest.Opened && mode != Mode.Dead)) return;
            lid.localRotation = lidClosed * Quaternion.Euler(lidAngle, 0, 0);
        }

        /// <summary>0 tongue inside, 1 hanging out over the front.</summary>
        void SetTongue(float amount)
        {
            if (!tongue) return;
            // Inside it lies back and down; out it swings up over the front edge and hangs down outside.
            tongue.localRotation = tongueRest * Quaternion.Euler(Mathf.Lerp(0, -247, amount), 0, 0);
            tongue.localScale = Vector3.one * Mathf.Lerp(.85f, 1.15f, amount);
        }

        void ApplyTilt()
        {
            Vector3 euler = transform.eulerAngles;
            transform.rotation = Quaternion.Euler(0, euler.y, tilt);
        }

        void Tint(bool frozen)
        {
            if (tinted == null) return;
            block ??= new MaterialPropertyBlock();
            foreach (var renderer in tinted)
            {
                if (!renderer) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, frozen ? frozenTint : Color.white);
                renderer.SetPropertyBlock(block);
            }
        }

        // ---------------- ground and circle ----------------

        /// <summary>A point kept within the circle of <see cref="leash"/> around home (a little inside for the body).</summary>
        public Vector3 Leashed(Vector3 point)
        {
            Vector3 offset = Flat(point - home);
            float limit = Mathf.Max(0, leash - .4f);
            return offset.magnitude > limit ? new Vector3(home.x, point.y, home.z) + offset.normalized * limit : point;
        }

        /// <summary>The best spot to run to: inside the circle, on walkable ground, as far from the player as possible.</summary>
        Vector3 FleeSpot()
        {
            Vector3 player = PlayerFeet(), best = transform.position;
            float bestScore = float.MinValue;
            for (int ring = 0; ring < 3; ring++)
                for (int i = 0; i < 12; i++)
                {
                    float radius = (leash - .6f) * (.45f + ring * .27f), angle = (i * 30 + ring * 15) * Mathf.Deg2Rad;
                    Vector3 candidate = home + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
                    if (!TryGround(candidate, out var ground, out var normal) || normal.y < .8f) continue;
                    if (Mathf.Abs(ground.y - transform.position.y) > 2.5f) continue;
                    float score = Flat(ground - player).magnitude - Flat(ground - transform.position).magnitude * .15f;
                    if (score > bestScore) { bestScore = score; best = ground; }
                }
            return best;
        }

        Vector3 Ground(Vector3 point) => TryGround(point, out var ground, out _) ? ground : point;

        void SnapToGround()
        {
            if (TryGround(transform.position, out var ground, out _)) transform.position = ground;
        }

        /// <summary>The first solid surface under the point that is not this chest, a coin or water.</summary>
        bool TryGround(Vector3 point, out Vector3 ground, out Vector3 normal)
        {
            ground = point;
            normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 1.5f, Vector3.down, Hits, 5, ~((1 << 2) | (1 << 4)), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= best) continue;
                if (hit.collider.attachedRigidbody && !hit.collider.attachedRigidbody.isKinematic) continue;
                best = hit.distance;
                ground = hit.point;
                normal = hit.normal;
            }
            return best < float.MaxValue;
        }

        Vector3 PlayerFeet()
        {
            var world = World;
            return world && world.motor ? world.motor.transform.position : transform.position + Vector3.forward * 100;
        }

        float PlayerDistance() => Vector3.Distance(PlayerFeet(), transform.position);

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 centre = Application.isPlaying && homed ? home : transform.position;
            UnityEditor.Handles.color = new Color(1, .3f, .2f, .8f);
            UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, leash);
            UnityEditor.Handles.color = new Color(1, .9f, .3f, .5f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, tellDistance);
        }
#endif
    }
}
