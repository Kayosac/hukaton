using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S5, the stone frame: a cold gust comes through it from the cloud, dust drifts toward the player; past it the breath
    /// is muffled and the wind begins (both by the route, on the hub).
    /// </summary>
    public sealed class GodFrameDraft : GodMoment
    {
        void Reset() => atMetres = 95.5f;

        protected override IEnumerator Play()
        {
            Vector3 frame = Hub.RoutePoint(Hub.Progress + 2.5f) + Vector3.up * 1.2f;
            Vector3 toPlayer = Hub.Eye.position - frame;
            Hub.Sound(Hub.gust, frame, 1, .55f);
            Hub.Effect(Hub.draftEffect, frame, Quaternion.LookRotation(toPlayer.sqrMagnitude > 1e-3f ? toPlayer : Vector3.forward), 1.2f, 6);
            Hub.BoostWind(.6f, 2.5f);
            Hub.Shake(.04f);
            yield break;
        }
    }
}
