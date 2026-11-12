using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The gust of «Сквозняк» (prefab <c>Assets/Game/Prefabs/Cards/Draft/Draft gust.prefab</c>): Retro Arsenal wind
    /// streaks along the strip and, every tick while it blows, the wind's work on what is in the strip (see
    /// <see cref="DraftEffect"/>). The numbers come from the card asset. Removed by itself when the gust is over and the
    /// carried clouds have arrived (and by the end of the outing in any case).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DraftGust : MonoBehaviour, IOutingSpawned
    {
        [Header("Look")]
        [Tooltip("Wind streak systems (Retro Arsenal Wind). Each blows along its local +X; the emitter box is fitted to the strip.")]
        public ParticleSystem[] streams;
        [Min(0)] public float streaksPerSquareMetre = 1.2f;
        [Min(.1f)] public float streakHeight = 1.8f;
        [Tooltip("Size of a streak particle: its trail is this wide, m.")]
        [Min(.01f)] public float streakSize = .45f;
        [Tooltip("Streaks are born in this share of the strip from its start and fly on to its end.")]
        [Range(.1f, 1)] public float bornShare = .6f;
        [Tooltip("Speed of a streak along the wind (the Wind system's velocity), m/s: sets their lifetime.")]
        [Min(.1f)] public float streakSpeed = 7;
        [Tooltip("The gust object is removed at the latest this long after the wind stops, s.")]
        [Min(0)] public float lingerSeconds = 2.5f;

        struct Fire
        {
            public Vector3 point;
            public object owner;
        }

        struct Drift
        {
            public RainCloudZone rain;
            public Vector3 from, to;
            public float start;
        }

        static readonly List<ICombatant> Fighters = new List<ICombatant>();
        static readonly List<ICardZone> Zones = new List<ICardZone>();
        static readonly Collider[] Hits = new Collider[256];
        static readonly RaycastHit[] GroundHits = new RaycastHit[16];

        readonly List<Fire> fires = new List<Fire>();
        readonly List<Drift> drifts = new List<Drift>();
        readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();
        readonly HashSet<object> handled = new HashSet<object>();
        ExpeditionWorld world;
        DraftEffect numbers;
        DraftStrip strip;
        Color tint = Color.white;
        float born, nextTick;
        bool started, pushedPlayer;

        public DraftStrip Strip => strip;
        /// <summary>What this gust did so far (Play checks).</summary>
        public int Ignited { get; private set; }
        public int Dried { get; private set; }
        public int Pushed { get; private set; }
        public int FogsBlown { get; private set; }
        public int CloudsCarried { get; private set; }
        public int TarsBlownOut { get; private set; }
        public bool Blowing => numbers && Time.time - born <= numbers.gustSeconds;

        public void OnSpawned(ExpeditionWorld owner) => world = owner;

        /// <summary>Starts the gust; returns true when its first breath already changed something.</summary>
        public bool Setup(DraftEffect effect, DraftStrip windStrip, Color colour)
        {
            numbers = effect;
            strip = windStrip;
            tint = colour;
            born = Time.time;
            transform.SetPositionAndRotation(strip.Origin, strip.Rotation);
            PlayStreams();
            return world && Tick();
        }

        void Update()
        {
            if (!world || !numbers)
            {
                Destroy(gameObject);
                return;
            }
            float age = Time.time - born;
            if (age <= numbers.gustSeconds && Time.time >= nextTick) Tick();
            CarryClouds();
            if (age > numbers.gustSeconds && drifts.Count == 0 && (age > numbers.gustSeconds + lingerSeconds || !StreamsAlive())) Destroy(gameObject);
        }

        // ---------------- the wind's work ----------------

        bool Tick()
        {
            nextTick = Time.time + numbers.tickSeconds;
            int before = Ignited + Dried + Pushed + FogsBlown + CloudsCarried + TarsBlownOut;
            Zones.Clear();
            Zones.AddRange(world.Zones.All);
            // Tar burning when the wind comes: the wind blows its fire onward first, then the flames go out.
            List<TarPuddleZone> burningTar = null;
            if (!started)
            {
                foreach (var zone in Zones)
                    if (zone is TarPuddleZone tar && tar && tar.Burning && strip.Overlaps(tar.transform.position, tar.radius))
                        (burningTar ??= new List<TarPuddleZone>()).Add(tar);
            }
            CarryFire();
            if (burningTar != null)
                foreach (var tar in burningTar)
                    if (tar && tar.Extinguish()) TarsBlownOut++;
            started = true;
            BlowFogAndClouds();
            DryAndPush();
            return Ignited + Dried + Pushed + FogsBlown + CloudsCarried + TarsBlownOut > before;
        }

        void CarryFire()
        {
            var direction = strip.Direction;
            fires.Clear();
            Fighters.Clear();
            world.Combatants(Fighters);
            foreach (var fighter in Fighters)
                if (fighter is WorldCreature creature && creature.Burning && strip.Contains(creature.Position))
                    fires.Add(new Fire { point = creature.Position, owner = creature });
            foreach (var zone in Zones)
            {
                if (zone is TarPuddleZone tar && tar && tar.Burning && strip.Overlaps(tar.transform.position, tar.radius))
                    fires.Add(new Fire { point = tar.transform.position + direction * tar.radius, owner = tar });
                else if (zone is BranchBridge bridge && bridge && bridge.Burning)
                {
                    var bounds = bridge.Bounds;
                    if (!strip.Overlaps(bounds.center, Mathf.Max(bounds.extents.x, bounds.extents.z))) continue;
                    fires.Add(new Fire { point = bounds.ClosestPoint(bounds.center + direction * 1000), owner = bridge });
                }
            }
            if (fires.Count == 0) return;
            float reach = numbers.fireReach, spread = numbers.fireSpread;

            foreach (var fighter in Fighters)
            {
                if (!(fighter is WorldCreature creature) || !creature || !creature.Alive || creature.Burning || !strip.Contains(creature.Position)) continue;
                foreach (var fire in fires)
                {
                    if (ReferenceEquals(fire.owner, creature) || !DraftRules.Downwind(fire.point, creature.Position, direction, reach, spread)) continue;
                    creature.Damage(numbers.fireDamage, Vector3.zero, creature.Centre, numbers.fireBurnSeconds);
                    Sparkle(creature.Centre);
                    Ignited++;
                    break;
                }
            }

            foreach (var zone in Zones)
            {
                if (!(zone is IIgnitable ignitable) || zone is Object unityObject && !unityObject) continue;
                foreach (var fire in fires)
                {
                    if (ReferenceEquals(fire.owner, zone)) continue;
                    Vector3 point = zone is CardZoneBehaviour disc
                        ? DraftRules.NearestOnDisc(disc.transform.position, disc.radius, fire.point)
                        : zone.Bounds.ClosestPoint(fire.point);
                    if (!strip.Contains(point) || !DraftRules.Downwind(fire.point, point, direction, reach, spread)) continue;
                    if (!ignitable.Ignite(IgnitionSource.Fire, point)) continue;
                    Sparkle(point);
                    Ignited++;
                    break;
                }
            }
        }

        void BlowFogAndClouds()
        {
            foreach (var zone in Zones)
            {
                if (zone is FogZone fog && fog && fog.Active && !handled.Contains(fog) && strip.Overlaps(fog.transform.position, fog.radius))
                {
                    handled.Add(fog);
                    fog.Disperse(strip.Direction * numbers.fogDriftSpeed, numbers.fogDisperseSeconds);
                    FogsBlown++;
                }
                else if (zone is RainCloudZone rain && rain && rain.Raining && !handled.Contains(rain) && strip.Overlaps(rain.transform.position, rain.radius * .5f))
                {
                    handled.Add(rain);
                    var from = rain.transform.position;
                    drifts.Add(new Drift { rain = rain, from = from, to = GroundAt(strip.CarriedToEnd(from)), start = Time.time });
                    CloudsCarried++;
                }
            }
        }

        void DryAndPush()
        {
            foreach (var fighter in Fighters)
                if (fighter.Root && (strip.Contains(fighter.Position) || strip.Contains(fighter.Centre)) && Dry(fighter.Root)) Dried++;

            Vector3 push = (strip.Direction + Vector3.up * numbers.lift).normalized * numbers.pushImpulse;
            int count = Physics.OverlapBoxNonAlloc(strip.Centre, strip.HalfExtents, Hits, strip.Rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var body = Hits[i].attachedRigidbody;
                if (!body || body.isKinematic || !pushed.Add(body)) continue;
                if (Dry(body)) Dried++;
                if (numbers.pushImpulse <= 0) continue;
                var creature = body.GetComponent<WorldCreature>();
                if (creature && creature.Alive && numbers.creatureStagger > 0) creature.Stagger(numbers.creatureStagger, body.position - strip.Direction);
                CardPhysics.Push(body, push);
                Pushed++;
            }
            var motor = world.motor;
            if (!pushedPlayer && motor && numbers.pushImpulse > 0 && strip.Contains(motor.transform.position))
            {
                pushedPlayer = true;
                motor.ApplyNormalizedImpulse(push);
                Pushed++;
            }
        }

        static bool Dry(Component body)
        {
            var status = body ? body.GetComponentInParent<WetStatus>() : null;
            if (!status || !status.Wet) return false;
            status.Dry();
            return true;
        }

        void Sparkle(Vector3 point)
        {
            world.Spawn(numbers.igniteSpark, point + Vector3.up * .2f, Quaternion.identity, new Color(1, .55f, .2f), numbers.igniteSparkScale);
            world.Play(numbers.igniteSound, point);
        }

        // ---------------- carried rain clouds ----------------

        void CarryClouds()
        {
            for (int i = drifts.Count - 1; i >= 0; i--)
            {
                var drift = drifts[i];
                if (!drift.rain)
                {
                    drifts.RemoveAt(i);
                    continue;
                }
                float t = Mathf.Clamp01((Time.time - drift.start) / numbers.rainCarrySeconds);
                var position = Vector3.Lerp(drift.from, drift.to, Mathf.SmoothStep(0, 1, t));
                drift.rain.transform.position = position;
                if (drift.rain.Raining && world.sfx && drift.rain.rainLoop) world.sfx.Loop(drift.rain, drift.rain.rainLoop, position + Vector3.up * 1.5f);
                if (t >= 1) drifts.RemoveAt(i);
            }
        }

        /// <summary>The ground under a point (not a body standing there); the point itself when there is none.</summary>
        static Vector3 GroundAt(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 8, Vector3.down, GroundHits, 24, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            var ground = point;
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider.attachedRigidbody || hit.distance >= best) continue;
                best = hit.distance;
                ground = hit.point;
            }
            return ground;
        }

        // ---------------- look ----------------

        void PlayStreams()
        {
            if (streams == null) return;
            float length = strip.Length, width = strip.HalfWidth * 2, seconds = numbers.gustSeconds;
            float flight = length * (1 - bornShare * .5f) / streakSpeed;
            var colour = Color.Lerp(tint, Color.white, .55f);
            foreach (var stream in streams)
            {
                if (!stream) continue;
                stream.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = stream.main;
                main.duration = Mathf.Max(.05f, seconds);
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(flight * .6f, flight);
                main.startColor = new ParticleSystem.MinMaxGradient(colour, Color.white);
                main.startSize = new ParticleSystem.MinMaxCurve(streakSize * .6f, streakSize);
                var shape = stream.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.rotation = Vector3.zero;
                shape.position = new Vector3(length * bornShare * .5f, streakHeight * .5f, 0);
                shape.scale = new Vector3(length * bornShare, streakHeight, width);
                var emission = stream.emission;
                emission.rateOverTime = streaksPerSquareMetre * width * length / Mathf.Max(.05f, seconds);
                stream.Play(false);
            }
        }

        bool StreamsAlive()
        {
            if (streams == null) return false;
            foreach (var stream in streams)
                if (stream && stream.IsAlive(false)) return true;
            return false;
        }

        void OnDrawGizmosSelected()
        {
            if (!numbers) return;
            Gizmos.color = new Color(.7f, .9f, 1, .5f);
            Gizmos.matrix = Matrix4x4.TRS(strip.Centre, strip.Rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, strip.HalfExtents * 2);
        }
    }
}
