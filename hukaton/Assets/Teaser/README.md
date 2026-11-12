# PENUS teaser — «А хозяин не придёт?»

18 s, 16:9, 30 fps (540 frames). Scene: `Scenes/PenusTeaser.unity`.

The scene is a copy of the game scene with the gameplay systems, player, menus and other locations removed. It keeps the authored shop room, the Old Apiary forest (creatures, finds and chests switched off), the trading HUD (only the customer's speech plate) and the main menu (only the logo). Everything on screen is the game's own art: the Hangover coin as a 3D model built from the HUD coin sprites (`Assets/Content/Currency/HangoverCoin`), the thrown card with the PENUS back, the shop customer actor with the peasant appearance, the goblin dog and fang drawings through the loot sprite view (HandDrawnSprite), the card dealer on the ceiling rig, the menu logo with its card finish, the speech plate with the Text Animator typewriter.

## How it plays

`PENUS teaser — 18 s` holds `TeaserTimeline`: a list of shots (frame range, camera pose and optional end pose, field of view, black matte or a black camera background, objects visible only in that shot) and a list of tracks evaluated on teaser time. `Time.captureFramerate` is fixed to 30 and the random seed is fixed, so every run gives the same frames.

| Track | What it does |
| --- | --- |
| `TeaserTurntable` (Stage / Hangover coin) | turns the 3D coin on a curve; its shot clears the camera to black |
| `TeaserCardFlight` (camera child) | flies the card in camera space, so it carries through the forest → shop cut unchanged |
| `TeaserCustomer` (ShopCustomer) | walks the real customer to the counter in the pre-roll, sets expressions, hides him off his shots |
| `TeaserSpeech` (ShopTradingHUD) | shows the plate and types the line; the label's Text Animator runs on the Script loop, advanced by teaser time |
| `TeaserSpriteStand` (Stage) | shows the dog (default → attack at 8 s) and the fang at their in-game sizes |
| `TeaserSway` | the dealer's pendulum sway and the logo's turn (same 14° / 5 s as the menu) |
| `TeaserSparkle` (on the logo) | one pass of Retro Arsenal `minisparkles` over the logo's own star at 15.6 s |

Shot poses live under `Shot poses`; move them in the Scene view to reframe.

## Re-render with new art

1. Replace art where it is referenced: the coin prefab under `Stage` (rebuild it from new sprites with `output/coin-model/build_coin.py`), dog and fang sprites on the two `TeaserSpriteStand` keys, the peasant via `PeasantAppearance`, card textures on `TeaserCardFlight`.
2. **Hakaton → Teaser → Render frames (1920x1080)**. It opens the scene, sets the Game view to 1920 × 1080, plays one recorded run and stops by itself. Frames go to `Logs/teaser-frames/frame_0000.png … frame_0539.png`.
3. Build the MP4 with the sound from `output/teaser/audio/` (see `output/teaser/README.md`).
