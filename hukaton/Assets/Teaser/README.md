# PENUS teaser — «А хозяин не придёт?»

22 s, 16:9, 30 fps (660 frames). Scene: `Scenes/PenusTeaser.unity`.

The scene is a copy of the game scene with the gameplay systems, player, menus and other locations removed. It keeps the authored shop room, the Old Apiary forest (creatures, finds and chests switched off) and the trading HUD (only the customer's speech plate). Everything on screen is the game's own art (plus the staging sprites below): the Hangover coin as a 3D model built from the HUD coin sprites (`Assets/Content/Currency/HangoverCoin`), the thrown card with the PENUS back, the shop customer actor, the goblin dog and fang drawings through the loot sprite view, the card dealer on the ceiling rig, the speech plate with the Text Animator typewriter, and the PENUS logo with its Card UI finish on a world-space canvas.

## How it plays

`PENUS teaser — 22 s` holds `TeaserTimeline`: a list of shots (frame range, camera pose and optional end pose, field of view, black matte or a black camera background, objects visible only in that shot) and a list of tracks evaluated on teaser time. `Time.captureFramerate` is fixed to 30 and the random seed is fixed, so every run gives the same frames.

| Track | What it does |
| --- | --- |
| `TeaserTurntable`, `TeaserTint`, `TeaserMove`, `TeaserParticleCue` (coin) | the 3D coin comes out of the dark as a crescent (a soft dark disc slides off it while its brightness rises), spins once fast and smoothly, flashes and gets a Retro Arsenal sparkle; the camera drifts away from it on a black background |
| `TeaserCardFlight` (camera child) | the card in the middle of the picture spins about its own axis and rolls about the view axis (175° over the flight) as it drifts into the depth in slow motion; camera space, so it carries through the forest → shop cut unchanged |
| `TeaserCustomer` (ShopCustomer) | walks the real customer to the counter in the pre-roll, hides him off his shots; front view in 05, his own displeased drawing in 10, his default drawing for the line in 11 (`Art/Peasant/PeasantTeaserMixed`) |
| `TeaserSpeech` (ShopTradingHUD) | shows the plate and types the line; the label's Text Animator runs on the Script loop, advanced by teaser time |
| `TeaserSpriteStand` (Stage) | the goblin dog front view (06), the open-mouth close-up (08, its own stand at head height), the fang on the counter's red pad (09 close-up stand, 10–11 stand); a key can set its own canvas height |
| `TeaserSway` | the dealer's sway on the rig, 6 mm, through zero on the cut into 07 |
| `TeaserTurntable`, `TeaserGraphicTint`, `TeaserMove`, `TeaserParticleCue`, `TeaserTwoSided` (logo) | the logo comes out of the dark the way the coin does: brightness from black and a dark disc sliding off (a crescent of letters first), one full turn that starts fast and eases to rest, a Retro Arsenal sparkle; `TeaserTwoSided` mirrors it while its back faces the camera, so the letters read on both sides. The camera pulls back for the whole 7 s |

**Eye match (05 → 06 → 07).** The customer, the goblin dog and the card dealer look into the lens with their eyes on the same spot: eye midpoint at the centre, 44 % from the top, 222 px between the pupils at 1920 × 1080 (FOV 40, camera pitched down 2.5°). The poses were solved from the pupils in the front drawings; the dog's stands face a fixed direction (`faceCamera` off), the customer and the dealer billboard to the camera from right in front of their pivots.

**Fang match (08 → black → 09).** The close-up of the open mouth (`Art/GoblinDog_mouth_closeup.png`) is framed like the reference crop and pushes in 1.25× on the big cracked fang, which stays put on screen. After 0.6 s of black the fang on the counter appears exactly there: same centre, same length of enamel, same lean (both fang stands lean 12° so the loot fang matches the fang in the jaw, lifted so the leaned tip stands on the pad), then the camera pulls back 1.1×.

**Pixel size.** Teaser copies of the hand-drawn material (`Art/Materials`) set the world pixel grid so that one drawing pixel is about 6 screen pixels in the close-ups (05–09): `HandDrawnSprite Teaser` 180 px/m (customer, dog, fang in 10–11, all other hand-drawn sprites in the scene), `HandDrawnSprite Goblin Teaser` 683 px/m (dealer), `… Teaser Close` 453 px/m (fang close-up), `… Teaser Mouth` 330 px/m (open-mouth close-up). Sway and line wobble are scaled with the grid, so the drawings move as much as in the game. The front drawings and the teaser copies of the peasant, fang and open-mouth art are 1.5–3× Lanczos upscales of the sources, uncompressed, so every grid cell samples clean colour.

Staging art: `Art/Front` (front views from `output/PENUS-Teaser-Front-Sprites-v1.zip`), `Art/Peasant` (the peasant's default and displeased drawings cut from `PeasantAtlas`), `GoblinDog_mouth_closeup.png` (the open-mouth close-up drawn for the teaser, 2× upscale), `GoblinDog_fang_teaser.png`. `GoblinDog_attack_teaser.png` (the attack master, cropped) is kept as an alternative open-mouth drawing. The upside-down forest is a teaser grove planted with the forest prefabs inside the Old Apiary (`Stage/Forest grove (teaser)`).

Shot poses live under `Shot poses`; move them in the Scene view to reframe.

## Re-render

1. Replace art where it is referenced: the coin prefab under `Stage` (rebuild it from new sprites with `output/coin-model/build_coin.py`), dog and fang sprites on the `TeaserSpriteStand` keys, the peasant on `PeasantTeaserMixed`, card textures on `TeaserCardFlight`.
2. **Hakaton → Teaser → Render frames (1920x1080)**. It opens the scene, sets the Game view to 1920 × 1080, plays one recorded run and stops by itself. Frames go to `Logs/teaser-frames/frame_0000.png … frame_0659.png`. For a quick look at a few frames set the session string `TeaserRecorder.CheckFramesKey` (for example `150,300-310`) before rendering: only those frames are written, to `Logs/teaser-frames-check`.
3. Add the end glitch and build the MP4 with the score (see `output/teaser/README.md`).
