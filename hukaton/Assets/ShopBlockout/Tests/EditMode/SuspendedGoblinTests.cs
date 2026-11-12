using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hakaton.ShopBlockout.Tests
{
    public sealed class SuspendedGoblinTests
    {
        GameObject fixture;
        SuspendedGoblin lift;

        [SetUp]
        public void UseAuthoredRigWithItsSavedGeometryAndPoses()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShopBlockout/Prefabs/GoblinCeilingRig.prefab");
            fixture=Object.Instantiate(prefab);
            fixture.SetActive(false);
            // The saved mechanism and billboard are editable before Play.
            lift=fixture.GetComponent<SuspendedGoblin>();
            Assert.That(lift,Is.Not.Null);
            Assert.That(lift.goblinRoot&&lift.stowedPose&&lift.deployedPose&&lift.hatchHinge&&lift.pullHandle,Is.True);
            for(int i=0;i<2;i++)
                Assert.That(lift.cableSprites[i]&&lift.cableTops[i]&&lift.cableHarnesses[i]&&lift.cableHarnesses[i].IsChildOf(lift.goblinRoot),Is.True);
            lift.startDeployed=false;
        }

        [TearDown] public void CleanUp() {Object.DestroyImmediate(fixture);}

        [Test]
        public void OpensHatchBeforeLoweringAndIgnoresRepeatedPulls()
        {
            Assert.That(lift.Summon(),Is.True);
            Vector3 start=lift.stowedPose.position;
            lift.Advance(lift.hatchSeconds*.5f);
            Assert.That(lift.State,Is.EqualTo(GoblinLiftState.OpeningHatch));
            Assert.That(lift.goblinRoot.position,Is.EqualTo(start));
            Assert.That(Quaternion.Angle(lift.hatchHinge.localRotation,Quaternion.Euler(lift.closedHatchEuler)),Is.GreaterThan(10));
            Assert.That(lift.Summon(),Is.False);
            lift.Advance(lift.hatchSeconds*.5f+.1f);
            Assert.That(lift.State,Is.EqualTo(GoblinLiftState.Lowering));
            Assert.That(lift.goblinRoot.position.y,Is.LessThan(start.y));
            Assert.That(Quaternion.Angle(lift.hatchHinge.localRotation,Quaternion.Euler(lift.openHatchEuler)),Is.LessThan(.001f));
            lift.Advance(10);
            Assert.That(lift.IsDeployed,Is.True);
            Assert.That(Vector3.Distance(lift.goblinRoot.position,lift.deployedPose.position),Is.LessThan(.0001f));
            Assert.That(lift.Summon(),Is.False);
            AssertCablesAttached();
        }

        [Test]
        public void BothCablesStayAttachedWhileTheTinyGoblinDescends()
        {
            lift.Summon();
            for(int i=0;i<18;i++)
            {
                lift.Advance(.1f);
                AssertCablesAttached();
                Assert.That(lift.goblinRoot.localScale,Is.EqualTo(Vector3.one),"The 35 cm character must not be scaled by the lift.");
            }
            Assert.That(lift.IsDeployed,Is.True);
        }

        [Test]
        public void HatchOvershootsAndReboundsBeforeTheGoblinDescends()
        {
            lift.Summon();
            var closed = Quaternion.Euler(lift.closedHatchEuler);
            var open = Quaternion.Euler(lift.openHatchEuler);
            float openAngle = Quaternion.Angle(closed, open);
            lift.Advance(lift.hatchSeconds * .40f);
            Assert.That(Quaternion.Angle(closed, lift.hatchHinge.localRotation), Is.GreaterThan(openAngle + 10));
            Assert.That(lift.goblinRoot.position, Is.EqualTo(lift.stowedPose.position));
            lift.Advance(lift.hatchSeconds * .24f);
            Assert.That(Quaternion.Angle(closed, lift.hatchHinge.localRotation), Is.LessThan(openAngle - 3));
            Assert.That(lift.State, Is.EqualTo(GoblinLiftState.OpeningHatch));
            lift.Advance(lift.hatchSeconds * .18f);
            Assert.That(Quaternion.Angle(closed, lift.hatchHinge.localRotation), Is.GreaterThan(openAngle + 3));
            lift.Advance(lift.hatchSeconds * .18f + .0001f);
            Assert.That(Quaternion.Angle(open, lift.hatchHinge.localRotation), Is.LessThan(.001f));
            Assert.That(lift.State, Is.EqualTo(GoblinLiftState.Lowering));
            Assert.That(lift.hatchHinge.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void HatchSquashesAtItsFixedRearHingeAndClearsTheWallAndDoorway()
        {
            var panel = lift.hatchHinge.GetComponentInChildren<MeshFilter>();
            var bounds = panel.sharedMesh.bounds;
            var room = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShopBlockout/Prefabs/RoomShell.prefab");
            BoxCollider wall = null;
            foreach (var collider in room.GetComponentsInChildren<BoxCollider>())
                if (collider.name == "Back wall") wall = collider;
            Assert.That(wall, Is.Not.Null);
            float wallFace = wall.transform.TransformPoint(wall.center + Vector3.forward * wall.size.z * .5f).z;
            float closedFront = panel.transform.TransformPoint(bounds.center + Vector3.forward * bounds.extents.z).z;
            Vector3 hingePosition = lift.hatchHinge.position;
            Assert.That(hingePosition.z, Is.LessThan(panel.transform.position.z));
            float minDepthScale = 1, maxDepthScale = 1;
            lift.Summon();
            for (int frame = 0; frame < 120; frame++)
            {
                lift.Advance(lift.hatchSeconds / 120);
                Assert.That(lift.hatchHinge.position, Is.EqualTo(hingePosition));
                minDepthScale = Mathf.Min(minDepthScale, lift.hatchHinge.localScale.z);
                maxDepthScale = Mathf.Max(maxDepthScale, lift.hatchHinge.localScale.z);
                for (int corner = 0; corner < 8; corner++)
                {
                    var signs = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    var point = panel.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, signs));
                    Assert.That(point.z, Is.GreaterThan(wallFace + .01f), "The bouncing lid must clear the back wall.");
                    Assert.That(point.z, Is.LessThanOrEqualTo(closedFront + .001f), "The lid must never swing toward the doorway.");
                }
            }
            Assert.That(minDepthScale, Is.LessThan(.8f));
            Assert.That(maxDepthScale, Is.GreaterThan(1.2f));
            lift.Advance(.001f);
            Assert.That(lift.hatchHinge.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void GoblinIsAnUprightCapsuleSolidOnlyWhileHangingStillAndTheRopeNeverBlocks()
        {
            var capsule = lift.bodyCollider as CapsuleCollider;
            Assert.That(capsule, Is.Not.Null, "An upright capsule is the same from every side of the turning billboard.");
            Assert.That(capsule.transform, Is.EqualTo(lift.goblinRoot));
            Assert.That(capsule.direction, Is.EqualTo(1));
            Assert.That(lift.goblinRoot.GetComponent<BoxCollider>(), Is.Null, "No turning box that sweeps into the player.");
            lift.Summon();
            lift.Advance(lift.hatchSeconds + lift.lowerSeconds * .5f);
            Assert.That(lift.State, Is.EqualTo(GoblinLiftState.Lowering));
            Assert.That(capsule.isTrigger, Is.True, "Coming down he passes through whoever stands below.");
            lift.Advance(10);
            Assert.That(lift.IsDeployed, Is.True);
            Assert.That(capsule.isTrigger, Is.False, "Hanging still he is as solid as he looks.");
            Assert.That(lift.Raise(), Is.True);
            lift.Advance(.05f);
            Assert.That(capsule.isTrigger, Is.True);

            var rope = fixture.transform.Find("Pull rope interaction");
            Assert.That(rope, Is.Not.Null);
            Assert.That(rope.GetComponent<ShopInteractionTarget>().kind, Is.EqualTo(ShopServiceKind.GoblinRope));
            foreach (var collider in rope.GetComponents<Collider>())
                Assert.That(collider.isTrigger, Is.True, "The dangling handle is aimed at, never walked into.");
        }

        void AssertCablesAttached()
        {
            for(int i=0;i<2;i++)
            {
                var rope=lift.cableSprites[i];
                var cable=rope.transform;
                Vector3 top=cable.TransformPoint(Vector3.up*(rope.size.y*.5f));
                Vector3 bottom=cable.TransformPoint(Vector3.down*(rope.size.y*.5f));
                Assert.That(Vector3.Distance(top,lift.cableTops[i].position),Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(bottom,lift.cableHarnesses[i].position),Is.LessThan(.0001f));
            }
        }

        [Test]
        public void WholeSuspensionFacesPlayerWithoutPitchRollOrDetachedRopes()
        {
            var viewer = new GameObject("Test viewer").transform;
            try
            {
                lift.viewer = viewer;
                lift.Summon();
                for (int step = 0; step < 24; step++)
                {
                    float angle = step * Mathf.PI / 6;
                    viewer.position = lift.transform.position + new Vector3(Mathf.Sin(angle) * 3, step % 2 == 0 ? 5 : -2, Mathf.Cos(angle) * 3);
                    lift.Advance(.1f);
                    typeof(SuspendedGoblin).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(lift, null);
                    Vector3 away = lift.suspensionRoot.position - viewer.position;
                    away.y = 0;
                    Assert.That(Vector3.Dot(lift.suspensionRoot.forward, away.normalized), Is.GreaterThan(.9999f));
                    Assert.That(Vector3.Distance(lift.suspensionRoot.up, Vector3.up), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(lift.drawing.transform.rotation, lift.suspensionRoot.rotation), Is.LessThan(.001f));
                    foreach (var rope in lift.cableSprites)
                    {
                        Assert.That(rope.drawMode, Is.EqualTo(SpriteDrawMode.Tiled));
                        Assert.That(rope.transform.localScale, Is.EqualTo(Vector3.one));
                        Assert.That(Vector3.Dot(rope.transform.forward, lift.suspensionRoot.forward), Is.GreaterThan(.9999f));
                    }
                    AssertCablesAttached();
                }
                Quaternion previous = lift.suspensionRoot.rotation;
                viewer.position = lift.suspensionRoot.position + Vector3.up * 3;
                typeof(SuspendedGoblin).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(lift, null);
                Assert.That(Quaternion.Angle(previous, lift.suspensionRoot.rotation), Is.LessThan(.001f));
            }
            finally { Object.DestroyImmediate(viewer.gameObject); }
        }
    }
}
