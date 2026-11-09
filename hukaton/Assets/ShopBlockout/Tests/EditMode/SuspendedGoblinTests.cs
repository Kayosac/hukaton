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
            // The rig's own saved references: hatch, handle and cables live in the nested ShopArt mesh prefab.
            lift=fixture.GetComponent<SuspendedGoblin>();
            Assert.That(lift,Is.Not.Null);
            Assert.That(lift.goblinRoot&&lift.stowedPose&&lift.deployedPose&&lift.hatchHinge&&lift.pullHandle,Is.True);
            for(int i=0;i<2;i++)
                Assert.That(lift.cableMeshes[i]&&lift.cableTops[i]&&lift.cableHarnesses[i]&&lift.cableHarnesses[i].IsChildOf(lift.goblinRoot),Is.True);
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
                var cable=lift.cableMeshes[i];
                Vector3 top=cable.TransformPoint(Vector3.down*(lift.cableMeshLength*.5f));
                Vector3 bottom=cable.TransformPoint(Vector3.up*(lift.cableMeshLength*.5f));
                Assert.That(Vector3.Distance(top,lift.cableTops[i].position),Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(bottom,lift.cableHarnesses[i].position),Is.LessThan(.0001f));
            }
        }
    }
}
