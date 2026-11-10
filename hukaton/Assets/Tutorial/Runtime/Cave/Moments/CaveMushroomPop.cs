using System.Collections;
using Hakaton.Expeditions;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: the first meeting with a cave mushroom. It hides until the player comes close, then pops out of a crack
    /// in a puff of dust, squeaks and scurries away (its own timid nature does the running). Afterwards it lives on as
    /// an ordinary cave mushroom around that crack.
    /// </summary>
    public sealed class CaveMushroomPop : CaveMoment
    {
        public WorldCreature mushroom;
        [Tooltip("The crack it pops out of (on the floor, facing out).")]
        public Transform crack;
        public GameObject puffEffect;
        public SfxCue pop;

        protected override void Prepare()
        {
            if (mushroom) mushroom.gameObject.SetActive(false);
        }

        protected override IEnumerator Play()
        {
            if (!mushroom) yield break;
            Effect(puffEffect, crack.position + Vector3.up * .2f, Quaternion.identity, .6f, 3);
            Sound(pop, crack.position, 1, 1.15f);
            mushroom.spawnPoints = new[] { crack };
            mushroom.ResetForOuting(new System.Random(7));
            Sound(mushroom.alertCue, crack.position);
            // pops out: from a squashed seed to full size with an overshoot
            var body = mushroom.transform;
            Vector3 size = body.localScale;
            for (float t = 0; t < .35f; t += Time.deltaTime)
            {
                float k = t / .35f;
                float s = Mathf.LerpUnclamped(.2f, 1, Ease.OutBack(k));
                body.localScale = size * s;
                yield return null;
            }
            body.localScale = size;
        }
    }
}
