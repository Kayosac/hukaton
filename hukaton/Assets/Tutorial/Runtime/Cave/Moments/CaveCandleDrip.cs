using System.Collections;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Tutorial
{
    /// <summary>
    /// Moment: water drips from the ceiling onto a candle. The first drop lands beside it and the flame shies;
    /// the second hits the wick: a hiss, the flame dies and a wisp of smoke rises.
    /// </summary>
    public sealed class CaveCandleDrip : CaveMoment
    {
        public Transform candle;
        [Tooltip("Where the drops form (on the ceiling above the candle).")]
        public Transform ceiling;
        public Material dropMaterial;
        public GameObject splashEffect, smokeEffect;
        public SfxCue drip, hiss;
        [Min(0)] public float pause = .9f;

        protected override IEnumerator Play()
        {
            var flame = Flame.Of(candle);
            Vector3 wick = flame.light ? flame.light.transform.position : candle.position + Vector3.up * .2f;
            // first drop: beside the flame
            Vector3 miss = wick + new Vector3(.14f, -.2f, .06f);
            yield return Drop(new Vector3(miss.x, ceiling.position.y, miss.z), miss);
            Sound(drip, miss, .8f, 1.1f);
            Effect(splashEffect, miss, Quaternion.identity, .25f, 2);
            StartCoroutine(Gust(flame, .5f, .6f));
            yield return new WaitForSeconds(pause);
            // second drop: on the wick
            yield return Drop(new Vector3(wick.x, ceiling.position.y, wick.z), wick);
            Sound(hiss, wick, .55f, 1.35f);
            Effect(splashEffect, wick, Quaternion.identity, .2f, 2);
            yield return PutOut(flame, smokeEffect, .3f);
        }

        IEnumerator Drop(Vector3 from, Vector3 to)
        {
            var drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(drop.GetComponent<Collider>());
            drop.name = "Drip";
            drop.transform.SetParent(transform, true);
            drop.transform.localScale = new Vector3(.03f, .055f, .03f);
            var renderer = drop.GetComponent<Renderer>();
            if (dropMaterial) renderer.sharedMaterial = dropMaterial;
            CaveOnly(drop);
            // swells on the ceiling, then falls
            for (float t = 0; t < .5f; t += Time.deltaTime)
            {
                drop.transform.position = from + Vector3.down * t * .04f;
                drop.transform.localScale = new Vector3(.03f, .03f + .025f * t / .5f, .03f) * (.6f + .4f * t / .5f);
                yield return null;
            }
            float height = Mathf.Max(.1f, from.y - to.y), fall = Mathf.Sqrt(2 * height / 9.81f);
            for (float t = 0; t < fall; t += Time.deltaTime)
            {
                drop.transform.position = Vector3.Lerp(from, to, t * t / (fall * fall));
                yield return null;
            }
            Destroy(drop);
        }
    }
}
