using System.Collections.Generic;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// The awakened tree goes wherever the player goes (playtest): the player's trail with its jumps and gaps, the
    /// crumb the tree heads for, the arc of its jump, when it burrows and where it comes up again.
    /// </summary>
    public sealed class TreeAllyPathingTests
    {
        const float G = 25, Dt = .02f;
        static readonly TrailRules Rules = TrailRules.Default;
        static readonly BurrowRules Burrow = BurrowRules.Default;

        float time;

        void Feed(TreeAllyTrail trail, Vector3 feet, bool grounded, bool carried = false)
        {
            time += Dt;
            trail.Observe(feet, grounded, carried, time, Dt, G);
        }

        /// <summary>The player walks on the ground from one point to another in 5 cm steps.</summary>
        void Walk(TreeAllyTrail trail, Vector3 from, Vector3 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .05f));
            for (int i = 0; i <= steps; i++) Feed(trail, Vector3.Lerp(from, to, (float)i / steps), true);
        }

        /// <summary>The feet in the air along a parabola from one point to another, peaking <paramref name="peak"/> above the start.</summary>
        void Fly(TreeAllyTrail trail, Vector3 from, Vector3 to, float peak, int frames = 30)
        {
            for (int i = 1; i < frames; i++)
            {
                float t = (float)i / frames;
                Vector3 p = Vector3.Lerp(from, to, t);
                p.y = Mathf.Lerp(from.y, to.y, t) + 4 * peak * t * (1 - t);
                Feed(trail, p, false);
            }
        }

        static TreeAllyTrail Line(params (float x, float y, CrumbKind kind)[] crumbs)
        {
            var trail = new TreeAllyTrail();
            foreach (var c in crumbs) trail.Add(new Vector3(c.x, c.y, 0), c.kind, c.y);
            return trail;
        }

        // ---------- recording the trail ----------

        [Test]
        public void WalkingLeavesACrumbEveryHalfMetre()
        {
            var trail = new TreeAllyTrail();
            Walk(trail, Vector3.zero, new Vector3(5, 0, 0));
            Assert.That(trail.Count, Is.InRange(10, 11));
            for (int i = trail.First; i < trail.End; i++)
            {
                Assert.That(trail[i].Kind, Is.EqualTo(CrumbKind.Walk));
                if (i > trail.First) Assert.That(Vector3.Distance(trail[i].Position, trail[i - 1].Position), Is.InRange(.5f, .56f));
            }
            Assert.That(trail.LastGap, Is.EqualTo(-1));
        }

        [Test]
        public void AJumpOverAGapLeavesTheTakeOffAndTheLanding()
        {
            var trail = new TreeAllyTrail();
            Walk(trail, Vector3.zero, new Vector3(3, 0, 0));
            Fly(trail, new Vector3(3, 0, 0), new Vector3(6, 0, 0), 1);
            Feed(trail, new Vector3(6, 0, 0), true);
            var landing = trail.Newest;
            var takeOff = trail[trail.End - 2];
            Assert.That(landing.Kind, Is.EqualTo(CrumbKind.Jump), "The tree can repeat a 1 m high, 3 m long jump.");
            Assert.That(landing.Position.x, Is.EqualTo(6).Within(1e-3f));
            Assert.That(landing.Apex, Is.EqualTo(1).Within(.02f));
            Assert.That(takeOff.Kind, Is.EqualTo(CrumbKind.Walk));
            Assert.That(takeOff.Position.x, Is.EqualTo(3).Within(1e-3f), "It jumps from where the player left the ground.");
        }

        [Test]
        public void WalkingOffALedgeIsAJumpDown()
        {
            var trail = new TreeAllyTrail();
            Walk(trail, new Vector3(0, 2, 0), new Vector3(2, 2, 0));
            Fly(trail, new Vector3(2, 2, 0), new Vector3(3.5f, 0, 0), 0, 20);
            Feed(trail, new Vector3(3.5f, 0, 0), true);
            Assert.That(trail.Newest.Kind, Is.EqualTo(CrumbKind.Jump));
            Assert.That(trail.LastGap, Is.EqualTo(-1));
        }

        [Test]
        public void ATeleportIsAGap()
        {
            var trail = new TreeAllyTrail();
            Walk(trail, Vector3.zero, new Vector3(2, 0, 0));
            Feed(trail, new Vector3(40, 0, 0), true);
            Assert.That(trail.Newest.Kind, Is.EqualTo(CrumbKind.Gap));
            Assert.That(trail.LastGap, Is.EqualTo(trail.End - 1));
            Walk(trail, new Vector3(40, 0, 0), new Vector3(42, 0, 0));
            Assert.That(trail.Newest.Kind, Is.EqualTo(CrumbKind.Walk), "Walking on from there is a trail again.");
        }

        [Test]
        public void AnUpdraftLiftIsAGapButABounceInPlaceIsNot()
        {
            var trail = new TreeAllyTrail();
            Walk(trail, Vector3.zero, new Vector3(2, 0, 0));
            for (int i = 1; i <= 30; i++) Feed(trail, new Vector3(2 + i * .05f, i * .2f, 0), false);
            Feed(trail, new Vector3(3.6f, 6, 0), true);
            Assert.That(trail.Newest.Kind, Is.EqualTo(CrumbKind.Gap), "Lifted 6 m onto a cliff: the tree cannot jump that.");

            var bounce = new TreeAllyTrail();
            Walk(bounce, Vector3.zero, new Vector3(2, 0, 0));
            Fly(bounce, new Vector3(2, 0, 0), new Vector3(2.3f, 0, 0), 4);
            Feed(bounce, new Vector3(2.3f, 0, 0), true);
            Assert.That(bounce.LastGap, Is.EqualTo(-1), "A mushroom bounce back to where it started is no gap.");
            for (int i = bounce.First; i < bounce.End; i++) Assert.That(bounce[i].Kind, Is.EqualTo(CrumbKind.Walk));
        }

        [Test]
        public void ARopeRideIsAGapUnlessItEndsWhereItStarted()
        {
            var trail = new TreeAllyTrail();
            Walk(trail, Vector3.zero, new Vector3(2, 0, 0));
            for (int i = 1; i <= 40; i++) Feed(trail, new Vector3(2 + i * .15f, Mathf.Min(3, i * .2f), 0), false, true);
            Fly(trail, new Vector3(8, 3, 0), new Vector3(8.6f, 1, 0), 0, 10);
            Feed(trail, new Vector3(8.6f, 1, 0), true);
            Assert.That(trail.Newest.Kind, Is.EqualTo(CrumbKind.Gap), "Swung across on the rope.");

            var back = new TreeAllyTrail();
            Walk(back, Vector3.zero, new Vector3(2, 0, 0));
            for (int i = 1; i <= 20; i++) Feed(back, new Vector3(2, Mathf.Min(1, i * .1f), 0), false, true);
            for (int i = 1; i <= 10; i++) Feed(back, new Vector3(2.1f, 1 - i * .1f, 0), false);
            Feed(back, new Vector3(2.2f, 0, 0), true);
            Assert.That(back.LastGap, Is.EqualTo(-1), "Let go of the rope where it was grabbed: no gap.");
        }

        [Test]
        public void TheTrailKeepsOnlyTheNewestCrumbs()
        {
            var trail = new TreeAllyTrail(8);
            for (int i = 0; i < 20; i++) trail.Add(new Vector3(i, 0, 0), i == 5 ? CrumbKind.Gap : CrumbKind.Walk, 0);
            Assert.That(trail.First, Is.EqualTo(12));
            Assert.That(trail.End, Is.EqualTo(20));
            Assert.That(trail.Has(11), Is.False);
            Assert.That(trail.Has(12), Is.True);
            Assert.That(trail[12].Position.x, Is.EqualTo(12));
            Assert.That(trail.LastGap, Is.EqualTo(-1), "The gap fell out of the ring.");
        }

        // ---------- jumping ----------

        [Test]
        public void TheJumpArcLandsOnTheLanding()
        {
            var cases = new[]
            {
                (from: Vector3.zero, to: new Vector3(3, 0, 0), apex: 1f),
                (from: new Vector3(0, 2, 0), to: new Vector3(2, 0, 1), apex: 2f),
                (from: Vector3.zero, to: new Vector3(1, 1.2f, 0), apex: 1.4f),
                (from: new Vector3(5, 6, 5), to: new Vector3(7, 0, 4), apex: 6f),
            };
            foreach (var c in cases)
            {
                Vector3 v = TreeAllyPathing.JumpVelocity(c.from, c.to, c.apex, G);
                float top = c.from.y + v.y * v.y / (2 * G);
                Assert.That(top, Is.GreaterThanOrEqualTo(c.apex - 1e-3f), "It goes as high as the player did.");
                Assert.That(top, Is.GreaterThanOrEqualTo(Mathf.Max(c.from.y, c.to.y) + .25f - 1e-3f), "It clears both ends.");
                // When it comes down to the landing's height (on the way down) it is right over the landing.
                float rise = c.to.y - c.from.y;
                float t = (v.y + Mathf.Sqrt(v.y * v.y - 2 * G * rise)) / G;
                Vector3 at = c.from + new Vector3(v.x, 0, v.z) * t + Vector3.up * (v.y * t - .5f * G * t * t);
                Assert.That(Vector3.Distance(at, c.to), Is.LessThan(1e-3f), $"{c.from} → {c.to}");
            }
        }

        [Test]
        public void TheTreeRepeatsThePlayersJumpsButNotFlights()
        {
            Assert.That(TreeAllyPathing.CanJump(Vector3.zero, new Vector3(1.5f, 1, 0), 1.25f, G, Rules), Is.True, "The player's jump onto a 1 m ledge.");
            Assert.That(TreeAllyPathing.CanJump(new Vector3(0, 6, 0), new Vector3(2, 0, 0), 6, G, Rules), Is.True, "A 6 m drop.");
            Assert.That(TreeAllyPathing.CanJump(Vector3.zero, new Vector3(4.5f, 0, 0), 1.2f, G, Rules), Is.True, "A sprinting jump over a gap.");
            Assert.That(TreeAllyPathing.CanJump(Vector3.zero, new Vector3(1, 6, 0), 6.5f, G, Rules), Is.False, "Lifted by an updraft.");
            Assert.That(TreeAllyPathing.CanJump(Vector3.zero, new Vector3(12, 0, 0), 4.8f, G, Rules), Is.False, "A Moon-gravity leap.");
            Assert.That(TreeAllyPathing.CanJump(Vector3.zero, new Vector3(20, 0, 0), 1, G, Rules), Is.False, "Flung far by an impulse.");
            Assert.That(TreeAllyPathing.CanJump(Vector3.zero, new Vector3(1, 2, 0), 2, G, Rules), Is.False, "Onto a 2 m wall.");
        }

        // ---------- which crumb ----------

        static TreeAllyTrail LedgeTrail() => Line(
            (0, 0, CrumbKind.Walk), (.5f, 0, CrumbKind.Walk), (1, 0, CrumbKind.Walk), (1.5f, 0, CrumbKind.Walk),
            (2, 0, CrumbKind.Walk), (2.5f, 0, CrumbKind.Walk), (3, 0, CrumbKind.Walk), (3.5f, 0, CrumbKind.Walk),
            (4, 0, CrumbKind.Walk), (6, -2, CrumbKind.Jump), (6.5f, -2, CrumbKind.Walk));

        [Test]
        public void TheTreeHeadsForTheFarthestCrumbItCanWalkTo()
        {
            var trail = LedgeTrail();
            Assert.That(TreeAllyPathing.FarthestReachable(trail, 0, false, Vector3.zero, 9, p => p.x <= 3.2f), Is.EqualTo(6),
                "Up to the first crumb it cannot walk to.");
            Assert.That(TreeAllyPathing.FarthestReachable(trail, 0, false, Vector3.zero, 9, p => true), Is.EqualTo(8),
                "Never past a jump: it jumps from the crumb before it.");
            Assert.That(TreeAllyPathing.FarthestReachable(trail, 0, false, Vector3.zero, 2, p => true), Is.EqualTo(4), "Within reach only.");
            Assert.That(TreeAllyPathing.FarthestReachable(trail, 9, false, new Vector3(4, 0, 0), 9, p => true), Is.EqualTo(-1),
                "At a jump there is nothing to walk to.");
            Assert.That(TreeAllyPathing.FarthestReachable(trail, 9, true, new Vector3(5.8f, -2, 0), 9, p => true), Is.EqualTo(10),
                "A crumb chosen as reachable on foot is walked to, whatever its kind.");
        }

        const float Climb = .6f, Drop = 6, Deep = .8f;

        static bool Walk(float fromY, float goalY, float[] ground, float[] water = null)
            => TreeAllyPathing.WalkableGround(fromY, goalY, ground, water, Climb, Drop, Deep);

        [Test]
        public void ACrumbBelowALedgeIsReachedByWalkingOff()
        {
            // Playtest: the tree stood at a 2 m ledge the player had walked off. Walking off a drop the player survives
            // (about 6 m) is just walking.
            Assert.That(Walk(2, 0, new[] { 2f, 2f, 2f, 0f, 0f }), Is.True, "Off a 2 m ledge onto the crumb below.");
            Assert.That(Walk(2, 0, new[] { 2f, 2f }), Is.True, "The crumb right below the edge (the last bit is not sampled).");
            Assert.That(Walk(6, 0, new[] { 6f, 0f }), Is.True, "A 6 m drop.");
            Assert.That(Walk(7, 0, new[] { 7f, 0f }), Is.False, "Deeper than the player survives.");
            Assert.That(Walk(0, 0, new[] { 0f, float.NaN, 0f }), Is.False, "Over a hole with nothing under it.");
            Assert.That(Walk(0, 0, new[] { 0f, -3f, 0f }), Is.False, "Across a canyon: down is fine, but no walking out of it.");
        }

        [Test]
        public void AStraightWalkClimbsSlopesAndStepsButNotLedgesOrDeepWater()
        {
            Assert.That(Walk(0, 1.5f, new[] { .3f, .6f, .9f, 1.2f }), Is.True, "Up a slope.");
            Assert.That(Walk(0, .35f, new[] { 0f, .35f }), Is.True, "Up a step.");
            Assert.That(Walk(0, 2, new[] { 0f, 0f }), Is.False, "Up onto a 2 m ledge is a jump, not a walk.");
            Assert.That(Walk(0, 0, new[] { 0f, 1.5f, 0f }), Is.False, "Over a wall.");
            Assert.That(Walk(0, -1.2f, new[] { -1.2f, -1.2f }, new[] { 0f, 0f }), Is.False, "Into deep water.");
            Assert.That(Walk(0, -.3f, new[] { -.3f }, new[] { 0f }), Is.True, "Wading through shallow water.");
        }

        [Test]
        public void TheTakeOffOfAJumpIsTheCrumbBeforeTheLanding()
        {
            var trail = LedgeTrail();
            Assert.That(TreeAllyPathing.IsTakeOff(trail, 8), Is.True, "The player left the ground at the edge.");
            Assert.That(TreeAllyPathing.IsTakeOff(trail, 7), Is.False);
            Assert.That(TreeAllyPathing.IsTakeOff(trail, 9), Is.False, "The landing.");
            Assert.That(TreeAllyPathing.IsTakeOff(trail, 10), Is.False, "The newest crumb.");
        }

        [Test]
        public void ATakeOffHangingOverTheEdgeIsRecordedWhereThePlayerLeftTheGround()
        {
            // The controller's round bottom keeps it on the ground until its middle is ~0.3 m over the edge, sinking a little.
            var trail = new TreeAllyTrail();
            Walk(trail, new Vector3(0, 2, 0), new Vector3(4, 2, 0));
            Feed(trail, new Vector3(4.15f, 1.97f, 0), true);
            Feed(trail, new Vector3(4.3f, 1.92f, 0), true);
            Fly(trail, new Vector3(4.3f, 1.92f, 0), new Vector3(5.8f, 0, 0), 0, 20);
            Feed(trail, new Vector3(5.8f, 0, 0), true);
            Assert.That(trail.Newest.Kind, Is.EqualTo(CrumbKind.Jump));
            Assert.That(TreeAllyPathing.IsTakeOff(trail, trail.End - 2), Is.True);
            Assert.That(trail[trail.End - 2].Position.x, Is.EqualTo(4.3f).Within(1e-3f), "Over the edge: the tree is told to walk to it anyway.");
        }

        [Test]
        public void CrumbsTheTreeReachedAreBehindItButNotThoseBelow()
        {
            var trail = LedgeTrail();
            Assert.That(TreeAllyPathing.Advance(trail, 0, new Vector3(1, 0, 0)), Is.EqualTo(4), "Past 0.5, 1 and 1.5 m.");
            Assert.That(TreeAllyPathing.Advance(trail, 0, new Vector3(-3, 0, 0)), Is.EqualTo(0));
            Assert.That(TreeAllyPathing.Advance(trail, 9, new Vector3(4.2f, 0, 0)), Is.EqualTo(9), "The landing 2 m below is not reached.");
            Assert.That(TreeAllyPathing.Advance(trail, 9, new Vector3(6.2f, -2, 0)), Is.EqualTo(11), "Landed: past the landing.");
        }

        [Test]
        public void TheTreeJoinsTheTrailOnItsOwnLevel()
        {
            var trail = Line(
                (0, 0, CrumbKind.Walk), (.5f, 0, CrumbKind.Walk), (1, 0, CrumbKind.Walk), (1.5f, 0, CrumbKind.Walk), (2, 0, CrumbKind.Walk),
                (1, 2, CrumbKind.Jump), (1.2f, 2, CrumbKind.Walk), (1.4f, 2, CrumbKind.Walk));
            Assert.That(TreeAllyPathing.Anchor(trail, new Vector3(.9f, 0, 0), 9, .9f, p => true), Is.EqualTo(2),
                "The nearest crumb on the ground, not the ones on the ledge above.");
            Assert.That(TreeAllyPathing.Anchor(trail, new Vector3(.9f, 0, 0), 9, .9f, p => p.x > 1.1f), Is.EqualTo(3), "One it can walk to.");
            Assert.That(TreeAllyPathing.Anchor(trail, new Vector3(1.2f, 2, 0), 9, .9f, p => true), Is.EqualTo(6), "On the ledge.");
            Assert.That(TreeAllyPathing.Anchor(trail, new Vector3(30, 0, 0), 9, .9f, p => true), Is.EqualTo(-1), "Too far from any.");
        }

        // ---------- burrowing ----------

        static BurrowSituation Following => new BurrowSituation { Distance = 10, Height = 0, HasRoute = true };

        [Test]
        public void ItBurrowsWhenItCannotFollowButNeverMidFight()
        {
            var s = Following;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.None));

            s = Following; s.NextIsGap = true;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.Gap), "Rope, teleport, updraft ahead.");
            s = Following; s.Distance = 31;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.TooFar), "More than 30 m behind.");
            s = Following; s.StuckSeconds = 3.1f;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.Stuck));
            s.StuckSeconds = 2.9f;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.None), "Stuck under 3 s: it keeps trying.");
            s = Following; s.Height = -4; s.HasRoute = false;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.Level), "Fell into a canyon with no way up.");
            s.HasRoute = true;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.None), "A way up exists.");

            s = Following; s.Fighting = true; s.NextIsGap = true; s.Distance = 40; s.StuckSeconds = 9;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.None), "Never in the middle of a fight.");
            s.InDeepWater = true;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.Water), "Deep water: always.");
            s.FellOut = true;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.Fell));

            s = Following; s.Settled = true; s.NextIsGap = true;
            Assert.That(TreeAllyPathing.Burrow(s, Burrow), Is.EqualTo(BurrowReason.None), "Already beside the player.");
        }

        static RiseCandidate Spot(float distance, bool inView = false, bool onMesh = true, bool free = true, bool water = false,
            float height = 0, bool trail = false) => new RiseCandidate
        {
            Feet = new Vector3(distance, height, 0), DistanceToPlayer = distance, Height = height, Free = free, Grounded = true,
            Water = water, OnNavMesh = onMesh, InView = inView, FromTrail = trail,
        };

        [Test]
        public void ItComesUpBehindThePlayerOutOfSightAndNeverOnTopOfThem()
        {
            var spots = new List<RiseCandidate>
            {
                Spot(1),                     // 0: on top of the player
                Spot(3, inView: true),       // 1: in front of the camera
                Spot(3.5f),                  // 2: behind, out of sight
                Spot(3, water: true),        // 3: in the water
                Spot(3, free: false),        // 4: inside a rock
                Spot(2.5f, height: 3),       // 5: on a cliff above
                Spot(8),                     // 6: too far
            };
            Assert.That(TreeAllyPathing.ChooseRiseSpot(spots, Burrow), Is.EqualTo(2));
            spots.RemoveAt(2);
            Assert.That(TreeAllyPathing.ChooseRiseSpot(spots, Burrow), Is.EqualTo(1), "In sight only when nothing else will do.");
            Assert.That(TreeAllyPathing.ChooseRiseSpot(new List<RiseCandidate> { Spot(1.2f), Spot(3, water: true) }, Burrow), Is.EqualTo(-1),
                "No spot yet: it waits under the ground.");
        }

        [Test]
        public void ItPrefersTheMeshAndThePlayersOwnFootsteps()
        {
            var spots = new List<RiseCandidate> { Spot(3, onMesh: false), Spot(3.6f) };
            Assert.That(TreeAllyPathing.ChooseRiseSpot(spots, Burrow), Is.EqualTo(1), "On the mesh it walks on at once.");
            spots = new List<RiseCandidate> { Spot(3.2f), Spot(3.5f, trail: true) };
            Assert.That(TreeAllyPathing.ChooseRiseSpot(spots, Burrow), Is.EqualTo(1), "Where the player stood.");
        }
    }
}
