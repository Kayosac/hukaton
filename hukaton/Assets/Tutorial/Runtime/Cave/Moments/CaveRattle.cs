using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>Knocks of a loose bone or stone: a sound per bump, louder for harder hits.</summary>
    public sealed class CaveRattle : MonoBehaviour
    {
        public SfxCue knock;
        [Min(0)] public float minimumSpeed = .4f;
        float next;

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed < minimumSpeed || Time.time < next || !knock) return;
            next = Time.time + .08f;
            var world = Object.FindAnyObjectByType<ExpeditionWorld>();
            if (world) world.Play(knock, transform.position, Mathf.Clamp01(speed / 3f), Random.Range(.9f, 1.15f));
        }
    }
}
