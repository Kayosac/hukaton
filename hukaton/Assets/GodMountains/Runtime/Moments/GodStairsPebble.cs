using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S4, the top of the too-even stairs: a pebble slips from under the foot and rattles down the steps into the dark,
    /// further and further, longer than the stairs should be.
    /// </summary>
    public sealed class GodStairsPebble : GodMoment
    {
        [Min(.05f)] public float size = .22f;
        [Min(0)] public float push = 2.2f;
        [Min(1)] public float lifetime = 10;

        void Reset() => atMetres = 77.5f;

        protected override IEnumerator Play()
        {
            Vector3 at = Hub.RoutePoint(Hub.Progress + 1.2f) + Vector3.up * .5f;
            var pebble = Hub.Stone(at, size);
            if (!pebble) yield break;
            pebble.linearVelocity = Hub.RouteForward(Hub.Progress + 3) * push;
            pebble.angularVelocity = Random.insideUnitSphere * 10;
            var clack = pebble.gameObject.AddComponent<Clack>();
            clack.hub = Hub;
            Destroy(pebble.gameObject, lifetime);
            yield break;
        }

        /// <summary>Every knock of the pebble on the steps: a small click, fainter as it goes.</summary>
        sealed class Clack : MonoBehaviour
        {
            public GodMoments hub;
            float next, volume = .6f;

            void OnCollisionEnter(Collision c)
            {
                if (Time.time < next || c.relativeVelocity.magnitude < .6f) return;
                next = Time.time + .09f;
                hub.Sound(hub.rockImpact, transform.position, volume, Random.Range(2.2f, 2.8f));
                volume *= .85f;
            }
        }
    }
}
