# Reusable characters

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

`FirstPersonModalView` provides a fixed camera without moving the character body. Assign the camera, shared control gate and an authored full-screen black `CanvasGroup`. `Enter(pose, fov)` and `SetPose(pose, fov)` switch to external scene markers under a 0.2 s blink; `Exit()` restores the FPS camera and releases only this component's control claim. Disable also restores the camera and clears its claim. It is independent of the trading layer; `TradingShopView` uses it for the counter and display viewpoints. Transition timings and FOV are editable in the Inspector.

## Tuning

Movement defaults: walk 4 m/s, sprint 6.5 m/s, acceleration 18 m/s², braking 24 m/s², jump height 1.2 m. Jump buffer 0.14 s and coyote time 0.12 s permit slightly early/late inputs. Sprint uses 20 stamina/s, jump costs 12; recovery starts after 1 s at 25/s. Exhaustion requires recovery to 25% before sprinting is available again.

Base camera FOV is 75°. Walk bob amplitude is 5.5 cm with a slower sideways sway and 0.65° gait roll; sprint increases the amplitude by 1.45× and the pace from 1.8 to 2.5 Hz. Motion blends in and out with actual movement. Strafe roll is 0.8°, landing dip at most 4.5 cm, sprint FOV +5°. These values are Inspector fields and can be reduced independently.

EditMode tests are in `Hakaton.Character.Tests` and cover the stamina budget and independent control owners.

PlayMode tests are in `Hakaton.Character.PlayModeTests`. They instantiate the saved player prefab on a disposable physics surface and drive keyboard input through Unity's Input System to check walking, sprinting, braking, jumping, landing, stamina consumption, and modal control ownership. Run them in the Unity Test Runner's PlayMode tab in the Editor; the fixture loads its prefab through AssetDatabase.

## Sprite NPCs

`Prefabs/SpriteNpc.prefab` is a reusable, authored visitor rig. Its `SpriteNpcActor` has no dependency on trading. Assign an external entrance-to-destination `route` and the viewing camera's transform to `viewer` on the scene instance. The actor root sits at the feet; the `Facing` child provides yaw billboarding and the turn; `Foot anchored step` provides distance-driven Y scaling. The SpriteRenderer uses the shared `HandDrawnSprite` material, with a per-renderer UV rectangle for the current atlas cell. Edit mode previews the authored sprite without starting a visit.

NPCs, monsters and world loot share **Pixels Per World Unit** (`_PixelsPerUnit`) in `Assets/Rendering/Materials/HandDrawnSprite.mat`. Higher values give finer pixels; lower values give larger blocks. The shared density is **500 pixels per world unit**, giving the 25 cm counter goblin about 125 blocks across its visible height. This replaces the initial bee-swarm calibration of 184.875 so small characters retain readable facial details. Spawn-size multipliers do not change this constant: differently sized creatures keep the same world pixel size. The shader measures the rendered sprite plane, including parent scale, flips and the active atlas cell. Its grid moves with the drawing and naturally becomes smaller on screen with distance. There are no per-creature/loot pixel-size fields or overrides to maintain. Each world pixel is ONE filtered read from the mip level matching its size, so every sprite shows the same averaged drawing per world pixel whatever its source resolution; sprite textures used with this material must have mipmaps enabled (with Preserve Coverage for the alpha cutoff). Sprites take the scene fog like the rest of the world. The bubble and suspension-rope material variants inherit the same density while preserving their animation settings. `WorldSpritePixelTests` covers the GPU grid and the shared material setup. Camera-relative coordinates come from the inverse view matrix, so an overridden camera view works even when its Transform position differs. The regression tests cover equivalent PPU/Transform combinations and the tutorial bush at its cave coordinates.

`NpcAppearance` assets hold the name, body scale, neutral/front, back, displeased, happy and angry sprites. The saved scale of `stepRoot` represents the default appearance; other appearance scales are relative to it. All views must share the canvas size, pivot and PPU. The existing peasant and blacksmith appearances and prefab variants are in `Assets/Content/NPCs/`.

Call `BeginVisit(appearance)` while idle; wait for `ReadyForTrade` before starting a conversation. Use `SetExpression` for in-conversation feedback and `Leave(outcome)` to show the result, turn with a midpoint swap to the back sprite, and retrace the route. `Cancel()` skips the reaction hold but still turns and walks out. `IsBusy` remains true until arrival at the exit, so a subsequent visit cannot teleport the current one. Cancellation during arrival returns through the waypoints already reached.

Movement and departure timings are serialized Inspector fields. `SpriteNpcActorTests` covers turn/sprite timing, interruption on a corner, distance-driven gait, feet alignment and frame-independent travel. Trading's PlayMode fixture checks the integration against its saved scene.
