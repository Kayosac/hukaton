using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>Cluster C — Stone cube, Branch bridge, Rope: placement, growth length, the pendulum's release speed, assets.</summary>
    public sealed class BuildingCardTests
    {
        const float Epsilon = 1e-4f;

        // ---------- Stone cube: free spot ----------

        [Test]
        public void CubeStandsHalfASideOutFromTheSurface()
        {
            Vector3 floor = StoneCubePlacement.BaseCentre(new Vector3(2, 0, 3), Vector3.up, 1.5f);
            Assert.That(Vector3.Distance(floor, new Vector3(2, .75f + StoneCubePlacement.Skin, 3)), Is.LessThan(Epsilon));
            Vector3 wall = StoneCubePlacement.BaseCentre(new Vector3(0, 1, 0), Vector3.left * 3, 2);
            Assert.That(Vector3.Distance(wall, new Vector3(-1 - StoneCubePlacement.Skin, 1, 0)), Is.LessThan(Epsilon),
                "From a wall the cube appears in front of it (the normal need not be unit length).");
        }

        [Test]
        public void CubeTakesTheFirstFreeSpot()
        {
            var tried = new List<Vector3>();
            bool found = StoneCubePlacement.TryFind(Vector3.zero, Vector3.up, 1.5f, centre => { tried.Add(centre); return true; }, out var spot);
            Assert.That(found, Is.True);
            Assert.That(tried.Count, Is.EqualTo(1), "A free base spot is used as is.");
            Assert.That(spot.y, Is.EqualTo(.75f + StoneCubePlacement.Skin).Within(Epsilon));
        }

        [Test]
        public void CubeMovesOutAlongTheNormalWhenBlocked()
        {
            // Something solid in front of a wall up to 1 m out: the cube has to stand farther away from the wall.
            bool found = StoneCubePlacement.TryFind(new Vector3(0, 1, 0), Vector3.right, 1.5f, centre => centre.x - .75f >= 1, out var spot);
            Assert.That(found, Is.True);
            Assert.That(spot.x - .75f, Is.GreaterThanOrEqualTo(1 - Epsilon));
            Assert.That(spot.x, Is.LessThan(.75f + 1.5f + Epsilon), "Never farther than one whole side beyond the base spot along the normal.");
            Assert.That(spot.y, Is.EqualTo(1).Within(Epsilon), "Along the normal first, not upward.");
        }

        [Test]
        public void CubeNeverMovesIntoTheSurfaceAndMissesWithoutRoom()
        {
            var tried = new List<Vector3>();
            bool found = StoneCubePlacement.TryFind(Vector3.zero, Vector3.up, 2, centre => { tried.Add(centre); return false; }, out _);
            Assert.That(found, Is.False, "No free spot at all: a miss.");
            Assert.That(tried.Count, Is.EqualTo(StoneCubePlacement.CandidateCount));
            foreach (var centre in tried)
                Assert.That(centre.y, Is.GreaterThanOrEqualTo(1 + StoneCubePlacement.Skin - Epsilon), "Every candidate keeps the cube off the surface.");
        }

        [Test]
        public void CubeStaysUprightFacingTheFlight()
        {
            var rotation = StoneCubePlacement.Orientation(new Vector3(1, -.7f, 0));
            Assert.That(Vector3.Angle(rotation * Vector3.up, Vector3.up), Is.LessThan(.01f), "The top stays level (a mushroom can stand on it).");
            Assert.That(Vector3.Angle(rotation * Vector3.forward, Vector3.right), Is.LessThan(.01f));
        }

        // ---------- Branch bridge: direction and length ----------

        [Test]
        public void BranchGrowsHorizontallyTowardTheThrower()
        {
            Vector3 direction = BranchBridgeMath.GrowthDirection(new Vector3(0, 3, 10), Vector3.back, new Vector3(1, 1.6f, 0), Vector3.forward);
            Assert.That(direction.y, Is.EqualTo(0).Within(Epsilon));
            Assert.That(direction.magnitude, Is.EqualTo(1).Within(Epsilon));
            Assert.That(Vector3.Angle(direction, new Vector3(1, 0, -10)), Is.LessThan(.01f));
        }

        [Test]
        public void BranchNeverGrowsAlongTheWall()
        {
            // Thrown almost along the wall: the growth turns to at most 70° from the wall's outward direction.
            Vector3 direction = BranchBridgeMath.GrowthDirection(Vector3.zero, Vector3.back, new Vector3(10, 0, -.5f), Vector3.forward, 70);
            Assert.That(Vector3.Angle(direction, Vector3.back), Is.EqualTo(70).Within(.05f));
            Assert.That(Vector3.Dot(direction, Vector3.back), Is.GreaterThan(0), "Out of the wall, never into it.");
        }

        [Test]
        public void BranchFallsBackOnTheFlightWhenThrownFromAbove()
        {
            Vector3 direction = BranchBridgeMath.GrowthDirection(Vector3.zero, Vector3.up, new Vector3(0, 5, 0), new Vector3(0, -1, 1));
            Assert.That(Vector3.Angle(direction, Vector3.back), Is.LessThan(.01f), "Back against the flight, horizontally.");
        }

        [Test]
        public void BranchIsCutShortByAnObstacle()
        {
            Assert.That(BranchBridgeMath.ClampLength(5, null, .08f, 1), Is.EqualTo(5), "Nothing in the way: the full board length.");
            Assert.That(BranchBridgeMath.ClampLength(7, 9, .08f, 1), Is.EqualTo(7));
            Assert.That(BranchBridgeMath.ClampLength(5, 3, .08f, 1), Is.EqualTo(2.92f).Within(Epsilon), "Stops just before the obstacle.");
            Assert.That(BranchBridgeMath.ClampLength(5, .9f, .08f, 1), Is.EqualTo(0), "No room: no branch.");
        }

        [Test]
        public void BranchTopSitsOnAWallPointAndAboveAFloor()
        {
            Assert.That(BranchBridgeMath.TopOffset(Vector3.back, .42f), Is.EqualTo(0));
            Assert.That(BranchBridgeMath.TopOffset(Vector3.up, .42f), Is.GreaterThan(0));
            Assert.That(BranchBridgeMath.TopOffset(Vector3.down, .42f), Is.LessThan(-.42f), "Below a ceiling the whole branch hangs under it.");
            Assert.That(BranchBridgeMath.Growth(0), Is.EqualTo(0).Within(Epsilon));
            Assert.That(BranchBridgeMath.Growth(1), Is.EqualTo(1).Within(Epsilon));
        }

        // ---------- Rope: pendulum and release ----------

        [Test]
        public void ReleaseKeepsTheTangentialSpeedWithoutABonus()
        {
            Vector3 anchor = new Vector3(0, 10, 0), body = new Vector3(3, 6, 0); // 5 m, taut
            Vector3 tangent = new Vector3(4, 3, 0).normalized;
            Vector3 radial = (body - anchor).normalized;
            Vector3 velocity = tangent * 6 + radial * 2;
            Vector3 released = RopeMath.ReleaseVelocity(anchor, body, velocity, 5);
            Assert.That(Vector3.Distance(released, tangent * 6), Is.LessThan(Epsilon), "The outward part a taut rope cannot have is removed.");
            Assert.That(released.magnitude, Is.LessThanOrEqualTo(velocity.magnitude + Epsilon), "Letting go adds nothing.");
            Vector3 swing = tangent * 6;
            Assert.That(Vector3.Distance(RopeMath.ReleaseVelocity(anchor, body, swing, 5), swing), Is.LessThan(Epsilon), "A pure swing is kept as is.");
            Vector3 inward = tangent * 6 - radial * 2;
            Assert.That(Vector3.Distance(RopeMath.ReleaseVelocity(anchor, body, inward, 5), inward), Is.LessThan(Epsilon));
            Vector3 slack = new Vector3(1, 7, 0);
            Assert.That(Vector3.Distance(RopeMath.ReleaseVelocity(anchor, slack, velocity, 5), velocity), Is.LessThan(Epsilon), "A slack rope does not act.");
        }

        [Test]
        public void PendulumKeepsItsLengthAndGainsNoEnergy()
        {
            Vector3 anchor = Vector3.up * 10;
            const float length = 5, g = 25, dt = 1 / 120f;
            Vector3 position = anchor + Quaternion.Euler(0, 0, 40) * Vector3.down * length, velocity = Vector3.zero;
            float startHeight = position.y, maxSpeed = 0, maxHeightLater = float.MinValue;
            for (int i = 0; i < 360; i++)
            {
                RopeMath.Step(ref position, ref velocity, anchor, length, Vector3.down * g, dt);
                Assert.That((position - anchor).magnitude, Is.LessThanOrEqualTo(length + 1e-3f));
                maxSpeed = Mathf.Max(maxSpeed, velocity.magnitude);
                if (i > 60) maxHeightLater = Mathf.Max(maxHeightLater, position.y);
            }
            float ideal = Mathf.Sqrt(2 * g * length * (1 - Mathf.Cos(40 * Mathf.Deg2Rad)));
            Assert.That(maxSpeed, Is.EqualTo(ideal).Within(ideal * .08f), "Lowest point: about √(2gh).");
            Assert.That(maxHeightLater, Is.LessThanOrEqualTo(startHeight + 1e-3f), "It never swings higher than it started.");
        }

        [Test]
        public void HangingStillPullsTheAnchorWithTheBodysWeight()
        {
            Vector3 anchor = Vector3.up * 5, position = Vector3.zero, velocity = Vector3.zero;
            const float g = 25, dt = 1 / 120f, mass = 1;
            Vector3 total = Vector3.zero;
            for (int i = 0; i < 120; i++)
                total += RopeMath.TensionImpulse(RopeMath.Step(ref position, ref velocity, anchor, 5, Vector3.down * g, dt), mass, dt);
            Assert.That(total.y, Is.EqualTo(-mass * g).Within(.05f), "One second of hanging: an impulse of m·g toward the body.");
            Assert.That(Mathf.Abs(total.x) + Mathf.Abs(total.z), Is.LessThan(1e-3f));
        }

        [Test]
        public void LookingUpOrDownClimbsOtherwiseTheKeysSwing()
        {
            Assert.That(RopeMath.ClimbRate(Vector2.up, 40, 25, 2), Is.EqualTo(2), "W while looking up: up at the board's 2 m/s.");
            Assert.That(RopeMath.ClimbRate(Vector2.up, -40, 25, 2), Is.EqualTo(-2), "W while looking down: down.");
            Assert.That(RopeMath.ClimbRate(Vector2.down, 40, 25, 2), Is.EqualTo(-2));
            Assert.That(RopeMath.ClimbRate(Vector2.up, 10, 25, 2), Is.EqualTo(0));
            Vector3 pump = RopeMath.PumpAcceleration(new Vector3(0, .2f, 1), Vector3.right, Vector2.up, 10, 25, 5);
            Assert.That(Vector3.Distance(pump, Vector3.forward * 5), Is.LessThan(Epsilon), "W while looking level: swing forward.");
            Assert.That(RopeMath.PumpAcceleration(Vector3.forward, Vector3.right, Vector2.up, 40, 25, 5).magnitude, Is.EqualTo(0).Within(Epsilon));
            Assert.That(Vector3.Distance(RopeMath.PumpAcceleration(Vector3.forward, Vector3.right, Vector2.right, 40, 25, 5), Vector3.right * 5), Is.LessThan(Epsilon));
        }

        [Test]
        public void TheCrosshairFindsTheClosestPieceOfRope()
        {
            float distance = RopeMath.RayToSegment(Vector3.zero, Vector3.forward, 10, new Vector3(.2f, 2, 3), new Vector3(.2f, -2, 3), out float t, out float u);
            Assert.That(distance, Is.EqualTo(.2f).Within(Epsilon));
            Assert.That(t, Is.EqualTo(3).Within(Epsilon));
            Assert.That(u, Is.EqualTo(.5f).Within(Epsilon));
            float far = RopeMath.RayToSegment(Vector3.zero, Vector3.forward, 2, new Vector3(0, 1, 3), new Vector3(0, -1, 3), out t, out _);
            Assert.That(t, Is.EqualTo(2).Within(Epsilon), "Beyond the reach the ray stops at its end.");
            Assert.That(far, Is.EqualTo(1).Within(Epsilon));
        }

        // ---------- assets ----------

        [Test]
        public void EffectAssetsCarryTheBoardNumbersAndPrefabs()
        {
            var cube = AssetDatabase.LoadAssetAtPath<StoneCubeEffect>("Assets/Game/Data/Effects/Stone cube.asset");
            Assert.That(cube, Is.Not.Null);
            // Owner, 09.11 evening: the base cube is a step for the 1.2 m jump; the upgrade keeps the board's ×4/3 growth (1.5 → 2).
            Assert.That(cube.sizeBase, Is.EqualTo(1.1f)); Assert.That(cube.sizeUpgraded, Is.EqualTo(1.47f)); Assert.That(cube.mass, Is.EqualTo(1));
            Assert.That(cube.sizeBase, Is.LessThan(1.2f), "Lower than the player's jump: climbed with an ordinary jump.");
            Assert.That(cube.sizeUpgraded / cube.sizeBase, Is.EqualTo(2f / 1.5f).Within(.01f), "Grows by the board's ratio when upgraded.");
            Assert.That(cube.TryGetStat(out var label, out var baseSide, out var upgradedSide, out var unit), Is.True);
            Assert.That((label, baseSide, upgradedSide, unit), Is.EqualTo(("Сторона", 1.1f, 1.47f, " м")), "The workshop line: «Сторона 1,1 → 1,5 м».");
            Assert.That(cube.cubePrefab, Is.Not.Null, "The cube prefab is referenced from the effect asset.");
            Assert.That(cube.cubePrefab.body.isKinematic, Is.False, "A real physical body: Impulse and Attraction move it.");
            Assert.That(cube.cubePrefab.transform.localScale, Is.EqualTo(Vector3.one), "Unscaled root: what rides on the cube keeps its size.");

            var branch = AssetDatabase.LoadAssetAtPath<BranchBridgeEffect>("Assets/Game/Data/Effects/Branch bridge.asset");
            Assert.That(branch, Is.Not.Null);
            Assert.That(branch.lengthBase, Is.EqualTo(5)); Assert.That(branch.lengthUpgraded, Is.EqualTo(7)); Assert.That(branch.width, Is.EqualTo(1.2f));
            Assert.That(branch.branchPrefab, Is.Not.Null);
            Assert.That(branch.branchPrefab.walkway, Is.Not.Null);

            var rope = AssetDatabase.LoadAssetAtPath<RopeEffect>("Assets/Game/Data/Effects/Rope.asset");
            Assert.That(rope, Is.Not.Null);
            Assert.That(rope.lengthBase, Is.EqualTo(5)); Assert.That(rope.lengthUpgraded, Is.EqualTo(10)); Assert.That(rope.climbSpeed, Is.EqualTo(2));
            Assert.That(rope.ropePrefab, Is.Not.Null);
            Assert.That(rope.ropePrefab.segments.Length * rope.ropePrefab.segmentLength, Is.GreaterThanOrEqualTo(rope.lengthUpgraded),
                "Enough authored pieces for the upgraded length.");
            foreach (var piece in rope.ropePrefab.segments) Assert.That(piece, Is.Not.Null);

            foreach (var line in new ICardStatLine[] { cube, branch, rope })
                Assert.That(line.TryGetStat(out _, out _, out _, out _), Is.True);
        }
    }
}
