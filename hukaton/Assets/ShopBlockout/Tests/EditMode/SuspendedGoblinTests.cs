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
