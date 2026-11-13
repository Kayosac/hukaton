using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// Z2, the second corner (the exact copy of the first): a few footsteps behind the player, their own steps, a beat late.
    /// Turn round: nobody. Then they stop, as if whoever it was stopped too.
    /// </summary>
    public sealed class GodEchoSteps : GodMoment
    {
        [Min(1)] public int count = 4;
        [Tooltip("How far behind along the route, m.")]
        public float behind = 5.5f;
        [Min(.1f)] public float interval = .52f;
        [Range(0, 1)] public float volume = .45f;

        void Reset() => atMetres = 55;

        protected override IEnumerator Play()
        {
            if (Hub.steps == null || Hub.steps.Length == 0) yield break;
            yield return new WaitForSeconds(.4f);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = Hub.RoutePoint(Mathf.Max(0, Hub.Progress - behind)) + Vector3.up * .1f;
                var clip = Hub.steps[i % Hub.steps.Length];
                Hub.Clip(clip, at, volume * (i == count - 1 ? .6f : 1), Random.Range(.88f, .95f), 1, 1.5f, 18);
                yield return new WaitForSeconds(interval * Random.Range(.95f, 1.1f));
            }
        }
    }
}
