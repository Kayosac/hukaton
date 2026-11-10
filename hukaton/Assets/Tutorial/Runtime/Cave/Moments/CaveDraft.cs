using System.Collections;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: once the roots have burnt through, air pulls through the opened passage. A low gust, dust drifting along
    /// the corridor; the candles beyond go out one after another and a lantern gutters but holds.
    /// </summary>
    public sealed class CaveDraft : CaveMoment
    {
        public WorldObstacle roots;
        [Min(0)] public float delay = 1.3f;
        [Tooltip("Where the draft comes through (the burnt gap).")]
        public Transform gap;
        [Tooltip("Flames the draft puts out, in order.")]
        public Transform[] putOut = new Transform[0];
        [Tooltip("Flames that gutter and recover.")]
        public Transform[] gutter = new Transform[0];
        public GameObject dustEffect, smokeEffect;
        public SfxCue gust;
        [Min(0)] public float stagger = .45f;

        protected override bool Ready => roots && roots.Resolved;

        protected override IEnumerator Play()
        {
            yield return new WaitForSeconds(delay);
            Sound(gust, gap.position, 1, .55f);
            Effect(dustEffect, gap.position + Vector3.up * .8f, Quaternion.LookRotation(Vector3.up), 1.2f, 6);
            foreach (var flame in gutter) if (flame) StartCoroutine(Gust(Flame.Of(flame), 2.2f, .75f));
            yield return new WaitForSeconds(.35f);
            foreach (var candle in putOut)
            {
                if (!candle) continue;
                var flame = Flame.Of(candle);
                yield return Gust(flame, .45f, .5f);
                StartCoroutine(PutOut(flame, smokeEffect, .25f));
                yield return new WaitForSeconds(stagger);
            }
        }
    }
}
