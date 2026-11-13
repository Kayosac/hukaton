using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S5, the stone frame: a cold gust of cloud pushes through it; past it the breath is muffled and the wind begins
    /// (both on the hub).
    /// </summary>
    public sealed class GodFrameDraft : GodMoment
    {
        [Tooltip("Where the frame is, m along the route.")]
        public float frameAt = 99.5f;

        void Reset() => atMetres = 95.5f;

        public override void PreviewPoints(List<Vector3> points) => points.Add(Hub.RoutePoint(frameAt) + Vector3.up * 1.1f);

        protected override IEnumerator Play()
        {
            Vector3 frame = Hub.RoutePoint(frameAt) + Vector3.up * 1.1f;
            Hub.Sound(Hub.gust, frame, 1, .55f);
            Hub.BoostWind(.65f, 3f);
            Hub.Shake(.05f);
            yield break;
        }
    }
}
