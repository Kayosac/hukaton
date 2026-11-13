using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S3, the third corner: the slab hangs over the path. Right under it the mountain stops breathing; the slab groans,
    /// shivers and sheds grit; then a long heavy out-breath and the walk goes on.
    /// </summary>
    public sealed class GodHangingSlab : GodMoment
    {
        [Tooltip("Name of the slab in the corner module (the one nearest the S3 mark is taken).")]
        public string slabName = "П — плита A06";
        [Min(0)] public float hold = 2.8f;
        [Tooltip("Shiver of the slab, degrees.")]
        public float shiver = .9f;
        [Min(.1f)] public float shiverSeconds = 2.2f;

        Transform slab;

        void Reset() => atMetres = 64;

        protected override void Awake()
        {
            base.Awake();
            Vector3 near = Hub.thirdCorner ? Hub.thirdCorner.position : transform.position;
            float best = 12 * 12;
            foreach (var t in transform.root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != slabName) continue;
                float d = (t.position - near).sqrMagnitude;
                if (d < best) { best = d; slab = t; }
            }
        }

        protected override IEnumerator Play()
        {
            Hub.HoldBreath(hold);
            yield return new WaitForSeconds(.7f);
            Vector3 at = slab ? slab.position : Hub.RoutePoint(Hub.Progress + 1) + Vector3.up * 2.2f;
            Hub.Clip(Hub.groan, at, .75f, Random.Range(.85f, 1f), .9f, 2, 25);
            Hub.Effect(Hub.gritEffect, at - Vector3.up * .3f, Quaternion.identity, .9f, 4);
            Hub.Shake(.05f, at);
            if (slab)
            {
                var rest = slab.localRotation;
                var axis = Random.onUnitSphere;
                for (float t = 0; t < shiverSeconds; t += Time.deltaTime)
                {
                    float k = 1 - t / shiverSeconds;
                    slab.localRotation = rest * Quaternion.AngleAxis(Mathf.Sin(t * 31) * shiver * k * k, axis);
                    yield return null;
                }
                slab.localRotation = rest;
            }
            yield return new WaitForSeconds(Mathf.Max(0, hold - .7f - shiverSeconds));
            Hub.Clip(Hub.thud, at, .55f, 1.1f, .5f, 5, 40);
            Hub.Shake(.15f);
        }
    }
}
