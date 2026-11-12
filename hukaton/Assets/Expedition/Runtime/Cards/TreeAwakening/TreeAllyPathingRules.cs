using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>How the tree gets to a crumb of the player's trail from the crumb before it.</summary>
    public enum CrumbKind : byte
    {
        /// <summary>The player walked here: so can the tree.</summary>
        Walk,
        /// <summary>The player jumped or dropped here from the crumb before: the tree jumps the same arc.</summary>
        Jump,
        /// <summary>No way the tree can follow (teleport, swap, rope, updraft, a lift too high): it burrows instead.</summary>
        Gap,
    }

    /// <summary>One point of the player's trail: where the feet were on the ground.</summary>
    public struct TrailCrumb
    {
        public Vector3 Position;
        public CrumbKind Kind;
        /// <summary>For a jump: the highest the feet were in the air between the take-off and this landing.</summary>
        public float Apex;
    }

    /// <summary>Thresholds of the trail recorder (the player's jumps the tree can repeat and what is a gap).</summary>
    [Serializable]
    public struct TrailRules
    {
        [Tooltip("A crumb every this many metres while the player walks.")]
        public float spacing;
        [Tooltip("The feet moved farther than this in one frame (teleport, swap): a gap, m.")]
        public float teleportDistance;
        [Tooltip("…or faster than this between frames (and more than 1.2 m), m/s.")]
        public float teleportSpeed;
        [Tooltip("The tree jumps at most this high above its take-off (the player jumps 1.2 m): higher lifts are gaps, m.")]
        public float maxJumpRise;
        [Tooltip("The tree's fastest horizontal jump speed: farther flights are gaps, m/s.")]
        public float maxJumpSpeed;
        [Tooltip("A ride (rope, updraft, bounce) that came back down this close to where it left is no gap, m.")]
        public float nearReturn;

        public static TrailRules Default => new TrailRules
        {
            spacing = .5f, teleportDistance = 3, teleportSpeed = 50, maxJumpRise = 1.6f, maxJumpSpeed = 9, nearReturn = 1.5f,
        };
    }

    /// <summary>
    /// The player's trail for the awakened tree: a ring of the last crumbs where the player's feet were on the ground
    /// (every <see cref="TrailRules.spacing"/> m), with the jumps between them (take-off crumb, then the landing crumb
    /// marked <see cref="CrumbKind.Jump"/> with the jump's apex) and the places the tree cannot follow
    /// (<see cref="CrumbKind.Gap"/>). Crumbs are numbered for ever; only the last <see cref="Capacity"/> are kept.
    /// Fed once a frame with the player's feet (<see cref="Observe"/>); pure, so it is tested without a scene.
    /// </summary>
    public sealed class TreeAllyTrail
    {
        public const int DefaultCapacity = 256;

        public TrailRules Rules = TrailRules.Default;
        readonly TrailCrumb[] crumbs;

        bool started, airborne, pendingGap;
        Vector3 last, lastGround, takeOff, gapFrom;
        float apex, airSince;

        public TreeAllyTrail(int capacity = DefaultCapacity) => crumbs = new TrailCrumb[Mathf.Max(4, capacity)];

        public int Capacity => crumbs.Length;
        /// <summary>Number of the oldest crumb still kept.</summary>
        public int First { get; private set; }
        /// <summary>One past the number of the newest crumb.</summary>
        public int End { get; private set; }
        public int Count => End - First;
        /// <summary>Number of the newest gap crumb, or −1.</summary>
        public int LastGap { get; private set; } = -1;
        public bool Has(int index) => index >= First && index < End;
        public TrailCrumb this[int index] => crumbs[index % crumbs.Length];
        public TrailCrumb Newest => this[End - 1];

        public void Clear()
        {
            First = End = 0;
            LastGap = -1;
            started = airborne = pendingGap = false;
        }

        public void Add(Vector3 position, CrumbKind kind, float jumpApex)
        {
            crumbs[End % crumbs.Length] = new TrailCrumb { Position = position, Kind = kind, Apex = jumpApex };
            if (kind == CrumbKind.Gap) LastGap = End;
            End++;
            if (End - First > crumbs.Length) First = End - crumbs.Length;
            if (LastGap < First) LastGap = -1;
        }

        /// <summary>
        /// The player's feet this frame. <paramref name="grounded"/>: standing on something; <paramref name="carried"/>:
        /// something else moves the body (rope). <paramref name="gravity"/>: the tree's, to judge which jumps it can repeat.
        /// </summary>
        public void Observe(Vector3 feet, bool grounded, bool carried, float time, float deltaTime, float gravity)
        {
            if (!started)
            {
                started = true;
                last = lastGround = feet;
                if (carried) MarkGap();
                else if (grounded) Add(feet, CrumbKind.Walk, feet.y);
                else Leave(feet, time);
                return;
            }
            float moved = (feet - last).magnitude;
            last = feet;
            float leap = Mathf.Min(Rules.teleportDistance, Mathf.Max(1.2f, Rules.teleportSpeed * Mathf.Max(0, deltaTime)));
            if (moved > leap) MarkGap();
            if (carried)
            {
                MarkGap();
                airborne = false;
                return;
            }
            if (!grounded)
            {
                if (!airborne) Leave(feet, time);
                else apex = Mathf.Max(apex, feet.y);
                return;
            }

            if (pendingGap)
            {
                pendingGap = false;
                airborne = false;
                if (!Near(gapFrom, feet, Rules.nearReturn))
                {
                    Add(feet, CrumbKind.Gap, feet.y);
                    lastGround = feet;
                    return;
                }
            }
            else if (airborne)
            {
                airborne = false;
                // Stumbling over a bump or down a step is walking.
                bool stumble = time - airSince < .25f && Mathf.Abs(feet.y - takeOff.y) < .5f && apex - takeOff.y < .35f;
                if (!stumble && !Near(takeOff, feet, .8f))
                {
                    var kind = TreeAllyPathing.CanJump(takeOff, feet, apex, gravity, Rules) ? CrumbKind.Jump : CrumbKind.Gap;
                    if (Count == 0 || (Newest.Position - takeOff).sqrMagnitude > .0025f) Add(takeOff, CrumbKind.Walk, takeOff.y);
                    Add(feet, kind, apex);
                    lastGround = feet;
                    return;
                }
            }
            lastGround = feet;
            if (Count == 0 || (feet - Newest.Position).magnitude >= Rules.spacing) Add(feet, CrumbKind.Walk, feet.y);
        }

        void Leave(Vector3 feet, float time)
        {
            airborne = true;
            takeOff = lastGround;
            apex = Mathf.Max(takeOff.y, feet.y);
            airSince = time;
        }

        void MarkGap()
        {
            if (pendingGap) return;
            pendingGap = true;
            gapFrom = lastGround;
        }

        static bool Near(Vector3 a, Vector3 b, float flat)
        {
            Vector3 d = b - a;
            return Mathf.Abs(d.y) < .4f && new Vector2(d.x, d.z).magnitude < flat;
        }
    }

    /// <summary>Thresholds of burrowing and of the spot the tree comes up at.</summary>
    [Serializable]
    public struct BurrowRules
    {
        [Tooltip("Farther than this from the player the tree burrows, m.")]
        public float maxDistance;
        [Tooltip("No progress along the trail this long: it burrows, s.")]
        public float stuckSeconds;
        [Tooltip("The player this much above or below with no way there: it burrows, m.")]
        public float levelDifference;
        [Tooltip("It comes up at least this far from the player, m…")]
        public float minFromPlayer;
        [Tooltip("…at most this far, m…")]
        public float maxFromPlayer;
        [Tooltip("…preferably this far (2–4 m behind), m.")]
        public float preferredFromPlayer;
        [Tooltip("At most this much above or below the player's feet, m.")]
        public float maxRiseHeight;
        [Tooltip("Score penalty (m) for a spot off the navigation mesh.")]
        public float offMeshPenalty;
        [Tooltip("Score penalty (m) for a spot the camera sees (it should come up out of sight when it can).")]
        public float inViewPenalty;
        [Tooltip("Score bonus (m) for a spot of the trail (the player stood there).")]
        public float trailBonus;

        public static BurrowRules Default => new BurrowRules
        {
            maxDistance = 30, stuckSeconds = 3, levelDifference = 3,
            minFromPlayer = 1.5f, maxFromPlayer = 6, preferredFromPlayer = 3, maxRiseHeight = 2.5f,
            offMeshPenalty = 1.5f, inViewPenalty = 4, trailBonus = .5f,
        };
    }

    public enum BurrowReason { None, Gap, Stuck, TooFar, Level, Water, Fell }

    /// <summary>What the tree knows when it decides whether to burrow.</summary>
    public struct BurrowSituation
    {
        /// <summary>It has a foe or its branch is up: never in the middle of a fight.</summary>
        public bool Fighting;
        /// <summary>Within its follow distance of the player: nothing to catch up.</summary>
        public bool Settled;
        /// <summary>Standing in deep water.</summary>
        public bool InDeepWater;
        /// <summary>Fell out of the world.</summary>
        public bool FellOut;
        /// <summary>The next crumb of the trail is a gap.</summary>
        public bool NextIsGap;
        /// <summary>A way on exists (a complete path on the mesh, or a crumb or jump ahead).</summary>
        public bool HasRoute;
        /// <summary>Seconds without progress along the trail.</summary>
        public float StuckSeconds;
        /// <summary>Distance to the player, m, and the player's height above the tree, m.</summary>
        public float Distance, Height;
    }

    /// <summary>One place the tree could come up at, around the player.</summary>
    public struct RiseCandidate
    {
        public Vector3 Feet;
        /// <summary>Flat distance to the player, m, and height above the player's feet, m.</summary>
        public float DistanceToPlayer, Height;
        /// <summary>A capsule of the tree's body fits there.</summary>
        public bool Free;
        /// <summary>Solid ground under it.</summary>
        public bool Grounded;
        /// <summary>In water.</summary>
        public bool Water;
        public bool OnNavMesh;
        public Vector3 NavPoint;
        /// <summary>The camera sees it.</summary>
        public bool InView;
        /// <summary>The player stood there (a crumb of the trail).</summary>
        public bool FromTrail;
    }

    /// <summary>
    /// How the awakened tree follows wherever the player can go (owner, playtest: «пусть может пройти везде, где может
    /// пройти игрок»): which crumb of the trail to head for, the arc of a jump, when it gives up and burrows, and where it
    /// comes up again. Pure rules: the physics questions come in as predicates, so EditMode tests cover them.
    /// </summary>
    public static class TreeAllyPathing
    {
        /// <summary>
        /// Launch velocity of a ballistic jump from <paramref name="from"/> through a top at least <paramref name="apex"/>
        /// (and at least <paramref name="lift"/> above both ends) down onto <paramref name="to"/> under
        /// <paramref name="gravity"/>.
        /// </summary>
        public static Vector3 JumpVelocity(Vector3 from, Vector3 to, float apex, float gravity, float lift = .25f)
        {
            gravity = Mathf.Max(.1f, gravity);
            float top = Mathf.Max(apex, Mathf.Max(from.y, to.y) + lift);
            float up = top - from.y, down = top - to.y;
            float vy = Mathf.Sqrt(2 * gravity * up);
            float time = vy / gravity + Mathf.Sqrt(2 * down / gravity);
            Vector3 flat = to - from;
            flat.y = 0;
            return flat / Mathf.Max(.05f, time) + Vector3.up * vy;
        }

        /// <summary>Can the tree repeat the player's flight from take-off to landing with its own jump?</summary>
        public static bool CanJump(Vector3 takeOff, Vector3 landing, float apex, float gravity, in TrailRules rules)
        {
            if (Mathf.Max(apex, landing.y) - takeOff.y > rules.maxJumpRise) return false;
            Vector3 v = JumpVelocity(takeOff, landing, apex, gravity);
            return new Vector2(v.x, v.z).magnitude <= rules.maxJumpSpeed;
        }

        /// <summary>
        /// One step of a straight walk over sampled ground (<paramref name="level"/>: the feet's height so far). Up at most
        /// <paramref name="climb"/> per step (slopes and steps, not ledges), down any drop up to <paramref name="maxDrop"/>
        /// (it walks off like the player does), never where nothing is under (NaN) or into water deeper than
        /// <paramref name="deepWater"/>. On success the feet are at the new ground.
        /// </summary>
        public static bool GroundStep(ref float level, float ground, float water, float climb, float maxDrop, float deepWater)
        {
            if (float.IsNaN(ground) || ground > level + climb || level - ground > maxDrop || water - ground > deepWater) return false;
            level = ground;
            return true;
        }

        /// <summary>
        /// Can the tree walk straight from a height <paramref name="fromY"/> over the sampled ground (water surfaces
        /// optional, −∞ for none) onto a foothold at <paramref name="goalY"/>? See <see cref="GroundStep"/>.
        /// </summary>
        public static bool WalkableGround(float fromY, float goalY, IReadOnlyList<float> ground, IReadOnlyList<float> water,
            float climb, float maxDrop, float deepWater)
        {
            float level = fromY;
            for (int i = 0; i < ground.Count; i++)
            {
                float surface = water != null && i < water.Count ? water[i] : float.NegativeInfinity;
                if (!GroundStep(ref level, ground[i], surface, climb, maxDrop, deepWater)) return false;
            }
            return GroundStep(ref level, goalY, float.NegativeInfinity, climb, maxDrop, deepWater);
        }

        /// <summary>The crumb is where the player left the ground for a jump (the next crumb is its landing).</summary>
        public static bool IsTakeOff(TreeAllyTrail trail, int index)
            => trail.Has(index) && trail.Has(index + 1) && trail[index + 1].Kind == CrumbKind.Jump;

        /// <summary>
        /// The cursor after the tree reached crumbs: moves past every crumb within <paramref name="reach"/> (flat) and
        /// <paramref name="rise"/> (height) among the next <paramref name="window"/>.
        /// </summary>
        public static int Advance(TreeAllyTrail trail, int cursor, Vector3 self, float reach = .7f, float rise = .9f, int window = 12)
        {
            int next = cursor;
            int end = Mathf.Min(trail.End, cursor + window);
            for (int i = Mathf.Max(cursor, trail.First); i < end; i++)
            {
                Vector3 d = trail[i].Position - self;
                if (Mathf.Abs(d.y) <= rise && new Vector2(d.x, d.z).magnitude <= reach) next = i + 1;
            }
            return next;
        }

        /// <summary>
        /// From the cursor on, the farthest crumb the tree can walk to in a straight line: it stops before a jump or a gap
        /// (those start from the crumb before them), beyond <paramref name="maxReach"/> (flat) and at the first crumb
        /// <paramref name="walkable"/> refuses. −1: not even the cursor's crumb. <paramref name="cursorWalkable"/>: the
        /// cursor's crumb was chosen as reachable on foot (whatever its kind).
        /// </summary>
        public static int FarthestReachable(TreeAllyTrail trail, int cursor, bool cursorWalkable, Vector3 self, float maxReach,
            Func<Vector3, bool> walkable, int window = 16)
        {
            int best = -1;
            int end = Mathf.Min(trail.End, cursor + window);
            for (int i = Mathf.Max(cursor, trail.First); i < end; i++)
            {
                var crumb = trail[i];
                if (crumb.Kind != CrumbKind.Walk && !(i == cursor && cursorWalkable)) break;
                Vector3 d = crumb.Position - self;
                if (new Vector2(d.x, d.z).magnitude > maxReach) break;
                if (walkable != null && !walkable(crumb.Position)) break;
                best = i;
            }
            return best;
        }

        static readonly List<(float score, int index)> Ranked = new List<(float, int)>();

        /// <summary>
        /// Where the tree joins the trail: the nearest crumb on its own level (within <paramref name="maxFlat"/>,
        /// <paramref name="maxRise"/>) it can walk to, newer crumbs a little preferred. −1: none.
        /// </summary>
        public static int Anchor(TreeAllyTrail trail, Vector3 self, float maxFlat, float maxRise, Func<Vector3, bool> walkable, int tries = 8)
        {
            Ranked.Clear();
            int count = Mathf.Max(1, trail.Count);
            for (int i = trail.First; i < trail.End; i++)
            {
                Vector3 d = trail[i].Position - self;
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat > maxFlat || Mathf.Abs(d.y) > maxRise) continue;
                Ranked.Add((flat + 2 * Mathf.Abs(d.y) - .5f * (i - trail.First) / count, i));
            }
            Ranked.Sort((a, b) => a.score.CompareTo(b.score));
            for (int k = 0; k < Ranked.Count && k < tries; k++)
                if (walkable == null || walkable(trail[Ranked[k].index].Position)) return Ranked[k].index;
            return -1;
        }

        /// <summary>
        /// Should the tree burrow now, and why? Out of the world or in deep water: always. Otherwise never mid-fight or
        /// when already beside the player; then a gap ahead, too far, no progress for too long, or another level with no way.
        /// </summary>
        public static BurrowReason Burrow(in BurrowSituation s, in BurrowRules rules)
        {
            if (s.FellOut) return BurrowReason.Fell;
            if (s.InDeepWater) return BurrowReason.Water;
            if (s.Fighting || s.Settled) return BurrowReason.None;
            if (s.NextIsGap) return BurrowReason.Gap;
            if (s.Distance > rules.maxDistance) return BurrowReason.TooFar;
            if (s.StuckSeconds > rules.stuckSeconds) return BurrowReason.Stuck;
            if (Mathf.Abs(s.Height) > rules.levelDifference && !s.HasRoute) return BurrowReason.Level;
            return BurrowReason.None;
        }

        /// <summary>
        /// The spot to come up at: free, on solid dry ground, between <see cref="BurrowRules.minFromPlayer"/> and
        /// <see cref="BurrowRules.maxFromPlayer"/> from the player, near their level; best near the preferred distance,
        /// on the navigation mesh, out of the camera's sight and where the player walked. −1: none yet.
        /// </summary>
        public static int ChooseRiseSpot(IReadOnlyList<RiseCandidate> spots, in BurrowRules rules)
        {
            int best = -1;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                if (!s.Free || !s.Grounded || s.Water) continue;
                if (s.DistanceToPlayer < rules.minFromPlayer || s.DistanceToPlayer > rules.maxFromPlayer) continue;
                if (Mathf.Abs(s.Height) > rules.maxRiseHeight) continue;
                float score = Mathf.Abs(s.DistanceToPlayer - rules.preferredFromPlayer) + 2 * Mathf.Abs(s.Height);
                if (!s.OnNavMesh) score += rules.offMeshPenalty;
                if (s.InView) score += rules.inViewPenalty;
                if (s.FromTrail) score -= rules.trailBonus;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }
    }
}
