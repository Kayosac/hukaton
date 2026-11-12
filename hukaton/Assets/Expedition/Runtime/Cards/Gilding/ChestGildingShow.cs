using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The visible change of a gilded chest (prefab «Gilding show», spawned for the outing by <see cref="GildingEffect"/>).
    /// The chest shudders crookedly and sinks while golden sparks swirl round it; at <see cref="swapAt"/> it bursts and
    /// comes out as the richer model (<see cref="ChestLook.Copy"/>), springs up past its size and settles. It moves the
    /// chest only in LateUpdate, over the chest's own focus pulse, and lets go at once when the chest is opened.
    /// </summary>
    public sealed class ChestGildingShow : MonoBehaviour
    {
        [Header("Effects (Retro Arsenal)")]
        [Tooltip("Golden sparks round the chest during the change (looping; released at the end).")]
        public VfxBurst sparkle;
        public Color sparkleTint = new Color(1f, .85f, .35f);
        [Min(.01f)] public float sparkleScale = 1;
        [Tooltip("One-shot burst when the new model appears.")]
        public VfxBurst swapBurst;
        public Color swapTint = new Color(1f, .82f, .3f);
        [Min(.01f)] public float swapScale = 1;
        public SfxCue startCue, swapCue;

        [Header("Motion")]
        [Min(.2f)] public float seconds = 1.2f;
        [Tooltip("Share of the show at which the model changes.")]
        [Range(.1f, .9f)] public float swapAt = .45f;
        [Tooltip("Crooked shudder before the change, degrees.")]
        [Min(0)] public float wobble = 7;
        [Tooltip("It sinks to this share of its height before the change (and bulges a little).")]
        [Range(.5f, 1)] public float squash = .8f;
        [Tooltip("How far past its size it springs after the change (1 + this).")]
        [Range(0, 1)] public float pop = .25f;

        ExpeditionWorld world;
        TreasureChest chest, look;
        Quaternion rest;
        float time, seed;
        bool swapped, running;
        VfxBurst loop;

        /// <summary>Starts the change of <paramref name="chest"/>; <paramref name="look"/> null keeps its model (coins only).</summary>
        public void Play(ExpeditionWorld world, TreasureChest chest, TreasureChest look)
        {
            this.world = world;
            this.chest = chest;
            this.look = look;
            if (!world || !chest) { Destroy(gameObject); return; }
            rest = chest.transform.localRotation;
            seed = Random.value * 10;
            running = true;
            world.Play(startCue, chest.transform.position);
            loop = world.Spawn(sparkle, Middle, Quaternion.identity, sparkleTint, sparkleScale);
            if (loop)
            {
                loop.HoldFor(seconds + 1);
                loop.Follow(chest.transform, Middle - chest.transform.position);
            }
        }

        Vector3 Middle => chest.mouth ? chest.mouth.position : chest.transform.position + chest.transform.up * .3f;

        void LateUpdate()
        {
            if (!running) return;
            if (!chest || !chest.isActiveAndEnabled) { End(); return; }
            if (chest.Opened) { Swap(); End(); return; }

            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / seconds);
            float t = time + seed;
            var body = chest.transform;
            // TreasureChest.Update has just set this frame's scale (its focus pulse): shape on top of it, never accumulate.
            if (k < swapAt)
            {
                float a = k / swapAt, shake = wobble * a * a;
                body.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 41) * shake * .5f, Mathf.Sin(t * 23) * shake * .4f, Mathf.Sin(t * 53 + 1.3f) * shake);
                float sink = Mathf.Lerp(1, squash, a * a), bulge = 1 + (1 - sink) * .5f;
                body.localScale = Vector3.Scale(body.localScale, new Vector3(bulge, sink, bulge));
            }
            else
            {
                Swap();
                float b = (k - swapAt) / (1 - swapAt);
                // From squashed it springs up past its size, wobbles crooked and settles.
                float spring = 1 - (1 - squash) * Mathf.Cos(b * Mathf.PI * 2.5f) * Mathf.Exp(-b * 4) + pop * Mathf.Sin(b * Mathf.PI) * Mathf.Exp(-b * 2);
                float side = 1 + (1 - spring) * .4f;
                body.localScale = Vector3.Scale(body.localScale, new Vector3(side, spring, side));
                float lean = wobble * .6f * Mathf.Sin(b * Mathf.PI * 3) * (1 - b);
                body.localRotation = rest * Quaternion.Euler(lean * .3f, 0, lean);
            }
            if (k >= 1) End();
        }

        void Swap()
        {
            if (swapped || !chest) return;
            swapped = true;
            if (look) ChestLook.Copy(look, chest);
            world.Play(swapCue, Middle);
            world.Spawn(swapBurst, Middle, Quaternion.identity, swapTint, swapScale);
        }

        void End()
        {
            running = false;
            // An opened chest is the opening's to move; the scale goes back with the chest's own Update.
            if (chest && !chest.Opened) chest.transform.localRotation = rest;
            if (loop) loop.Release(.8f);
            Destroy(gameObject);
        }
    }
}
