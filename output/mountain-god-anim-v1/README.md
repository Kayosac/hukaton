# MG01: god animation (draft v3 for review)

The god's beats for the encounter on the summit, baked in Blender from procedural keys over IK, plus the rocks under
his support hands. Previews: `MG01_rider.mp4` (riding the finger: the pull and the talk), `MG01_body.mp4` (the body up
close until the finger waits at the lip), `MG01_wide.mp4`; `MG01_first_person.mp4` is the v2 view from the lip.

## v3 (after the second review)

- The other cradle hand (C02) no longer drags a stretched mess: its fingers were fused to the belly, to thigh L02 and
  to its own shoulder. Faces bridging two limbs, or bones far apart inside one free limb (C01, C02, A01–A04, the
  dropping legs L01 and L02), are cut; each side of an opening is closed on its own; weights follow the same split.
  Stretched edges at the waiting pose: 186 before, 30 now (left at the hip, knee and shoulder joints).
- The pull: the hand goes off the lip down to the cloud sea, sweeps toward him low over the clouds (the player rides
  about 50 m above them, under the god's whole height, past his support hand and its rock), then climbs along his front
  to the face. He bends down after it and straightens as it rises. 16 s.
- C01's wrist resists bending (IK stiffness), so when the long arm folds the elbow and the forearm take it.

## v2 (after the first review)

- He sits deep at first (pelvis 70 m below the model's pose, hunched) and really stands up on his support arms when he
  sees the player (126 m up, a push-off dip and a heavy settle). The support arms' IK now reaches to their roots.
- He lets go of himself first: both C hands slide down off the thighs (C01 down and out to the left, C02 down and out
  to the right, where it then hangs and drifts with the body).
- His two ordinary legs (L01, L02) drop and straighten under him, hip first and knee after, swinging past and back;
  then they hang and sway. The torso sags under their weight with a bounce.
- Only then C01 swings forward, in front of the legs, and lays the index finger under the lip.
- The wrist never folds back: on the way the hand trails the arm (low IK orientation weight); at the lip and at the
  face the target is lined up with the forearm, and the index finger bends at its knuckle to point where it must.

## Clips (30 fps)

| Clip | Length | Loop | What happens |
|---|---|---|---|
| Idle | 12 s | yes | Sits deep, breathing (6 s), small arms and the raised legs sway; S hands on their rocks, C hands on the thighs. |
| Notice | 3.6 s | no | Pushes off and stands up on the support arms, head turns to the lip. |
| AlertIdle | 12 s | yes | Stands, watching the lip. |
| Reach | 11 s | no | Lets go (hands down), legs drop straight, torso sags and bounces, then leans 13 deg as C01 swings to the lip. |
| ReachHold | 12 s | yes | Waits at the lip; free fingers twitch, the legs and C02 hang and sway. |
| Lift | 16 s | no | Down to the clouds, a low sweep toward himself, then up along his front to the face. |
| Talk | 12 s | yes | Nods and tilts, small arms gesture, the hand holds steady. |

Dock: the index fingertip's end tucks 5 m under the lip edge, the finger's top 1 m below the platform. IK misses
(support hands on their rocks, C hands on their targets) stay under 0.3 m in every clip (`clips.json`).

## Rig changes

- C01 and the small arms A01–A04 are cut free from the body at their roots (faces bridging limbs removed, holes capped);
  stray cross-limb weights cleaned.
- `hat_00` and `beard_00` hang from `head_00` (they were on the neck), so nods carry them.
- Hat and beard are springs at runtime (`GodSpringBones.cs`, same model as `springs.py` in the preview).

## Rocks under the support hands

`hand_rocks.py` builds three summits with the kit's rock functions (far materials), one per S hand: crown just under
the palm, slopes through the claws (claws dig in), crags beside the hand, body down into the clouds. Built in metres
at god coordinates x 100: in Unity they take the god group's position and 180 deg turn at scale 1.

## Files

- `godrig.py` — rig setup (limb separation, weights, IK targets), helpers.
- `clips.py` — the clips; `export_anim.py` — FBX with the takes for Unity.
- `springs.py`, `GodSpringBones.cs` — hat and beard springs.
- `hand_rocks.py`, `probe2.py` (hand contact data) — the rocks.
- `env.py`, `seq.py`, `stamp.py` — preview renders.
