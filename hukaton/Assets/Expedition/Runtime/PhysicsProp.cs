using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>A loose physical object (crate) that returns to its authored pose at the start of each outing.</summary>
    public sealed class PhysicsProp : MonoBehaviour
    {
        public Rigidbody body;
        Vector3 start;
        Quaternion rotation;

        void Awake() { start = transform.position; rotation = transform.rotation; }

        public void ResetForOuting()
        {
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = start;
            body.rotation = rotation;
        }
    }
}
