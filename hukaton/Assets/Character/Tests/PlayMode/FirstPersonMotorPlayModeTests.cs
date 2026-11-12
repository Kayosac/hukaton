#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Hakaton.Character.Tests.PlayMode
{
    /// <summary>Actual player-loop and CharacterController checks using the authored player prefab.</summary>
    public sealed class FirstPersonMotorPlayModeTests
    {
        const string PlayerPrefabPath = "Assets/Character/Prefabs/FirstPersonPlayer.prefab";
        GameObject fixtureRoot;
        FirstPersonMotor motor;
        PlayerControlGate control;
        PlayerStamina stamina;
        Keyboard keyboard;
        Mouse mouse;
        Keyboard previousKeyboard;
        Mouse previousMouse;
        KeyboardState heldKeys;
        InputSettings.UpdateMode previousUpdateMode;
        InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        InputSettings.BackgroundBehavior previousBackgroundBehavior;
        CursorLockMode originalCursorLock;
        bool originalCursorVisible;

        [UnitySetUp]
        public IEnumerator SpawnAuthoredPlayerOnIsolatedPhysicsFloor()
        {
            originalCursorLock = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            previousKeyboard = Keyboard.current;
            previousMouse = Mouse.current;
            previousUpdateMode = InputSystem.settings.updateMode;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            heldKeys = new KeyboardState();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            // Keep the real player loop and existing UI actions intact. Resetting the global
            // InputSystem through InputTestFixture can invalidate actions in an open scene.
            InputSystem.onBeforeUpdate += QueueHeldKeysForPlayerUpdate;
            InputSystem.onAfterUpdate += SelectFixtureDevicesAfterPlayerUpdate;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null, "The reusable player must exist as an authored prefab.");
            fixtureRoot = new GameObject("Disposable character physics fixture");
            // A disposable collision surface is test data, never a delivered game-scene builder.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Physics test floor";
            floor.transform.SetParent(fixtureRoot.transform);
            floor.transform.position = new Vector3(2000, 999.5f, 2000);
            floor.transform.localScale = new Vector3(60, 1, 60);
            var player = Object.Instantiate(prefab, new Vector3(2000, 1000.02f, 2000), Quaternion.identity,
                fixtureRoot.transform);
            motor = player.GetComponent<FirstPersonMotor>();
            control = player.GetComponent<PlayerControlGate>();
            stamina = player.GetComponent<PlayerStamina>();
            Assert.That(motor, Is.Not.Null);
            Assert.That(motor.enabled, Is.True);
            Assert.That(stamina, Is.Not.Null);
            yield return null; // Start initializes capture and the physics controller settles.
            // Exercise the normal focus callback; no internal input or velocity state is injected.
            control.SendMessage("OnApplicationFocus", true);
            control.CaptureCursor();
            yield return Advance(.2f);
            Assert.That(control.GameplayActive, Is.True, "PlayMode tests need cursor capture in the game view.");
            Assert.That(motor.IsGrounded, Is.True);
        }

        [UnityTearDown]
        public IEnumerator RemoveFixtureAndRestoreCursor()
        {
            InputSystem.onBeforeUpdate -= QueueHeldKeysForPlayerUpdate;
            InputSystem.onAfterUpdate -= SelectFixtureDevicesAfterPlayerUpdate;
            if (fixtureRoot != null) Object.Destroy(fixtureRoot);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            yield return null;
            Cursor.lockState = originalCursorLock;
            Cursor.visible = originalCursorVisible;
        }

        [UnityTest]
        public IEnumerator WalkingSprintingAndBrakingUseRealDisplacementAndStamina()
        {
            Keys(Key.W);
            yield return Advance(.7f);
            Assert.That(motor.input.Move.y, Is.EqualTo(1),
                "The real input sampler must receive the queued W key during a player-loop update.");
            Assert.That(motor.PlanarVelocity.magnitude, Is.EqualTo(motor.walkSpeed).Within(.35f));
            var walkStart = motor.transform.position;
            float walkStartTime = Time.time;
            yield return Advance(.5f);
            float walkSpeed = HorizontalDistance(walkStart, motor.transform.position) / (Time.time - walkStartTime);
            float beforeSprint = stamina.Value;

            Keys(Key.W, Key.LeftShift);
            yield return Advance(.7f);
            Assert.That(motor.IsSprinting, Is.True, "Queued Shift+W must engage sprint through the real input sampler.");
            Assert.That(motor.PlanarVelocity.magnitude, Is.EqualTo(motor.sprintSpeed).Within(.4f));
            var sprintStart = motor.transform.position;
            float sprintStartTime = Time.time;
            yield return Advance(.5f);
            float sprintSpeed = HorizontalDistance(sprintStart, motor.transform.position) / (Time.time - sprintStartTime);
            Assert.That(sprintSpeed, Is.GreaterThan(walkSpeed * 1.35f));
            Assert.That(stamina.Value, Is.LessThan(beforeSprint - 10));

            Keys();
            var brakeStart = motor.transform.position;
            yield return Advance(.5f);
            Assert.That(motor.PlanarVelocity.magnitude, Is.LessThan(.1f));
            Assert.That(motor.IsSprinting, Is.False);
            Assert.That(HorizontalDistance(brakeStart, motor.transform.position), Is.LessThan(1.5f),
                "Releasing movement must brake promptly rather than continue sliding.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator JumpLeavesGroundReachesApexLandsAndSpendsOnce()
        {
            float groundY = motor.transform.position.y;
            float before = stamina.Value;
            int jumps = 0, landings = 0;
            motor.Jumped += () => jumps++;
            motor.Landed += _ => landings++;
            Keys(Key.Space);
            yield return null;
            yield return null;
            Keys();
            float peak = motor.transform.position.y;
            float lowestStamina = stamina.Value;
            bool wasAirborne = !motor.IsGrounded;
            float deadline = Time.time + 1.3f;
            while (Time.time < deadline)
            {
                peak = Mathf.Max(peak, motor.transform.position.y);
                lowestStamina = Mathf.Min(lowestStamina, stamina.Value);
                wasAirborne |= !motor.IsGrounded;
                yield return null;
            }
            Assert.That(wasAirborne, Is.True);
            Assert.That(jumps, Is.EqualTo(1));
            Assert.That(peak - groundY, Is.InRange(motor.jumpHeight * .65f, motor.jumpHeight * 1.1f));
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.transform.position.y, Is.EqualTo(groundY).Within(.1f));
            Assert.That(landings, Is.EqualTo(1));
            Assert.That(lowestStamina, Is.EqualTo(before - stamina.jumpCost).Within(.05f));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ModalOwnersStopMovementAndResumeWithoutReplayingBlockedJump()
        {
            Keys(Key.W);
            yield return Advance(.5f);
            var firstOwner = new GameObject("Conversation owner");
            var secondOwner = new GameObject("Inventory owner");
            firstOwner.transform.SetParent(fixtureRoot.transform);
            secondOwner.transform.SetParent(fixtureRoot.transform);
            control.SetBlocked(firstOwner, true);
            control.SetBlocked(secondOwner, true);
            var stopped = motor.transform.position;
            Keys(Key.W, Key.Space, Key.LeftShift);
            yield return Advance(.3f);
            Assert.That(control.GameplayActive, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(HorizontalDistance(stopped, motor.transform.position), Is.LessThan(.01f));
            control.SetBlocked(firstOwner, false);
            yield return Advance(.15f);
            Assert.That(control.GameplayActive, Is.False);

            int jumps = 0;
            motor.Jumped += () => jumps++;
            control.SetBlocked(secondOwner, false);
            yield return Advance(.6f);
            Assert.That(control.GameplayActive, Is.True);
            Assert.That(motor.IsSprinting, Is.True, $"grounded={motor.IsGrounded} stamina={stamina.Value:0.00} canSprint={stamina.CanSprint} speed={motor.PlanarVelocity.magnitude:0.00}");
            Assert.That(HorizontalDistance(stopped, motor.transform.position), Is.GreaterThan(1));
            Assert.That(jumps, Is.Zero, "A Space press consumed during a modal must not become a jump on return.");
            Assert.That(motor.IsGrounded, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PushingIntoANarrowWedgeHoldsStillWithoutSideToSideBouncing()
        {
            // Two walls meet 50° apart straight ahead, slightly off-centre: the player slides along one into the V.
            var start = motor.transform.position;
            var apex = start + new Vector3(.12f, 0, 2.4f);
            const float halfAngle = 25;
            for (int side = -1; side <= 1; side += 2)
            {
                float radians = halfAngle * Mathf.Deg2Rad;
                var along = new Vector3(side * Mathf.Sin(radians), 0, -Mathf.Cos(radians));
                var outward = new Vector3(side * Mathf.Cos(radians), 0, Mathf.Sin(radians));
                // Each wall's inner face runs through the apex; the slab lies on its outer side.
                Box(side < 0 ? "Wedge left" : "Wedge right", apex + along * 2 + outward * .1f + Vector3.up * 1.2f,
                    new Vector3(.2f, 2.5f, 4), Quaternion.LookRotation(along));
            }
            Keys(Key.W);
            yield return Advance(1.6f);
            Assert.That(motor.transform.position.z - start.z, Is.GreaterThan(1),
                "The player must have walked into the wedge before the measurement.");
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, maxSideways = 0, maxSpeed = 0;
            float end = Time.time + 1;
            while (Time.time < end)
            {
                var position = motor.transform.position;
                minX = Mathf.Min(minX, position.x);
                maxX = Mathf.Max(maxX, position.x);
                maxSideways = Mathf.Max(maxSideways, Mathf.Abs(motor.PlanarVelocity.x));
                maxSpeed = Mathf.Max(maxSpeed, motor.PlanarVelocity.magnitude);
                yield return null;
            }
            Assert.That(maxX - minX, Is.LessThan(.02f), "Held against two walls, the body must not be shoved from side to side.");
            Assert.That(maxSideways, Is.LessThan(.3f), "Corrections out of the walls must never become sideways speed.");
            Assert.That(maxSpeed, Is.LessThan(motor.walkSpeed * .25f), "Pushing into a dead end is not running.");
            Keys();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RunningDiagonallyIntoAWallSlidesAlongIt()
        {
            var start = motor.transform.position;
            // A long wall a step to the right; W+D drives into it at 45°.
            Box("Side wall", start + new Vector3(.8f, 1.2f, 3), new Vector3(.2f, 2.5f, 12), Quaternion.identity);
            Keys(Key.W, Key.D);
            yield return Advance(.8f);
            float wallFace = start.x + .7f;
            Assert.That(motor.transform.position.x, Is.GreaterThan(wallFace - motor.characterController.radius - .1f),
                "The player must be pressed against the wall.");
            float slideStart = motor.transform.position.z, slideStartTime = Time.time;
            yield return Advance(.6f);
            float slideSpeed = (motor.transform.position.z - slideStart) / (Time.time - slideStartTime);
            Assert.That(slideSpeed, Is.GreaterThan(motor.walkSpeed * .6f), "The part of the run along the wall is kept.");
            Assert.That(motor.transform.position.x, Is.LessThan(wallFace), "The wall is never passed.");
            Assert.That(motor.PlanarVelocity.x, Is.LessThan(.35f), "No speed is kept pointing into the wall.");
            Assert.That(motor.PlanarVelocity.z, Is.GreaterThan(motor.walkSpeed * .6f));
            Keys();
            LogAssert.NoUnexpectedReceived();
        }

        void Box(string name, Vector3 center, Vector3 size, Quaternion rotation)
        {
            // Disposable test geometry inside the fixture, removed with it.
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(fixtureRoot.transform);
            box.transform.SetPositionAndRotation(center, rotation);
            box.transform.localScale = size;
            Physics.SyncTransforms();
        }

        void Keys(params Key[] keys) => heldKeys = new KeyboardState(keys);

        void QueueHeldKeysForPlayerUpdate()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            // Events queued here are processed in this same Dynamic update, before MonoBehaviour.Update.
            // Do not call InputSystem.Update manually: it would consume the Space pressed-this-frame edge.
            InputSystem.QueueStateEvent(keyboard, heldKeys);
        }

        void SelectFixtureDevicesAfterPlayerUpdate()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            // Editor mouse motion must not select a physical device before the player's Update.
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
        }
        static float HorizontalDistance(Vector3 from, Vector3 to)
        { var delta = to - from; delta.y = 0; return delta.magnitude; }
        static IEnumerator Advance(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }
    }
}
#endif
