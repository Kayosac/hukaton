using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>Numbers of one scarecrow, from the card asset at the copy's level.</summary>
    public struct ScarecrowNumbers
    {
        public int Health;
        public float Lifetime, DrawRadius, BurnSeconds, BurstRadius, BurstBurnSeconds;
        public int BurstDamage;
    }

    /// <summary>
    /// «Чучело»: the thrown card itself, stuck upright in the ground and grown to about 1.7 m — a crooked standing card
    /// with its own illustration, swaying (prefab <c>Assets/Game/Prefabs/Cards/Scarecrow/Scarecrow.prefab</c>).
    /// A fighter of the player's side (<see cref="Faction.Player"/>, in <see cref="ExpeditionWorld.Allies"/>, like the
    /// awakened tree): aggressive and territorial creatures within 12 m that go at the player turn on it instead
    /// (<see cref="WorldCreature.SetTarget"/>), and are let go when they calm down or it is gone. Creatures' strikes,
    /// their projectiles, chain lightning and burning tar hurt it through <see cref="TakeDamage"/>; the card itself is
    /// its collider. Stands for 25 s (then sinks back into the ground) or until destroyed. Fire (a Fire card, lightning,
    /// burning tar, a burning creature touching it) sets it alight: it burns 4 s and breaks. Level III flares up when
    /// destroyed: creatures within 2.5 m catch fire. Outlined on the ground (its 12 m reach) while it stands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScarecrowDecoy : CardZoneBehaviour, ICombatant, IIgnitable, ICardAreaOutline
    {
        [Header("Authored parts")]
        [Tooltip("Pivot on the ground: lean, sway and growth turn and scale the card and its collider around it.")]
        public Transform sway;
        [Tooltip("The standing card (Hakaton/Card on a bent quad): shows the thrown card's faces.")]
        public CardView view;
        [Tooltip("The card's own collider: what creatures hit and cards bounce off.")]
        public Collider body;
        [Tooltip("Health pips above the card.")]
        public CreatureOverhead overhead;

        [Header("Look")]
        [Tooltip("Height of the grown card, m (its collider and the outline follow).")]
        [Min(.2f)] public float cardHeight = 1.7f;
        [Tooltip("Shoots up out of the ground this fast, s; starts at this share of its size (the thrown card).")]
        [Min(0)] public float growSeconds = .55f;
        [Range(.01f, 1)] public float startScale = .14f;
        [Tooltip("Crooked: a random lean on landing, degrees (min, max).")]
        public Vector2 leanDegrees = new Vector2(3, 9);
        [Tooltip("Idle sway, degrees, and its frequency, Hz.")]
        [Min(0)] public float swayDegrees = 3f, swayFrequency = .45f;
        [Tooltip("Jolt of a hit (spring velocity, degrees per second).")]
        [Min(0)] public float hurtKick = 160;
        [Min(0)] public float sinkSeconds = .6f;
        [Tooltip("The card chars toward this colour while it burns.")]
        public Color burntTint = new Color(.28f, .17f, .1f, 1);
        public Color hitFlash = new Color(1f, .45f, .4f, 1);

        [Header("Effects (Retro Arsenal variants)")]
        public VfxBurst riseVfx;
        public VfxBurst hitVfx;
        [Tooltip("Flames riding on the card while it burns (held for the burn).")]
        public VfxBurst flamesVfx;
        public VfxBurst breakVfx;
        [Tooltip("Level III: the flare when it is destroyed.")]
        public VfxBurst burstVfx;
        public VfxBurst sinkVfx;
        [Min(0)] public float riseScale = 1.2f, hitScale = .9f, flamesScale = 1.5f, breakScale = 2.4f, burstScale = 1.1f, sinkScale = 1.2f;
        public Color fireTint = new Color(1f, .55f, .2f, 1);
        public Color dustTint = new Color(.75f, .66f, .52f, 1);

        [Header("Sounds")]
        public SfxCue riseCue;
        public SfxCue hitCue, igniteCue, breakCue, burstCue, sinkCue;

        [Header("Drawing creatures")]
        [Tooltip("How often creatures around are checked, s.")]
        [Min(.05f)] public float thinkInterval = .25f;
        [Tooltip("A creature dragged farther than the reach × this lets go of the scarecrow.")]
        [Min(1)] public float releaseFactor = 1.3f;

        ScarecrowNumbers numbers = new ScarecrowNumbers { Health = 6, Lifetime = 25, DrawRadius = 12, BurnSeconds = 4 };
        Color accent = Color.white;
        Quaternion lean = Quaternion.identity;
        SpringFloat wobble;
        float age, nextThink, burnLeft, flash, swayPhase, goneAt;
        bool burning, broken, sinking;
        VfxBurst flames;

        public int Health { get; private set; } = 6;
        public int MaximumHealth => numbers.Health;
        public bool Burning => burning && !broken;
        /// <summary>Seconds it still stands (0 once broken or sinking).</summary>
        public float Remaining => Alive ? Mathf.Max(0, numbers.Lifetime - age) : 0;
        public float DrawRadius => numbers.DrawRadius;

        // ---------------- zone ----------------

        public override CardZoneKind Kind => CardZoneKind.Other;

        /// <summary>The card itself: burning bodies that touch it and Fire hits near it set it alight.</summary>
        public override Bounds Bounds => body && body.enabled ? body.bounds : new Bounds(transform.position + Vector3.up * cardHeight * .5f, new Vector3(.4f, cardHeight, .4f));

        public override bool Contains(Vector3 point) => Bounds.Contains(point);

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            world.Allies.Add(this);
        }

        /// <summary>Right after spawning: the card's faces and the numbers of its level.</summary>
        public void Setup(Texture front, Texture back, int level, Color cardAccent, ScarecrowNumbers values)
        {
            numbers = values;
            numbers.Health = Mathf.Max(1, values.Health);
            Health = numbers.Health;
            accent = cardAccent;
            if (view) view.Show(front, back ? back : front, level);
            float tilt = Random.Range(Mathf.Min(leanDegrees.x, leanDegrees.y), Mathf.Max(leanDegrees.x, leanDegrees.y));
            lean = Quaternion.AngleAxis(tilt, Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward)
                * Quaternion.Euler(0, Random.Range(-12f, 12f), 0);
            swayPhase = Random.Range(0, 10f);
            age = 0;
            Present(0);
            if (World)
            {
                World.Spawn(riseVfx, transform.position + Vector3.up * .05f, Quaternion.identity, dustTint, riseScale);
                World.Play(riseCue, transform.position);
                if (World.feel && Vector3.Distance(World.motor.transform.position, transform.position) < 14) World.feel.AddTrauma(.12f);
            }
        }

        // ---------------- ICombatant ----------------

        public Faction Faction => Faction.Player;
        public bool Alive => Health > 0 && !broken && !sinking;
        public Vector3 Position => transform.position;
        public Vector3 Centre => transform.position + Vector3.up * cardHeight * .5f * (sway ? sway.localScale.y : 1);
        public Transform Root => transform;

        /// <summary>
        /// A creature's strike, a projectile, chain lightning, burning tar. Rooted in the ground: no push. Fire and
        /// lightning set it alight as well.
        /// </summary>
        public bool TakeDamage(in DamageInfo damage)
        {
            if (!Alive || !Running) return false;
            if (damage.Kind == DamageKind.Fire) Ignite(IgnitionSource.Fire, damage.Point);
            else if (damage.Kind == DamageKind.Lightning) Ignite(IgnitionSource.Lightning, damage.Point);
            int amount = Mathf.Max(0, damage.Amount);
            if (amount > 0)
            {
                Health = ScarecrowRules.AfterHit(Health, amount);
                flash = 1;
                Vector3 from = damage.Point - transform.position;
                wobble.Kick(hurtKick * (Vector3.Dot(from, transform.right) >= 0 ? 1 : -1));
                World.Spawn(hitVfx, damage.Point, Quaternion.identity, accent, hitScale);
                World.Play(hitCue, damage.Point, 1, Random.Range(.85f, 1.05f));
                if (overhead) overhead.ShowHealth(Health, numbers.Health);
            }
            if (Health == 0) Break(true);
            return true;
        }

        // ---------------- IIgnitable ----------------

        /// <summary>Paper on a stick: any fire catches. Burns <see cref="ScarecrowNumbers.BurnSeconds"/> and breaks.</summary>
        public bool Ignite(IgnitionSource source, Vector3 point)
        {
            if (!Alive || burning || !Running) return false;
            burning = true;
            burnLeft = numbers.BurnSeconds;
            flames = World.Spawn(flamesVfx, Centre, Quaternion.identity, fireTint, flamesScale);
            if (flames)
            {
                flames.Follow(transform, Vector3.up * cardHeight * .4f);
                flames.HoldFor(numbers.BurnSeconds + .5f);
            }
            World.Play(igniteCue, Centre);
            return true;
        }

        // ---------------- outline ----------------

        public bool TryGetOutline(out Vector3 centre, out float radius)
        {
            centre = transform.position;
            radius = numbers.DrawRadius;
            return Alive;
        }

        // ---------------- life ----------------

        bool Running => World && World.Running;

        void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            age += dt;
            if (sinking)
            {
                float k = sinkSeconds > 0 ? Mathf.Clamp01((Time.time - goneAt) / sinkSeconds) : 1;
                Present(dt, 1 - Ease.InCubic(k), k * cardHeight * .3f);
                if (k >= 1) Destroy(gameObject);
                return;
            }
            if (broken) return;

            if (burning)
            {
                burnLeft -= dt;
                float charred = numbers.BurnSeconds > 0 ? 1 - Mathf.Clamp01(burnLeft / numbers.BurnSeconds) : 1;
                if (view) view.SetTint(Color.Lerp(Color.white, burntTint, charred));
                if (burnLeft <= 0) { Break(true); return; }
            }
            if (age >= numbers.Lifetime) { Sink(); return; }

            if (Time.time >= nextThink)
            {
                nextThink = Time.time + thinkInterval;
                DrawCreatures();
            }
            Present(dt);
        }

        /// <summary>Lean, sway, the jolt of hits, growth and the flash of a hit.</summary>
        void Present(float dt, float shrink = 1, float sink = 0)
        {
            if (!sway) return;
            float grow = ScarecrowRules.Growth(age, growSeconds, startScale) * shrink;
            float w = wobble.Step(0, 7, .22f, dt);
            float t = Time.time * swayFrequency * Mathf.PI * 2 + swayPhase;
            float burnShiver = burning ? Mathf.Sin(Time.time * 31) * 1.5f : 0;
            var swing = Quaternion.Euler(Mathf.Sin(t * .77f) * swayDegrees * .6f, 0, Mathf.Sin(t) * swayDegrees + w * .12f + burnShiver);
            sway.localRotation = lean * swing;
            sway.localScale = Vector3.one * Mathf.Max(.001f, grow);
            sway.localPosition = Vector3.down * sink;
            if (view && flash > 0)
            {
                flash = Mathf.Max(0, flash - dt * 6);
                view.SetFlash(flash * .8f, hitFlash);
            }
        }

        /// <summary>Aggressive and territorial creatures that go at the player turn on the scarecrow; calm ones are let go.</summary>
        void DrawCreatures()
        {
            var creatures = World.ActiveCreatures;
            if (creatures == null) return;
            Vector3 here = transform.position;
            var player = World.Player;
            foreach (var creature in creatures)
            {
                if (!creature || !creature.isActiveAndEnabled || !creature.Alive) continue;
                Vector3 to = creature.Position - here;
                to.y = 0;
                float distance = to.magnitude;
                var target = creature.Target;
                if (ReferenceEquals(target, this))
                {
                    if (ScarecrowRules.Releases(creature.State, distance, numbers.DrawRadius, releaseFactor)) creature.SetTarget(null);
                    continue;
                }
                if (ScarecrowRules.Draws(creature.temperament, creature.State, ReferenceEquals(target, player), creature.Brawling, distance, numbers.DrawRadius))
                    creature.SetTarget(this);
            }
        }

        void ReleaseCreatures()
        {
            var creatures = World ? World.ActiveCreatures : null;
            if (creatures == null) return;
            foreach (var creature in creatures)
                if (creature && ReferenceEquals(creature.Target, this)) creature.SetTarget(null);
        }

        /// <summary>Destroyed (health gone or burnt): torn paper, and on level III a flare that sets creatures nearby alight.</summary>
        void Break(bool destroyed)
        {
            if (broken || sinking) return;
            broken = true;
            Health = 0;
            ReleaseCreatures();
            if (overhead) overhead.Clear();
            if (body) body.enabled = false;
            Vector3 centre = transform.position + Vector3.up * cardHeight * .5f;
            if (flames) { flames.Release(.4f); flames = null; }
            if (view) view.Hide();
            World.Spawn(breakVfx, centre, Quaternion.identity, burning ? fireTint : accent, breakScale);
            World.Play(breakCue, centre);
            if (World.feel && Vector3.Distance(World.motor.transform.position, centre) < 14) World.feel.AddTrauma(.15f);
            if (ScarecrowRules.Flares(numbers.BurstRadius, destroyed)) Flare(centre);
            Destroy(gameObject, 1.5f);
        }

        /// <summary>Level III: the scarecrow flares up; every creature within the flare catches fire.</summary>
        void Flare(Vector3 centre)
        {
            World.Spawn(burstVfx, transform.position + Vector3.up * .3f, Quaternion.identity, fireTint, burstScale);
            World.Play(burstCue, centre);
            Vector3 here = transform.position;
            var creatures = World.ActiveCreatures;
            if (creatures != null)
                foreach (var creature in creatures)
                {
                    if (!creature || !creature.isActiveAndEnabled || !creature.Alive) continue;
                    if (!ScarecrowRules.CaughtByFlare(here, creature.Position, creature.CapsuleRadius, numbers.BurstRadius)) continue;
                    Vector3 away = creature.Position - here;
                    away.y = 0;
                    Vector3 push = (away.sqrMagnitude > .01f ? away.normalized : Vector3.zero) * 2 + Vector3.up;
                    creature.Damage(numbers.BurstDamage, push, creature.Centre, numbers.BurstBurnSeconds, DamageKind.Fire);
                }
            foreach (var other in World.Wild.All)
            {
                if (!other.Alive || !ScarecrowRules.CaughtByFlare(here, other.Position, .4f, numbers.BurstRadius)) continue;
                other.TakeDamage(new DamageInfo(numbers.BurstDamage, Vector3.up, other.Centre, here, DamageKind.Fire));
            }
            // Tar and other card things that burn catch from the flare too.
            World.Ignite(null, here + Vector3.up * .3f, numbers.BurstRadius, IgnitionSource.Fire);
        }

        /// <summary>Its time is up: it sinks back into the ground (no flare).</summary>
        void Sink()
        {
            if (broken || sinking) return;
            sinking = true;
            goneAt = Time.time;
            ReleaseCreatures();
            if (overhead) overhead.Clear();
            if (body) body.enabled = false;
            if (flames) { flames.Release(.3f); flames = null; }
            World.Spawn(sinkVfx, transform.position + Vector3.up * .05f, Quaternion.identity, dustTint, sinkScale);
            World.Play(sinkCue, transform.position);
        }
    }
}
