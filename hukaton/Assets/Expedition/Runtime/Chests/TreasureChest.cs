using System.Collections;
using System.Collections.Generic;
using Hakaton.ChestArt;
using Hakaton.Feel;
using Hakaton.Loot;
using UnityEngine;

namespace Hakaton.Expeditions
{
    /// <summary>
    /// A treasure chest lying in an outing. Aim at it and press E: it rattles, the lid flies open and coins burst
    /// out, scattered around and then pulled into the bag (<see cref="WorldCoin"/>). Everything about the tier —
    /// payout, coin kinds, Retro Arsenal effects, sounds — lives in its <see cref="ChestTier"/>. The model and lid
    /// come from ChestArt; this component sits on a prefab variant of the art prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreasureChest : MonoBehaviour
    {
        public ChestTier tier;
        public ChestLid lid;
        [Tooltip("Inside the chest, just under the lid: where the coins leave from.")]
        public Transform mouth;
        [Tooltip("Set by whoever spawns the chest (the outing's location).")]
        public ExpeditionWorld world;

        public bool Opened { get; private set; }
        public bool Available => !Opened && isActiveAndEnabled;
        /// <summary>Coins the last opening paid out (tests, debug).</summary>
        public int LastPayout { get; private set; }

        VfxBurst marker;
        bool focused;
        float focusPulse;
        Quaternion rest;
        Vector3 restScale;
        bool recorded;
        readonly List<(WorldCoin prefab, int value)> pieces = new List<(WorldCoin, int)>();
        readonly List<LootItem> extras = new List<LootItem>();

        Vector3 Mouth => mouth ? mouth.position : transform.position + transform.up * .3f;

        void Awake() => Record();

        void Start() => ShowMarker();

        void OnDisable()
        {
            if (marker) Destroy(marker.gameObject);
            marker = null;
        }

        void OnEnable() { if (recorded) ShowMarker(); }

        void Record()
        {
            if (recorded) return;
            recorded = true;
            rest = transform.localRotation;
            restScale = transform.localScale;
        }

        /// <summary>Shut again, as new: for a location that reuses its chests between outings.</summary>
        public void ResetForOuting()
        {
            Record();
            StopAllCoroutines();
            Opened = false;
            LastPayout = 0;
            transform.localRotation = rest;
            transform.localScale = restScale;
            if (lid) lid.SetOpen(false, true);
            ShowMarker();
        }

        /// <summary>Already opened (a chunk coming back after the player left it): lid up, nothing inside.</summary>
        public void SetEmpty()
        {
            Record();
            StopAllCoroutines();
            Opened = true;
            if (lid) lid.SetOpen(true, true);
            if (marker) Destroy(marker.gameObject);
            marker = null;
        }

        public void SetFocused(bool value) => focused = value;

        /// <summary>Opens the chest during an outing; false if it is already open or there is no outing.</summary>
        public bool Open()
        {
            if (Opened || !tier || !world || !world.Running) return false;
            Opened = true;
            StartCoroutine(Opening());
            return true;
        }

        void ShowMarker()
        {
            if (Opened || marker || !tier || !tier.idleMarker || !isActiveAndEnabled) return;
            marker = VfxBurst.Spawn(tier.idleMarker, transform.TransformPoint(tier.idleOffset), transform.rotation, transform, tier.idleTint, tier.idleScale);
            if (marker) marker.HoldFor(float.MaxValue / 4);
        }

        IEnumerator Opening()
        {
            focused = false;
            world.Play(tier.rattleCue, transform.position);
            float time = 0;
            while (time < tier.rattleSeconds)
            {
                time += Time.deltaTime;
                float fade = 1 - time / tier.rattleSeconds;
                float angle = Mathf.Sin(time * 70) * tier.rattleAngle * fade;
                transform.localRotation = rest * Quaternion.Euler(0, angle * .5f, angle);
                transform.localScale = restScale * (1 + Mathf.Abs(Mathf.Sin(time * 35)) * .04f * fade);
                yield return null;
            }
            transform.localRotation = rest;
            transform.localScale = restScale;

            if (marker) marker.Release(.4f);
            marker = null;
            if (lid) lid.Open();
            world.Play(tier.openCue, transform.position);
            if (world.feel)
            {
                if (tier.trauma > 0) world.feel.AddTrauma(tier.trauma);
                if (tier.fovPunch != 0) world.feel.PunchFov(tier.fovPunch);
            }
            if (world.time && tier.hitStop > 0) world.time.HitStop(tier.hitStop);
            if (!string.IsNullOrEmpty(tier.announcement)) world.Announce(tier.announcement, 2.5f);
            foreach (var burst in tier.bursts)
                if (burst.prefab) StartCoroutine(PlayBurst(burst));

            while (lid && lid.Openness < tier.spewAtOpenness) yield return null;

            if (tier.openGlow)
            {
                var glow = world.Spawn(tier.openGlow, Mouth, transform.rotation, tier.openGlowTint, tier.openGlowScale);
                if (glow) StartCoroutine(ReleaseLater(glow, tier.openGlowSeconds));
            }

            System.Func<float> next = () => Random.value;
            LastPayout = tier.RollCoins(next);
            pieces.Clear();
            tier.Split(LastPayout, next, pieces);
            extras.Clear();
            if (tier.bonus != null) tier.bonus.Roll(next, extras);

            int total = pieces.Count + extras.Count;
            float gap = total > 1 ? tier.spewSeconds / (total - 1) : 0;
            int launched = 0;
            foreach (var item in extras)
            {
                var drop = world.SpawnLoot(item, Mouth);
                if (drop) drop.Pop(LaunchVelocity() * .8f);
                launched++;
                if (gap > 0) yield return new WaitForSeconds(gap);
            }
            for (int i = 0; i < pieces.Count; i++)
            {
                SpawnCoin(pieces[i].prefab, pieces[i].value);
                if (launched++ % tier.spewCueEvery == 0)
                    world.Play(tier.spewCue, Mouth, 1, 1 + Random.Range(-.06f, .1f));
                if (gap > 0 && i < pieces.Count - 1) yield return new WaitForSeconds(gap);
            }
        }

        IEnumerator PlayBurst(ChestTier.Burst burst)
        {
            if (burst.delay > 0) yield return new WaitForSeconds(burst.delay);
            Vector3 at = Mouth + transform.rotation * burst.offset;
            var effect = world.Spawn(burst.prefab, at, transform.rotation, burst.tint, burst.scale > 0 ? burst.scale : 1);
            if (effect && burst.hold > 0) StartCoroutine(ReleaseLater(effect, burst.hold));
        }

        static IEnumerator ReleaseLater(VfxBurst effect, float seconds)
        {
            effect.HoldFor(seconds + 1);
            yield return new WaitForSeconds(seconds);
            if (effect) effect.Release(.6f);
        }

        void SpawnCoin(WorldCoin prefab, int value)
        {
            if (!prefab) return;
            Vector3 jitter = Random.insideUnitSphere * .08f;
            var coin = Instantiate(prefab, Mouth + jitter, Random.rotation, world.spawnedObjects);
            coin.Launch(world, value, LaunchVelocity());
        }

        /// <summary>Up and a little towards the front of the chest (-z), inside the tier's cone.</summary>
        Vector3 LaunchVelocity()
        {
            Vector3 axis = (transform.up + -transform.forward * .35f).normalized;
            Vector3 direction = Quaternion.AngleAxis(Random.Range(0, 360f), axis)
                                * Quaternion.AngleAxis(Random.Range(0, tier.spreadAngle), Vector3.Cross(axis, transform.right).normalized)
                                * axis;
            return direction * Random.Range(tier.launchSpeed.x, tier.launchSpeed.y);
        }

        void Update()
        {
            if (Opened || !recorded) return;
            // Aimed at: a soft breathing pulse, like a find under the crosshair.
            focusPulse = Mathf.MoveTowards(focusPulse, focused ? 1 : 0, Time.deltaTime * 6);
            transform.localScale = restScale * (1 + focusPulse * (.035f + Mathf.Sin(Time.time * 9) * .015f));
            focused = false;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .8f, .3f, .9f);
            Gizmos.DrawWireSphere(Mouth, .06f);
            if (!tier) return;
            Vector3 axis = (transform.up + -transform.forward * .35f).normalized;
            Gizmos.DrawRay(Mouth, axis * .8f);
        }
#endif
    }
}
