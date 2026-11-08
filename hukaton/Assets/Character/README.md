# Reusable first-person character

The components in `Runtime` do not reference trading, UI panels, or a particular scene. They operate on a preconfigured player prefab. Configure references in the Inspector; no component assembles a player at runtime.

Drag `Prefabs/FirstPersonPlayer.prefab` into another scene, or create a prefab variant for a particular game layer. `Trading/Prefabs/Shopkeeper.prefab` is an example variant. Open `Scenes/MovementPlayground.unity` to try the player on its saved `MovementCourse` prefab: stairs, a slope, jump obstacles, and overhead clearance.

## Component responsibilities

- `PlayerControlGate` owns cursor capture and modal ownership. Escape releases the cursor and suppresses gameplay input. A click captures again; `CapturedThisFrame` prevents that click from also interacting with the world. Losing application focus releases capture. Each UI or game layer blocks using its own owner; one layer cannot release another layer's block.
- `FirstPersonInput` samples movement, mouse look, sprint and jump. Input is cleared while capture is unavailable.
- `FirstPersonMotor` moves the authored `CharacterController`: acceleration, braking, air control, jump buffering, coyote time, gravity and stamina use. `Teleport(position, rotation)` clears motion and aligns yaw and view pitch.
- `PlayerStamina` owns the stamina budget. Its independent `StaminaState` implements delay, recovery and exhaustion hysteresis.
- `FirstPersonView` controls yaw/pitch and modest movement feedback. The camera's authored position, rotation and FOV are the baseline. `motionIntensity = 0` disables bob, roll and landing dip; `sprintFov = 0` disables the sprint FOV change.
- Optional `FirstPersonAudio` produces footsteps by actual travelled distance and listens to jump/landing events. Its clips and AudioSource are authored assets/references.

## Authored hierarchy

Player root: `CharacterController`, control gate, input, motor, stamina, view and optional audio. A `PitchPivot` child sits at eye height; its camera child starts at local position zero. Assign the root to `yawRoot`, pivot to `pitchPivot`, and camera transform to `cameraTransform`. Objects held by the character can be children of the camera; they are outside this module's responsibilities.

Use a dedicated AudioSource for movement so other game sounds do not share step pitch variation. Layer-specific prefabs can be variants of the base player prefab.

## Temporarily owning control

```csharp
// Opening a conversation or another modal interaction.
control.SetBlocked(this, true);
motor.Teleport(conversationPose.position, conversationPose.rotation);

// Closing it restores capture only when it was captured before the first modal owner.
control.SetBlocked(this, false);
```

Always release your own claim on disable/destruction. World interaction should require `control.GameplayActive && !control.CapturedThisFrame`. `PlayerStamina.Normalized` supplies a HUD gauge without a dependency from the player to any UI.

## Tuning

Movement defaults: walk 4 m/s, sprint 6.5 m/s, acceleration 18 m/s², braking 24 m/s², jump height 1.2 m. Jump buffer 0.14 s and coyote time 0.12 s permit slightly early/late inputs. Sprint uses 20 stamina/s, jump costs 12; recovery starts after 1 s at 25/s. Exhaustion requires recovery to 25% before sprinting is available again.

Base camera FOV is 75°. Walk bob amplitude is 5.5 cm with a slower sideways sway and 0.65° gait roll; sprint increases the amplitude by 1.45× and the pace from 1.8 to 2.5 Hz. Motion blends in and out with actual movement. Strafe roll is 0.8°, landing dip at most 4.5 cm, sprint FOV +5°. These values are Inspector fields and can be reduced independently.

EditMode tests are in `Hakaton.Character.Tests` and cover the stamina budget and independent control owners.

PlayMode tests are in `Hakaton.Character.PlayModeTests`. They instantiate the saved player prefab on a disposable physics surface and drive keyboard input through Unity's Input System to check walking, sprinting, braking, jumping, landing, stamina consumption, and modal control ownership. Run them in the Unity Test Runner's PlayMode tab in the Editor; the fixture loads its prefab through AssetDatabase.
