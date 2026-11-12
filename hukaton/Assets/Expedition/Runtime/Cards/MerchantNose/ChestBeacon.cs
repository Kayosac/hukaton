using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// A tall pillar of light on a shut chest (prefab «Chest beacon», Merchant's nose), seen through walls (its material
    /// draws dimmer where something stands in front). It follows the chest upright — a hopping mimic too — and goes out
    /// after its time or as soon as the chest is opened, switched off or gone. Under it a mimic now and then gives one
    /// small shiver (<see cref="ChestMimic.Startle"/>): the same rare, subtle tell as close up, nothing louder.
    /// </summary>
    [RequireComponent(typeof(VfxBurst))]
    public sealed class ChestBeacon : MonoBehaviour
    {
        [Tooltip("The pillar's particle systems (Retro Arsenal loot ray, stretched tall).")]
        public VfxBurst burst;
        public Color tint = new Color(1f, .86f, .45f);
        [Tooltip("The pillar's foot above the chest's origin, m.")]
        public float footHeight = .05f;
        [Tooltip("Fade when it goes out, s.")]
        [Min(0)] public float fadeSeconds = 1.2f;
        [Tooltip("A soft chime where the pillar rises.")]
        public SfxCue lightCue;
        [Range(0, 1)] public float lightVolume = .5f;

        static readonly List<ChestBeacon> Live = new List<ChestBeacon>();

        TreasureChest chest;
        ChestMimic mimic;
        float until, nextShiver = float.MaxValue;
        Vector2 shiverEvery;
        bool lit, fading;

        public TreasureChest Chest => chest;
        public bool Lit => lit && !fading;
        public float Remaining => Lit ? Mathf.Max(0, until - Time.time) : 0;

        /// <summary>The pillar burning on this chest now, or null.</summary>
        public static ChestBeacon On(TreasureChest chest)
        {
            foreach (var beacon in Live) if (beacon && beacon.chest == chest && beacon.Lit) return beacon;
            return null;
        }

        /// <summary>
        /// Lights a pillar on <paramref name="chest"/> for <paramref name="seconds"/> (one that burns already lasts longer
        /// instead). On a mimic the first shiver comes after <paramref name="firstShiver"/> s, then every <paramref name="shiverEvery"/> s.
        /// </summary>
        public static ChestBeacon Light(ExpeditionWorld world, ChestBeacon prefab, TreasureChest chest, float seconds, Vector2 firstShiver, Vector2 shiverEvery)
        {
            if (!world || !chest) return null;
            var beacon = On(chest);
            if (beacon)
            {
                beacon.until = MerchantNoseRules.Until(beacon.until, Time.time, seconds);
                beacon.burst.HoldFor(beacon.until - Time.time + beacon.fadeSeconds + 1);
                return beacon;
            }
            if (!prefab) return null;
            beacon = world.SpawnForOuting(prefab, chest.transform.position + Vector3.up * prefab.footHeight, Quaternion.identity);
            if (beacon) beacon.Begin(world, chest, seconds, firstShiver, shiverEvery);
            return beacon;
        }

        void Reset() => burst = GetComponent<VfxBurst>();

        void OnEnable() => Live.Add(this);
        void OnDisable() => Live.Remove(this);

        void Begin(ExpeditionWorld world, TreasureChest chest, float seconds, Vector2 firstShiver, Vector2 every)
        {
            if (!burst) burst = GetComponent<VfxBurst>();
            this.chest = chest;
            mimic = chest.GetComponent<ChestMimic>();
            until = Time.time + seconds;
            shiverEvery = every;
            if (mimic) nextShiver = Time.time + Random.Range(firstShiver.x, firstShiver.y);
            lit = true;
            burst.Play(tint);
            burst.HoldFor(seconds + fadeSeconds + 1);
            world.Play(lightCue, transform.position, lightVolume, Random.Range(.95f, 1.08f));
        }

        void LateUpdate()
        {
            if (!lit || fading) return;
            if (!chest || chest.Opened || !chest.isActiveAndEnabled || Time.time >= until)
            {
                fading = true;
                burst.Release(fadeSeconds);
                return;
            }
            // Follows the chest but stays upright: a tilting pillar would shout what a mimic only whispers.
            transform.SetPositionAndRotation(chest.transform.position + Vector3.up * footHeight, Quaternion.identity);
            if (mimic && Time.time >= nextShiver)
            {
                mimic.Startle();
                nextShiver = Time.time + Random.Range(shiverEvery.x, shiverEvery.y);
            }
        }
    }
}
