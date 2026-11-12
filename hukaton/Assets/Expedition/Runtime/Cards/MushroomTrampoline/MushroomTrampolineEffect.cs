using Hakaton.Decks.Unity;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>
    /// «Гриб-батут» (земля, ранг 3/5). A mushroom with a 1.6 m cap for 15 s. Where it grows
    /// (<see cref="MushroomPlacement"/>, playtest «смягчить ограничения»): on floors and slopes up to the player's slope
    /// limit; on a wall as a bracket shelf (<see cref="shelfPrefab"/>) with a level top, or on the ground in front of the
    /// wall when the hit is low; from a ceiling, a creature or a find on the ground below; on the top of a movable
    /// object such as the Stone cube it rides along. Not on deep water. A cramped spot moves up to 0.8 m or shrinks the
    /// cap to 70 %. Landing on the cap from above: no fall damage, the body is thrown up:
    /// h_eq = v²/2g, h = clamp(k·h_eq, 1.5, 10), v_up = √(2gh), g = 9.81 in design units, k = 0.8 (upgraded 1);
    /// a series of bounces from mushroom to mushroom adds 5 % of the first bounce each time, up to 25 %
    /// (<see cref="MushroomSeries"/>). Horizontal speed is kept. Only the player bounces in this version.
    /// See <see cref="MushroomTrampoline"/>. Rules and numbers: tmp/new-cards/cards.md; shared systems: tmp/new-cards/API.md.
    /// </summary>
    public sealed class MushroomTrampolineEffect : CardEffect, ICardStatLine
    {
        [Header("Board numbers (I / II / III)")]
        [Tooltip("Mushroom lifetime, s (same on both levels).")]
        [Min(0)] public float lifetimeSeconds = 15;
        [Tooltip("Share of the equivalent fall height returned (board: 80 % → 100 %).")]
        public LevelFloat heightReturn = new LevelFloat(0.8f, 0.9f, 1);
        [Tooltip("Bounce height limits over the cap, m (design units).")]
        public LevelFloat minBounceHeight = new LevelFloat(1.5f, 2.5f, 4);
        [Min(0)] public float maxBounceHeight = 10;
        [Tooltip("Cap diameter, m.")]
        [Min(0)] public float capDiameter = 1.6f;

        [Header("Series of bounces")]
        [Tooltip("Each next bounce of a series (mushroom to mushroom without standing on the ground) adds this share of the series' first bounce.")]
        [Range(0, 1)] public float seriesStepShare = MushroomBounce.SeriesStepShare;
        [Tooltip("Bounces after the first that still add height (5: up to +25 %, then it stays).")]
        [Min(0)] public int seriesMaxSteps = MushroomBounce.SeriesMaxSteps;

        [Header("Placement")]
        [Tooltip("Steepest surface that grows an ordinary mushroom, degrees (the player's slope limit). Steeper ones down to slight overhangs grow a wall shelf.")]
        [Range(0, 89)] public float maxSlope = 48;
        [Tooltip("Surfaces whose normal points down at least this much (y) are ceilings: the mushroom grows on the ground below.")]
        [Range(-1, 0)] public float ceilingNormal = -.3f;
        [Tooltip("A wall hit this low over the ground grows an ordinary mushroom on that ground instead of a shelf, m.")]
        [Min(0)] public float lowWallHeight = .6f;
        [Tooltip("That mushroom's stem stands this far out from the wall, m.")]
        [Min(0)] public float wallClearance = .6f;
        [Tooltip("From a ceiling, a creature or a find the ground is looked for this far down, m.")]
        [Min(0)] public float dropDistance = 4;
        [Tooltip("Water this shallow over the bottom still grows a mushroom from the bottom, m.")]
        [Min(0)] public float shallowWater = .35f;
        [Tooltip("Where even the neighbouring spots are cramped, the cap may shrink down to this share of its size.")]
        [Range(.3f, 1)] public float minCapScale = .7f;

        [Header("Prefabs")]
        public MushroomTrampoline mushroomPrefab;
        [Tooltip("The bracket shelf grown on walls (MushroomShape.WallShelf).")]
        public MushroomTrampoline shelfPrefab;

        [Header("Notices")]
        public string waterNotice = "Гриб не растёт на воде";
        public string noSupportNotice = "Грибу нужна твёрдая опора";
        public string ceilingNotice = "Под потолком грибу не за что зацепиться";
        public string crowdedNotice = "Грибу здесь тесно";

        public MushroomPlacementRules Rules => new MushroomPlacementRules
        {
            maxSlope = maxSlope,
            ceilingNormalY = ceilingNormal,
            lowWallHeight = lowWallHeight,
            wallClearance = wallClearance,
            dropDistance = dropDistance,
            shallowWater = shallowWater,
            minCapScale = minCapScale,
            floorRoomCentre = mushroomPrefab ? mushroomPrefab.roomCentre : MushroomTrampoline.DefaultRoomCentre,
            floorRoomSize = mushroomPrefab ? mushroomPrefab.roomSize : MushroomTrampoline.DefaultRoomSize,
            shelfRoomCentre = shelfPrefab ? shelfPrefab.roomCentre : MushroomPlacementRules.Default.shelfRoomCentre,
            shelfRoomSize = shelfPrefab ? shelfPrefab.roomSize : MushroomPlacementRules.Default.shelfRoomSize,
        };

        public override void Apply(in ImpactContext c)
        {
            var request = new MushroomPlacement.Request
            {
                target = c.Target,
                point = c.Point,
                normal = c.Normal,
                direction = c.Direction,
                living = c.Creature || c.HitCombatant != null || c.TargetAs<WorldLoot>() != null,
                yaw = Random.Range(0f, 360f),
            };
            if (!MushroomPlacement.TrySolve(request, Rules, out var spot, out var problem))
            {
                Feedback(c, false);
                string notice = Notice(problem);
                if (!string.IsNullOrEmpty(notice)) c.World.Announce(notice, 1.6f);
                return;
            }
            var prefab = spot.shelf && shelfPrefab ? shelfPrefab : mushroomPrefab;
            if (spot.shelf && !shelfPrefab)
            {
                Debug.LogWarning("MushroomTrampolineEffect: no shelf prefab assigned.", this);
                Feedback(c, false);
                return;
            }
            // A movable support (Stone cube, crate): the mushroom rides on it.
            var body = spot.support ? spot.support.attachedRigidbody : null;
            var parent = body ? body.transform : null;
            var mushroom = c.World.SpawnForOuting(prefab, spot.position, spot.rotation, parent);
            if (!mushroom) { Feedback(c, false); return; }
            if (parent)
            {
                // On a scaled support (a bigger cube) the mushroom keeps its own size.
                Vector3 lossy = parent.lossyScale;
                mushroom.transform.localScale = new Vector3(1 / Mathf.Max(.01f, lossy.x), 1 / Mathf.Max(.01f, lossy.y), 1 / Mathf.Max(.01f, lossy.z));
            }
            mushroom.Configure(lifetimeSeconds, c.At(heightReturn), c.At(minBounceHeight), maxBounceHeight, capDiameter * spot.capScale,
                seriesStepShare, seriesMaxSteps);
            mushroom.RestOn(spot.support);
            Feedback(c, true);
        }

        string Notice(MushroomPlacementProblem problem)
        {
            switch (problem)
            {
                case MushroomPlacementProblem.Water: return waterNotice;
                case MushroomPlacementProblem.NoSupport: return noSupportNotice;
                case MushroomPlacementProblem.Ceiling: return ceilingNotice;
                case MushroomPlacementProblem.Crowded: return crowdedNotice;
                default: return null;
            }
        }

        public bool TryGetStat(out string label, out LevelFloat values, out string unit)
        {
            label = "Возврат высоты";
            values = new LevelFloat(heightReturn.i * 100, heightReturn.ii * 100, heightReturn.iii * 100);
            unit = " %";
            return true;
        }
    }
}
