# MG01: god animation (v6)

The god's beats for the encounter on the summit, baked in Blender from procedural keys over IK, plus the rocks under
his support hands. Previews: `MG01_rider.mp4` (riding the finger: the pull and the talk), `MG01_body.mp4` (the body up
close until the finger waits at the lip), `MG01_wide.mp4`; `MG01_first_person.mp4` is the v2 view from the lip.

## v6 (deeper sit, a longer pull, the hand out of the way at the face)

- He sits deeper in the cradle (pelvis 125 m down instead of 70, hunched 8 deg); Notice is 4.4 s for the longer push up.
  The support hands stay on their rocks (IK misses 0); if the body now cuts into a rock, the rock is the one to move.
- The pull is 28 s: the hand lifts off the lip, dives in a wide arc to the cloud sea on his left, skims it in a long
  sweep around in front of him, swings out to his right and climbs along his front to the face. The finger turns from
  the lip toward his face in one turn about the vertical through his left (no flip on the way); he leans down after
  the hand and turns with it. The arm stays on one IK branch the whole way.
- At the face (Talk) the player stands 320 m in front of him and 160 m below his eyes, the index finger pointing at
  his face; the hand is turned so the arm hangs low from the shoulder and comes up from below and outside. Nothing of
  the hand is between the player and his face (`facesearch.py`: candidates scored by the share of the player's view
  the hand covers and by how high the arm loops; the old pose looped the arm over his head).
- `liftseq.py` — contact sheet of a clip: a wide view and the player's view every 2 s.

## v5 (after the review of v4: the caps looked like patches)

The v4 caps over the torn places showed in the game as fans with a seam on the chest and patched folds on the palm.
v5 does not cap anything: the whole skin is rebuilt (`rebuild/`, see `rebuild/README.md`).

- The split skin is posed apart as in the clips (hands off the thighs and the chest, legs down, the beard and arm A03
  slid away); skins that face each other closely but are not one skin are pushed apart; a screened Poisson surface
  through it is one closed smooth skin with every torn place healed; it comes back to rest through the inverse pose,
  and healed sheets that came back crumpled are faired. 240 k triangles, no open edges.
- Its own UVs (xatlas, about 2000 charts) and a colour map baked from the old skin (`T_MG01_BodyAnim_BaseColor.png`,
  2048, bilinear in Unity; material `M_MG01_BodyAnim`, a copy of `M_MG01_Body` with that map).
- Weights from the nearest old skin, at most four bones a vertex; the beard and hat springs carry only skin that was
  theirs (a healed place on the chest under the beard swung with it and tore open into a dark pocket in Unity), and no
  vertex follows a bone farther from it than that bone's old skin reached (a few beard vertices had ended up on C01's
  fingers and stretched into a beam into the sky whenever the hand moved; `rebuild/check_curtains.py` now finds no edge
  between limbs that stretches in the clips). Faces
  whose corners follow different limbs (they would stretch into curtains when the limbs part): C01 55, C02 54, L01 77 —
  the weight seams the old skin already had at the root joints.
- The finger is a little thicker than in v4 (top 16.5 m above the bone at the tip): the dock and the Unity capsules
  (0.24 / 0.20 / 0.165) follow it.
- Checked in Blender (back faces culled) at the waiting pose, in the pull and in the talk, and in Unity Play from the
  edge to the talk: the chest under the C02 hand, the C01 palm and the thigh it held are smooth skin. The baked skin
  checked against the drawn one: `GodCheck.DumpBody` writes the skin as Unity draws it.

## v4 (after the review in Unity: holes, torn limbs, a faceted hand)

What was wrong, and what changed:

- The limb separation deleted faces and closed the holes with fans that were sometimes wound backwards. Blender's
  previews draw back faces, Unity culls them, so in the game the god had holes, torn-off limbs and see-through caps.
  Now nothing is deleted: every face goes to the part most of its corners belong to, the skin is split along face edges
  between parts that move apart, corners left shared at the end of a tear are ripped, stray faces join the part around
  them, and each opening gets a domed cap wound like its neighbours. Checked on every build: no open edges, no faces
  wound against their neighbours, one piece (`topo2.py`).
- Weights follow the side a vertex ended up on (the bone its faces follow, not its own heaviest weight); a limb never
  follows another limb, even at a joint; the beard comes away from the chest. Long stretched edges at the waiting pose:
  216 before, 60 now, all at the hip, knee and shoulder joints (`diag2.py`).
- The model was low-poly and flat-shaded: up close the 400 m hand broke into huge facets. One Catmull-Clark level
  (weights interpolated) and smooth shading: 115 k quads, a whole round skin. The finger is thinner after smoothing
  (top 14 m above the bone at the tip); the dock and the Unity capsules use the new thickness.
- IK no longer twists the long arms inside their chains (only the chain root and the hand may turn about their own
  length), so they do not fold like a sweet wrapper.
- Checked in Unity Play (world worktree): the views from the review (the god from the lip, the hand at the lip, the
  ride under the body) are smooth and whole; the meeting runs from the edge to the end of the talk.

## v3 (after the second review)

- The other cradle hand (C02) no longer drags a stretched mess: its fingers were fused to the belly, to thigh L02 and
  to its own shoulder. Faces bridging two limbs, or bones far apart inside one free limb (C01, C02, A01–A04, the
  dropping legs L01 and L02), are cut; each side of an opening is closed on its own; weights follow the same split.
  Stretched edges at the waiting pose: 186 before, 31 now (left at the hip, knee and shoulder joints).
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
| Notice | 4.4 s | no | Pushes off and stands up on the support arms, head turns to the lip. |
| AlertIdle | 12 s | yes | Stands, watching the lip. |
| Reach | 11 s | no | Lets go (hands down), legs drop straight, torso sags and bounces, then leans 13 deg as C01 swings to the lip. |
| ReachHold | 12 s | yes | Waits at the lip; free fingers twitch, the legs and C02 hang and sway. |
| Lift | 28 s | no | Off the lip, a dive to the clouds on his left, a long sweep around in front of him over the clouds, out to his right and up along his front to the face. |
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

- `godrig.py` — rig setup (limb separation, weights, IK targets; the rebuilt skin when `out/MG01_Body_rebuilt.npz` exists), helpers.
- `rebuild/` — the v5 skin rebuild (`run.sh`, steps in `rebuild/README.md`).
- `clips.py` — the clips; `export_anim.py` — FBX with the takes for Unity.
- `springs.py`, `GodSpringBones.cs` — hat and beard springs.
- `hand_rocks.py`, `probe2.py` (hand contact data) — the rocks.
- `env.py`, `seq.py`, `stamp.py` — preview renders; `qa2.py`, `qa3.py` — QA renders as Unity draws them (back faces
  culled) at every second of every clip; `topo2.py`, `diag2.py` — mesh and stretch checks; `qa5.py` — close-ups of the healed places through the clips.
- `unity/GodEncounterBuilder.cs`, `unity/GodCheck.cs` — the one-off scene builder and the Play check (not in Assets).
