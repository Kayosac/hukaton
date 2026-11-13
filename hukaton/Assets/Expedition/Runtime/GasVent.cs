using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A gas vent of the rotten swamp (Kayospace «Биом: Гнилая топь», болотный газ): a mound of silt breathing marsh
    /// gas — small bubbles all the time, now and then a big belch you can see and hear. Fire or Lightning near it (a
    /// card, burning tar, a burning creature: whatever sets card zones alight) sets the gas off: a flash that throws
    /// everyone round it away (no harm done) and lights what burns nearby (tar, grass). Then the vent is spent and
    /// fills again. A card zone of the gas column over the vent, so the world's ignition finds it; placed in a chunk,
    /// the streamer hands it the outing (<see cref="CardZoneBehaviour.OnSpawned"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GasVent : CardZoneBehaviour, IIgnitable
    {
        [Header("Flash")]
        [Tooltip("Everyone this close to the vent is thrown, m.")]
        [Min(.5f)] public float flashRadius = 4.5f;
        [Tooltip("Throw speed right at the vent (x) and at the edge of the flash (y), m/s (design units, like Impulse).")]
        public Vector2 throwSpeed = new Vector2(11, 5);
        [Tooltip("How much of the throw goes up (0 flat, 1 straight up).")]
        [Range(0, 1)] public float lift = .55f;
        [Tooltip("Creatures are knocked off their feet this long, s.")]
        [Min(0)] public float creatureStagger = .8f;
        [Tooltip("The flash sets alight what burns this close (tar, grass, branches), m.")]
        [Min(0)] public float igniteRadius = 3.5f;
        [Tooltip("After a flash the vent fills again in this time, s; it does not flash meanwhile.")]
        [Min(0)] public float refillSeconds = 18;

        [Header("Breathing")]
        [Tooltip("A big belch of gas every this many seconds (random between), while full.")]
        public Vector2 belchEvery = new Vector2(4, 9);
        [Tooltip("Small bubbles all the time while the vent is full (Retro Arsenal bubbles).")]
        public ParticleSystem bubbles;
        public VfxBurst belch;
        public Color belchTint = new Color(.55f, .6f, .35f);
        public SfxCue belchSound;

        [Header("Flash look")]
        public VfxBurst flash;
        public Light flashLight;
        [Min(0)] public float flashLightIntensity = 6;
        [Min(.05f)] public float flashLightSeconds = .5f;
        public SfxCue flashSound;
        [Min(0)] public float shake = .3f;

        float fullAt, nextBelch, flashedAt = float.NegativeInfinity;

        public override CardZoneKind Kind => CardZoneKind.Other;
        /// <summary>Full of gas: it flashes at a spark.</summary>
        public bool Full => Time.time >= fullAt;

        public override void OnSpawned(ExpeditionWorld world)
        {
            base.OnSpawned(world);
            fullAt = 0;
            nextBelch = Time.time + Random.Range(belchEvery.x, belchEvery.y);
        }

        void Awake()
        {
            if (flashLight) flashLight.enabled = false;
        }

        void Update()
        {
            if (flashLight)
            {
                float k = 1 - (Time.time - flashedAt) / flashLightSeconds;
                flashLight.enabled = k > 0;
                if (k > 0) flashLight.intensity = flashLightIntensity * k * k;
            }
            if (bubbles)
            {
                var emission = bubbles.emission;
                emission.enabled = Full;
            }
            if (!World || !World.Running || !Full || Time.time < nextBelch) return;
            nextBelch = Time.time + Random.Range(belchEvery.x, belchEvery.y);
            World.Spawn(belch, transform.position + Vector3.up * .2f, Quaternion.identity, belchTint, 1);
            World.Play(belchSound, transform.position, .8f, Random.Range(.85f, 1.1f));
        }

        public bool Ignite(IgnitionSource source, Vector3 point)
        {
            if (!World || !World.Running || !Full) return false;
            Flash();
            return true;
        }

        /// <summary>The gas goes off: throws everyone near, lights what burns, empties the vent.</summary>
        public void Flash()
        {
            var world = World;
            if (!world) return;
            Vector3 at = transform.position + Vector3.up * .4f;
            fullAt = Time.time + refillSeconds;
            flashedAt = Time.time;
            world.Spawn(flash, at, Quaternion.identity, Color.white, 1.4f);
            world.Play(flashSound, at);

            var motor = world.motor;
            if (motor)
            {
                Vector3 centre = motor.transform.position + Vector3.up * .9f;
                float d = Vector3.Distance(centre, at);
                if (d < flashRadius)
                {
                    var push = Throw(at, centre, d);
                    motor.ApplyNormalizedImpulse(push);
                    world.OnPlayerLaunched(push);
                    if (world.feel) world.feel.AddTrauma(shake * (1 - d / flashRadius));
                }
            }
            var seen = new HashSet<Rigidbody>();
            foreach (var hit in Physics.OverlapSphere(at, flashRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.attachedRigidbody;
                if (!body || body.isKinematic || !seen.Add(body)) continue;
                float d = Vector3.Distance(body.worldCenterOfMass, at);
                CardPhysics.Push(body, Throw(at, body.worldCenterOfMass, d));
                var creature = body.GetComponent<WorldCreature>();
                if (creature) creature.Stagger(creatureStagger, at);
            }
            // What burns round the vent catches (tar, grass); never the vent itself.
            world.Zones.Ignite(at, at, igniteRadius, 0, IgnitionSource.Fire, this);
        }

        Vector3 Throw(Vector3 from, Vector3 to, float distance)
        {
            var away = to - from;
            away.y = 0;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            float speed = Mathf.Lerp(throwSpeed.x, throwSpeed.y, Mathf.Clamp01(distance / flashRadius));
            return (away * (1 - lift) + Vector3.up * lift).normalized * speed;
        }

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            Gizmos.color = new Color(1, .6f, .2f, .5f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * .4f, flashRadius);
        }
    }
}
