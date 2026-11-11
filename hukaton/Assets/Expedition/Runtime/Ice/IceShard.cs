using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>A piece of a shattered ice shell: flies out, tumbles, lies a moment, then shrinks away.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class IceShard : MonoBehaviour
    {
        public Rigidbody body;
        [Min(.05f)] public float shrinkSeconds = .45f;

        float dieAt, shrinkFrom = -1;
        Vector3 scale;

        /// <summary>Throws the shard; the player's own colliders are ignored so pieces never trip the camera.</summary>
        public void Launch(Vector3 velocity, Vector3 spin, float lifetime, Collider[] ignore)
        {
            if (!body) body = GetComponent<Rigidbody>();
            scale = transform.localScale;
            dieAt = Time.time + lifetime;
            var own = GetComponent<Collider>();
            if (own && ignore != null) foreach (var other in ignore) if (other) Physics.IgnoreCollision(own, other);
            body.linearVelocity = velocity;
            body.angularVelocity = spin;
        }

        void Update()
        {
            if (dieAt <= 0 || Time.time < dieAt) return;
            if (shrinkFrom < 0) shrinkFrom = Time.time;
            float k = (Time.time - shrinkFrom) / shrinkSeconds;
            if (k >= 1) { Destroy(gameObject); return; }
            transform.localScale = scale * (1 - k * k);
        }
    }
}
