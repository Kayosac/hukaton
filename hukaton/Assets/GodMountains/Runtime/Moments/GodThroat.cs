using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// Z3, the bottom of the stairs where the walls close in under a roof: the mountain draws a deep breath and the
    /// throat squeezes — a low thud, both walls burst out dust at eye height, stones rain from the roof, the view shakes.
    /// </summary>
    public sealed class GodThroat : GodMoment
    {
        [Range(0, 1)] public float shake = .38f;
        [Min(0)] public int stones = 6;

        void Reset() => atMetres = 85.5f;

        void Locate(Vector3 eye, Vector3 forward, List<(Vector3 at, Vector3 normal)> walls, out Vector3? roof)
        {
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 ahead = eye + forward * 2.5f;
            foreach (var side in new[] { right, -right })
                if (Hub.Wall(ahead, side, 3, out var p, out var n)) walls.Add((p + n * .25f, n));
            roof = null;
            // the roof slab: the nearest one ahead within a few metres, so the stones fall in front of the face
            for (float d = 1; d <= 3.5f && !roof.HasValue; d += .5f)
                if (Hub.Wall(eye + forward * d, Vector3.up, 4, out var rp, out _)) roof = rp - Vector3.up * .3f;
        }

        public override void PreviewPoints(List<Vector3> points)
        {
            var v = PreviewView();
            var walls = new List<(Vector3, Vector3)>();
            Locate(v.eye, v.forward, walls, out var roof);
            foreach (var w in walls) points.Add(w.Item1);
            if (roof.HasValue) points.Add(roof.Value);
        }

        protected override IEnumerator Play()
        {
            var eye = Hub.Eye;
            var forward = eye.forward; forward.y = 0; forward.Normalize();
            Hub.Sound(Hub.rumble, eye.position + forward * 3, 1, .7f);
            yield return new WaitForSeconds(.5f);
            var walls = new List<(Vector3 at, Vector3 normal)>();
            Locate(eye.position, forward, walls, out var roof);
            Hub.Clip(Hub.thud, eye.position, .95f, .9f, .3f, 3, 40);
            Hub.Shake(shake);
            if (roof.HasValue)
            {
                for (int i = 0; i < stones; i++)
                {
                    var s = Hub.Stone(roof.Value + new Vector3(Random.Range(-.6f, .6f), 0, Random.Range(-.6f, .6f)), Random.Range(.12f, .26f));
                    if (s) StartCoroutine(Hub.Settle(s, 3));
                    yield return new WaitForSeconds(Random.Range(.04f, .15f));
                }
            }
            yield return new WaitForSeconds(.6f);
            Hub.Sound(Hub.crumble, eye.position + Vector3.up, .7f, .7f);
        }
    }
}
