using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S3, the third corner: the slab hangs over the path. Right under it the mountain stops breathing; the slab groans,
    /// shivers, sheds a cloud of dust and a rain of pebbles; then a long heavy out-breath and the walk goes on.
    /// </summary>
    public sealed class GodHangingSlab : GodMoment
    {
        [Tooltip("Name of the slab in the corner module (the one nearest the S3 mark is taken).")]
        public string slabName = "П — плита A06";
        [Min(0)] public float hold = 3.2f;
        [Tooltip("Shiver of the slab, degrees.")]
        public float shiver = 1.6f;
        [Min(.1f)] public float shiverSeconds = 2.2f;
        [Min(0)] public int pebbles = 6;

        Transform slab;

        void Reset() => atMetres = 64;

        Transform Slab()
        {
            if (slab) return slab;
            Vector3 near = Hub.thirdCorner ? Hub.thirdCorner.position : transform.position;
            float best = 12 * 12;
            foreach (var t in transform.root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != slabName) continue;
                float d = (t.position - near).sqrMagnitude;
                if (d < best) { best = d; slab = t; }
            }
            return slab;
        }

        Vector3 Under()
        {
            var s = Slab();
            if (!s) return Hub.RoutePoint(atMetres + 1) + Vector3.up * 2.2f;
            var r = s.GetComponentInChildren<Renderer>();
            var b = r ? r.bounds : new Bounds(s.position, Vector3.one);
            return new Vector3(b.center.x, b.min.y - .1f, b.center.z);
        }

        public override (Vector3 eye, Vector3 forward) PreviewView()
        {
            var v = Hub.ViewAt(atMetres);
            return (v.eye, (Under() - v.eye).normalized);
        }

        public override void PreviewPoints(List<Vector3> points) => points.Add(Under());

        protected override IEnumerator Play()
        {
            Hub.HoldBreath(hold);
            yield return new WaitForSeconds(.7f);
            Vector3 at = Under();
            Hub.Clip(Hub.groan, at, .8f, Random.Range(.85f, 1f), .9f, 2, 25);
            Hub.Dust(Hub.smokeMedium, at, 1.3f);
            Hub.Shake(.08f, at);
            var s = Slab();
            if (s)
            {
                var rest = s.localRotation;
                var axis = Random.onUnitSphere;
                for (float t = 0; t < shiverSeconds; t += Time.deltaTime)
                {
                    float k = 1 - t / shiverSeconds;
                    s.localRotation = rest * Quaternion.AngleAxis(Mathf.Sin(t * 31) * shiver * k * k, axis);
                    if (pebbles > 0 && Random.value < Time.deltaTime * pebbles / shiverSeconds * 1.5f)
                    {
                        var p = Hub.Stone(at + Random.insideUnitSphere * .5f, Random.Range(.1f, .2f));
                        if (p) StartCoroutine(Hub.Settle(p, 3));
                    }
                    yield return null;
                }
                s.localRotation = rest;
            }
            yield return new WaitForSeconds(Mathf.Max(0, hold - .7f - shiverSeconds));
            Hub.Clip(Hub.thud, at, .6f, 1.1f, .5f, 5, 40);
            Hub.Dust(Hub.smokeSmall, at, 1.3f, 4);
            Hub.Shake(.18f);
        }
    }
}
