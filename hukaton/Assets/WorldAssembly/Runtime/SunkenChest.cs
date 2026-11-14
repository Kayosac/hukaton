using Hakaton.Expeditions;
using UnityEngine;

namespace Hakaton.WorldAssembly.Unity
{
    /// <summary>
    /// A chest on the bed of deep water, rich as no other (WorldContent.sunkenRiches): too deep to wade to. The
    /// «Притяжение» card tears it out of the silt; then it floats up and drifts to the player, and opens as any chest
    /// once in reach. Water holds it back (drag) the whole way.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SunkenChest : MonoBehaviour
    {
        [Tooltip("The water's surface over it, world y.")]
        public float level;
        public Transform player;
        public GameObject bubbles;

        Rigidbody body;
        public bool Afloat { get; private set; }

        void Awake() => body = GetComponent<Rigidbody>();

        void FixedUpdate()
        {
            if (!body || body.isKinematic) return;
            var centre = body.worldCenterOfMass;
            float depth = level - centre.y;
            // Pulled out of the silt: it floats from now on.
            if (!Afloat && body.linearVelocity.sqrMagnitude > .3f)
            {
                Afloat = true;
                if (bubbles) Destroy(bubbles);
            }
            if (depth > -.1f)
            {
                body.linearDamping = 2.5f;
                body.angularDamping = 3;
                if (Afloat)
                {
                    // Up to the surface, then towards the player (until it is in reach or grounded).
                    body.AddForce(Vector3.up * Mathf.Clamp(depth + .35f, 0, 1.4f) * 15, ForceMode.Acceleration);
                    if (player)
                    {
                        var to = player.position - centre;
                        to.y = 0;
                        if (to.magnitude > 1.6f) body.AddForce(to.normalized * 1.4f, ForceMode.Acceleration);
                    }
                }
            }
            else
            {
                body.linearDamping = .4f;
                body.angularDamping = .8f;
            }
        }
    }
}
