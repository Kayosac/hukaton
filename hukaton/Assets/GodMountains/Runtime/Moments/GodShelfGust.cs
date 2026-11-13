using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S6, the white shelf: a gust from behind, the shrubs (already leaning along the path) bow further the way the
    /// player goes and leaves fly ahead into the cloud, as if the mountain pointed.
    /// </summary>
    public sealed class GodShelfGust : GodMoment
    {
        [Tooltip("Shrubs whose name contains this, within Reach of the player, bow.")]
        public string shrubName = "A07";
        [Min(1)] public float reach = 22;
        public float bow = 14;
        [Min(.1f)] public float seconds = 3.2f;

        void Reset() => atMetres = 116;

        protected override IEnumerator Play()
        {
            var eye = Hub.Eye;
            Vector3 along = Hub.RouteForward(Hub.Progress);
            Hub.Sound(Hub.gust, eye.position - along * 4, 1, .7f);
            Hub.BoostWind(.75f, seconds + 1);
            var shrubs = new List<(Transform t, Quaternion rest, Vector3 axis)>();
            foreach (var t in transform.root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Contains(shrubName) || (t.position - eye.position).sqrMagnitude > reach * reach) continue;
                if (t.parent && t.parent.name.Contains(shrubName)) continue; // the model root, not its parts
                shrubs.Add((t, t.rotation, Vector3.Cross(Vector3.up, along).normalized));
            }
            int puffs = 0;
            foreach (var s in shrubs)
                if (puffs++ < 3) Hub.Effect(Hub.leavesEffect, s.t.position + Vector3.up * .6f, Quaternion.LookRotation(along), .5f, 4);
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / (seconds * .35f))) * (t < seconds * .35f ? 1 : 0)
                          + Mathf.Exp(-(t - seconds * .35f) * 2.2f) * Mathf.Cos((t - seconds * .35f) * 9) * (t >= seconds * .35f ? 1 : 0);
                foreach (var s in shrubs)
                    if (s.t) s.t.rotation = Quaternion.AngleAxis(-bow * k, s.axis) * s.rest;
                yield return null;
            }
            foreach (var s in shrubs) if (s.t) s.t.rotation = s.rest;
        }
    }
}
