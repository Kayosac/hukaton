using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S6, the white shelf: a gust from behind; cloud rolls past along the floor, the shrubs (already leaning along the
    /// path) bow further the way the player goes and leaves fly ahead into the white, as if the mountain pointed.
    /// </summary>
    public sealed class GodShelfGust : GodMoment
    {
        [Tooltip("The shrub model's part: every placed shrub within Reach of the player (the part's parent) bows.")]
        public string shrubName = "A07_shrub";
        [Min(1)] public float reach = 22;
        public float bow = 18;
        [Min(.1f)] public float seconds = 3.2f;

        void Reset() => atMetres = 116;

        List<Transform> Shrubs(Vector3 near)
        {
            var list = new List<Transform>();
            foreach (var part in Hub.Named(shrubName))
                if (part.parent && (part.position - near).sqrMagnitude <= reach * reach) list.Add(part.parent);
            return list;
        }

        public override void PreviewPoints(List<Vector3> points)
        {
            foreach (var t in Shrubs(PreviewView().eye)) points.Add(t.position + Vector3.up * .6f);
        }

        protected override IEnumerator Play()
        {
            var eye = Hub.Eye;
            Vector3 along = Hub.RouteForward(Hub.Progress);
            Hub.Sound(Hub.gust, eye.position - along * 4, 1, .7f);
            Hub.BoostWind(.8f, seconds + 1);
            for (int i = 0; i < 4; i++)
                Hub.Drift(Hub.smokeBig, Hub.Floor(eye.position - along * 2 + Random.insideUnitSphere * 1.2f) + Vector3.up * .5f,
                          along * Random.Range(3f, 4.5f), Random.Range(1f, 1.4f), 3);
            var shrubs = Shrubs(eye.position);
            var rest = new List<Quaternion>();
            foreach (var s in shrubs) rest.Add(s.rotation);
            var axis = Vector3.Cross(Vector3.up, along).normalized;
            for (int i = 0; i < shrubs.Count; i++)
                Hub.Effect(Hub.leavesEffect, shrubs[i].position + Vector3.up * .6f, Quaternion.LookRotation(along), 2.4f, 4);
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float rise = seconds * .3f;
                float k = t < rise ? Mathf.Sin(Mathf.PI * .5f * t / rise) : Mathf.Exp(-(t - rise) * 2f) * Mathf.Cos((t - rise) * 8);
                for (int i = 0; i < shrubs.Count; i++)
                    if (shrubs[i]) shrubs[i].rotation = Quaternion.AngleAxis(-bow * k, axis) * rest[i];
                yield return null;
            }
            for (int i = 0; i < shrubs.Count; i++) if (shrubs[i]) shrubs[i].rotation = rest[i];
        }
    }
}
