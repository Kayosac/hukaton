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
        float quietUntil;
        Collider[] colliders;

        void Awake() { start = transform.position; rotation = transform.rotation; }

        // Appearing with its chunk it settles on the ground: that is not a knock.
        void OnEnable() => quietUntil = Time.time + 1;

        public void ResetForOuting()
        {
            if (TryGetComponent(out Breakable breakable)) breakable.Restore();
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
            return true;
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
