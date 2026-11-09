using Hakaton.Decks.Unity;
using Hakaton.Feel;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Каменный куб» (земля, ранг 3/5). A movable stone cube 1.1 m (upgraded 1.47 m), mass μ = 1, until the end of the outing: a step, cover, an obstacle;
    /// the base cube is a step the player climbs with an ordinary jump (1.2 m), the upgraded one is 4/3 taller as on the board (1.5 → 2)
    /// and a wall to hide behind (owner, 09.11 evening: the board's 1.5 m was too tall to climb);
    /// physical effects move it (Impulse, Attraction), Swap may exchange places with it, a Rope may hang from it, a mushroom may stand on its top.
    /// It appears only where it does not intersect geometry (or the player): next to the surface that was hit, moved out along its
    /// normal when needed (<see cref="StoneCubePlacement"/>). No room — a miss.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class StoneCubeEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Cube side, m. Owner (09.11 evening): the base cube is a step for an ordinary 1.2 m jump — 1.1 m; " +
                 "the upgraded one keeps the board's growth 1.5 → 2 (×4/3) — 1.47 m, shown as 1,5.")]
        [Min(0)] public float sizeBase = 1.1f, sizeUpgraded = 1.47f;
        [Tooltip("Rigidbody mass = board μ (same on both levels; the player is μ = 1 too).")]
        [Min(0)] public float mass = 1;

        [Header("Prefab and placement")]
        public StoneCubeBody cubePrefab;
        [Tooltip("Random turn about the vertical, degrees: no two cubes stand exactly alike.")]
        [Min(0)] public float yawJitter = 14;
        [Tooltip("The free-space test shrinks the box by this much on each side (contact is fine, overlap is not).")]
        [Min(0)] public float overlapTolerance = .03f;
        [Tooltip("Dust and stones where the cube appears (Retro Arsenal).")]
        public VfxBurst appearVfx;
        public Color appearTint = new Color(.62f, .6f, .55f);
        [Tooltip("Shown briefly when there is no room for the cube.")]
        public string noRoomNotice = "Каменный куб: здесь нет места";

        public override void Apply(in ImpactContext c)
        {
            float side = c.Pick(sizeBase, sizeUpgraded);
            var rotation = StoneCubePlacement.Orientation(c.Direction, Random.Range(-yawJitter, yawJitter));
            var half = Vector3.one * Mathf.Max(.01f, side * .5f - overlapTolerance);
            // Everything solid counts, the player included (layer 2); cards and triggers do not.
            int mask = CardFlight.CollisionMask | (1 << 2);
            bool found = StoneCubePlacement.TryFind(c.Point, c.Normal, side,
                centre => !Physics.CheckBox(centre, half, rotation, mask, QueryTriggerInteraction.Ignore), out var position);
            if (!found || !cubePrefab)
            {
                if (!cubePrefab) Debug.LogWarning("StoneCubeEffect: no cube prefab assigned.", this);
                Feedback(c, false, .6f);
                if (!string.IsNullOrEmpty(noRoomNotice)) c.World.Announce(noRoomNotice, 1.4f);
                return;
            }
            var cube = c.World.SpawnForOuting(cubePrefab, position, rotation);
            if (!cube) return;
            cube.Configure(side, mass);
            c.World.Spawn(appearVfx, position - Vector3.up * side * .45f, Quaternion.identity, appearTint, side / 1.5f);
            Feedback(c, true);
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Сторона";
            baseValue = sizeBase;
            upgradedValue = sizeUpgraded;
            unit = " м";
            return true;
        }
    }
}
