using System.Collections.Generic;
using Hakaton.Character;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// A tar puddle left by «Лужа смолы» (prefab <c>Assets/Game/Prefabs/Cards/TarPuddle/Tar puddle.prefab</c>), alive
    /// until the end of the outing. Unlit tar slows every fighter standing on it (player, creatures, allies through
    /// <see cref="IMoveSpeedScalable"/>). Fire or Lightning (<see cref="IIgnitable"/>) lights the whole puddle: it burns
    /// for the card's time, hurting everyone in the fire by the time spent there (<see cref="TarFireExposure"/>),
    /// without slowing, then burns out and disappears. Rain (<see cref="IWashable"/>) removes it whole, lit or not.
    /// The rules live in <see cref="TarPuddleState"/>; numbers come from the card asset through <see cref="Setup"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TarPuddleZone : CardZoneBehaviour, IIgnitable, IWashable
    {
        [Header("Tar")]
        [Tooltip("The fire reaches this high above the tar: a fighter whose feet are lower burns.")]
        [Min(.1f)] public float fireHeight = 1.1f;
        [Tooltip("A fire or lightning hit this far past the edge still lights the tar, m.")]
        [Min(0)] public float igniteMargin = .4f;
        [Tooltip("The slow is renewed every frame for this long, so it ends this soon after stepping off, s.")]
        [Min(.02f)] public float slowRefreshSeconds = .15f;

        [Header("Tarred creatures")]
        [Tooltip("A creature without its own fire vulnerability that is set alight while in this tar (on it unlit, or in its fire) burns like a fire-vulnerable one until that burn ends, by these rules (the shared Fire vulnerability asset): multipliers, flames placed as the creature sets them, setting things alight by touch. Empty: tar does not make anyone vulnerable.")]
        public FireVulnerabilitySettings tarredFire;

        [Header("Look")]
        [Tooltip("The crooked tar blob: a unit-radius mesh scaled to the puddle.")]
        public Transform blob;
        public Renderer blobRenderer;
        [Min(.01f)] public float blobThickness = .16f;
        [Tooltip("Slow bubbles in unlit tar (Retro Arsenal), stopped by the fire.")]
        public ParticleSystem bubbles;
        [Tooltip("Retro Arsenal fire field, started when the tar catches fire.")]
        public ParticleSystem fire;
        [Min(0)] public float fireRatePerSquareMetre = 6;
        public Light fireLight;
        [Min(0)] public float fireLightIntensity = 3;
        public Color tarColour = new Color(.06f, .035f, .05f), charColour = new Color(.12f, .08f, .06f);
        [ColorUsage(false, true)] public Color emberColour = new Color(1.6f, .45f, .08f);
        [Min(0)] public float spreadSeconds = .5f, fadeSeconds = 1.2f;
        [Tooltip("Slow breathing of the viscous blob (fraction of its size).")]
        [Min(0)] public float wobble = .04f;
        [Min(0)] public float wobbleFrequency = .45f;

        [Header("Feedback")]
        [Tooltip("Dark splash where the tar lands.")]
        public VfxBurst landSplash;
        public VfxBurst igniteBurst, washPuff, burnOutPuff;
        public SfxCue igniteSound, crackleLoop, washSound, burnOutSound;

        static readonly List<ICombatant> Fighters = new List<ICombatant>();
        static readonly List<ICardZone> Nearby = new List<ICardZone>();
        static readonly Dictionary<ExpeditionWorld, TarFireExposure> Exposures = new Dictionary<ExpeditionWorld, TarFireExposure>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");

        TarPuddleState state = new TarPuddleState(30);
        float speedMultiplier = .5f, damagePerSecond = 1;
        float born, endedAt = -1, phase, layerHeight, layerDepth;
        bool registeredGone;
        MaterialPropertyBlock block;

        public override CardZoneKind Kind => CardZoneKind.TarPuddle;
        public TarPhase Phase => state.Phase;
        public bool Burning => state.Burns;
        public float BurnRemaining => state.BurnRemaining;
        public float SpeedMultiplier => speedMultiplier;
        public float DamagePerSecond => damagePerSecond;

        /// <summary>The fire bookkeeping of an outing, shared by all its puddles (overlaps never stack).</summary>
        public static TarFireExposure ExposureFor(ExpeditionWorld world)
        {
            if (!world) return null;
            if (!Exposures.TryGetValue(world, out var exposure))
            {
                Exposures[world] = exposure = new TarFireExposure();
                world.OutingCleared += exposure.Clear;
            }
            return exposure;
        }

        /// <summary>Numbers from the card asset, right after spawning.</summary>
        public void Setup(float puddleRadius, float slowMultiplier, float burnSeconds, float burnDamagePerSecond)
        {
            radius = Mathf.Max(.05f, puddleRadius);
            speedMultiplier = Mathf.Max(0, slowMultiplier);
            damagePerSecond = Mathf.Max(0, burnDamagePerSecond);
            state = new TarPuddleState(burnSeconds);
            // On a slope the vertical zone must still cover the whole tilted puddle.
            float tilt = Mathf.Min(Vector3.Angle(Vector3.up, transform.up), 60) * Mathf.Deg2Rad;
            float rise = radius * Mathf.Tan(tilt);
            height = layerHeight + rise;
            depth = layerDepth + rise;
            if (bubbles)
            {
                var shape = bubbles.shape;
                shape.radius = radius * .75f;
            }
            if (World) World.Spawn(landSplash, transform.position + Vector3.up * .05f, Quaternion.identity, tarColour, Mathf.Max(.6f, radius * .6f));
            Animate();
        }

        void Awake()
        {
            block = new MaterialPropertyBlock();
            phase = Random.value * 10;
            layerHeight = height;
            layerDepth = depth;
            if (fire) fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (fireLight) fireLight.enabled = false;
        }

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            born = Time.time;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (World && World.sfx) World.sfx.StopLoop(this);
        }

        void Update()
        {
            if (!World) return;
            if (state.Gone)
            {
                FadeAway();
                return;
            }
            if (state.Slows) SlowEveryoneOnIt();
            else if (state.Burns)
            {
                BurnEveryoneInIt(Time.deltaTime);
                if (state.Advance(Time.deltaTime)) End(burnOutPuff, burnOutSound);
            }
            Animate();
        }

        // ---------- rules ----------

        bool Inside(Vector3 feet, float top)
        {
            Vector3 o = transform.position;
            float dx = feet.x - o.x, dz = feet.z - o.z;
            return dx * dx + dz * dz <= radius * radius && feet.y >= o.y - depth && feet.y <= o.y + top;
        }

        /// <summary>Standing on unlit tar (feet within the puddle's thin layer; a jump leaves it).</summary>
        public bool OnTar(Vector3 feet) => Inside(feet, height);

        /// <summary>Standing in the fire.</summary>
        public bool InFire(Vector3 feet) => Inside(feet, fireHeight + height - layerHeight);

        /// <summary>
        /// In this tar: on it while unlit, in its fire while burning (the tar lit under a creature still holds it).
        /// Burnt-out or washed tar holds nobody.
        /// </summary>
        public bool Holds(Vector3 feet) => state.Slows ? OnTar(feet) : state.Burns && InFire(feet);

        void SlowEveryoneOnIt()
        {
            Fighters.Clear();
            World.Combatants(Fighters);
            foreach (var fighter in Fighters)
            {
                if (!OnTar(fighter.Position)) continue;
                if (ReferenceEquals(fighter, World.Player))
                {
                    if (World.motor) World.motor.ApplyModifier(MotorModifier.MoveSpeed, speedMultiplier, slowRefreshSeconds);
                }
                else if (fighter is WorldCreature creature) creature.ApplyMoveSpeedScale(speedMultiplier, slowRefreshSeconds);
                else
                {
                    var scalable = fighter as IMoveSpeedScalable ?? (fighter.Root ? fighter.Root.GetComponentInParent<IMoveSpeedScalable>() : null);
                    scalable?.ApplyMoveSpeedScale(speedMultiplier, slowRefreshSeconds);
                }
            }
        }

        void BurnEveryoneInIt(float dt)
        {
            var exposure = ExposureFor(World);
            Fighters.Clear();
            World.Combatants(Fighters);
            foreach (var fighter in Fighters)
            {
                if (!InFire(fighter.Position)) continue;
                int due = exposure.Expose(fighter, Time.frameCount, dt * damagePerSecond);
                if (due > 0) fighter.TakeDamage(new DamageInfo(due, Vector3.zero, fighter.Centre, transform.position, DamageKind.Fire));
            }
        }

        /// <summary>Under a rain that is still falling (any new fire there goes out at once).</summary>
        public bool UnderRain()
        {
            Nearby.Clear();
            World.Zones.Overlapping(Bounds, Nearby, CardZoneKind.Rain);
            foreach (var zone in Nearby)
                if (zone is RainCloudZone rain && rain.Raining && rain.Reaches(this)) return true;
            return false;
        }

        // ---------- reactions ----------

        public bool Ignite(IgnitionSource source, Vector3 point)
        {
            if (!World || !state.Slows) return false;
            Vector3 o = transform.position;
            float dx = point.x - o.x, dz = point.z - o.z, reach = radius + igniteMargin;
            if (dx * dx + dz * dz > reach * reach || Mathf.Abs(point.y - o.y) > fireHeight + 1) return false;
            // Rain puts any new fire out at once; its next tick washes the tar away.
            if (UnderRain()) return false;
            if (!state.Ignite()) return false;
            StartFire();
            return true;
        }

        public bool Wash(ICardZone rain)
        {
            if (!World || !state.Wash()) return false;
            End(washPuff, washSound);
            return true;
        }

        // ---------- presentation ----------

        void StartFire()
        {
            if (fire)
            {
                var shape = fire.shape;
                shape.radius = radius * .85f;
                var emission = fire.emission;
                emission.rateOverTime = fireRatePerSquareMetre * Mathf.PI * radius * radius;
                fire.Play(true);
            }
            if (bubbles) bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (fireLight) fireLight.enabled = true;
            World.Spawn(igniteBurst, transform.position + Vector3.up * .15f, Quaternion.identity, new Color(1f, .55f, .2f), Mathf.Max(.7f, radius * .5f));
            World.Play(igniteSound, transform.position);
            if (World.sfx && crackleLoop) World.sfx.Loop(this, crackleLoop, transform.position);
        }

        void End(VfxBurst puff, SfxCue sound)
        {
            endedAt = Time.time;
            if (!registeredGone) { registeredGone = true; World.Zones.Remove(this); }
            if (fire) fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (bubbles) bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (World.sfx) World.sfx.StopLoop(this);
            World.Spawn(puff, transform.position + Vector3.up * .2f, Quaternion.identity, Color.white, Mathf.Max(.7f, radius * .55f));
            World.Play(sound, transform.position);
        }

        void FadeAway()
        {
            float k = fadeSeconds > 0 ? Mathf.Clamp01((Time.time - endedAt) / fadeSeconds) : 1;
            if (blob)
            {
                float burnt = state.Phase == TarPhase.BurntOut ? 1 : 0;
                float xz = radius * Mathf.Lerp(1, .82f, burnt) * (1 - Ease.InCubic(k) * .9f);
                blob.localScale = new Vector3(xz, blobThickness * Mathf.Lerp(1, .5f, burnt) * (1 - k), xz);
            }
            if (fireLight) fireLight.intensity = fireLightIntensity * (1 - k);
            Tint(state.Phase == TarPhase.BurntOut ? charColour : tarColour, Color.black);
            if (k >= 1 && (!fire || !fire.IsAlive(true))) Destroy(gameObject);
        }

        void Animate()
        {
            if (!blob) return;
            float age = Time.time - born;
            float spread = spreadSeconds > 0 ? Ease.OutCubic(age / spreadSeconds) : 1;
            float w = Mathf.Sin((Time.time + phase) * wobbleFrequency * Mathf.PI * 2) * wobble;
            float w2 = Mathf.Sin((Time.time * 1.7f + phase * 3) * wobbleFrequency * Mathf.PI * 2) * wobble * .5f;
            float burnt = state.BurnProgress;
            float xz = radius * Mathf.Lerp(.25f, 1, spread) * Mathf.Lerp(1, .82f, burnt);
            float y = blobThickness * Mathf.Lerp(1, .5f, burnt) * (1 - w * 1.5f);
            blob.localScale = new Vector3(xz * (1 + w), y, xz * (1 + w2));
            if (state.Burns)
            {
                float flicker = .75f + .25f * Mathf.PerlinNoise(Time.time * 6, phase);
                Tint(Color.Lerp(tarColour, charColour, burnt), emberColour * flicker * Mathf.Lerp(1, .35f, burnt));
                if (fireLight) fireLight.intensity = fireLightIntensity * flicker * Mathf.Lerp(1, .6f, burnt);
            }
            else Tint(tarColour, Color.black);
        }

        void Tint(Color baseColour, Color emission)
        {
            if (!blobRenderer) return;
            // Survives a script reload in Play (Awake does not run again).
            if (block == null) block = new MaterialPropertyBlock();
            blobRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, baseColour);
            block.SetColor(EmissionId, emission);
            blobRenderer.SetPropertyBlock(block);
        }

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            Gizmos.color = new Color(1f, .5f, .1f, .5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
