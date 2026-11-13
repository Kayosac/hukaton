using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S4, the top of the too-even stairs: a stone slips from under the foot and rattles down the steps into the dark,
    /// a puff of dust at every knock, further and fainter, longer than the stairs should be.
    /// </summary>
    public sealed class GodStairsPebble : GodMoment
    {
        [Min(.05f)] public float size = .4f;
        [Min(0)] public float push = 2.2f;
        [Min(1)] public float lifetime = 12;

        void Reset() => atMetres = 77.5f;

        public override (Vector3 eye, Vector3 forward) PreviewView()
        {
            var v = Hub.ViewAt(atMetres);
            return (v.eye, (v.forward - Vector3.up * .45f).normalized);
        }

        [Tooltip("How far ahead it slips out, m (in view: the player looks down the stairs).")]
        public float ahead = 3f;

        public override void PreviewPoints(List<Vector3> points) => points.Add(Hub.RoutePoint(atMetres + ahead) + Vector3.up * .4f);

        protected override IEnumerator Play()
        {
            Vector3 at = Hub.RoutePoint(Hub.Progress + ahead) + Vector3.up * .4f;
            var pebble = Hub.Stone(at, size);
            if (!pebble) yield break;
            pebble.linearVelocity = Hub.RouteForward(Hub.Progress + 3) * push;
            pebble.angularVelocity = Random.insideUnitSphere * 10;
            var clack = pebble.gameObject.AddComponent<Clack>();
            clack.hub = Hub;
            Destroy(pebble.gameObject, lifetime);
        }

        /// <summary>Every knock of the stone on the steps: a click and a little dust, fainter as it goes.</summary>
        sealed class Clack : MonoBehaviour
        {
            public GodMoments hub;
            float next, volume = .7f;

            void OnCollisionEnter(Collision c)
            {
                if (Time.time < next || c.relativeVelocity.magnitude < .6f) return;
                next = Time.time + .09f;
                hub.Sound(hub.rockImpact, transform.position, volume, Random.Range(2f, 2.6f));
                hub.Dust(hub.smokeTiny, transform.position, 1f, 3);
                volume *= .85f;
            }
        }
    }
}
