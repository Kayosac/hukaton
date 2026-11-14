using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A loose physical object (crate, log, pot) that returns to its authored pose at the start of each outing and
    /// knocks when it hits something (<see cref="PropImpacts"/>).
    /// </summary>
    public sealed class PhysicsProp : MonoBehaviour
    {
        public Rigidbody body;
        [Tooltip("What it is made of: picks the knock when it hits something.")]
        public PropSurface surface;
        Vector3 start;
        Quaternion rotation;
        float quietUntil;

        void Awake() { start = transform.position; rotation = transform.rotation; }

        // Appearing with its chunk it settles on the ground: that is not a knock.
        void OnEnable() => quietUntil = Time.time + 1;

        public void ResetForOuting()
        {
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = start;
            body.rotation = rotation;
        }

        /// <summary>Something is about to move it: the body must be awake to take the push.</summary>
        public void Wake()
        {
            if (body && !body.isKinematic) body.WakeUp();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (body) PropImpacts.Collided(body, surface, collision, ref quietUntil);
        }
    }
}
