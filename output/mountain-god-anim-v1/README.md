# MG01: god animation v1 (draft for review)

The god's five beats for the encounter on the summit, baked in Blender from procedural keys over IK, plus the rocks
under his support hands. Previews: `MG01_first_person.mp4` (from the lip, then riding the finger), `MG01_wide.mp4`.

## Clips (30 fps)

| Clip | Length | Loop | What happens |
|---|---|---|---|
| Idle | 12 s | yes | Breathing (6 s), small arms and feet sway; S hands pinned to their rocks, C hands on the thighs. |
| Notice | 3 s | no | Rises 22 m with a settle, head turns toward the lip. |
| AlertIdle | 12 s | yes | Held up, watching the lip, livelier sway. |
| Reach | 9 s | no | Anticipation, C01 lets go of the thigh; pelvis and legs drop 35 m and bounce (the cradle is gone); torso leans 12 deg after the arm; the arm arcs out left and over, the index straightens, the other fingers curl; the fingertip lands under the lip. |
| ReachHold | 12 s | yes | Waits at the lip, free fingers twitch. |
| Lift | 12 s | no | Carries the finger up to the face (end point to be tuned in tests). |
| Talk | 12 s | yes | Nods and tilts, small arms gesture, the hand holds steady. |

Dock: the index fingertip's end tucks 5 m under the lip edge, the finger's top 1 m below the platform (miss 0.04 m).

## Rig changes

- C01 and the small arms A01–A04 are cut free from the body at their roots (faces bridging limbs removed, holes capped);
  stray cross-limb weights cleaned.
- `hat_00` and `beard_00` now hang from `head_00` (they were on the neck), so nods carry them.
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
