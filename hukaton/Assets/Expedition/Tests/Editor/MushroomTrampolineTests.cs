using System.Collections.Generic;
using Hakaton.Decks.Unity;
using Hakaton.Expeditions.Cards;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.Expeditions.Tests
{
    /// <summary>
    /// Playtest change of the Mushroom trampoline: the series of bounces (+5 % of the first bounce per bounce, up to
    /// +25 %, and when it ends), the softer placement (wall shelf, low walls, ceilings, creatures, water, cramped spots —
    /// real physics queries far above the valley) and the authored shelf prefab.
    /// </summary>
    public sealed class MushroomTrampolineTests
    {
        static readonly Vector3 Far = new Vector3(-900, 2600, 900);
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            created.Clear();
            Physics.SyncTransforms();
        }

        GameObject Box(string name, Vector3 min, Vector3 max)
        {
            var go = new GameObject(name);
            created.Add(go);
            go.transform.position = Far + (min + max) * .5f;
            go.AddComponent<BoxCollider>().size = max - min;
            return go;
        }

        static MushroomPlacementRules Rules => MushroomPlacementRules.Default;

        static MushroomPlacement.Request Hit(Collider target, Vector3 local, Vector3 normal, Vector3 direction, bool living = false)
            => new MushroomPlacement.Request { target = target, point = Far + local, normal = normal, direction = direction, living = living, yaw = 30 };

        // ---------- the series ----------

        [Test]
        public void EachBounceOfASeriesAddsFivePercentOfTheFirstUpToTwentyFive()
        {
            float[] expected = { 4, 4.2f, 4.4f, 4.6f, 4.8f, 5, 5, 5 };
            for (int step = 0; step < expected.Length; step++)
                Assert.That(MushroomBounce.SeriesHeight(4, step), Is.EqualTo(expected[step]).Within(1e-4f), $"bounce {step + 1}");
            Assert.That(MushroomBounce.SeriesStepShare, Is.EqualTo(.05f));
            Assert.That(MushroomBounce.SeriesMaxSteps, Is.EqualTo(5));
        }

        [Test]
        public void InsideASeriesTheHeightReturnNoLongerShrinksTheBounces()
        {
            var series = new MushroomSeries();
            float first = series.Next(MushroomBounce.BounceHeight(MushroomBounce.FallSpeed(5), .8f, 1.5f, 10), 10, .05f, 5);
            Assert.That(first, Is.EqualTo(4).Within(1e-3f), "The first bounce is the fall's own: 5 m at k 0.8.");
            float previous = first;
            for (int i = 1; i <= 7; i++)
            {
                // Each landing comes from the previous bounce; k alone would shrink it to 80 %.
                float fall = MushroomBounce.BounceHeight(MushroomBounce.FallSpeed(previous), .8f, 1.5f, 10);
                float height = series.Next(fall, 10, .05f, 5);
                Assert.That(height, Is.EqualTo(4 * (1 + .05f * Mathf.Min(i, 5))).Within(1e-3f), $"bounce {i + 1}");
                previous = height;
            }
            Assert.That(series.Bounces, Is.EqualTo(8));
            Assert.That(series.FirstHeight, Is.EqualTo(4).Within(1e-3f), "The line stays on the first bounce.");
        }

        [Test]
        public void ABiggerFallStillWinsAndTheSeriesStopsAtTheMaximum()
        {
            var series = new MushroomSeries();
            series.Next(2, 10, .05f, 5);
            Assert.That(series.Next(7.2f, 10, .05f, 5), Is.EqualTo(7.2f), "A drop from high above gives its own, bigger bounce.");
            Assert.That(series.Next(1.5f, 10, .05f, 5), Is.EqualTo(2.2f).Within(1e-4f), "Back on the series' line (third bounce: +10 %).");

            var high = new MushroomSeries();
            high.Next(9.5f, 10, .05f, 5);
            for (int i = 0; i < 6; i++) Assert.That(high.Next(7.6f, 10, .05f, 5), Is.LessThanOrEqualTo(10f));
            Assert.That(MushroomBounce.SeriesBounceHeight(7.6f, 9.5f, 5, 10), Is.EqualTo(10).Within(1e-4f), "Never above the card's 10 m.");

            var soft = new MushroomSeries();
            Assert.That(soft.Next(MushroomBounce.BounceHeight(0, .8f, 1.5f, 10), 10, .05f, 5), Is.EqualTo(1.5f), "A hop starts at the minimum…");
            Assert.That(soft.Next(MushroomBounce.BounceHeight(0, .8f, 1.5f, 10), 10, .05f, 5), Is.EqualTo(1.575f).Within(1e-4f), "…and climbs from there.");
        }

        static MushroomSeries.Sample Sample(bool running = true, bool rope = false, bool grounded = false, bool onMushroom = false,
            Vector3 position = default, float speed = 6)
            => new MushroomSeries.Sample(running, rope, grounded, onMushroom, position, speed);

        [Test]
        public void TheSeriesEndsOnTheGroundOnTheRopeAfterAJumpOrWithTheOuting()
        {
            Assert.That(MushroomSeries.Check(Sample(), 1, .1f, .2f), Is.EqualTo(MushroomSeries.Break.None), "In the air: goes on.");
            Assert.That(MushroomSeries.Check(Sample(grounded: true, onMushroom: true), 1, 0, .2f), Is.EqualTo(MushroomSeries.Break.None),
                "Touching a cap or a shelf: the bounce follows, the series goes on.");
            Assert.That(MushroomSeries.Check(Sample(grounded: true), 1, 0, .2f), Is.EqualTo(MushroomSeries.Break.Ground));
            Assert.That(MushroomSeries.Check(Sample(rope: true), 1, 0, .2f), Is.EqualTo(MushroomSeries.Break.ExternallyControlled));
            Assert.That(MushroomSeries.Check(Sample(running: false), 1, 0, .2f), Is.EqualTo(MushroomSeries.Break.OutingOver));
            Assert.That(MushroomSeries.Check(Sample(), 3, 0, .2f), Is.EqualTo(MushroomSeries.Break.Unwatched), "No mushroom was watching.");
            Assert.That(MushroomSeries.Check(Sample(), 1, 12, .3f), Is.EqualTo(MushroomSeries.Break.Moved), "Teleport, Swap.");
            Assert.That(MushroomSeries.Check(Sample(), 1, 1.6f, .3f), Is.EqualTo(MushroomSeries.Break.None), "Fast flight is not a jump.");
        }

        [Test]
        public void ObservingKeepsALongFlightAndEndsOnTheGround()
        {
            var series = new MushroomSeries();
            series.Observe(1, .016f, Sample(grounded: true));
            Assert.That(series.Active, Is.False);
            series.Next(3, 10, .05f, 5);
            series.Touch(1, Vector3.zero, 8);
            // Moon gravity: seven seconds in the air, many frames, small honest moves.
            for (int frame = 2; frame < 440; frame++)
                series.Observe(frame, .016f, Sample(position: Vector3.up * (frame * .05f), speed: 4));
            Assert.That(series.Active, Is.True, "No time limit.");
            series.Observe(439, .016f, Sample(grounded: true));
            Assert.That(series.Active, Is.True, "A second look in the same frame is ignored.");
            series.Observe(440, .016f, Sample(position: Vector3.up * 22, grounded: true));
            Assert.That(series.Active, Is.False);
            Assert.That(series.LastBreak, Is.EqualTo(MushroomSeries.Break.Ground));

            series.Next(3, 10, .05f, 5);
            series.Touch(500, Vector3.zero, 8);
            series.Observe(503, .016f, Sample());
            Assert.That(series.Active, Is.False, "Frames without any mushroom end it.");
            Assert.That(series.LastBreak, Is.EqualTo(MushroomSeries.Break.Unwatched));
            Assert.That(series.Next(3, 10, .05f, 5), Is.EqualTo(3), "The next bounce starts afresh.");
        }

        // ---------- placement: pure rules ----------

        [Test]
        public void SurfacesSortIntoFloorsWallsAndCeilings()
        {
            Vector3 Tilted(float degrees) => Quaternion.Euler(degrees, 0, 0) * Vector3.up;
            Assert.That(MushroomPlacement.Classify(Vector3.up, 48, -.3f), Is.EqualTo(MushroomSurface.Floor));
            Assert.That(MushroomPlacement.Classify(Tilted(45), 48, -.3f), Is.EqualTo(MushroomSurface.Floor), "Up to the player's slope limit.");
            Assert.That(MushroomPlacement.Classify(Tilted(50), 48, -.3f), Is.EqualTo(MushroomSurface.Wall), "Steeper: a shelf.");
            Assert.That(MushroomPlacement.Classify(Vector3.right, 48, -.3f), Is.EqualTo(MushroomSurface.Wall));
            Assert.That(MushroomPlacement.Classify(new Vector3(0, -.25f, .97f), 48, -.3f), Is.EqualTo(MushroomSurface.Wall), "A slight overhang.");
            Assert.That(MushroomPlacement.Classify(new Vector3(0, -.5f, .87f), 48, -.3f), Is.EqualTo(MushroomSurface.Ceiling));
            Assert.That(MushroomPlacement.Classify(Vector3.down, 48, -.3f), Is.EqualTo(MushroomSurface.Ceiling));
        }

        [Test]
        public void AShelfFacesStraightOutOfTheWallWithALevelTop()
        {
            var normal = new Vector3(.6f, -.2f, .77f);
            var rotation = MushroomPlacement.ShelfRotation(normal);
            Vector3 level = new Vector3(normal.x, 0, normal.z).normalized;
            Assert.That(Vector3.Angle(rotation * Vector3.forward, level), Is.LessThan(.01f));
            Assert.That(Vector3.Angle(rotation * Vector3.up, Vector3.up), Is.LessThan(.01f));
        }

        [Test]
        public void NeighbourSpotsStayCloseAndCapsShrinkToSeventyPercent()
        {
            var seen = new HashSet<Vector3>();
            for (int i = 0; i < MushroomPlacement.CandidateCount; i++)
            {
                Vector3 floor = MushroomPlacement.FloorOffset(i, new Vector3(.3f, -.5f, 1));
                Assert.That(floor.magnitude, Is.LessThanOrEqualTo(.8f + 1e-4f));
                Assert.That(floor.y, Is.EqualTo(0));
                Assert.That(seen.Add(floor), Is.True, "Every neighbour is a different spot.");
                Vector3 wall = MushroomPlacement.ShelfOffset(i, Vector3.right);
                Assert.That(wall.magnitude, Is.LessThanOrEqualTo(.81f));
                Assert.That(Vector3.Dot(wall, Vector3.right), Is.EqualTo(0).Within(1e-5f), "Along the wall, never into it.");
            }
            Assert.That(MushroomPlacement.FloorOffset(1, Vector3.forward), Is.EqualTo(Vector3.back * .4f), "First back toward the thrower.");
            Assert.That(MushroomPlacement.CapScale(0, .7f), Is.EqualTo(1));
            Assert.That(MushroomPlacement.CapScale(MushroomPlacement.ScaleSteps - 1, .7f), Is.EqualTo(.7f).Within(1e-5f));
        }

        // ---------- placement: physics ----------

        static void Solve(MushroomPlacement.Request request, out bool found, out MushroomSpot spot, out MushroomPlacementProblem problem)
        {
            Physics.SyncTransforms();
            found = MushroomPlacement.TrySolve(request, Rules, out spot, out problem);
        }

        [Test]
        public void AFloorHitGrowsTheMushroomRightThere()
        {
            var floor = Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5)).GetComponent<Collider>();
            Solve(Hit(floor, Vector3.zero, Vector3.up, new Vector3(0, -.5f, 1)), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(spot.shelf, Is.False);
            Assert.That(Vector3.Distance(spot.position, Far), Is.LessThan(.001f));
            Assert.That(spot.support, Is.SameAs(floor));
            Assert.That(spot.capScale, Is.EqualTo(1));
        }

        [Test]
        public void AHighWallHitGrowsALevelShelfOnTheWall()
        {
            Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5));
            var wall = Box("Wall", new Vector3(-1, 0, -3), new Vector3(0, 4, 3)).GetComponent<Collider>();
            Solve(Hit(wall, new Vector3(0, 2, 0), Vector3.right, Vector3.left), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(spot.shelf, Is.True);
            Assert.That(Vector3.Distance(spot.position, Far + new Vector3(0, 2, 0)), Is.LessThan(.001f), "Its hinge is where the card hit.");
            Assert.That(Vector3.Angle(spot.rotation * Vector3.forward, Vector3.right), Is.LessThan(.01f));
            Assert.That(Vector3.Angle(spot.rotation * Vector3.up, Vector3.up), Is.LessThan(.01f));
            Assert.That(spot.support, Is.SameAs(wall));
        }

        [Test]
        public void ALowWallHitGrowsTheMushroomOnTheGroundInFrontOfIt()
        {
            var floor = Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5)).GetComponent<Collider>();
            var wall = Box("Wall", new Vector3(-1, 0, -3), new Vector3(0, 4, 3)).GetComponent<Collider>();
            Solve(Hit(wall, new Vector3(0, .4f, 0), Vector3.right, Vector3.left), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(spot.shelf, Is.False);
            Assert.That(spot.position.y, Is.EqualTo(Far.y).Within(.001f), "On the ground.");
            Assert.That(spot.position.x - Far.x, Is.EqualTo(.6f).Within(.01f), "The stem stands 0.6 m out from the wall.");
            Assert.That(spot.support, Is.SameAs(floor));
        }

        [Test]
        public void ACeilingHitDropsTheMushroomToTheGroundBelow()
        {
            var floor = Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5)).GetComponent<Collider>();
            var ceiling = Box("Ceiling", new Vector3(-3, 3, -3), new Vector3(3, 3.5f, 3)).GetComponent<Collider>();
            Solve(Hit(ceiling, new Vector3(.5f, 3, .5f), Vector3.down, Vector3.up), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(spot.shelf, Is.False);
            Assert.That(Vector3.Distance(spot.position, Far + new Vector3(.5f, 0, .5f)), Is.LessThan(.01f));
            Assert.That(spot.support, Is.SameAs(floor));
        }

        [Test]
        public void AHighCeilingWithNoGroundInReachRefuses()
        {
            var ceiling = Box("Ceiling", new Vector3(-3, 10, -3), new Vector3(3, 10.5f, 3)).GetComponent<Collider>();
            Solve(Hit(ceiling, new Vector3(0, 10, 0), Vector3.down, Vector3.up), out bool found, out _, out var problem);
            Assert.That(found, Is.False);
            Assert.That(problem, Is.EqualTo(MushroomPlacementProblem.Ceiling));
        }

        [Test]
        public void DeepWaterRefuses()
        {
            int water = LayerMask.NameToLayer("Water");
            Assume.That(water, Is.GreaterThanOrEqualTo(0), "The project has a Water layer.");
            Box("Lake bed", new Vector3(-5, -3, -5), new Vector3(5, -2, 5));
            var surface = Box("Water", new Vector3(-5, -.1f, -5), new Vector3(5, 0, 5));
            surface.layer = water;
            Solve(Hit(surface.GetComponent<Collider>(), Vector3.zero, Vector3.up, new Vector3(0, -.5f, 1)), out bool found, out _, out var problem);
            Assert.That(found, Is.False);
            Assert.That(problem, Is.EqualTo(MushroomPlacementProblem.Water));
        }

        [Test]
        public void AShallowBottomUnderWaterCarriesTheMushroom()
        {
            int water = LayerMask.NameToLayer("Water");
            Assume.That(water, Is.GreaterThanOrEqualTo(0), "The project has a Water layer.");
            var bed = Box("Stream bed", new Vector3(-5, -1, -5), new Vector3(5, -.2f, 5)).GetComponent<Collider>();
            var surface = Box("Water", new Vector3(-5, -.1f, -5), new Vector3(5, 0, 5));
            surface.layer = water;
            Solve(Hit(surface.GetComponent<Collider>(), Vector3.zero, Vector3.up, new Vector3(0, -.5f, 1)), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(spot.position.y, Is.EqualTo(Far.y - .2f).Within(.001f), "From the bottom, 20 cm under the surface.");
            Assert.That(spot.support, Is.SameAs(bed));
        }

        [Test]
        public void ACreatureHitGrowsTheMushroomOnTheGroundBesideIt()
        {
            Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5));
            var creature = Box("Creature", new Vector3(-.3f, 0, -.3f), new Vector3(.3f, 1.6f, .3f));
            creature.AddComponent<WorldCreature>();
            Solve(Hit(creature.GetComponent<Collider>(), new Vector3(0, 1, .3f), Vector3.forward, Vector3.back, living: true),
                out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(spot.shelf, Is.False);
            Assert.That(spot.position.y, Is.EqualTo(Far.y).Within(.001f), "On the ground, not on the creature.");
            var flat = new Vector2(spot.position.x - Far.x, spot.position.z - Far.z);
            Assert.That(flat.magnitude, Is.GreaterThanOrEqualTo(.5f), "Beside it: the stem never grows through a body that stands still.");
        }

        [Test]
        public void OnlyTheStemNeedsRoomTheCapMayTouchATrunk()
        {
            var floor = Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5)).GetComponent<Collider>();
            Box("Trunk", new Vector3(.45f, 0, -.15f), new Vector3(.75f, 4, .15f));
            Solve(Hit(floor, Vector3.zero, Vector3.up, new Vector3(0, -.5f, 1)), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(Vector3.Distance(spot.position, Far), Is.LessThan(.001f), "Right where the card hit, 0.45 m from the trunk.");
        }

        [Test]
        public void ACrampedSpotMovesToANeighbour()
        {
            var floor = Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5)).GetComponent<Collider>();
            Box("Stone", new Vector3(-.3f, .4f, -.3f), new Vector3(.3f, 1, .3f));
            Solve(Hit(floor, Vector3.zero, Vector3.up, new Vector3(0, -.5f, 1)), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(Vector3.Distance(spot.position, Far), Is.EqualTo(.8f).Within(.01f), "The 0.4 m ring is still under the stone.");
            Assert.That(spot.capScale, Is.EqualTo(1));
        }

        [Test]
        public void NoRoomAnywhereSaysItIsCramped()
        {
            var floor = Box("Floor", new Vector3(-5, -1, -5), new Vector3(5, 0, 5)).GetComponent<Collider>();
            Box("Low roof", new Vector3(-5, .5f, -5), new Vector3(5, 1, 5));
            Solve(Hit(floor, Vector3.zero, Vector3.up, new Vector3(0, -.5f, 1)), out bool found, out _, out var problem);
            Assert.That(found, Is.False);
            Assert.That(problem, Is.EqualTo(MushroomPlacementProblem.Crowded));
        }

        [Test]
        public void ACrampedShelfSlidesAlongTheWall()
        {
            var wall = Box("Wall", new Vector3(-1, 0, -3), new Vector3(0, 4, 3)).GetComponent<Collider>();
            // A block standing out of the wall right at the hit, above and below it; the probes along the wall start inside it.
            Box("Ledge", new Vector3(.1f, 1.3f, -.2f), new Vector3(1, 2.9f, .2f));
            Solve(Hit(wall, new Vector3(0, 2, 0), Vector3.right, Vector3.left), out bool found, out var spot, out _);
            Assert.That(found, Is.True);
            Assert.That(spot.shelf, Is.True);
            Assert.That(spot.position.x - Far.x, Is.EqualTo(0).Within(.001f), "Still on the wall's face.");
            Assert.That(Mathf.Abs(spot.position.z - Far.z), Is.EqualTo(.8f).Within(.01f), "Moved 0.8 m along the wall.");
            Assert.That(spot.position.y - Far.y, Is.EqualTo(2).Within(.001f));
        }

        // ---------- the authored assets ----------

        [Test]
        public void TheCardGrowsShelvesFromItsAuthoredPrefab()
        {
            var effect = AssetDatabase.LoadAssetAtPath<MushroomTrampolineEffect>("Assets/Game/Data/Effects/Mushroom trampoline.asset");
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.maxSlope, Is.EqualTo(48), "The player's slope limit.");
            Assert.That(effect.seriesStepShare, Is.EqualTo(.05f).Within(1e-6f));
            Assert.That(effect.seriesMaxSteps, Is.EqualTo(5));
            Assert.That(effect.minCapScale, Is.EqualTo(.7f).Within(1e-6f));
            Assert.That(effect.mushroomPrefab.shape, Is.EqualTo(MushroomShape.Upright));

            var shelf = effect.shelfPrefab;
            Assert.That(shelf, Is.Not.Null, "Run the shelf authoring (Mushroom shelf.prefab) first.");
            Assert.That(shelf.shape, Is.EqualTo(MushroomShape.WallShelf));
            Assert.That(shelf.visual, Is.Not.Null);
            var collider = shelf.capCollider as MeshCollider;
            Assert.That(collider, Is.Not.Null, "The shelf's own mesh hull, not a box.");
            Assert.That(collider.convex, Is.True);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(shelf.colliders, Does.Contain(collider));
            var hull = collider.sharedMesh;
            Assert.That(hull, Is.Not.Null);
            Assert.That(hull.triangles.Length / 3, Is.LessThanOrEqualTo(255), "A convex collider keeps its exact hull.");
            Assert.That(hull.bounds.max.y, Is.EqualTo(shelf.capTop).Within(.03f), "The hull's top is the shelf's level top.");
            Assert.That(hull.bounds.min.z, Is.LessThan(0), "Its back edge is sunk into the wall.");

            var visual = shelf.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(visual.subMeshCount, Is.EqualTo(3));
            Assert.That(Vector3.Distance(visual.bounds.min, hull.bounds.min), Is.LessThan(.03f), "The hull matches the visible shelf.");
            Assert.That(Mathf.Abs(visual.bounds.max.x - hull.bounds.max.x) + Mathf.Abs(visual.bounds.max.z - hull.bounds.max.z), Is.LessThan(.03f));

            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Game/Data/mushroom_trampoline.asset");
            Assert.That(card.description, Does.Contain("гриб-полк"));
            Assert.That(card.description, Does.Contain("серия"));
        }
    }
}
