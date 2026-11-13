using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>S1: the eyes open on the wall; the mountain breathes out and grit trickles right in front of the face.</summary>
    public sealed class GodWake : GodMoment
    {
        [Tooltip("Seconds after the level starts (the screen has opened in the pocket).")]
        [Min(0)] public float delay = 2.8f;
        protected override bool Ready => Hub.RunningFor >= delay;

        protected override IEnumerator Play()
        {
            var eye = Hub.Eye;
            Vector3 front = eye.position + Flat(eye.forward) * .9f;
            var roof = Hub.Ceiling(front, 6) ?? front + Vector3.up * 1.6f;
            Hub.Effect(Hub.gritEffect, roof, Quaternion.identity, .6f, 4);
            Hub.Sound(Hub.crumble, roof, .55f, .8f);
            Hub.Shake(.12f);
            yield return new WaitForSeconds(.7f);
            Hub.Effect(Hub.gritEffect, roof + Flat(eye.right) * .4f, Quaternion.identity, .35f, 4);
            Hub.Sound(Hub.crumble, roof, .3f, 1.1f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }
    }
}
