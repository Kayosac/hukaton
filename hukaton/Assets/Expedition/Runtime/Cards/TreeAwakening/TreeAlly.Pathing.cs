using System;
using System.Collections.Generic;
using Hakaton.Feel;
using UnityEngine;
using UnityEngine.AI;

namespace Hakaton.Expeditions.Cards
{
    /// <summary>How the awakened tree is getting after the player right now.</summary>
    public enum TreeAllyRoute
    {
        /// <summary>Along the navigation mesh (the agent plans, the body walks; chunk seams are crossed by hand).</summary>
        NavMesh,
        /// <summary>Along the player's own trail: crumbs walked straight, the player's jumps repeated.</summary>
        Trail,
    }

    /// <summary>
    /// Getting about (owner, playtest: «пусть может пройти везде, где может пройти игрок»). The body is the player's size
    /// (capsule r 0.3, h 1.8 under the 3 m drawing) with a ground probe, steps up to <see cref="stepHeight"/>, hops up
    /// ledges up to <see cref="ledgeHop"/> and falls with the player's gravity. On the navigation mesh it walks the agent's
    /// path; where the mesh does not go (rocks, narrow ledges, drops, gaps between chunks) it follows the player's trail
    /// (<see cref="TreeAllyTrail"/>), jumping where the player jumped. Where the player went a way the tree cannot
    /// (rope, teleport, swap, updraft, card passages), or it is stuck, far behind or in deep water, it burrows: sinks
    /// with a burst of leaves and comes up out of sight 2–4 m behind the player.
    /// </summary>
    public sealed partial class TreeAlly
    {
        [Header("Body (like the player's)")]
        [Tooltip("Gravity on the tree in the air, m/s²: Unity's own is topped up to this (the player's motor falls at 25).")]
        [Min(1)] public float gravity = 25;
        [Tooltip("Steps up to this height are climbed at once, m (the player's controller: 0.28).")]
        [Min(0)] public float stepHeight = .35f;
        [Tooltip("Ledges up to this height are hopped onto, m (the player jumps 1.2 m).")]
        [Min(0)] public float ledgeHop = 1.3f;
        [Tooltip("Water deeper than this over the feet sends the tree under the ground, m.")]
        [Min(.1f)] public float deepWater = .8f;
        [Tooltip("Off a drop up to this deep the tree just walks, like the player (who survives about 6 m), m.")]
        [Min(0)] public float walkOffDrop = 6;

        [Header("Following the player's trail")]
        public TrailRules trailRules = TrailRules.Default;
        [Tooltip("The farthest crumb of the trail the tree heads for in a straight line, m.")]
        [Min(1)] public float trailReach = 9;

        [Header("Burrowing (no way to follow: under the ground and up behind the player)")]
        public BurrowRules burrowRules = BurrowRules.Default;
        [Tooltip("How long the tree takes to sink, s.")]
        [Min(.1f)] public float burrowSeconds = .6f;
        [Tooltip("Leaves where it goes under (Retro Arsenal LeafExplosion). It comes up with the awakening burst and cue.")]
        public VfxBurst burrowVfx;
        public SfxCue burrowCue;

        /// <summary>How it follows now.</summary>
        public TreeAllyRoute Route { get; private set; }
        /// <summary>Sinking or under the ground (not a fighter meanwhile).</summary>
        public bool Burrowed => burrowPhase != BurrowPhase.None;
        /// <summary>How many times it burrowed this outing, and why the last time.</summary>
        public int Burrows { get; private set; }
        public BurrowReason LastBurrowReason { get; private set; }
        /// <summary>The ground probe found ground under the feet in the last physics step.</summary>
        public bool Grounded { get; private set; }
        /// <summary>The player's trail the tree follows off the mesh.</summary>
        public TreeAllyTrail PlayerTrail => trail;

        /// <summary>One line of what the pathing is doing (tests and debugging): route, state, body, cursor, target, timers.</summary>
        public string DebugPathing
        {
            get
            {
                if (trail == null) return "not set up";
                string crumb = trail.Has(cursor)
                    ? $"{cursor}:{trail[cursor].Kind}{(cursorWalkable ? "*" : "")}{Fmt(trail[cursor].Position)}{(TreeAllyPathing.IsTakeOff(trail, cursor) ? "^" : "")}"
                    : $"{cursor}:end";
                string target = trailTarget >= 0 && trail.Has(trailTarget) ? $"{trailTarget}{Fmt(trail[trailTarget].Position)}" : "-";
                string agent = navigator && navigator.enabled
                    ? (navigator.isOnNavMesh ? (navigator.isOnOffMeshLink ? "link" : "mesh") : "off-mesh")
                    : "off";
                string air = jumping ? (hopping ? "hop" : "jump") : "-";
                return $"{Route}/{State} {Fmt(Position)} g={(Grounded ? 1 : 0)} v={Fmt(body ? body.linearVelocity : Vector3.zero)} air={air} " +
                       $"cur={crumb} tgt={target} way={pickWay} ahead={(routeAhead ? 1 : 0)} noProgress={Time.time - progressAt:0.0}s " +
                       $"fails={failedJumps} trail={trail.First}..{trail.End} gap={trail.LastGap} agent={agent} burrow={burrowPhase}";
            }
        }

        static string Fmt(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";

        enum BurrowPhase { None, Sinking, Underground }
        enum Way { Clear, Blocked, NoGround }

        // Ground and obstacles: not the player (Ignore Raycast), water, UI, cards or their artwork.
        const int WaterLayer = 4;
        const int SolidMask = ~((1 << 2) | (1 << WaterLayer) | (1 << 5) | (1 << 8) | (1 << 9));
        const int WaterMask = 1 << WaterLayer;
        const float MinGroundNormal = .5f;
        // A straight walk climbs at most this much per 0.6 m sample (slopes and steps; ledges are hopped or jumped).
        const float GroundClimb = .6f, GroundSpacing = .6f;
        // The player's controller stands with its middle up to ~0.3 m over an edge: the last bit before a crumb is not sampled.
        const float FootholdSlack = .45f;
        // Chasing a foe off the mesh it does not leap down from heights.
        const float EngageDrop = 1.5f;
        static readonly RaycastHit[] Hits = new RaycastHit[24];
        static readonly Collider[] Overlaps = new Collider[24];

        TreeAllyTrail trail;
        CapsuleCollider capsule;
        NavMeshPath probePath;
        Func<Vector3, bool> walkableTo;
        Func<Collider, bool> ownCollider;
        readonly List<RiseCandidate> riseSpots = new List<RiseCandidate>();

        BurrowPhase burrowPhase;
        float burrowStarted, nextRiseSearch;
        int cursor, trailTarget = -1, jumpCrumb = -1, failedJumps;
        bool cursorWalkable, routeAhead, jumping, hopping;
        Way pickWay;
        float nextTrailPick, nextNavReturn, navCooldownUntil, progressAt, closest = float.PositiveInfinity;
        float watchAt, jumpStarted, jumpLockUntil, hopTop, nextHop, nextGoCheck, nextWaterCheck;
        float playerOffMeshSince = -1, linkSince = -1;
        Vector3 watchFrom, jumpPlanar;
        Way goWay;
        bool inDeepWater;

        float BodyRadius => capsule ? capsule.radius : .3f;
        float BodyHeight => capsule ? capsule.height : 1.8f;

        void SetUpPathing()
        {
            capsule = GetComponent<CapsuleCollider>();
            trail = new TreeAllyTrail { Rules = trailRules };
            probePath = new NavMeshPath();
            walkableTo = point => Walkable(point, true, walkOffDrop) == Way.Clear;
            ownCollider = Own;
            navigator.autoTraverseOffMeshLink = false;
        }

        void ResetPathing(float now)
        {
            cursor = trail.End;
            cursorWalkable = false;
            trailTarget = jumpCrumb = -1;
            failedJumps = 0;
            jumping = hopping = false;
            routeAhead = true;
            pickWay = goWay = Way.Clear;
            progressAt = now;
            closest = float.PositiveInfinity;
            watchFrom = Position;
            watchAt = now + 1.5f;
            nextTrailPick = 0;
            nextNavReturn = now + .5f;
            playerOffMeshSince = linkSince = -1;
            hasDestination = false;
            nextPath = 0;
        }

        bool Own(Collider other) => other && (other.attachedRigidbody == body || other.transform.IsChildOf(transform));

        /// <summary>The navigation mesh right under the feet (within 0.5 m aside and 1 m up or down).</summary>
        bool MeshUnder(Vector3 feet, out Vector3 point)
        {
            point = feet;
            if (!NavMesh.SamplePosition(feet, out var hit, .8f, navigator.areaMask)) return false;
            point = hit.position;
            return Flat(hit.position - feet).magnitude <= .5f && Mathf.Abs(hit.position.y - feet.y) <= 1;
        }

        /// <summary>Puts the agent on the mesh at <paramref name="point"/> (turning it on the first time).</summary>
        bool PlaceAgent(Vector3 point)
        {
            if (!navigator.enabled) navigator.enabled = true;
            return navigator.Warp(point);
        }

        // ---------------- the player's trail ----------------

        void RecordTrail()
        {
            var motor = world.motor;
            if (!motor) return;
            trail.Rules = trailRules;
            trail.Observe(motor.transform.position, motor.IsGrounded, motor.ExternallyControlled, Time.time, Time.deltaTime, gravity);
        }

        // ---------------- following ----------------

        /// <summary>Follows the player by the current route; the navigation mesh hands over to the trail and back.</summary>
        void FollowPlayer(Vector3 player, float speed)
        {
            if (Route == TreeAllyRoute.NavMesh) FollowOnNavMesh(player, speed);
            // Not "else": the mesh may have handed over to the trail this very frame.
            if (Route == TreeAllyRoute.Trail) FollowTrail(player, speed);
        }

        void FollowOnNavMesh(Vector3 player, float speed)
        {
            float now = Time.time;
            if (!navigator.enabled || !navigator.isOnNavMesh) { EnterTrail(false); return; }
            if (navigator.isOnOffMeshLink) { CrossLink(speed); return; }
            linkSince = -1;
            if (!InStepWithAgent()) { EnterTrail(false); return; }
            navigator.speed = speed;
            if (now >= nextPath)
            {
                nextPath = now + .3f;
                if (PlayerOnMesh(player, out var goal))
                {
                    playerOffMeshSince = -1;
                    if (navigator.SetDestination(goal)) { destination = goal; hasDestination = true; }
                }
                else
                {
                    // In the air, or where the mesh does not reach (a rock top, a narrow ledge): the trail goes there.
                    if (world.motor.IsGrounded && playerOffMeshSince < 0) playerOffMeshSince = now;
                    if (hasDestination) navigator.SetDestination(destination);
                }
            }
            if (playerOffMeshSince >= 0 && now - playerOffMeshSince > .6f) { EnterTrail(false); return; }
            if (hasDestination && !navigator.pathPending && navigator.pathStatus != NavMeshPathStatus.PathComplete) { EnterTrail(false); return; }
            Steer(navigator.desiredVelocity, speed);
            if (NoProgress(now)) EnterTrail(true);
        }

        /// <summary>The player stands on the mesh: a walkable point right under them.</summary>
        bool PlayerOnMesh(Vector3 player, out Vector3 goal)
        {
            goal = player;
            if (!NavMesh.SamplePosition(player, out var hit, playerNavSearch, navigator.areaMask)) return false;
            goal = hit.position;
            return Flat(hit.position - player).magnitude < 1 && Mathf.Abs(hit.position.y - player.y) < 1.2f;
        }

        /// <summary>
        /// The agent and the body drifted apart (a push, a fall, a hop off the mesh): the agent jumps back to the body
        /// when the mesh is right there; otherwise the tree goes on by the trail.
        /// </summary>
        bool InStepWithAgent()
        {
            Vector3 gap = navigator.nextPosition - body.position;
            if (Flat(gap).magnitude <= .6f && Mathf.Abs(gap.y) <= 1) return true;
            if (!Grounded || jumping) return true;
            if (!MeshUnder(body.position, out var near) || !navigator.Warp(near)) return false;
            hasDestination = false;
            nextPath = 0;
            return true;
        }

        /// <summary>A link between chunks: walked (or hopped) by hand to its far end, then the agent is put there.</summary>
        void CrossLink(float speed)
        {
            float now = Time.time;
            if (linkSince < 0) linkSince = now;
            Vector3 end = navigator.currentOffMeshLinkData.endPos;
            Vector3 to = end - Position;
            if (Grounded && Flat(to).magnitude < .45f && Mathf.Abs(to.y) < 1.2f)
            {
                navigator.CompleteOffMeshLink();
                if (NavMesh.SamplePosition(Position, out var near, .8f, navigator.areaMask)) navigator.Warp(near.position);
                linkSince = -1;
                hasDestination = false;
                nextPath = 0;
                return;
            }
            if (now - linkSince > 4)
            {
                navigator.CompleteOffMeshLink();
                linkSince = -1;
                EnterTrail(true);
                return;
            }
            Steer(to, speed);
        }

        void EnterTrail(bool stuckOnMesh)
        {
            float now = Time.time;
            if (stuckOnMesh) navCooldownUntil = now + 3;
            Route = TreeAllyRoute.Trail;
            if (navigator.enabled && navigator.isOnNavMesh)
            {
                if (navigator.isOnOffMeshLink) navigator.CompleteOffMeshLink();
                navigator.ResetPath();
            }
            hasDestination = false;
            linkSince = playerOffMeshSince = -1;
            Reanchor();
            progressAt = now;
            closest = float.PositiveInfinity;
            nextTrailPick = 0;
            nextNavReturn = now + .5f;
            watchFrom = Position;
            watchAt = now + 1.5f;
        }

        /// <summary>Back to the mesh when the tree stands on it and a complete path to the player exists.</summary>
        bool TryReturnToNavMesh()
        {
            float now = Time.time;
            if (now < navCooldownUntil || jumping || !Grounded) return false;
            if (!MeshUnder(Position, out var self)) return false;
            if (!PlayerOnMesh(PlayerPosition, out var goal)) return false;
            if (!NavMesh.CalculatePath(self, goal, navigator.areaMask, probePath) || probePath.status != NavMeshPathStatus.PathComplete) return false;
            if (!PlaceAgent(self)) return false;
            Route = TreeAllyRoute.NavMesh;
            hasDestination = false;
            nextPath = 0;
            playerOffMeshSince = linkSince = -1;
            watchFrom = Position;
            watchAt = now + 1.5f;
            return true;
        }

        void Reanchor()
        {
            if (cursor < trail.First) cursor = trail.First;
            int at = TreeAllyPathing.Anchor(trail, Position, trailReach, .9f, walkableTo);
            if (at >= 0) { cursor = at; cursorWalkable = true; }
            else cursorWalkable = false;
            trailTarget = -1;
            nextTrailPick = 0;
        }

        void MoveCursor(int to, float now)
        {
            cursor = to;
            cursorWalkable = false;
            if (trailTarget < cursor) trailTarget = -1;
            progressAt = now;
            closest = float.PositiveInfinity;
        }

        void FollowTrail(Vector3 player, float speed)
        {
            float now = Time.time;
            // In the air on the player's arc; a hop up a ledge counts no progress until it lands up there.
            if (jumping) { routeAhead = true; if (!hopping) progressAt = now; return; }
            if (cursor < trail.First) Reanchor();
            int reached = TreeAllyPathing.Advance(trail, cursor, Position);
            if (reached != cursor) MoveCursor(reached, now);
            if (now >= nextNavReturn)
            {
                nextNavReturn = now + .5f;
                if (TryReturnToNavMesh()) return;
            }

            if (!trail.Has(cursor))
            {
                // Caught up with the end of the trail: the player stands, flies or hangs on a rope. Straight at them
                // when the ground between holds.
                if (now >= nextTrailPick)
                {
                    nextTrailPick = now + .25f;
                    pickWay = world.motor.IsGrounded ? Walkable(player, true, walkOffDrop) : Way.NoGround;
                }
                routeAhead = pickWay == Way.Clear;
                if (routeAhead) Steer(player - Position, speed);
                else Halt();
                progressAt = now;
                return;
            }

            var next = trail[cursor];
            var kind = cursorWalkable ? CrumbKind.Walk : next.Kind;
            if (kind == CrumbKind.Gap)
            {
                // The player went where the tree cannot follow; the mesh may still lead there (a swap across open ground).
                Halt();
                routeAhead = false;
                return;
            }
            if (kind == CrumbKind.Jump)
            {
                routeAhead = true;
                // Already down there (slipped off the edge, or pushed): the jump is done.
                Vector3 toLanding = next.Position - Position;
                if (Flat(toLanding).magnitude <= 1.2f && Mathf.Abs(toLanding.y) <= .9f)
                {
                    MoveCursor(cursor + 1, now);
                    return;
                }
                Vector3 takeOff = trail.Has(cursor - 1) ? trail[cursor - 1].Position : Position;
                Vector3 toTakeOff = takeOff - Position;
                if (Flat(toTakeOff).magnitude > .6f)
                {
                    Steer(toTakeOff, Mathf.Min(speed, walkSpeed));
                    TrackProgress(takeOff, now);
                    return;
                }
                Halt();
                // On the ground at the take-off (or just slipping off its edge): the player's arc.
                if (Grounded || Mathf.Abs(body.linearVelocity.y) < 1) LaunchJump(next.Position, next.Apex + .15f);
                return;
            }

            if (now >= nextTrailPick)
            {
                nextTrailPick = now + .25f;
                trailTarget = TreeAllyPathing.FarthestReachable(trail, cursor, cursorWalkable, Position, trailReach, walkableTo);
                pickWay = trailTarget >= 0 ? Way.Clear : Walkable(next.Position, true, walkOffDrop);
            }
            if (trailTarget >= 0 && !trail.Has(trailTarget)) trailTarget = -1;
            // Never over a hole, a cliff deeper than the player survives or into deep water for a shortcut: it waits (and
            // burrows when that lasts). The take-off of a jump is always walked to: the player stepped off right there.
            if (trailTarget < 0 && pickWay == Way.NoGround && !TreeAllyPathing.IsTakeOff(trail, cursor)) { Halt(); routeAhead = false; return; }
            routeAhead = true;
            Vector3 goal = trailTarget >= 0 ? trail[trailTarget].Position : next.Position;
            Steer(goal - Position, speed);
            TrackProgress(next.Position, now);
        }

        void TrackProgress(Vector3 goal, float now)
        {
            Vector3 d = goal - Position;
            float left = Flat(d).magnitude + Mathf.Abs(d.y);
            if (left < closest - .3f) { closest = left; progressAt = now; }
        }

        /// <summary>
        /// Straight line to a point on foot, following the ground (<see cref="TreeAllyPathing.GroundStep"/>): up slopes and
        /// steps, off drops up to <paramref name="maxDrop"/>, never over a hole or deep water, with room for the body along
        /// the way (Blocked otherwise). A <paramref name="foothold"/> (a crumb, the grounded player) is solid ground itself,
        /// even where it hangs over an edge.
        /// </summary>
        Way Walkable(Vector3 goal, bool foothold, float maxDrop)
        {
            Vector3 from = Position;
            Vector3 flat = Flat(goal - from);
            float length = flat.magnitude;
            if (length < .05f) return Way.Clear;
            Vector3 direction = flat / length;
            float sampled = foothold ? Mathf.Max(0, length - FootholdSlack) : length;
            int steps = Mathf.CeilToInt(sampled / GroundSpacing);
            if (steps > 64) return Way.NoGround;
            float level = from.y;
            Vector3 last = from;
            bool blocked = false;
            int count = steps + (foothold ? 1 : 0);
            for (int i = 1; i <= count; i++)
            {
                Vector3 at;
                float ground, water;
                if (i <= steps)
                {
                    at = from + direction * (sampled * i / steps);
                    // Probed from above the feet's height so far, down past the deepest drop it walks off.
                    Vector3 probe = new Vector3(at.x, level + 1.2f, at.z);
                    if (!GroundBelow(probe, 1.4f + maxDrop, out ground, out water)) ground = float.NaN;
                }
                else
                {
                    at = goal;
                    ground = goal.y;
                    water = float.NegativeInfinity;
                }
                float before = level;
                if (!TreeAllyPathing.GroundStep(ref level, ground, water, GroundClimb, maxDrop, deepWater)) return Way.NoGround;
                at.y = level;
                if (!blocked && !RoomAlong(last, at, before - level > GroundClimb)) blocked = true;
                last = at;
            }
            return blocked ? Way.Blocked : Way.Clear;
        }

        /// <summary>
        /// Room for the body from one point of a walk to the next (the capsule above step height): along the ground, or
        /// level at the old height over the edge of a drop (the body walks off, then falls).
        /// </summary>
        bool RoomAlong(Vector3 a, Vector3 b, bool drop)
        {
            if (drop) b.y = a.y;
            Vector3 delta = b - a;
            float distance = delta.magnitude;
            if (distance < .01f) return true;
            float r = BodyRadius * .9f;
            Vector3 low = a + Vector3.up * (stepHeight + r + .05f), high = a + Vector3.up * Mathf.Max(stepHeight + r + .1f, BodyHeight - r);
            int count = Physics.CapsuleCastNonAlloc(low, high, r, delta / distance, Hits, distance, SolidMask, QueryTriggerInteraction.Ignore);
            return !Nearest(count, out _);
        }

        /// <summary>Highest solid ground and water surface under a point (water: −∞ when none).</summary>
        bool GroundBelow(Vector3 above, float depth, out float ground, out float water)
        {
            ground = water = float.NegativeInfinity;
            // Water surfaces are solid meshes on the Water layer (the player falls through them, the tree does too).
            int count = Physics.RaycastNonAlloc(above, Vector3.down, Hits, depth, SolidMask | WaterMask, QueryTriggerInteraction.Ignore);
            float nearestGround = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.distance <= 0 || Own(hit.collider)) continue;
                if (hit.collider.gameObject.layer == WaterLayer) { water = Mathf.Max(water, hit.point.y); continue; }
                if (hit.distance < nearestGround) { nearestGround = hit.distance; ground = hit.point.y; }
            }
            return !float.IsPositiveInfinity(nearestGround);
        }

        /// <summary>The nearest hit among <see cref="Hits"/> that is not the tree itself (nor an overlap at the start).</summary>
        bool Nearest(int count, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.distance <= 0 || Own(hit.collider)) continue;
                if (hit.distance < best) { best = hit.distance; nearest = hit; }
            }
            return !float.IsPositiveInfinity(best);
        }

        bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < .01f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, SolidMask, QueryTriggerInteraction.Ignore);
            return Nearest(count, out _);
        }

        void Steer(Vector3 direction, float speed)
        {
            direction.y = 0;
            desired = direction.sqrMagnitude > .01f ? direction.normalized * speed : Vector3.zero;
            if (desired.sqrMagnitude > .01f) Face(desired);
        }

        /// <summary>Wants to move but barely moved in the last 1.5 s (hopping at a ledge it never gets up counts too).</summary>
        bool NoProgress(float now)
        {
            if (desired.sqrMagnitude < .01f || (jumping && !hopping))
            {
                watchFrom = Position;
                watchAt = now + 1.5f;
                return false;
            }
            if (now < watchAt) return false;
            bool stuck = (Position - watchFrom).magnitude < .3f;
            watchFrom = Position;
            watchAt = now + 1.5f;
            return stuck;
        }

        /// <summary>Off the mesh toward a foe: straight at it while the ground ahead holds.</summary>
        void GoStraight(Vector3 point, float speed)
        {
            Vector3 to = Flat(point - Position);
            if (Time.time >= nextGoCheck)
            {
                nextGoCheck = Time.time + .25f;
                float ahead = Mathf.Min(2, to.magnitude);
                Vector3 probe = Position + (to.sqrMagnitude > .01f ? to.normalized * ahead : Vector3.zero);
                probe.y = Mathf.Lerp(Position.y, point.y, ahead / Mathf.Max(.01f, to.magnitude));
                goWay = Walkable(probe, false, EngageDrop);
            }
            if (goWay == Way.NoGround) { Halt(); Face(to); }
            else Steer(to, speed);
        }

        // ---------------- body ----------------

        void LaunchJump(Vector3 landing, float apex)
        {
            Vector3 v = TreeAllyPathing.JumpVelocity(Position, landing, apex, gravity);
            float now = Time.time;
            jumping = true;
            hopping = false;
            jumpCrumb = cursor;
            jumpStarted = now;
            jumpLockUntil = now + .15f;
            jumpPlanar = new Vector3(v.x, 0, v.z);
            body.linearVelocity = v;
            Face(jumpPlanar);
            KickDust(stepDustCount * 2, false);
            if (world) world.Play(stepCue, Position, .6f, .9f);
        }

        void Landed()
        {
            jumping = false;
            if (hopping) { hopping = false; return; }
            if (Route != TreeAllyRoute.Trail) return;
            if (jumpCrumb == cursor && trail.Has(cursor))
            {
                Vector3 to = trail[cursor].Position - Position;
                if (Flat(to).magnitude <= 2 && Mathf.Abs(to.y) <= 1)
                {
                    failedJumps = 0;
                    MoveCursor(cursor + 1, Time.time);
                    return;
                }
            }
            // Fell short (or hit something): it tries again from where it is; twice is enough, then it burrows.
            failedJumps++;
            Reanchor();
        }

        /// <summary>Ground under the feet: a sphere cast down from just above the step height.</summary>
        bool ProbeGround()
        {
            float r = BodyRadius * .9f;
            float lift = stepHeight + .1f;
            Vector3 origin = body.position + Vector3.up * (r + lift);
            int count = Physics.SphereCastNonAlloc(origin, r, Vector3.down, Hits, lift + .3f, SolidMask, QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out var hit)) return false;
            return hit.distance - lift <= .08f && hit.normal.y >= MinGroundNormal;
        }

        /// <summary>
        /// A step or a ledge right ahead: steps (up to <see cref="stepHeight"/>) are climbed at once like the player's
        /// controller does; ledges up to <see cref="ledgeHop"/> are hopped onto with v = √(2g(h + 0.2)).
        /// </summary>
        void ClimbAhead(Vector3 move, float now)
        {
            Vector3 direction = Flat(move).normalized;
            float r = BodyRadius;
            // A face right in front of the feet (a low ray), or the lower cap running into something steep.
            int count = Physics.RaycastNonAlloc(body.position + Vector3.up * .08f, direction, Hits, r + .25f, SolidMask, QueryTriggerInteraction.Ignore);
            bool face = Nearest(count, out var wall) && wall.normal.y < MinGroundNormal;
            if (!face)
            {
                count = Physics.SphereCastNonAlloc(body.position + Vector3.up * (r + .04f), r * .95f, direction, Hits, .25f, SolidMask, QueryTriggerInteraction.Ignore);
                face = Nearest(count, out wall) && wall.normal.y < MinGroundNormal;
            }
            // Creatures and loose props are pushed, not climbed.
            if (!face || (wall.rigidbody && !wall.rigidbody.isKinematic)) return;
            float reach = ledgeHop + .3f;
            Vector3 above = wall.point + direction * .15f;
            above.y = body.position.y + reach;
            count = Physics.RaycastNonAlloc(above, Vector3.down, Hits, reach + .05f, SolidMask, QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out var top) || top.normal.y < MinGroundNormal) return;
            if (top.rigidbody && !top.rigidbody.isKinematic) return;
            float rise = top.point.y - body.position.y;
            if (rise < .04f || rise > ledgeHop) return;
            if (!CapsuleFree(top.point + Vector3.up * .03f, r * .95f)) return;
            if (rise <= stepHeight)
            {
                body.position += Vector3.up * (rise + .02f);
                return;
            }
            if (now < nextHop) return;
            nextHop = now + .6f;
            float vy = Mathf.Sqrt(2 * gravity * (rise + .2f));
            float forward = (Flat(top.point - body.position).magnitude + r) / Mathf.Max(.1f, vy / gravity);
            jumping = hopping = true;
            hopTop = top.point.y;
            jumpStarted = now;
            jumpLockUntil = now + .15f;
            jumpPlanar = direction * Mathf.Clamp(forward, 1.5f, Mathf.Max(walkSpeed, catchUpSpeed));
            body.linearVelocity = direction * .6f + Vector3.up * vy;
        }

        bool CapsuleFree(Vector3 feet, float radius)
        {
            Vector3 a = feet + Vector3.up * (radius + .02f), b = feet + Vector3.up * Mathf.Max(radius + .03f, BodyHeight - radius);
            int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, Overlaps, SolidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!Own(Overlaps[i])) return false;
            return true;
        }

        /// <summary>One physics step of the body: ground, gravity, steps and hops, walking or flying a jump.</summary>
        void StepBody(float dt)
        {
            float now = Time.time;
            Grounded = ProbeGround();
            if (jumping && now >= jumpLockUntil && Grounded) Landed();
            if (jumping && now - jumpStarted > 4) jumping = hopping = false;
            // Unity's gravity only on the ground (friction stays as it was); the player's in the air.
            float extra = gravity + Physics.gravity.y;
            if (body.useGravity && extra > 0 && (jumping || !Grounded)) body.AddForce(Vector3.down * extra, ForceMode.Acceleration);
            if (!jumping && Grounded && desired.sqrMagnitude > .01f) ClimbAhead(desired, now);
            Vector3 velocity = body.linearVelocity;
            Vector3 planar;
            if (jumping)
            {
                planar = jumpPlanar;
                // Up a ledge: only lean on it until the feet are over the top.
                if (hopping && body.position.y < hopTop + .05f) planar = jumpPlanar.normalized * Mathf.Min(.6f, jumpPlanar.magnitude);
            }
            else
            {
                planar = new Vector3(velocity.x, 0, velocity.z);
                planar = Vector3.MoveTowards(planar, desired * MoveSpeedScale, (Grounded ? acceleration : acceleration * .35f) * dt);
            }
            body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
        }

        void CheckWater(float now)
        {
            if (now < nextWaterCheck) return;
            nextWaterCheck = now + .25f;
            Vector3 feet = Position;
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * 4, Vector3.down, Hits, 4.5f, WaterMask, QueryTriggerInteraction.Ignore);
            float surface = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
                if (Hits[i].distance > 0) surface = Mathf.Max(surface, Hits[i].point.y);
            inDeepWater = surface - feet.y >= deepWater;
        }

        // ---------------- burrowing ----------------

        void ConsiderBurrow(float now)
        {
            Vector3 player = PlayerPosition;
            Vector3 to = player - Position;
            var situation = new BurrowSituation
            {
                Fighting = (foe && foe.Alive) || cycle.Phase != TreeAttackPhase.Ready,
                Settled = Flat(to).magnitude <= followDistance && Mathf.Abs(to.y) < burrowRules.levelDifference,
                InDeepWater = inDeepWater,
                NextIsGap = Route == TreeAllyRoute.Trail
                    && (failedJumps >= 2 || (!cursorWalkable && trail.Has(cursor) && trail[cursor].Kind == CrumbKind.Gap)),
                HasRoute = Route == TreeAllyRoute.NavMesh
                    ? navigator.isOnNavMesh && (!hasDestination || navigator.pathPending || navigator.pathStatus == NavMeshPathStatus.PathComplete)
                    : routeAhead,
                StuckSeconds = Route == TreeAllyRoute.Trail && !jumping ? now - progressAt : 0,
                Distance = to.magnitude,
                Height = to.y,
            };
            var reason = TreeAllyPathing.Burrow(situation, burrowRules);
            if (reason != BurrowReason.None) StartBurrow(reason);
        }

        /// <summary>Sinks under the ground with a burst of leaves (not a fighter until it comes up).</summary>
        void StartBurrow(BurrowReason reason)
        {
            if (burrowPhase != BurrowPhase.None || dying) return;
            float now = Time.time;
            burrowPhase = BurrowPhase.Sinking;
            burrowStarted = now;
            nextRiseSearch = now + burrowSeconds + .25f;
            Burrows++;
            LastBurrowReason = reason;
            foe = null;
            strikeTarget = null;
            cycle.Reset();
            jumping = hopping = false;
            Halt();
            Enter(TreeAllyState.Burrow);
            if (navigator.enabled && navigator.isOnNavMesh)
            {
                if (navigator.isOnOffMeshLink) navigator.CompleteOffMeshLink();
                navigator.ResetPath();
            }
            hasDestination = false;
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
            body.isKinematic = true;
            // Out of the fight and out of the way while under the ground.
            body.detectCollisions = false;
            if (world)
            {
                world.Spawn(burrowVfx, Position + Vector3.up * .5f, Quaternion.identity, leafColour, 1.2f);
                world.Play(burrowCue, Position, .9f, .8f);
            }
            KickDust(stepDustCount * 4, true);
        }

        void TickBurrow(float now)
        {
            Halt();
            if (burrowPhase == BurrowPhase.Sinking)
            {
                if (now - burrowStarted < burrowSeconds) return;
                burrowPhase = BurrowPhase.Underground;
            }
            if (now < nextRiseSearch) return;
            nextRiseSearch = now + .25f;
            var motor = world.motor;
            // Waits under the ground while the player hangs on a rope or flies.
            if (!motor || motor.ExternallyControlled || !motor.IsGrounded) return;
            if (FindRiseSpot(out var spot)) Resurface(spot);
        }

        bool FindRiseSpot(out RiseCandidate spot)
        {
            spot = default;
            riseSpots.Clear();
            Vector3 player = PlayerPosition;
            var settings = PlayerCapsulePlacement.Settings.For(BodyRadius, BodyHeight);
            // Where the player walked last (on this side of the newest gap).
            int from = Mathf.Max(trail.First, trail.LastGap);
            int looked = 0;
            for (int i = trail.End - 1; i >= from && looked < 40; i--, looked++)
            {
                Vector3 crumb = trail[i].Position;
                if (Flat(crumb - player).magnitude > burrowRules.maxFromPlayer) continue;
                if (!GroundAt(crumb + Vector3.up * .5f, 1, out var ground) || Mathf.Abs(ground.y - crumb.y) > .3f) continue;
                AddRiseSpot(ground, player, settings, true);
            }
            // Around the player, starting behind the camera.
            var camera = world.viewCamera;
            Vector3 back = -Flat(camera ? camera.transform.forward : world.motor.transform.forward);
            if (back.sqrMagnitude < .01f) back = Vector3.back;
            back.Normalize();
            Vector3 chest = player + Vector3.up * 1.2f;
            foreach (float radius in new[] { 2.5f, 3.5f })
                for (int k = 0; k < 8; k++)
                {
                    float angle = (k + 1) / 2 * 45f * (k % 2 == 0 ? 1 : -1);
                    Vector3 probe = player + Quaternion.Euler(0, angle, 0) * back * radius;
                    probe.y = player.y + burrowRules.maxRiseHeight;
                    if (!GroundAt(probe, burrowRules.maxRiseHeight * 2, out var ground)) continue;
                    // On the player's level (not on a crown or a rock top the player never stood on)…
                    if (Mathf.Abs(ground.y - player.y) > 1.2f) continue;
                    // …and not behind a wall from the player.
                    if (Blocked(chest, ground + Vector3.up * 1.2f)) continue;
                    AddRiseSpot(ground, player, settings, false);
                }
            int best = TreeAllyPathing.ChooseRiseSpot(riseSpots, burrowRules);
            if (best < 0) return false;
            spot = riseSpots[best];
            return true;
        }

        bool GroundAt(Vector3 above, float depth, out Vector3 ground)
        {
            ground = above;
            int count = Physics.RaycastNonAlloc(above, Vector3.down, Hits, depth, SolidMask, QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out var hit) || hit.normal.y < .7f) return false;
            ground = hit.point;
            return true;
        }

        void AddRiseSpot(Vector3 feet, Vector3 player, PlayerCapsulePlacement.Settings settings, bool fromTrail)
        {
            var spot = new RiseCandidate
            {
                Feet = feet,
                DistanceToPlayer = Flat(feet - player).magnitude,
                Height = feet.y - player.y,
                Grounded = true,
                FromTrail = fromTrail,
            };
            if (spot.DistanceToPlayer < burrowRules.minFromPlayer || spot.DistanceToPlayer > burrowRules.maxFromPlayer) return;
            // A hand's breadth up, so uneven ground under the capsule's round bottom does not count as in the way.
            spot.Free = PlayerCapsulePlacement.IsFree(feet + Vector3.up * .12f, settings, ownCollider);
            if (!spot.Free) return;
            spot.Water = WaterAbove(feet);
            if (NavMesh.SamplePosition(feet, out var hit, .5f, navigator.areaMask) && Mathf.Abs(hit.position.y - feet.y) < .6f)
            {
                spot.OnNavMesh = true;
                spot.NavPoint = hit.position;
            }
            spot.InView = InView(feet);
            riseSpots.Add(spot);
        }

        bool WaterAbove(Vector3 feet)
        {
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * 3, Vector3.down, Hits, 3.05f, WaterMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Hits[i].distance > 0 && Hits[i].point.y > feet.y - .05f) return true;
            return false;
        }

        bool InView(Vector3 feet)
        {
            var camera = world.viewCamera;
            if (!camera) return false;
            Vector3 v = camera.WorldToViewportPoint(feet + Vector3.up * 1.2f);
            return v.z > 0 && v.x > -.05f && v.x < 1.05f && v.y > -.05f && v.y < 1.05f;
        }

        /// <summary>Comes up at the spot with the uprooting burst, facing the player, and walks on from there.</summary>
        void Resurface(in RiseCandidate spot)
        {
            float now = Time.time;
            Vector3 feet = spot.Feet + Vector3.up * .02f;
            burrowPhase = BurrowPhase.None;
            Vector3 look = Flat(PlayerPosition - feet);
            if (look.sqrMagnitude > .01f) facing = Quaternion.LookRotation(look);
            transform.SetPositionAndRotation(feet, facing);
            body.position = feet;
            body.rotation = facing;
            body.isKinematic = false;
            body.detectCollisions = true;
            body.linearVelocity = Vector3.zero;
            ResetPathing(now);
            Route = TreeAllyRoute.Trail;
            if (spot.OnNavMesh && PlaceAgent(spot.NavPoint)) Route = TreeAllyRoute.NavMesh;
            if (Route == TreeAllyRoute.Trail) Reanchor();
            inDeepWater = false;
            awakenedAt = now;
            Enter(TreeAllyState.Awakening);
            if (world)
            {
                world.Spawn(awakenVfx, feet + Vector3.up * .3f, Quaternion.identity, leafColour, 1.2f);
                world.Play(awakenCue, feet, .9f);
                if (world.feel && Vector3.Distance(PlayerPosition, feet) < 14) world.feel.AddTrauma(.12f);
            }
            KickDust(stepDustCount * 5, true);
        }
    }
}
