using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// The mark of «Мародёр» over a beast's head (prefab <c>Assets/Game/Prefabs/Cards/Marauder/Marauder mark.prefab</c>,
    /// a child of the creature): the card's crossed-out loot sack, crooked and swaying, just above its drawing. It blinks in
    /// its last seconds and shrinks away when it runs out. While it holds, the creature's death rolls its drop table
    /// <see cref="Rolls"/> times (<see cref="ILootRollBonus"/>); a beast that dies marked pays out with a burst of coins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarauderMark : MonoBehaviour, IOutingSpawned, ILootRollBonus
    {
        [Header("Authored parts")]
        [Tooltip("The drawing of the mark.")]
        public SpriteRenderer sack;

        [Header("Look")]
        [Tooltip("Gap between the top of the creature's drawing and the middle of the mark, m.")]
        public float lift = .4f;
        [Tooltip("Without a drawing: height of the mark above the creature's feet, m.")]
        public float fallbackHeight = 2.2f;
        [Tooltip("Closer than this the mark shrinks with distance, so it never swallows the screen, m.")]
        [Min(.1f)] public float fullSizeDistance = 4;
        [Tooltip("Crooked sway around the view axis: degrees and cycles per second.")]
        public float swayDegrees = 9, swaySpeed = .37f;
        [Tooltip("Bob up and down: metres and cycles per second.")]
        public float bobHeight = .05f, bobSpeed = .5f;
        [Tooltip("Pops in this fast (and pops again when renewed), s.")]
        [Min(.01f)] public float popSeconds = .25f;
        [Tooltip("Blinks during the last seconds of the mark: seconds and blinks per second.")]
        [Min(0)] public float warnSeconds = 3, blinkRate = 4;
        [Tooltip("Shrinks away this fast when it runs out, s.")]
        [Min(.01f)] public float fadeSeconds = .3f;

        [Header("Payout")]
        [Tooltip("Burst of coins where the mark was when the beast dies marked (Retro Arsenal).")]
        public VfxBurst payoutBurst;
        [Min(0)] public float payoutScale = 1;
        public Color payoutTint = new Color(1f, .82f, .35f);
        public SfxCue payoutSound;
        [Tooltip("After the beast dies the mark stays (hidden) this long so its loot is still counted, s.")]
        [Min(1)] public float lingerSeconds = 4;

        WorldCreature creature;
        ExpeditionWorld world;
        float expiresAt, poppedAt = -10, fadeFrom = -1;
        int rolls = 1;
        bool dead, paying;
        Vector3 baseScale = Vector3.one;
        float phase;

        public WorldCreature Creature => creature;
        /// <summary>Seconds left (0 once run out or the beast died).</summary>
        public float Remaining => dead ? 0 : Mathf.Max(0, expiresAt - Time.time);
        /// <summary>Rolls of the drop table if the beast dies while marked.</summary>
        public int Rolls => rolls;
        /// <summary>The mark holds: the beast is alive and the time has not run out.</summary>
        public bool Marked => !dead && Time.time < expiresAt;

        /// <inheritdoc/>
        public int LootRolls => MarauderRules.LootRolls(dead ? paying : Marked, rolls);

        /// <summary>The mark on this creature, if any.</summary>
        public static MarauderMark On(WorldCreature creature) => creature ? creature.GetComponentInChildren<MarauderMark>() : null;

        void IOutingSpawned.OnSpawned(ExpeditionWorld outing) => world = outing;

        void Awake()
        {
            baseScale = transform.localScale;
            phase = Random.value * 10;
        }

        /// <summary>Marks <paramref name="target"/> (or renews the mark) for <paramref name="seconds"/>.</summary>
        public void Mark(WorldCreature target, float seconds, int lootRolls)
        {
            if (!target) return;
            if (creature != target)
            {
                if (creature) creature.OutingReset -= Remove;
                creature = target;
                // The creature is reused (respawned) for another encounter: the mark does not come back with it.
                creature.OutingReset += Remove;
            }
            expiresAt = MarauderRules.Expiry(fadeFrom >= 0 ? 0 : expiresAt, Time.time, seconds);
            rolls = MarauderRules.Rolls(rolls, lootRolls);
            poppedAt = Time.time;
            fadeFrom = -1;
            if (sack) sack.enabled = true;
            Place(1);
        }

        void Remove()
        {
            if (this) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (creature) creature.OutingReset -= Remove;
        }

        void LateUpdate()
        {
            if (!creature) { Destroy(gameObject); return; }
            if (dead) return;
            float now = Time.time;
            if (!creature.Alive)
            {
                // Died this frame or the last: was it still marked? The loot drops a moment later, so the mark stays
                // (hidden) until then.
                dead = true;
                paying = MarauderRules.DiedMarked(now - Time.deltaTime, expiresAt) && fadeFrom < 0;
                if (sack) sack.enabled = false;
                if (paying && world)
                {
                    world.Spawn(payoutBurst, transform.position, Quaternion.identity, payoutTint, payoutScale);
                    world.Play(payoutSound, transform.position);
                }
                Destroy(gameObject, lingerSeconds);
                return;
            }
            float size = 1;
            if (fadeFrom < 0 && now >= expiresAt) fadeFrom = now;
            if (fadeFrom >= 0)
            {
                size = 1 - Mathf.Clamp01((now - fadeFrom) / fadeSeconds);
                if (size <= 0) { Destroy(gameObject); return; }
            }
            else
            {
                // Pops in with a little overshoot.
                float pop = Mathf.Clamp01((now - poppedAt) / popSeconds);
                size = pop < 1 ? Ease.OutBack(pop) : 1;
                if (sack)
                {
                    float left = expiresAt - now;
                    sack.enabled = left > warnSeconds || Mathf.Repeat(left * blinkRate, 1) < .6f;
                }
            }
            Place(size);
        }

        void Place(float size)
        {
            if (!creature) return;
            float t = Time.time + phase;
            // Just above the top of the drawing (the "!!" marks of small beasts sit much higher).
            Vector3 at = creature.view
                ? creature.Position + Vector3.up * (creature.view.height * creature.Size + lift)
                : creature.Position + Vector3.up * fallbackHeight;
            at += Vector3.up * Mathf.Sin(t * bobSpeed * Mathf.PI * 2) * bobHeight;
            transform.position = at;
            var viewer = world && world.viewCamera ? world.viewCamera : Camera.main;
            float near = 1;
            if (viewer)
            {
                Vector3 toMark = at - viewer.transform.position;
                if (toMark.sqrMagnitude > 1e-4f)
                {
                    // Crooked: never quite level, swaying around the view axis.
                    float tilt = Mathf.Sin(t * swaySpeed * Mathf.PI * 2) * swayDegrees + Mathf.Sin(t * swaySpeed * 4.7f) * swayDegrees * .35f;
                    transform.rotation = Quaternion.LookRotation(toMark) * Quaternion.Euler(0, 0, tilt);
                }
                near = Mathf.Clamp(toMark.magnitude / fullSizeDistance, .35f, 1);
            }
            transform.localScale = baseScale * Mathf.Max(0, size) * near;
        }
    }
}
