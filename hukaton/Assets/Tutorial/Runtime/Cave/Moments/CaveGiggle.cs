using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: on the way to the grotto a giggle comes from somewhere ahead. Later, when the bush first comes into view,
    /// it rustles on its own and someone inside snickers. Foreshadows the goblin; only before the Strike lesson is done.
    /// </summary>
    public sealed class CaveGiggle : CaveMoment
    {
        [Tooltip("Where the bush first comes into view: it rustles when the player gets here.")]
        public Transform rustleTrigger;
        [Min(0)] public float rustleRadius = 2.5f;
        public GameObject leavesEffect;
        public SfxCue giggle, rustle;

        public bool Rustled { get; private set; }

        bool Waiting => Tutorial && (Tutorial.Stage == CaveStage.Water || Tutorial.Stage == CaveStage.Bush);
        protected override bool Ready => Waiting;

        protected override IEnumerator Play()
        {
            Vector3 bush = Level.bush ? Level.bush.position : transform.position;
            Sound(giggle, bush + Vector3.up * .6f, .7f, 1.12f);
            Tutorial.ShakeBush();
            if (!rustleTrigger) yield break;
            while (true)
            {
                if (!Waiting) yield break;
                Vector3 d = Player - rustleTrigger.position;
                if (Mathf.Abs(d.y) < 3 && new Vector2(d.x, d.z).sqrMagnitude < rustleRadius * rustleRadius) break;
                yield return null;
            }
            Rustled = true;
            Tutorial.ShakeBush();
            Effect(leavesEffect, bush + Vector3.up * .7f, Quaternion.identity, .5f, 4);
            Sound(rustle, bush, 1, .9f);
            yield return new WaitForSeconds(.35f);
            Sound(giggle, bush + Vector3.up * .6f, .4f, 1.35f);
        }
    }
}
