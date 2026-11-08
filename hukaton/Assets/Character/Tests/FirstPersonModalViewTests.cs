using NUnit.Framework;
using System.Reflection;
using UnityEngine;

namespace Hakaton.Character.Tests
{
    public sealed class FirstPersonModalViewTests
    {
        GameObject fixture;
        FirstPersonModalView modal;
        Camera camera;
        Transform poseA, poseB;
        Vector3 baseline;
        Quaternion baselineRotation;

        [SetUp]
        public void SetUpAuthoredCameraAndOverlay()
        {
            fixture = new GameObject("Modal camera fixture");
            var eye = new GameObject("Eye height").transform;
            eye.SetParent(fixture.transform, false);
            eye.localPosition = new Vector3(0, 1.6f, 0);
            camera = new GameObject("Authored camera").AddComponent<Camera>();
            camera.transform.SetParent(eye, false);
            baseline = camera.transform.localPosition = new Vector3(.02f, 0, .01f);
            baselineRotation = camera.transform.localRotation = Quaternion.Euler(0, 0, .3f);
            camera.fieldOfView = 75;
            var gate = fixture.AddComponent<PlayerControlGate>();
            var overlay = new GameObject("Authored black overlay").AddComponent<CanvasGroup>();
            overlay.transform.SetParent(fixture.transform);
            modal = fixture.AddComponent<FirstPersonModalView>();
            modal.viewCamera = camera;
            modal.control = gate;
            modal.blinkOverlay = overlay;
            poseA = new GameObject("A").transform;
            poseA.SetParent(fixture.transform);
            poseA.SetPositionAndRotation(new Vector3(0, 1.6f, -1), Quaternion.Euler(12, 0, 0));
            poseB = new GameObject("B").transform;
            poseB.SetParent(fixture.transform);
            poseB.SetPositionAndRotation(new Vector3(.4f, 1.6f, 1.7f), Quaternion.Euler(23, 90, 0));
        }

        [TearDown]
        public void DestroyFixture() => Object.DestroyImmediate(fixture);

        [Test]
        public void PositionRotationAndFovChangeOnlyAtFullyBlackMidpoint()
        {
            Vector3 start = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            Assert.That(modal.Enter(poseA, 70), Is.True);
            modal.Advance(modal.closeSeconds * .5f);
            Assert.That(modal.control.HasModalOwner, Is.True);
            Assert.That(modal.blinkOverlay.blocksRaycasts, Is.True);
            Assert.That(modal.blinkOverlay.alpha, Is.InRange(.1f, .9f));
            Assert.That(camera.transform.position, Is.EqualTo(start));
            Assert.That(Quaternion.Angle(camera.transform.rotation, rotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(75));
            modal.Advance(modal.closeSeconds * .5f);
            Assert.That(modal.blinkOverlay.alpha, Is.EqualTo(1));
            Assert.That(camera.transform.position, Is.EqualTo(poseA.position));
            Assert.That(Quaternion.Angle(camera.transform.rotation, poseA.rotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(70));
            modal.Advance(modal.openSeconds);
            Assert.That(modal.Active, Is.True);
            Assert.That(modal.IsTransitioning, Is.False);
            Assert.That(modal.blinkOverlay.blocksRaycasts, Is.False);
            Assert.That(modal.control.HasModalOwner, Is.True);
        }

        [Test]
        public void SwitchingHoldsPreviousViewAndOverridesLocomotionCameraWrites()
        {
            modal.Enter(poseA, 70);
            modal.Advance(1);
            Assert.That(modal.blinkOverlay.alpha, Is.EqualTo(1), "A slow frame must still render black before revealing the changed pose.");
            modal.Advance(1);
            modal.SetPose(poseB, 80);
            modal.Advance(modal.closeSeconds * .5f);
            Assert.That(camera.transform.position, Is.EqualTo(poseA.position));
            // FirstPersonView has an earlier LateUpdate order and writes this each frame.
            camera.transform.localPosition = baseline;
            camera.transform.localRotation = baselineRotation;
            camera.fieldOfView = 75;
            modal.Advance(0);
            Assert.That(camera.transform.position, Is.EqualTo(poseA.position));
            Assert.That(camera.fieldOfView, Is.EqualTo(70));
            modal.Advance(modal.closeSeconds * .5f);
            Assert.That(modal.CurrentPose, Is.SameAs(poseB));
            Assert.That(camera.transform.position, Is.EqualTo(poseB.position));
            Assert.That(camera.fieldOfView, Is.EqualTo(80));
        }

        [Test]
        public void ExitDuringSwitchRestoresBaselineWithoutReleasingOtherOwners()
        {
            modal.Enter(poseA, 70); modal.Advance(1); modal.Advance(1);
            modal.control.SetBlocked(poseA, true);
            modal.SetPose(poseB, 80);
            modal.Advance(modal.closeSeconds * .5f);
            modal.Exit();
            modal.Advance(modal.closeSeconds * .5f);
            Assert.That(modal.blinkOverlay.alpha, Is.EqualTo(1));
            AssertBaseline();
            Assert.That(modal.Active, Is.True, "Retain control while the return view is being revealed.");
            modal.Advance(modal.openSeconds);
            Assert.That(modal.Active, Is.False);
            Assert.That(modal.IsTransitioning, Is.False);
            Assert.That(modal.control.HasModalOwner, Is.True);
            modal.control.SetBlocked(poseA, false);
            Assert.That(modal.control.HasModalOwner, Is.False);
            AssertBaseline();
        }

        [Test]
        public void RepeatedRequestDoesNotRestartAndLatestRequestDuringClosingWins()
        {
            modal.Enter(poseA, 70);
            modal.Advance(modal.closeSeconds * .5f);
            modal.SetPose(poseA, 70);
            modal.SetPose(poseB, 80);
            modal.Advance(modal.closeSeconds * .5f);
            Assert.That(modal.CurrentPose, Is.SameAs(poseB));
            Assert.That(modal.blinkOverlay.alpha, Is.EqualTo(1));
            modal.Advance(modal.openSeconds);
            modal.SetPose(poseB, 80);
            Assert.That(modal.IsTransitioning, Is.False);
        }

        [Test]
        public void DisablingDuringExitClearsBlackoutAndReleasesControl()
        {
            modal.Enter(poseA, 70); modal.Advance(1); modal.Advance(1);
            modal.Exit(); modal.Advance(modal.closeSeconds * .5f);
            modal.enabled = false;
            // EditMode fixtures do not run the MonoBehaviour lifecycle; exercise its disable hook explicitly.
            typeof(FirstPersonModalView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(modal, null);
            Assert.That(modal.Active, Is.False);
            Assert.That(modal.IsTransitioning, Is.False);
            Assert.That(modal.blinkOverlay.alpha, Is.Zero);
            Assert.That(modal.blinkOverlay.blocksRaycasts, Is.False);
            Assert.That(modal.control.HasModalOwner, Is.False);
            AssertBaseline();
        }

        void AssertBaseline()
        {
            Assert.That(camera.transform.localPosition, Is.EqualTo(baseline));
            Assert.That(Quaternion.Angle(camera.transform.localRotation, baselineRotation), Is.LessThan(.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(75));
        }
    }
}
