using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S5, the stone frame: a cold tongue of cloud pushes through it — big pale puffs come out of the frame and roll
    /// along the floor toward the player; past it the breath is muffled and the wind begins (both on the hub).
    /// </summary>
    public sealed class GodFrameDraft : GodMoment
    {
        [Tooltip("Where the frame is, m along the route.")]
        public float frameAt = 99.5f;
        [Min(1)] public int puffs = 4;
        [Min(0)] public float speed = 1.6f;

        void Reset() => atMetres = 95.5f;

        public override void PreviewPoints(List<Vector3> points) => points.Add(Hub.RoutePoint(frameAt) + Vector3.up * 1.1f);

        protected override IEnumerator Play()
        {
            Vector3 frame = Hub.RoutePoint(frameAt) + Vector3.up * 1.1f;
            Hub.Sound(Hub.gust, frame, 1, .55f);
            Hub.BoostWind(.65f, 3f);
            Hub.Shake(.05f);
            for (int i = 0; i < puffs; i++)
            {
                Vector3 toPlayer = Hub.Eye.position - frame; toPlayer.y = 0;
                var dir = toPlayer.normalized;
                Hub.Drift(Hub.smokeMedium, frame + Random.insideUnitSphere * .4f - Vector3.up * .5f,
                          dir * speed * Random.Range(.8f, 1.2f), Random.Range(.7f, .9f), 3.5f);
                yield return new WaitForSeconds(Random.Range(.15f, .3f));
            }
        }
    }
}
