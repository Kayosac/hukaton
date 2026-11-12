using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Кривое зеркало» on a Stone cube or a Trampoline mushroom (added on demand): it swells to the card's multiplier
    /// with a crooked wobble and stays that big until it goes (the end of the outing for a cube, the end of its life for
    /// a mushroom). Colliders grow with it: the cube's box (its root stays unscaled, as <see cref="StoneCubeBody"/>
    /// wants) and the mushroom's whole root with its zone. A second mirror does not stack: the larger multiplier wins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrookedMirrorGrowth : MonoBehaviour
    {
        [Tooltip("Crooked wobble of the visual while it grows: stretch per axis.")]
        [Range(0, .5f)] public float wobble = .18f;
        [Tooltip("Overshoot of the growth (out-back).")]
        [Min(0)] public float overshoot = 1.8f;

        StoneCubeBody cube;
        MushroomTrampoline mushroom;
        Vector3 authoredBox, authoredScale;
        float authoredRadius, authoredHeight, from = 1, to = 1, current = 1, changeAt, seconds = .8f, seed;
        bool captured;

        /// <summary>The size it grows (or grew) to, relative to before the first mirror.</summary>
        public float Factor => to;
        public float Current => current;

        public static CrookedMirrorGrowth On(GameObject target)
        {
            var growth = target.GetComponent<CrookedMirrorGrowth>();
            return growth ? growth : target.AddComponent<CrookedMirrorGrowth>();
        }

        /// <summary>Grows to <paramref name="factor"/> (no stacking). False when it is already that big or bigger.</summary>
        public bool Grow(StoneCubeBody stone, MushroomTrampoline cap, float factor, float growSeconds)
        {
            if (!captured)
            {
                captured = true;
                cube = stone;
                mushroom = cap;
                if (cube && cube.box) authoredBox = cube.box.size;
                authoredScale = transform.localScale;
                if (mushroom) { authoredRadius = mushroom.radius; authoredHeight = mushroom.height; }
                seed = Random.Range(0, 100f);
            }
            float next = CrookedMirrorRules.Growth(to, factor);
            if (next <= to + .001f) return false;
            from = current;
            to = next;
            changeAt = Time.time;
            seconds = Mathf.Max(.05f, growSeconds);
            enabled = true;
            return true;
        }

        void Update()
        {
            if (!captured) { enabled = false; return; }
            float age = Time.time - changeAt;
            float t = Mathf.Clamp01(age / seconds);
            current = t >= 1 ? to : Mathf.LerpUnclamped(from, to, Ease.OutBack(t, overshoot));
            // The wobble rings on a little after the size has settled.
            float ring = Mathf.Exp(-age * 2.5f / seconds) * wobble;
            var crook = new Vector3(1 + Mathf.Sin(age * 19 + seed) * ring, 1 - Mathf.Sin(age * 19 + seed) * ring * .8f, 1 + Mathf.Sin(age * 15 + seed * 3) * ring);
            Apply(current, crook);
            if (t >= 1 && ring < .002f) { Apply(to, Vector3.one); enabled = false; }
        }

        void Apply(float factor, Vector3 crook)
        {
            factor = Mathf.Max(.1f, factor);
            if (cube)
            {
                // The collider stays a cube; only the stone's look is crooked while it grows.
                if (cube.box) cube.box.size = authoredBox * factor;
                if (cube.visual) cube.visual.localScale = Vector3.Scale(authoredBox * factor, crook);
                if (cube.body) cube.body.WakeUp();
            }
            else if (mushroom)
            {
                transform.localScale = Vector3.Scale(authoredScale * factor, crook);
                mushroom.radius = authoredRadius * factor;
                mushroom.height = authoredHeight * factor;
            }
        }
    }
}
