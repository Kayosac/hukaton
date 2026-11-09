using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Гриб-батут» (земля, ранг 3/5). A mushroom with a 1.6 m cap for 15 s on an upper surface (floor, the top of a
    /// support, the top of a movable object such as the Stone cube — then it rides along). Not on walls, ceilings or
    /// living things. Landing on the cap from above: no fall damage, the body is thrown up:
    /// h_eq = v²/2g, h = clamp(k·h_eq, 1.5, 10), v_up = √(2gh), g = 9.81 in design units, k = 0.8 (upgraded 1).
    /// Horizontal speed is kept. Only the player bounces in this version. See <see cref="MushroomTrampoline"/>.
    /// Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class MushroomTrampolineEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (base / upgraded)")]
        [Tooltip("Mushroom lifetime, s (same on both levels).")]
        [Min(0)] public float lifetimeSeconds = 15;
        [Tooltip("Share of the equivalent fall height returned (board: 80 % → 100 %).")]
        [Min(0)] public float heightReturnBase = .8f, heightReturnUpgraded = 1;
        [Tooltip("Bounce height limits over the cap, m (design units).")]
        [Min(0)] public float minBounceHeight = 1.5f, maxBounceHeight = 10;
        [Tooltip("Cap diameter, m.")]
        [Min(0)] public float capDiameter = 1.6f;

        [Header("Placement")]
        [Tooltip("Steepest surface the mushroom grows on, degrees: floors and tops of supports, not walls or ceilings.")]
        [Range(0, 89)] public float maxSlope = 40;
        [Tooltip("Room the mushroom needs above the surface is checked against static geometry; this much is forgiven at the edges, m.")]
        [Min(0)] public float spaceTolerance = .15f;

        [Header("Prefab")]
        public MushroomTrampoline mushroomPrefab;

        public override void Apply(in ImpactContext c)
        {
            if (!TryPlace(c, out var position, out var rotation, out var parent, out string problem))
            {
                Feedback(c, false);
                c.World.Announce(problem, 1.6f);
                return;
            }
            var mushroom = c.World.SpawnForOuting(mushroomPrefab, position, rotation, parent);
            if (!mushroom) { Feedback(c, false); return; }
            if (parent)
            {
                // On a scaled support (a bigger cube) the mushroom keeps its own size.
                Vector3 lossy = parent.lossyScale;
                mushroom.transform.localScale = new Vector3(1 / Mathf.Max(.01f, lossy.x), 1 / Mathf.Max(.01f, lossy.y), 1 / Mathf.Max(.01f, lossy.z));
            }
            mushroom.Configure(lifetimeSeconds, c.Pick(heightReturnBase, heightReturnUpgraded), minBounceHeight, maxBounceHeight, capDiameter);
            mushroom.RestOn(c.Target);
            Feedback(c, true);
        }

        bool TryPlace(in ImpactContext c, out Vector3 position, out Quaternion rotation, out Transform parent, out string problem)
        {
            position = c.Point;
            rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            parent = null;
            problem = null;
            if (c.Normal.y < Mathf.Cos(maxSlope * Mathf.Deg2Rad))
            {
                problem = "Гриб растёт только на ровной опоре";
                return false;
            }
            if (c.Creature || c.HitCombatant != null || c.TargetAs<WorldLoot>() != null)
            {
                problem = "Грибу нужна твёрдая опора";
                return false;
            }
            // A movable support (Stone cube, crate): the mushroom rides on it.
            var body = c.Body;
            if (body) parent = body.transform;
            if (!HasRoom(c, body))
            {
                problem = "Грибу здесь тесно";
                return false;
            }
            return true;
        }

        /// <summary>Static geometry inside the stem and cap blocks the mushroom; loose bodies are pushed aside by physics.</summary>
        bool HasRoom(in ImpactContext c, Rigidbody support)
        {
            if (!mushroomPrefab) return true;
            float half = capDiameter * .5f - spaceTolerance;
            float top = mushroomPrefab.capTop;
            Vector3 centre = c.Point + Vector3.up * (top * .5f + spaceTolerance);
            Vector3 extents = new Vector3(half, Mathf.Max(.05f, top * .5f - spaceTolerance), half);
            var hits = Physics.OverlapBox(centre, extents, Quaternion.identity, ~(1 << 2), QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit == c.Target) continue;
                var body = hit.attachedRigidbody;
                if (body && (body == support || !body.isKinematic)) continue;
                return false;
            }
            return true;
        }

        public bool TryGetStat(out string label, out float baseValue, out float upgradedValue, out string unit)
        {
            label = "Возврат высоты";
            baseValue = heightReturnBase * 100;
            upgradedValue = heightReturnUpgraded * 100;
            unit = " %";
            return true;
        }
    }
}
