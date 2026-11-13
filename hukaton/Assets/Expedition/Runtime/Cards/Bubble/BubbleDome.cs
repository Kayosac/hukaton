using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Пузырь»: an iridescent soap dome of radius 3 m standing on the ground for 4 / 6 / 9 s
    /// (prefab <c>Assets/Game/Prefabs/Cards/Bubble/Bubble.prefab</c>). It has no collider at all: the player walks
    /// and throws through it freely. Every physics step (after the creatures moved, see the execution order) it
    /// <list type="bullet">
    /// <item>pushes creatures out — one leaning on the film is put back on it and its inward speed removed (a charge
    /// bounces off), one caught inside when the dome grew is shoved out;</item>
    /// <item>pops every enemy projectile about to fly in (<see cref="EnemyProjectile.ShootDown"/>), with a pop on the film.</item>
    /// </list>
    /// A creature's strike from outside on anyone inside (the player at the very film too) lands on the film instead
    /// (<see cref="IStrikeShield"/>): no damage, no push, a ripple and a bump.
    /// Hits and pushes ripple and jiggle the film. Outlined on the ground while it stands; bursts when its time is up.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class BubbleDome : CardZoneBehaviour, ICardAreaOutline, IStrikeShield, ICardBarrier
    {
        [Header("Authored parts")]
        [Tooltip("The film: a crooked unit hemisphere (Hakaton/Soap Bubble), scaled to the radius.")]
        public Transform dome;
        public MeshRenderer film;

        [Header("Keeping creatures out")]
        [Tooltip("A creature caught inside is pushed out this fast, m/s.")]
        [Min(.5f)] public float expelSpeed = 7;
        [Tooltip("Up to this deep a creature is put right back on the film in one step, m.")]
        [Min(0)] public float snapDepth = .3f;
        [Tooltip("Faster than this into the film (a charge), a creature bounces back by the share below, m/s.")]
        [Min(0)] public float bounceSpeed = 3;
        [Range(0, 1.5f)] public float bounce = .6f;

        [Header("Projectiles")]
        [Tooltip("A projectile is popped when its next steps (this many) would carry it into the dome.")]
        [Min(1)] public float lookAheadSteps = 2;

        [Header("Look")]
        [Min(0)] public float growSeconds = .45f;
        [Min(0)] public float burstSeconds = .22f;
        [Tooltip("Jiggle of the film on a pop or a bump (shader _Kick), decaying.")]
        [Min(0)] public float popKick = .08f, bumpKick = .05f;
        [Min(0)] public float kickDecay = 2.5f;
        public Color tint = new Color(.7f, .9f, 1f, 1);

        [Header("Effects and sounds")]
        [Tooltip("Pop of a projectile on the film (Retro Arsenal BubbleExplosion).")]
        public VfxBurst popVfx;
        [Tooltip("The whole dome bursting at the end.")]
        public VfxBurst burstVfx;
        [Min(0)] public float popScale = .7f, burstScale = 2.2f;
        public SfxCue popCue, bumpCue, burstCue;

        static readonly int AlphaId = Shader.PropertyToID("_Alpha"), KickId = Shader.PropertyToID("_Kick"),
            RippleCentreId = Shader.PropertyToID("_RippleCentre"), RippleAgeId = Shader.PropertyToID("_RippleAge");

        MaterialPropertyBlock block;
        float until, bornAt, endedAt = -1, kick, rippleAt = -100, nextBump;
        Vector3 rippleCentre;
        int popped, pushed, blocked;

        public override CardZoneKind Kind => CardZoneKind.Other;
        public bool Standing => World && endedAt < 0;
        public float Remaining => Standing ? Mathf.Max(0, until - Time.time) : 0;
        /// <summary>Projectiles popped and creature pushes so far (tuning and tests).</summary>
        public int Popped => popped;
        public int Pushes => pushed;
        /// <summary>Creatures' strikes the film took.</summary>
        public int Blocked => blocked;
        /// <summary>The film's radius now: it grows from nothing when the card lands.</summary>
        public float CurrentRadius => radius * (growSeconds > 0 ? Ease.OutBack(Mathf.Clamp01((Time.time - bornAt) / growSeconds), 1.2f) : 1);

        Vector3 Centre => transform.position;

        public override bool Contains(Vector3 point) => BubbleRules.Inside(point, Centre, radius);

        /// <summary>Numbers from the card asset, right after spawning.</summary>
        public void Setup(float seconds, float domeRadius)
        {
            radius = Mathf.Max(.2f, domeRadius);
            height = radius;
            depth = BubbleRules.Below;
            bornAt = Time.time;
            until = Time.time + Mathf.Max(0, seconds);
            endedAt = -1;
            Present(0);
        }

        void Awake() => bornAt = Time.time;

        void Update()
        {
            if (!World) return;
            if (Standing)
            {
                if (!World.Running || Time.time >= until) Burst();
                Present(Time.deltaTime);
                return;
            }
            float k = burstSeconds > 0 ? Mathf.Clamp01((Time.time - endedAt) / burstSeconds) : 1;
            if (dome) dome.localScale = Vector3.one * radius * (1 + k * .25f);
            Apply(1 - k);
            if (k >= 1) Destroy(gameObject);
        }

        void Present(float dt)
        {
            kick = Mathf.Max(0, kick - kick * kickDecay * dt - dt * .01f);
            if (dome) dome.localScale = Vector3.one * Mathf.Max(.01f, CurrentRadius);
            // A last flicker before it bursts.
            float left = until - Time.time;
            float alpha = left < .8f ? .75f + .25f * Mathf.Sin(Time.time * 40) : 1;
            Apply(alpha);
        }

        void Apply(float alpha)
        {
            if (!film) return;
            block ??= new MaterialPropertyBlock();
            film.GetPropertyBlock(block);
            block.SetFloat(AlphaId, Mathf.Clamp01(alpha));
            block.SetFloat(KickId, kick);
            block.SetVector(RippleCentreId, rippleCentre);
            block.SetFloat(RippleAgeId, Time.time - rippleAt);
            film.SetPropertyBlock(block);
        }

        void FixedUpdate()
        {
            if (!Standing || !World.Running) return;
            float dt = Time.fixedDeltaTime;
            KeepCreaturesOut(dt);
            KeepOthersOut(dt);
            PopProjectiles(dt);
        }

        void KeepCreaturesOut(float dt)
        {
            var creatures = World.ActiveCreatures;
            if (creatures == null) return;
            float r = CurrentRadius;
            Vector3 centre = Centre;
            foreach (var creature in creatures)
            {
                if (!creature || !creature.isActiveAndEnabled || !creature.Alive) continue;
                var rigidbody = creature.body;
                if (!rigidbody || rigidbody.isKinematic) continue;
                Vector3 feet = rigidbody.position;
                float need = BubbleRules.Overlap(feet, creature.CapsuleRadius, creature.BodyHeight, centre, r, out var outward);
                if (need <= 0) continue;
                pushed++;
                rigidbody.position = feet + outward * BubbleRules.ExpelStep(need, snapDepth, expelSpeed, dt);
                rigidbody.linearVelocity = BubbleRules.Deflect(rigidbody.linearVelocity, outward, bounceSpeed, bounce, out float inward);
                if (inward > bounceSpeed) Bump(feet - outward * creature.CapsuleRadius + Vector3.up * Mathf.Min(1, creature.BodyHeight * .5f));
            }
        }

        void Bump(Vector3 point)
        {
            kick = Mathf.Max(kick, bumpKick);
            Ripple(point);
            if (Time.time < nextBump) return;
            nextBump = Time.time + .2f;
            World.Play(bumpCue, point, .7f, Random.Range(.8f, 1));
        }

        void PopProjectiles(float dt)
        {
            var shots = EnemyProjectile.Active;
            Vector3 centre = Centre;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var shot = shots[i];
                if (!shot || shot.Ended) continue;
                Vector3 from = shot.transform.position;
                Vector3 to = from + shot.Velocity * dt * lookAheadSteps;
                if (!BubbleRules.Enters(from, to, centre, radius, out var point)) continue;
                popped++;
                shot.ShootDown();
                Vector3 normal = point - centre;
                World.Spawn(popVfx, point, Quaternion.LookRotation(normal.sqrMagnitude > 1e-4f ? normal : Vector3.up), tint, popScale);
                World.Play(popCue, point, 1, Random.Range(.9f, 1.2f));
                kick = Mathf.Max(kick, popKick);
                Ripple(point);
            }
        }

        void Ripple(Vector3 point)
        {
            rippleCentre = point;
            rippleAt = Time.time;
        }

        /// <summary>Its time is up (or the outing ended): the film bursts.</summary>
        void Burst()
        {
            if (!Standing) return;
            endedAt = Time.time;
            World.Zones.Remove(this);
            if (World.Running)
            {
                World.Spawn(burstVfx, Centre + Vector3.up * radius * .4f, Quaternion.identity, tint, burstScale);
                World.Play(burstCue, Centre + Vector3.up);
            }
        }

        /// <summary>Move targets of fighters that move by script (the chest mimic) stay outside the film.</summary>
        public Vector3 KeepOut(Vector3 point, float bodyRadius)
        {
            if (!Standing) return point;
            return BubbleRules.KeepOut(point, bodyRadius, Centre, radius);
        }

        /// <summary>Other world fighters (chest mimics) caught inside are moved out like creatures.</summary>
        void KeepOthersOut(float dt)
        {
            var others = World.Wild.All;
            float r = CurrentRadius;
            for (int i = 0; i < others.Count; i++)
            {
                var other = others[i];
                if (other == null || other is WorldCreature || !other.Alive || !other.Root) continue;
                float need = BubbleRules.Overlap(other.Position, ChestMimic.BarrierRadius, 1, Centre, r, out var outward);
                if (need <= 0) continue;
                pushed++;
                other.Root.position += outward * BubbleRules.ExpelStep(need, snapDepth, expelSpeed, dt);
            }
        }

        /// <summary>A strike from outside at someone inside hits the film: ripple, jiggle, bump; no damage.</summary>
        public bool BlocksStrike(ICombatant attacker, ICombatant victim, Vector3 point)
        {
            if (!Standing || attacker == null || victim == null) return false;
            if (!BubbleRules.Shields(attacker.Position, victim.Position, Centre, radius)) return false;
            blocked++;
            Vector3 film = BubbleRules.FilmPoint(attacker.Position, Centre, radius, point.y);
            kick = Mathf.Max(kick, bumpKick * 1.6f);
            Ripple(film);
            World.Play(bumpCue, film, 1, Random.Range(.75f, .95f));
            return true;
        }

        public bool TryGetOutline(out Vector3 centre, out float outlineRadius)
        {
            centre = Centre;
            outlineRadius = radius;
            return Standing;
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.6f, .9f, 1f, .6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
