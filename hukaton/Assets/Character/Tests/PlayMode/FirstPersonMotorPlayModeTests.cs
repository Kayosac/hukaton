#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
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
        // Top centre of the disposable floor; the player spawns here facing +Z.
        static readonly Vector3 FloorTop = new Vector3(2000, 1000, 2000);
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
            Time.captureDeltaTime = 0;
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

        [UnityTest]
        public IEnumerator RunningDownRampStaysGrounded([Values(30, 40)] int degrees, [Values(false, true)] bool sprint)
        {
            Time.captureDeltaTime = 1 / 60f;
            motor.view.motionIntensity = 0; // The camera's speed is measured without head bob.
            const float height = 3;
            float edge = FloorTop.z + 3;
            Block("Ramp top", height, edge - 7, edge);
            float run = Ramp(edge, height, degrees);
            yield return StandOn(new Vector3(FloorTop.x, FloorTop.y + height, edge - 4.5f));
            var recorder = PlayerFrameRecorder.Attach(motor);
            Keys(sprint ? new[] { Key.W, Key.LeftShift } : new[] { Key.W });
            yield return Until(() => motor.transform.position.z > edge + run + 1.5f, 5);
            Keys();
            yield return Advance(.3f);

            var frames = recorder.frames;
            Assert.That(motor.transform.position.y, Is.EqualTo(FloorTop.y).Within(.1f), "The player ran down to the floor.");
            Assert.That(recorder.Landings, Is.Zero, "Running down a walkable ramp is not a fall: no landing.");
            int onRamp = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                float along = frames[i].body.z - edge;
                if (along < -.3f || along > run + .5f) continue;
                onRamp++;
                Assert.That(frames[i].grounded, Is.True, $"The body left the ground {along:0.00} m past the ramp's top edge.");
                if (sprint && along <= run)
                    Assert.That(frames[i].sprinting, Is.True, $"Sprint dropped {along:0.00} m past the ramp's top edge.");
            }
            Assert.That(onRamp, Is.GreaterThan(15), "The run must cover the ramp.");
            float slowest = float.PositiveInfinity, fastest = float.NegativeInfinity, cameraSum = 0, bodySum = 0;
            int middle = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                float from = frames[i - 1].body.z - edge, to = frames[i].body.z - edge;
                if (from < run * .25f || to > run * .75f) continue;
                float speed = CameraVerticalSpeed(frames, i);
                slowest = Mathf.Min(slowest, speed);
                fastest = Mathf.Max(fastest, speed);
                cameraSum += speed;
                bodySum += (frames[i].body.y - frames[i - 1].body.y) / frames[i].deltaTime;
                middle++;
            }
            Assert.That(middle, Is.GreaterThan(5), "The middle of the ramp must be sampled.");
            Assert.That(fastest - slowest, Is.LessThan(.5f), "Mid-ramp the eye descends at a steady speed.");
            Assert.That(cameraSum / middle, Is.EqualTo(bodySum / middle).Within(.3f), "The eye follows the ramp, not lags it.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WalkingDownStepsStaysGroundedAndEyeEases()
        {
            Time.captureDeltaTime = 1 / 60f;
            motor.view.motionIntensity = 0; // Head bob would move the eye up and down on its own.
            const int steps = 6;
            const float rise = .25f, tread = .5f, top = steps * rise;
            float edge = FloorTop.z + 3;
            // Six drops of 0.25 m: the landing, five 0.5 m treads, then the floor.
            Block("Stairs top", top, edge - 7, edge);
            for (int i = 1; i < steps; i++) Block($"Step {i}", top - i * rise, edge + (i - 1) * tread, edge + i * tread);
            float bottom = edge + (steps - 1) * tread;
            yield return StandOn(new Vector3(FloorTop.x, FloorTop.y + top, edge - 3));
            float standingEye = motor.view.cameraTransform.position.y - (FloorTop.y + top);
            var recorder = PlayerFrameRecorder.Attach(motor);
            Keys(Key.W);
            yield return Until(() => motor.transform.position.z > bottom + 1.5f, 4);
            Keys();
            yield return Advance(.6f);

            var frames = recorder.frames;
            Assert.That(motor.transform.position.y, Is.EqualTo(FloorTop.y).Within(.1f), "The player walked down to the floor.");
            Assert.That(recorder.Landings, Is.Zero, "Walking down steps is not a fall: no landing dip, sound or damage.");
            int descent = 0, grounded = 0;
            float highestRise = 0, sharpestChange = 0;
            string riseAt = "";
            for (int i = 1; i < frames.Count; i++)
            {
                float along = frames[i].body.z - edge;
                // Only the descent itself: on the flat start the settle after StandOn may still nudge the eye by a millimetre or two.
                if (along < -.3f || along > bottom - edge + .6f) continue;
                float riseNow = frames[i].cameraY - frames[i - 1].cameraY;
                if (riseNow > highestRise)
                {
                    highestRise = riseNow;
                    riseAt = $" (frame {i} of {frames.Count}, {along:0.00} m past the edge, body dy {frames[i].body.y - frames[i - 1].body.y:0.0000}, grounded {frames[i].grounded})";
                }
                descent++;
                if (frames[i].grounded) grounded++;
                if (i >= 2)
                    sharpestChange = Mathf.Max(sharpestChange,
                        Mathf.Abs(CameraVerticalSpeed(frames, i) - CameraVerticalSpeed(frames, i - 1)));
            }
            Assert.That(descent, Is.GreaterThan(20), "The walk must cover the stairs.");
            Assert.That(grounded, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(descent * .95f)),
                $"Only {grounded} of {descent} frames on the stairs had ground contact.");
            Assert.That(highestRise, Is.LessThan(.001f), "Going down, the eye never moves up" + riseAt + ".");
            Assert.That(sharpestChange, Is.LessThan(2f), "The eye glides down the stairs instead of dropping step by step.");
            Assert.That(motor.view.cameraTransform.position.y - FloorTop.y, Is.EqualTo(standingEye).Within(.02f),
                "Once stopped, the eye is back at its standing height above the floor.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WalkingUpStepsHasNoEyePop()
        {
            Time.captureDeltaTime = 1 / 60f;
            motor.view.motionIntensity = 0;
            const int steps = 6;
            const float rise = .25f, tread = .5f;
            float edge = FloorTop.z + 2;
            // Six 0.25 m steps up; the last one is a long landing.
            for (int i = 1; i <= steps; i++)
                Block($"Step {i}", i * rise, edge + (i - 1) * tread, i < steps ? edge + i * tread : edge + 12);
            float standingEye = motor.view.cameraTransform.position.y - FloorTop.y;
            var recorder = PlayerFrameRecorder.Attach(motor);
            Keys(Key.W);
            float landing = edge + (steps - 1) * tread;
            yield return Until(() => motor.transform.position.z > landing + 1.5f, 4);
            Keys();
            yield return Advance(.6f);

            var frames = recorder.frames;
            float top = FloorTop.y + steps * rise;
            Assert.That(motor.transform.position.y, Is.EqualTo(top).Within(.1f), "The player climbed the stairs.");
            Assert.That(recorder.Landings, Is.Zero, "Climbing steps is not a fall.");
            float highestRise = 0;
            for (int i = 1; i < frames.Count; i++)
                highestRise = Mathf.Max(highestRise, frames[i].cameraY - frames[i - 1].cameraY);
            Assert.That(highestRise, Is.LessThan(.1f), "Each step up is eased, not popped into the eye in one frame.");
            Assert.That(motor.view.cameraTransform.position.y - top, Is.EqualTo(standingEye).Within(.02f),
                "Once stopped, the eye is back at its standing height.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WalkingOffRealLedgeStillFallsAndLandsOnce()
        {
            Time.captureDeltaTime = 1 / 30f;
            const float height = .8f;
            float edge = FloorTop.z + 3;
            Block("Ledge", height, edge - 7, edge);
            yield return StandOn(new Vector3(FloorTop.x, FloorTop.y + height, edge - 4));
            var recorder = PlayerFrameRecorder.Attach(motor);
            Keys(Key.W, Key.LeftShift);
            yield return Until(() => motor.transform.position.z > edge + 3, 4);
            Keys();
            yield return Advance(.5f);

            int airborne = 0;
            foreach (var frame in recorder.frames) if (!frame.grounded) airborne++;
            Assert.That(airborne, Is.GreaterThanOrEqualTo(3), "A 0.8 m drop is a fall, not a step the body is pulled down.");
            Assert.That(recorder.Landings, Is.EqualTo(1), "The fall lands exactly once.");
            Assert.That(motor.IsGrounded, Is.True);
            Assert.That(motor.transform.position.y, Is.EqualTo(FloorTop.y).Within(.1f));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator JumpWhileRunningDownRampIsNotSnapped()
        {
            Time.captureDeltaTime = 1 / 60f;
            const float height = 3, degrees = 30;
            float edge = FloorTop.z + 3;
            Block("Ramp top", height, edge - 7, edge);
            Ramp(edge, height, degrees);
            yield return StandOn(new Vector3(FloorTop.x, FloorTop.y + height, edge - 3));
            var recorder = PlayerFrameRecorder.Attach(motor);
            Keys(Key.W);
            yield return Until(() => motor.transform.position.z > edge + 1.2f, 3);
            Assert.That(motor.IsGrounded, Is.True, "The player runs on the ramp before jumping.");
            int takeOff = recorder.frames.Count;
            Keys(Key.W, Key.Space);
            yield return null;
            yield return null;
            Keys(Key.W);
            yield return Until(() => recorder.Landings > 0, 2);
            yield return Advance(.4f);
            Keys();
            yield return Advance(.2f);

            float slope = Mathf.Tan(degrees * Mathf.Deg2Rad), peak = 0;
            int airborne = 0;
            for (int i = takeOff; i < recorder.frames.Count; i++)
            {
                var body = recorder.frames[i].body;
                float surface = FloorTop.y + Mathf.Clamp(height - (body.z - edge) * slope, 0, height);
                peak = Mathf.Max(peak, body.y - surface);
                if (!recorder.frames[i].grounded) airborne++;
            }
            Assert.That(recorder.Jumps, Is.EqualTo(1));
            Assert.That(airborne, Is.GreaterThan(10), "The jump leaves the ramp instead of being pulled back onto it.");
            Assert.That(peak, Is.GreaterThanOrEqualTo(motor.jumpHeight * .65f), "The jump rises clear of the ramp.");
            Assert.That(recorder.Landings, Is.EqualTo(1), "The jump lands exactly once.");
            Assert.That(motor.IsGrounded, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Moves the player onto a test structure (feet position) and lets it settle.</summary>
        IEnumerator StandOn(Vector3 feet)
        {
            Keys();
            motor.Teleport(feet + Vector3.up * .02f, Quaternion.identity);
            yield return Advance(.25f);
            Assert.That(motor.IsGrounded, Is.True, "The player must stand on the test structure before moving.");
        }

        /// <summary>A disposable solid block across the player's lane, from below the floor to <paramref name="top"/> metres above it.</summary>
        void Block(string name, float top, float zMin, float zMax)
        {
            const float bottom = -.1f;
            Box(name, new Vector3(FloorTop.x, FloorTop.y + (top + bottom) * .5f, (zMin + zMax) * .5f),
                new Vector3(4, top - bottom, zMax - zMin), Quaternion.identity);
        }

        /// <summary>
        /// A ramp whose surface starts exactly at the top edge of a block (<paramref name="height"/> above the floor at
        /// <paramref name="edgeZ"/>) and runs down at <paramref name="degrees"/> into the floor. Returns its horizontal length.
        /// </summary>
        float Ramp(float edgeZ, float height, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            var downhill = new Vector3(0, -Mathf.Sin(radians), Mathf.Cos(radians));
            var normal = new Vector3(0, Mathf.Cos(radians), Mathf.Sin(radians));
            float length = height / Mathf.Sin(radians) + .5f; // Half a metre more, hidden under the floor.
            const float thickness = .4f;
            var start = new Vector3(FloorTop.x, FloorTop.y + height, edgeZ);
            Box("Ramp", start + downhill * (length * .5f) - normal * (thickness * .5f), new Vector3(4, thickness, length),
                Quaternion.LookRotation(downhill, normal));
            return height / Mathf.Tan(radians);
        }

        static float CameraVerticalSpeed(List<PlayerFrameRecorder.Frame> frames, int i)
            => (frames[i].cameraY - frames[i - 1].cameraY) / frames[i].deltaTime;

        static IEnumerator Until(System.Func<bool> condition, float seconds)
        {
            float end = Time.time + seconds;
            while (!condition() && Time.time < end) yield return null;
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

    /// <summary>
    /// Test probe on the player: samples every frame after the view has placed the camera (LateUpdate, late order), so the
    /// test sees what is rendered rather than the camera of the previous frame.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    sealed class PlayerFrameRecorder : MonoBehaviour
    {
        public struct Frame
        {
            public float deltaTime, cameraY;
            public Vector3 body;
            public bool grounded, sprinting;
        }

        public readonly List<Frame> frames = new List<Frame>();
        public int Landings { get; private set; }
        public int Jumps { get; private set; }
        FirstPersonMotor motor;

        public static PlayerFrameRecorder Attach(FirstPersonMotor motor)
        {
            var recorder = motor.gameObject.AddComponent<PlayerFrameRecorder>();
            recorder.motor = motor;
            motor.Landed += recorder.OnLanded;
            motor.Jumped += recorder.OnJumped;
            return recorder;
        }

        void OnLanded(float speed) => Landings++;
        void OnJumped() => Jumps++;

        void LateUpdate()
        {
            if (!motor || Time.deltaTime <= 0) return;
            frames.Add(new Frame
            {
                deltaTime = Time.deltaTime,
                cameraY = motor.view.cameraTransform.position.y,
                body = motor.transform.position,
                grounded = motor.IsGrounded,
                sprinting = motor.IsSprinting
            });
        }

        void OnDestroy()
        {
            if (!motor) return;
            motor.Landed -= OnLanded;
            motor.Jumped -= OnJumped;
        }
    }
}
#endif
