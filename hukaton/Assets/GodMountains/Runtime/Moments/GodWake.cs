using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.GodMountains
{
    /// <summary>
    /// S1: the eyes open on the wall. The mountain breathes out and the wall in front sheds: a little cloud of dust bursts
    /// out of it at eye height and a handful of stones slides down its face to the feet.
    /// </summary>
    public sealed class GodWake : GodMoment
    {
        [Tooltip("Seconds after the level starts (the screen has opened in the pocket).")]
        [Min(0)] public float delay = 2.8f;
        [Min(0)] public int stones = 7;
        protected override bool Ready => Hub.RunningFor >= delay;

        bool Locate(Vector3 eye, Vector3 forward, out Vector3 at, out Vector3 normal)
        {
            forward.y = 0;
            at = default;
            if (!Hub.Wall(eye, forward.normalized, 6, out var hit, out normal)) return false;
            at = hit + normal * .7f + Vector3.up * .9f;
            return true;
        }

        public override (Vector3 eye, Vector3 forward) PreviewView()
        {
            var level = GetComponentInParent<GodMountainsLevel>();
            return level && level.spawn ? (level.spawn.position + Vector3.up * 1.62f, level.spawn.forward) : base.PreviewView();
        }

        public override void PreviewPoints(List<Vector3> points)
        {
            var v = PreviewView();
            if (Locate(v.eye, v.forward, out var at, out _)) points.Add(at);
        }

        protected override IEnumerator Play()
        {
            var eye = Hub.Eye;
            if (!Locate(eye.position, eye.forward, out var at, out var normal)) yield break;
            Hub.Dust(Hub.smokeMedium, at, 2.2f);
            Hub.Sound(Hub.crumble, at, .8f, .8f);
            Hub.Shake(.14f);
            for (int i = 0; i < stones; i++)
            {
                var s = Hub.Stone(at + new Vector3(Random.Range(-.6f, .6f), Random.Range(-.2f, .4f), Random.Range(-.6f, .6f)), Random.Range(.14f, .3f));
                if (s) { s.linearVelocity = normal * Random.Range(.3f, .9f); StartCoroutine(Hub.Settle(s, 3)); }
                yield return new WaitForSeconds(Random.Range(.06f, .18f));
            }
            yield return new WaitForSeconds(.6f);
            Hub.Dust(Hub.smokeTiny, Hub.Floor(at) + Vector3.up * .2f, 1.6f, 4);
            Hub.Sound(Hub.crumble, at, .4f, 1.15f);
        }
    }
}
