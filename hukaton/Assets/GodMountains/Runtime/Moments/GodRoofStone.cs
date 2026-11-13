using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// Z1, the slot: high on the wall a few metres ahead dust bursts out and a boulder breaks away with a hail of small
    /// stones; it drops down the face, smashes on the path in a cloud of dust and shakes the view. The pieces settle as
    /// rubble by the path (small enough to walk over).
    /// </summary>
    public sealed class GodRoofStone : GodMoment
    {
        [Tooltip("Where along the route ahead it may happen (the spot must be in view), m.")]
        public Vector2 ahead = new Vector2(5, 12);
        [Tooltip("How high on the wall it may break away, m above the path.")]
        public Vector2 height = new Vector2(2.5f, 4.5f);
        [Min(.1f)] public float size = 1.1f;
        [Min(0)] public float warning = .25f;
        [Min(0)] public int hail = 6;
        [Min(0)] public int pieces = 5;
        [Range(0, 1)] public float shake = .4f;

        void Reset() => atMetres = 14;

        Vector3 spot, spotNormal;

        bool Locate(float progress, Vector3 eye, Vector3 forward) =>
            Hub.VisibleSpot(progress, eye, forward, ahead, height, out spot, out spotNormal, prefer: 9, cone: 24);

        // waits (up to a few metres further) until a spot on the walls ahead is in view
        protected override bool Ready => Hub.Progress > atMetres + 12 || Locate(Hub.Progress, Hub.Eye.position, Hub.Eye.forward);

        public override void PreviewPoints(List<Vector3> points)
        {
            var v = PreviewView();
            if (!Locate(atMetres, v.eye, v.forward)) return;
            points.Add(spot);
            points.Add(Hub.Floor(spot));
        }

        protected override IEnumerator Play()
        {
            if (spot == default) yield break;
            Vector3 at = spot, normal = spotNormal;
            Hub.Sound(Hub.crumble, at, 1, .7f);
            Hub.Shake(.08f, at);
            for (int i = 0; i < 3; i++)
            {
                var s = Hub.Stone(at + Random.insideUnitSphere * .4f, Random.Range(.15f, .25f));
                if (s) { s.linearVelocity = normal * .6f; StartCoroutine(Hub.Settle(s, 4)); }
            }
            yield return new WaitForSeconds(warning);
            var stone = Hub.Stone(at, size);
            if (!stone) yield break;
            stone.linearVelocity = normal * 1.2f + Vector3.down * 3.5f;
            stone.angularVelocity = Random.insideUnitSphere * 3;
            for (int i = 0; i < hail; i++)
            {
                var s = Hub.Stone(at + Random.insideUnitSphere * .7f, Random.Range(.15f, .32f));
                if (s) { s.linearVelocity = normal * Random.Range(.4f, 1.6f); StartCoroutine(Hub.Settle(s, 4)); }
            }
            float time = 0;
            while (time < 4 && stone)
            {
                time += Time.deltaTime;
                if (time > .2f && Physics.Raycast(stone.position, Vector3.down, size * .55f, ~(1 << 2), QueryTriggerInteraction.Ignore)) break;
                yield return null;
            }
            if (!stone) yield break;
            Vector3 hit = stone.position;
            Hub.Sound(Hub.crash, hit, 1, .8f);
            Hub.Sound(Hub.rockImpact, hit, 1, .7f);
            Hub.Shake(shake, hit);
            Destroy(stone.gameObject);
            for (int i = 0; i < pieces; i++)
            {
                Vector3 offset = Quaternion.Euler(0, 360f * i / pieces + Random.Range(-25f, 25f), 0) * Vector3.forward * size * .3f;
                var piece = Hub.Stone(hit + offset + Vector3.up * .15f, size * Random.Range(.22f, .32f));
                if (!piece) continue;
                piece.linearVelocity = offset.normalized * Random.Range(.8f, 2f) + Vector3.up * Random.Range(1f, 2.4f);
                piece.angularVelocity = Random.insideUnitSphere * 8;
                StartCoroutine(Hub.Settle(piece, 4));
            }
        }
    }
}
