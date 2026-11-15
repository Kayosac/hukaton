using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A loose physical object (crate, log, pot, boulder) that returns to its authored pose at the start of each outing
    /// and knocks when it hits something (<see cref="PropImpacts"/>).
    /// A <see cref="dormant"/> one stands as still scenery — a kinematic body, drawn inside its chunk's merged mesh —
    /// until something touches it (<see cref="Wake"/>): then it is cut out of the merged mesh and becomes a real body.
    /// Once it has come to rest it is still again (<see cref="Rest"/>); only so many are awake at a time
    /// (<see cref="PropPhysics.MaxAwake"/>).
    /// </summary>
    public sealed class PhysicsProp : MonoBehaviour
    {
        public Rigidbody body;
        [Tooltip("What it is made of: picks the knock when it hits something.")]
        public PropSurface surface;
        [Tooltip("Still scenery (a kinematic body, drawn merged with its chunk) until something touches it.")]
        public bool dormant;

        /// <summary>It has been woken at least once: drawn by its own renderers, maybe not where it was made.</summary>
        public bool Stirred { get; private set; }
        /// <summary>Seconds it has lain still while awake (PropPhysics counts them to put it to rest).</summary>
        public float StillFor { get; set; }

        Vector3 start;
        Quaternion rotation;
        float quietUntil, thrownUntil, lastHurtAt;
        WorldCreature lastHurt;

        /// <summary>A thing thrown by a card hurts a creature it hits at least this fast, m/s.</summary>
        public const float HurtSpeed = 3.5f;

        /// <summary>Damage of a thrown thing hitting a creature: a pot 1, a crate about 3, a boulder up to 6.</summary>
        public static int ThrownDamage(float mass, float speed) => Mathf.Clamp(Mathf.RoundToInt(speed * Mathf.Sqrt(mass) / 3), 1, 6);

        /// <summary>A card has thrown it: for a while it hurts a creature it flies into.</summary>
        public void Thrown() => thrownUntil = Time.time + 2.5f;

        /// <summary>Flying from a card's throw (it hurts what it hits).</summary>
        public bool InFlight => Time.time < thrownUntil;
        Collider[] colliders;

        void Awake() { start = transform.position; rotation = transform.rotation; }

        // Appearing with its chunk it settles on the ground: that is not a knock.
        void OnEnable() => quietUntil = Time.time + 1;

        public void ResetForOuting()
        {
            if (TryGetComponent(out Breakable breakable)) breakable.Restore();
            // A ready-made place starts every outing as it was made: dormant things asleep again where they were.
            if (dormant) Rest();
            if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = start;
            body.rotation = rotation;
        }

        /// <summary>
        /// Something is about to move it. True when the body can take the push: it was awake already, or it was dormant
        /// and has just been made a real body. False for a body that is fixed for another reason (burnt to ash, held),
        /// or when too many are awake already.
        /// </summary>
        public bool Wake()
        {
            if (!body) return false;
            if (!body.isKinematic) { body.WakeUp(); return true; }
            if (!dormant || !isActiveAndEnabled) return false;
            colliders ??= GetComponentsInChildren<Collider>();
            // Burnt to ash: its colliders are off, there is nothing left to push.
            bool solid = false;
            foreach (var c in colliders) if (c && c.enabled) { solid = true; break; }
            if (!solid || !PropPhysics.Admit(this)) return false;
            Stir();
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = body.mass < 1 ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
            // Set a little into the ground as scenery: it eases out instead of jumping out.
            body.maxDepenetrationVelocity = 2;
            body.WakeUp();
            StillFor = 0;
            WakeResting(transform);
            return true;
        }

        static readonly Collider[] Above = new Collider[24];

        /// <summary>
        /// Dormant props lying on <paramref name="under"/> (a pile, a pot on a crate) wake with it: left asleep they
        /// would hang in the air when it goes.
        /// </summary>
        public static void WakeResting(Transform under)
        {
            Bounds b = default;
            bool any = false;
            foreach (var c in under.GetComponentsInChildren<Collider>())
                if (c && c.enabled && !c.isTrigger) { if (any) b.Encapsulate(c.bounds); else { b = c.bounds; any = true; } }
            if (!any) return;
            var centre = new Vector3(b.center.x, b.max.y, b.center.z);
            int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(b.extents.x + .05f, .12f, b.extents.z + .05f), Above, Quaternion.identity, ~(1 << 4), QueryTriggerInteraction.Ignore);
            // Collected first: waking one wakes what lies on it in turn, and that reuses the buffer.
            var resting = new List<PhysicsProp>();
            for (int i = 0; i < n; i++)
            {
                var body = Above[i] ? Above[i].attachedRigidbody : null;
                Above[i] = null;
                if (!body || !body.isKinematic || body.transform.IsChildOf(under) || under.IsChildOf(body.transform)) continue;
                if (body.TryGetComponent(out PhysicsProp prop) && prop.dormant && prop.transform.position.y > b.min.y && !resting.Contains(prop)) resting.Add(prop);
            }
            foreach (var prop in resting) if (prop) prop.Wake();
        }

        /// <summary>Out of the merged mesh, with shapes a moving body may have (a hull instead of the exact mesh).</summary>
        void Stir()
        {
            if (Stirred) return;
            Stirred = true;
            MergedLooks.Unmerge(gameObject);
            colliders ??= GetComponentsInChildren<Collider>();
            foreach (var c in colliders)
                if (c is MeshCollider mesh && !mesh.convex) mesh.convex = true;
        }

        /// <summary>It has come to rest: still scenery again (where it lies now), free to be woken later.</summary>
        public void Rest()
        {
            if (!dormant || !body || body.isKinematic) return;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>Its chunk came back: where it was left. A dormant one stays still there, drawn by itself.</summary>
        public void Restore(Vector3 position, Quaternion rotation)
        {
            if (dormant) Stir();
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!body) return;
            if (body.isKinematic)
            {
                // Something with weight ran or flew into it (a charging beast, a thrown crate): it gives way.
                var other = collision.rigidbody;
                if (dormant && other && !other.isKinematic && Time.time >= quietUntil
                    && other.mass * collision.relativeVelocity.magnitude > body.mass * .3f) Wake();
                return;
            }
            // Thrown by a card into a creature: it hurts.
            if (Time.time < thrownUntil && collision.rigidbody && collision.rigidbody.TryGetComponent(out WorldCreature creature) && creature.Alive)
            {
                float hitSpeed = collision.relativeVelocity.magnitude;
                if (hitSpeed > HurtSpeed && (creature != lastHurt || Time.time > lastHurtAt + .5f))
                {
                    lastHurt = creature;
                    lastHurtAt = Time.time;
                    var at = collision.contactCount > 0 ? collision.GetContact(0).point : creature.Centre;
                    var away = Vector3.ProjectOnPlane(creature.Position - body.position, Vector3.up).normalized + Vector3.up * .3f;
                    creature.Damage(ThrownDamage(body.mass, hitSpeed), away * Mathf.Min(hitSpeed, 10) * .35f, at, 0);
                }
            }
            if (Time.time >= quietUntil && TryGetComponent(out Breakable breakable))
            {
                // The change of its own speed: a coin or a shard bouncing off it does not break a pot.
                float speed = collision.impulse.magnitude / body.mass;
                var point = collision.contactCount > 0 ? collision.GetContact(0).point : body.worldCenterOfMass;
                if (breakable.Crash(speed, point, body.linearVelocity)) return;
            }
            PropImpacts.Collided(body, surface, collision, ref quietUntil);
        }
    }
}
