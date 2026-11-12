using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>What the card hit, by the surface's normal.</summary>
    public enum MushroomSurface { Floor, Wall, Ceiling }

    /// <summary>Why no mushroom could grow (the effect turns it into a short notice).</summary>
    public enum MushroomPlacementProblem { None, Water, NoSupport, Ceiling, Crowded }

    /// <summary>Tuning of <see cref="MushroomPlacement"/> (fields of <see cref="MushroomTrampolineEffect"/>; defaults for tests).</summary>
    public struct MushroomPlacementRules
    {
        /// <summary>Steepest surface that still counts as a floor, degrees (the player's slope limit).</summary>
        public float maxSlope;
        /// <summary>Normals with y at or below this face down: a ceiling (above it, a steep surface is a wall).</summary>
        public float ceilingNormalY;
        /// <summary>A wall hit this low over the ground grows an ordinary mushroom on that ground, m.</summary>
        public float lowWallHeight;
        /// <summary>That mushroom's stem stands this far out from the wall, m.</summary>
        public float wallClearance;
        /// <summary>From a ceiling, a creature or a find, the ground is looked for this far down, m.</summary>
        public float dropDistance;
        /// <summary>Water this shallow over solid ground is no obstacle: the mushroom grows from the bottom, m.</summary>
        public float shallowWater;
        /// <summary>When no spot has room, the cap may shrink down to this share of its size.</summary>
        public float minCapScale;
        /// <summary>Boxes (local centre and size at full cap size) that must be clear of static geometry.</summary>
        public Vector3 floorRoomCentre, floorRoomSize, shelfRoomCentre, shelfRoomSize;

        public static MushroomPlacementRules Default => new MushroomPlacementRules
        {
            maxSlope = 48,
            ceilingNormalY = -.3f,
            lowWallHeight = .6f,
            wallClearance = .6f,
            dropDistance = 4,
            shallowWater = .35f,
            minCapScale = .7f,
            floorRoomCentre = MushroomTrampoline.DefaultRoomCentre,
            floorRoomSize = MushroomTrampoline.DefaultRoomSize,
            shelfRoomCentre = new Vector3(0, .15f, .45f),
            shelfRoomSize = new Vector3(.9f, .6f, .6f),
        };
    }

    /// <summary>Where and how the mushroom grows.</summary>
    public struct MushroomSpot
    {
        /// <summary>A bracket shelf on a wall instead of a mushroom on a stem.</summary>
        public bool shelf;
        /// <summary>The foot of the stem, or the shelf's hinge on the wall surface.</summary>
        public Vector3 position;
        /// <summary>Upright; a shelf faces out of the wall (local +Z along the wall's level normal).</summary>
        public Quaternion rotation;
        /// <summary>The surface it grows on (it wilts when that goes; a body under it carries it along).</summary>
        public Collider support;
        /// <summary>Share of the full cap size (1, or less where it is cramped).</summary>
        public float capScale;
    }

    /// <summary>
    /// Where the trampoline mushroom grows (playtest: «смягчить ограничения по спавну»). Floors and slopes up to the
    /// player's slope limit grow the usual mushroom; steeper surfaces down to slight overhangs grow a bracket shelf on
    /// the wall — unless the hit is low over the ground, then the mushroom stands on that ground in front of the wall.
    /// A ceiling, a creature or a find sends the mushroom to the ground below. Water is no support (a shallow bottom
    /// is). Only the stem column (or the shelf's body) must be clear of static geometry: the cap may clip into trunks
    /// and rocks. A cramped spot tries eight neighbours 0.4 and 0.8 m away, then a smaller cap (down to 70 %).
    /// Real physics queries; covered by EditMode tests.
    /// </summary>
    public static class MushroomPlacement
    {
        /// <summary>One impact.</summary>
        public struct Request
        {
            public Collider target;
            public Vector3 point, normal, direction;
            /// <summary>The hit is a creature, an ally or a find (never a support itself).</summary>
            public bool living;
            /// <summary>Yaw of an upright mushroom, degrees.</summary>
            public float yaw;
        }

        /// <summary>Candidates per spot: the spot itself and eight neighbours.</summary>
        public const int CandidateCount = 9;
        /// <summary>Cap sizes tried, from full to <see cref="MushroomPlacementRules.minCapScale"/>.</summary>
        public const int ScaleSteps = 3;

        static readonly RaycastHit[] Hits = new RaycastHit[24];
        static readonly Collider[] Overlaps = new Collider[32];

        static int WaterMask
        {
            get
            {
                int water = LayerMask.NameToLayer("Water");
                return water >= 0 ? 1 << water : 0;
            }
        }

        /// <summary>Everything a mushroom can stand on or be blocked by: what cards hit, minus water.</summary>
        static int SolidMask => CardFlight.CollisionMask & ~WaterMask;

        // ---------- pure rules ----------

        public static MushroomSurface Classify(Vector3 normal, float maxSlope, float ceilingNormalY)
        {
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            if (n.y >= Mathf.Cos(Mathf.Clamp(maxSlope, 0, 89) * Mathf.Deg2Rad) - 1e-5f) return MushroomSurface.Floor;
            return n.y > ceilingNormalY ? MushroomSurface.Wall : MushroomSurface.Ceiling;
        }

        /// <summary>The wall's normal laid level; <paramref name="fallback"/> (for example against the throw) when the normal is vertical.</summary>
        public static Vector3 LevelNormal(Vector3 normal, Vector3 fallback)
        {
            var flat = new Vector3(normal.x, 0, normal.z);
            if (flat.sqrMagnitude > 1e-4f) return flat.normalized;
            flat = new Vector3(fallback.x, 0, fallback.z);
            return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        }

        /// <summary>A shelf faces straight out of the wall and keeps its top level.</summary>
        public static Quaternion ShelfRotation(Vector3 wallNormal) => Quaternion.LookRotation(LevelNormal(wallNormal, Vector3.forward), Vector3.up);

        /// <summary>Cap scale of attempt <paramref name="step"/> (0 … <see cref="ScaleSteps"/> − 1): 1, halfway, the minimum.</summary>
        public static float CapScale(int step, float minScale)
            => Mathf.Lerp(1, Mathf.Clamp(minScale, .1f, 1), ScaleSteps > 1 ? Mathf.Clamp01(step / (float)(ScaleSteps - 1)) : 0);

        /// <summary>
        /// Neighbour <paramref name="index"/> of a floor spot: the spot; 0.4 m back toward the thrower, to the sides,
        /// away; then the same at 0.8 m.
        /// </summary>
        public static Vector3 FloorOffset(int index, Vector3 throwDirection)
        {
            if (index <= 0) return Vector3.zero;
            Vector3 back = -LevelNormal(throwDirection, Vector3.forward);
            Vector3 side = Vector3.Cross(Vector3.up, back);
            float distance = index <= 4 ? .4f : .8f;
            switch ((index - 1) % 4)
            {
                case 0: return back * distance;
                case 1: return side * distance;
                case 2: return -side * distance;
                default: return -back * distance;
            }
        }

        static readonly Vector2[] ShelfSteps =
        {
            new Vector2(0, 0), new Vector2(.4f, 0), new Vector2(-.4f, 0), new Vector2(0, -.4f), new Vector2(0, .4f),
            new Vector2(.8f, 0), new Vector2(-.8f, 0), new Vector2(.4f, -.4f), new Vector2(-.4f, -.4f),
        };

        /// <summary>Neighbour <paramref name="index"/> of a shelf spot along the wall: sideways first, then lower, higher, further.</summary>
        public static Vector3 ShelfOffset(int index, Vector3 wallNormal)
        {
            var step = ShelfSteps[Mathf.Clamp(index, 0, ShelfSteps.Length - 1)];
            Vector3 along = Vector3.Cross(Vector3.up, LevelNormal(wallNormal, Vector3.forward));
            return along * step.x + Vector3.up * step.y;
        }

        // ---------- the search ----------

        public static bool TrySolve(in Request request, in MushroomPlacementRules rules, out MushroomSpot spot, out MushroomPlacementProblem problem)
        {
            spot = default;
            problem = MushroomPlacementProblem.None;
            var target = request.target;
            Vector3 point = request.point;
            Vector3 normal = request.normal.sqrMagnitude > 1e-6f ? request.normal.normalized : Vector3.up;

            if (target && IsWater(target))
            {
                // Water: only a shallow bottom right under the surface carries a mushroom.
                var below = FindBelow(point + Vector3.up * .05f, rules.shallowWater + .1f, rules, out var bottom);
                if (below == Below.Ground && TryFloor(bottom.point, bottom.collider, true, request, rules, out spot)) return true;
                problem = below == Below.Ground ? MushroomPlacementProblem.Crowded : MushroomPlacementProblem.Water;
                return false;
            }

            if (request.living || (target && !CanHold(target)))
            {
                // A creature, an ally, a find: the mushroom grows on the ground under it (beside it when it stands there).
                var below = FindBelow(point + Vector3.up * .3f, rules.dropDistance + .3f, rules, out var ground);
                if (below == Below.Ground) return Floor(ground.point, ground.collider, false, request, rules, out spot, out problem);
                problem = below == Below.Water ? MushroomPlacementProblem.Water : MushroomPlacementProblem.NoSupport;
                return false;
            }

            switch (Classify(normal, rules.maxSlope, rules.ceilingNormalY))
            {
                case MushroomSurface.Floor:
                    return Floor(point, target, true, request, rules, out spot, out problem);

                case MushroomSurface.Wall:
                {
                    Vector3 level = LevelNormal(normal, -request.direction);
                    // Low on a wall: an ordinary mushroom on the ground in front of it is easier to use.
                    var below = FindBelow(point + level * .15f + Vector3.up * .05f, rules.lowWallHeight + .05f, rules, out var ground);
                    if (below == Below.Ground)
                    {
                        Vector3 foot = ground.point + level * Mathf.Max(0, rules.wallClearance - .15f);
                        if (TryFloor(foot, null, false, request, rules, out spot)) return true;
                    }
                    if (TryShelf(point, normal, target, request, rules, out spot)) return true;
                    problem = MushroomPlacementProblem.Crowded;
                    return false;
                }

                default:
                {
                    var below = FindBelow(point + normal * .15f, rules.dropDistance, rules, out var ground);
                    if (below == Below.Ground) return Floor(ground.point, ground.collider, false, request, rules, out spot, out problem);
                    problem = below == Below.Water ? MushroomPlacementProblem.Water : MushroomPlacementProblem.Ceiling;
                    return false;
                }
            }
        }

        static bool Floor(Vector3 point, Collider support, bool exact, in Request request, in MushroomPlacementRules rules,
            out MushroomSpot spot, out MushroomPlacementProblem problem)
        {
            bool found = TryFloor(point, support, exact, request, rules, out spot);
            problem = found ? MushroomPlacementProblem.None : MushroomPlacementProblem.Crowded;
            return found;
        }

        /// <summary>
        /// An upright mushroom at <paramref name="point"/> or a neighbour. <paramref name="exact"/>: the point is on the
        /// support surface already (a direct hit); otherwise every candidate first finds the ground under it.
        /// </summary>
        static bool TryFloor(Vector3 point, Collider support, bool exact, in Request request, in MushroomPlacementRules rules, out MushroomSpot spot)
        {
            var rotation = Quaternion.Euler(0, request.yaw, 0);
            for (int step = 0; step < ScaleSteps; step++)
            {
                float scale = CapScale(step, rules.minCapScale);
                for (int i = 0; i < CandidateCount; i++)
                {
                    Vector3 foot;
                    Collider holder;
                    if (i == 0 && exact && support) { foot = point; holder = support; }
                    else
                    {
                        Vector3 probe = point + FloorOffset(i, request.direction);
                        if (FindBelow(probe + Vector3.up * .6f, 1.4f, rules, out var ground) != Below.Ground) continue;
                        foot = ground.point;
                        holder = ground.collider;
                    }
                    if (!HasRoom(foot, rotation, rules.floorRoomCentre, rules.floorRoomSize, scale, holder)) continue;
                    spot = new MushroomSpot { shelf = false, position = foot, rotation = rotation, support = holder, capScale = scale };
                    return true;
                }
            }
            spot = default;
            return false;
        }

        /// <summary>A shelf on the wall at <paramref name="point"/> or a neighbour along the wall.</summary>
        static bool TryShelf(Vector3 point, Vector3 normal, Collider wall, in Request request, in MushroomPlacementRules rules, out MushroomSpot spot)
        {
            Vector3 level = LevelNormal(normal, -request.direction);
            for (int step = 0; step < ScaleSteps; step++)
            {
                float scale = CapScale(step, rules.minCapScale);
                for (int i = 0; i < CandidateCount; i++)
                {
                    Vector3 hinge = point, surfaceNormal = normal;
                    Collider holder = wall;
                    if (i > 0)
                    {
                        // Find the wall surface at the neighbour: look into it from just in front.
                        Vector3 probe = point + ShelfOffset(i, level) + level * .6f;
                        if (!Physics.Raycast(probe, -level, out var hit, 1.1f, CardFlight.CollisionMask, QueryTriggerInteraction.Ignore)) continue;
                        if (IsWater(hit.collider) || !CanHold(hit.collider)) continue;
                        if (Classify(hit.normal, rules.maxSlope, rules.ceilingNormalY) != MushroomSurface.Wall) continue;
                        hinge = hit.point;
                        surfaceNormal = hit.normal;
                        holder = hit.collider;
                    }
                    var rotation = ShelfRotation(LevelNormal(surfaceNormal, level));
                    if (!HasRoom(hinge, rotation, rules.shelfRoomCentre, rules.shelfRoomSize, scale, holder)) continue;
                    spot = new MushroomSpot { shelf = true, position = hinge, rotation = rotation, support = holder, capScale = scale };
                    return true;
                }
            }
            spot = default;
            return false;
        }

        // ---------- physics ----------

        enum Below { None, Ground, Water }

        /// <summary>
        /// The first solid surface under <paramref name="origin"/> within <paramref name="distance"/>: ground when it is
        /// flat enough and not under deeper than shallow water. Living things and finds are looked past.
        /// </summary>
        static Below FindBelow(Vector3 origin, float distance, in MushroomPlacementRules rules, out RaycastHit ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, Mathf.Max(.01f, distance), CardFlight.CollisionMask, QueryTriggerInteraction.Ignore);
            SortByDistance(count);
            bool water = false;
            float waterLevel = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (IsWater(hit.collider))
                {
                    if (!water) { water = true; waterLevel = hit.point.y; }
                    continue;
                }
                if (!CanHold(hit.collider)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > rules.maxSlope + .01f) return water ? Below.Water : Below.None;
                ground = hit;
                return water && waterLevel - hit.point.y > rules.shallowWater ? Below.Water : Below.Ground;
            }
            return water ? Below.Water : Below.None;
        }

        static void SortByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                var hit = Hits[i];
                int j = i - 1;
                while (j >= 0 && Hits[j].distance > hit.distance) { Hits[j + 1] = Hits[j]; j--; }
                Hits[j + 1] = hit;
            }
        }

        /// <summary>
        /// The room box (local to the mushroom, its width and depth scaled with the cap) holds no static geometry but the
        /// support. Loose bodies are pushed aside by physics; the player is not on a queried layer.
        /// </summary>
        public static bool HasRoom(Vector3 position, Quaternion rotation, Vector3 roomCentre, Vector3 roomSize, float capScale, Collider support)
        {
            var scale = new Vector3(capScale, 1, capScale);
            Vector3 centre = position + rotation * Vector3.Scale(roomCentre, scale);
            Vector3 half = Vector3.Scale(roomSize, scale) * .5f;
            half = new Vector3(Mathf.Max(.01f, half.x), Mathf.Max(.01f, half.y), Mathf.Max(.01f, half.z));
            int count = Physics.OverlapBoxNonAlloc(centre, half, Overlaps, rotation, SolidMask, QueryTriggerInteraction.Ignore);
            var supportBody = support ? support.attachedRigidbody : null;
            for (int i = 0; i < count; i++)
            {
                var hit = Overlaps[i];
                if (!hit || hit == support) continue;
                var body = hit.attachedRigidbody;
                if (body && (body == supportBody || !body.isKinematic)) continue;
                return false;
            }
            return true;
        }

        static bool IsWater(Collider collider)
        {
            int mask = WaterMask;
            return collider && mask != 0 && (mask & (1 << collider.gameObject.layer)) != 0;
        }

        /// <summary>Solid, not living, not a find, not a card: something a mushroom can grow on.</summary>
        static bool CanHold(Collider collider)
        {
            if (!collider || collider.isTrigger) return false;
            if (Combat.Of(collider) != null) return false;
            if (collider.GetComponentInParent<WorldCreature>() || collider.GetComponentInParent<WorldLoot>()) return false;
            return !collider.GetComponentInParent<CardProjectile>();
        }
    }
}
