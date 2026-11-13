using System.Collections;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// Z1, under the stone roof: grit and a creak, then a stone breaks off a few metres ahead, smashes on the floor and
    /// shakes the view. The pieces settle as rubble beside the path.
    /// </summary>
    public sealed class GodRoofStone : GodMoment
    {
        [Tooltip("How far ahead along the route the stone drops, m.")]
        public float ahead = 4f;
        [Min(.1f)] public float size = .7f;
        [Min(0)] public float warning = 1.1f;
        [Min(0)] public int pieces = 4;
        [Range(0, 1)] public float shake = .35f;

        void Reset() => atMetres = 17;

        protected override IEnumerator Play()
        {
            Vector3 floor = Hub.RoutePoint(Hub.Progress + ahead);
            var roof = Hub.Ceiling(floor + Vector3.up * 1.2f, 7);
            if (!roof.HasValue) yield break;
            Hub.Effect(Hub.gritEffect, roof.Value, Quaternion.identity, .8f, 4);
            Hub.Sound(Hub.crumble, roof.Value, .9f, .75f);
            Hub.Shake(.06f, roof.Value);
            yield return new WaitForSeconds(warning);
            Hub.Effect(Hub.gritEffect, roof.Value, Quaternion.identity, 1.2f, 4);
            var stone = Hub.Stone(roof.Value - Vector3.up * size * .6f, size);
            if (!stone) yield break;
            stone.angularVelocity = Random.insideUnitSphere * 3;
            float time = 0;
            while (time < 3 && stone)
            {
                time += Time.deltaTime;
                if (time > .15f && Physics.Raycast(stone.position, Vector3.down, size * .6f, ~(1 << 2), QueryTriggerInteraction.Ignore)) break;
                yield return null;
            }
            if (!stone) yield break;
            Vector3 at = stone.position;
            Hub.Effect(Hub.impactEffect, at, Quaternion.identity, .9f, 4);
            Hub.Sound(Hub.rockImpact, at, 1, .8f);
            Hub.Shake(shake, at);
            Destroy(stone.gameObject);
            for (int i = 0; i < pieces; i++)
            {
                Vector3 offset = Quaternion.Euler(0, 360f * i / pieces + Random.Range(-25f, 25f), 0) * Vector3.forward * size * .3f;
                var piece = Hub.Stone(at + offset + Vector3.up * .15f, size * Random.Range(.3f, .45f));
                if (!piece) continue;
                piece.linearVelocity = offset.normalized * Random.Range(.6f, 1.5f) + Vector3.up * Random.Range(1f, 2f);
                piece.angularVelocity = Random.insideUnitSphere * 8;
                StartCoroutine(Hub.Settle(piece, 3.5f));
            }
        }
    }
}
