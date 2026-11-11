# Pig character: validation report

Written for: the project lead and the Unity/tech-art team deciding whether the pig asset is ready to use, and what still needs checking.

Date: 2026-10-30 · Blender 4.5.2 LTS (driven over MCP) · Unity 6000.5.6f1 / URP 17.5

Everything below was measured or rendered from the actual files. When a check could not be run, it says so.

## 1. Deliverables (paths confirmed on disk)

| Deliverable | Path |
|---|---|
| Editable Blender master | `ArtSource/PigCharacter/PigCharacter_Master.blend` |
| Animation test file (joined game mesh + rig + all clips on an NLA timeline with markers) | `ArtSource/PigCharacter/PigCharacter_AnimationTests.blend` |
| Game FBX | `Assets/Characters/Pig/Models/PigCharacter_Game.fbx` |
| Materials doc / Unity guide / this report | `ArtSource/PigCharacter/PigCharacter_Materials.md`, `…_Unity_Integration.md`, `…_Validation.md` |
| URP skin shader | `Assets/Characters/Pig/Shaders/PigSkin.shader` |
| Textures | `Assets/Characters/Pig/Textures/T_Pig_SkinDetail.png`, `T_Pig_SkinMask.png` (1024²) |
| Runtime scripts | `Assets/Characters/Pig/Scripts/PigFaceController.cs`, `PigSecondaryMotion.cs` |
| Editor scripts | `Assets/Characters/Pig/Editor/PigCharacterImporter.cs`, `PigCharacterSetup.cs`, `PigCharacterValidation.cs` |
| Expression presets and visemes | `ArtSource/PigCharacter/exports/PigExpressions.json` |
| High-res source mesh | `Pig_Body_Sculpt` in the master (104k quads, hidden `WIP_CONSTRUCTION` collection) |
| Reproducible build scripts | `ArtSource/PigCharacter/scripts/` (`pig_build_all.py` rebuilds stages 01–11, then `pig_12_animations.py` and `pig_13_export.py`) |
| Checkpoints | `ArtSource/PigCharacter/exports/_checkpoint_00…04*.blend` |
| Renders | `ArtSource/PigCharacter/renders/` (see section 8) |

## 2. Visual fidelity

The match was checked with a pixel-matched orthographic camera. `pig_util_compare.py` overlays the render on `GoGoPig.jpg` at 0.00236 m/px, with the feet at row 830.

- `renders/cmp_08_refpose.png` shows the `RefPose` clip (eyes rolled up, asymmetric ears, slight head tilt) against the reference. The silhouette sits inside the reference outline for the head, belly, arms, hands and hooves. The snout, nostrils, eyes, brows, smile line, belly button and patches all line up.
- Proportions were measured from a segmentation mask of the reference: total height 1.50 m to the top of the head (1.56 m to the ear tips), belly 1.0 m wide, crotch at 0.13 m, eye centres 0.17 m apart.
- Colour was sampled from the reference (belly mid-tone sRGB 0.92/0.63/0.71) and matches under the neutral preview light.
- **Hidden views are a design decision**, because the reference shows only the front: rounded rump, a corkscrew tail, patches on the back, and the side depth of the belly and snout. They are coherent, but they are not claimed to match anything in the image.
- The asymmetry in the reference (head turn, ear poses) is reproduced as a **pose** (`RefPose`). The rest mesh is symmetric, which is better for rigging.

## 3. Model quality (measured in Blender)

| Part | Tris | Non-manifold | Open boundary | Max influences |
|---|---|---|---|---|
| Pig_Body (quad retopo with mouth slit and bag) | 16,446 | 0 | 0 | 4 |
| Pig_Snout | 4,030 | 0 | 0 | 1 |
| Eyes ×2 | 1,088 each | 0 | 0 | 1 |
| Upper/lower lids ×4 | 560 each | 0 | 0 | 1 |
| Brows ×2 | 376 each | 0 | 0 | 1 |
| Ears ×2 | 720 each | 0 | 0 | 3 |
| Foot hooves ×2 / hand claws ×2 | 728 / 504 each | 0 | 0 | 1 |
| Tail | 1,218 | 0 | 0 | 2 |
| **Exported game mesh** | **30,766** (15,425 verts) | | | 4 |

- The body was retopologised with QuadriFlow: all quads, symmetric, 34 poles, loops wrapping the limbs.
- Defects found and fixed during validation:
  - the tail's buried base ring was open (10 boundary edges); it was capped, re-skinned and re-exported,
  - both ears were inside-out (negative signed volume); they were flipped and the generator fixed.

  All parts now have positive signed volume.

## 4. Rig and deformation

- `Pig_Rig` has 54 bones: 40 deform plus `Root` (all exported), and 13 controls (IK hands and feet, poles, eye targets, `CTRL_Face`, `CTRL_Body`). IK/FK is switchable per limb (`IK_Arm_L` …).
- Rest-pose drift with all constraints and drivers live is ≤ 0.5 mm. Pole angles were solved automatically.
- **Body stress test (12 poses)**, rendered in `renders/pose_body_03.png`: arms out, arms overhead, sharp elbows, hands to face, legs spread, crouch, deep squat, lean, twist, side bend, reach, and ears/tail.
  - Iteration 1 (bone-heat weights) creased across the chest and belly, and the mouth popped open when the chest moved (`pose_body_01.png`).
  - Iteration 2 (smoothed heat) still creased (`pose_body_02.png`).
  - Final: analytic weights (smooth spine z-blend, polyline-projected limbs, lips and chin locked to the head) give no creasing, no tearing, a stable mouth and controlled belly compression.
- Shoulder correctives (`Corr_Shoulder_Up_L/R`) are driven by upper-arm angle.

## 5. Facial system

- 23 blend shapes:
  - mouth: Smile, Smile_L/R, Frown, Mouth_Open, Mouth_Wide, Mouth_O, Cheek_Raise
  - snout: Snout_Compress, Snout_Widen
  - visemes: V_AA, V_EE, V_OH, V_FV, V_MBP, V_L, V_REST
  - body: Belly_Squash/Stretch, Body_Squash/Stretch
  - correctives: Corr_Shoulder_Up_L/R
- Bone controls: Blink_L/R/Both, Squint_L/R, Eyes_Wide, Eye_Look_X/Y, Brow_Raise_L/R, Brow_Furrow, Ear_Tilt_L/R.
- **Safe range is the slider range.** Every control was rendered at its extreme value (`renders/face_range_01.png`, `face_range_02.png`):
  - the mouth interior never disappears or inverts,
  - the lids cover the eyes on Blink,
  - pupils stay inside the eye at ±1 look,
  - the nostrils stay recessed under Snout_Compress and Snout_Widen,
  - the lips never cross into the snout.
- Combinations were tested: Laugh (Smile + Open + Cheek), Happy (smile + squint + brows), Angry (frown + furrow + widen + squint), plus all 10 presets (`renders/expression_presets.png`).
- The mouth is a real slit cut along the measured smile curve, with a dark mouth bag behind it. Open/O shapes rotate the chin about a jaw hinge, and the bag follows each lip side.

## 6. Cartoon deformation and secondary motion

- Squash and stretch are blend shapes on every part, so the eyes and hooves never detach. They are bounded 0..1 and scaled by `Cartoon_Intensity` (0..2).
- Follow-through on the ear tips, belly and tail comes from a damped spring driven by parent acceleration. It is hard-clamped, settles within about 0.5 s, and is baked into the clips. `PigSecondaryMotion` provides the same at runtime, with an `AddImpact()` landing squash.
- No soft-body physics is used anywhere, so motion is deterministic.

## 7. Animation suite (35 clips)

18 gameplay and test clips: Idle_Breathing, Walk, Run, Jump, Land, Squat, Look_LeftRight, Look_Up, Blink, Smile, Laugh, Talk, Surprised, Ear_FollowThrough, Wave, Hand_Gestures, Expression_Transitions, RefPose. There are also 17 single-frame `FACE_*` pose clips that drive the Unity face controller.

Inspected frame by frame:
- **Jump:** anticipation squash, launch stretch, airborne "oh" mouth, landing squash with belly compression, settle.
- **Walk:** alternating steps with lift, arm swing, hip sway and bob.
- **Hand gestures:** digits articulate without tearing.

FBX re-import in Blender found:
- 41 bones and 35 skeletal takes,
- **35 blend-shape takes**, for example `Talk` has curves on all 7 visemes, which proves facial animation survives export,
- bounds of 1.56 m with feet at z = 0.

## 8. Renders

| File | What it shows |
|---|---|
| `cmp_08_refpose.png` | reference overlay (outline, solo render, 55% blend) |
| `hero_neutral.png` | neutral 3/4 view |
| `expression_presets.png` | the 10 presets |
| `face_range_01.png`, `face_range_02.png` | every facial control at its extreme |
| `pose_body_01/02/03.png` | the deformation stress-test iterations |
| `unity_preview_*.png`, `unity_preview_sheet.png`, `unity_head_closeup.png` | rendered inside the live Unity editor with URP (see section 9) |

## 9. Unity validation

Validation ran **in the live `fartonline` editor** (Unity 6000.5.6f1, URP 17.5, `PC_RPAsset`), driven over MCP for Unity: refresh, then *Tools > Pig Character > Setup*, then *Tools > Pig Character > Run Validation*. It also ran in batch mode on an isolated copy of the project. The final report is `Assets/Characters/Pig/Validation/UnityValidationReport.txt` (a copy is at `ArtSource/PigCharacter/UnityValidationReport.txt`). **Result: all checks passed.**

| Check | Result |
|---|---|
| `FartOnline/PigSkin` compiles with no errors and is supported | PASS |
| `M_Pig_Skin` uses PigSkin; the URP/Lit fallback has the detail map | PASS |
| Changing `_SkinColor` leaves smoothness, specular, coat, SSS and detail untouched | PASS |
| Generic rig, blend shapes imported, scale 1, bake axis conversion | PASS |
| 35 clips, with `Idle_Breathing`, `Walk` and `Run` looping | PASS |
| `Talk` animates the viseme blend shapes (23 blend-shape curves) | PASS |
| 5 submeshes / 5 materials, all remapped to project materials | PASS |
| 23 blend shapes, PigMask vertex colours, 41 bones bound, skin weights present | PASS |
| Height 1.565 m, feet at y = −0.005 | PASS |
| Faces Unity +Z; `Eye_L` on the character's left (−X) | PASS |
| All 18 gameplay clips sample without NaN and stay in place (hips XZ drift ≤ 0.04 m) | PASS |
| `Jump` leaves the ground (+0.42 m) | PASS |
| Face controller loads 16 FACE pose clips; `Blink_Both` closes the lid by 158° (the Blender value) and releases | PASS |
| An expression preset drives the blend shapes (Happy, Smile = 80) | PASS |

URP renders are in `renders/unity_preview_*.png`, with all four in `unity_preview_sheet.png`: RefPose, Happy, blue Surprised, and green landing squash. They show the one-parameter recolour keeping identical gloss, patches and nostrils.

### Defects found by the Unity validation, and fixed

1. **All faces had material index 0** (1 submesh, pink eyes and hooves). The texture-bake step cleared the material slots on the game mesh. It now swaps slots in place.
2. **The pig faced −Z.** Unity normalises the FBX axis declaration. The armature is now yawed 180° for the export only (the master is unchanged), so in Unity the `Pig_Rig` node carries a static 180° Y rotation.
3. **Dark blotches on the head and cheeks** in Unity, and faintly in Blender. Two bugs in the AO bake:
   - the occluders were taken from the *posed* mesh (the RefPose action was active) while the samples were at rest,
   - the parked eyelid shells occluded the forehead.

   Both are fixed (rest-pose occluders, back-face pass-through, 64 rays, neighbour smoothing). An intermediate theory that the project's low shadow bias was responsible was tested and ruled out.
4. **Ears were inside-out** (negative signed volume), which matters because PigSkin culls back faces. They were flipped in place, and the generator now enforces outward normals.
5. **Buried geometry casting shadows** (snout back wall, parked lids, tail root) is now flagged with `PigMask.A = 0` and skipped in PigSkin's shadow pass. The mouth bag and eye materials receive shadows but don't cast them.
6. Validation tooling fixes: the first preview frame came out white (async shader compile, now disabled during renders), every shot showed the same pose (fixed by `forceMatrixRecalculationPerRender`), and there was a native crash when reassigning MSAA render targets (one target is now reused). The validator refuses to run while open scenes have unsaved changes and restores your scene setup afterwards. The live run left `SampleScene` open and clean.

### Not verified in Unity

- Runtime behaviour in Play Mode, including `PigSecondaryMotion` springs and `AddImpact`. The validation runs in Edit Mode and samples clips directly; the springs were only checked by code review and by the equivalent baked Blender springs.
- Mobile (`Mobile_RPAsset`) rendering and performance; only the PC pipeline asset was exercised.
- On-device frame cost.

## 10. Known limitations

- **Single reference view.** The back, sides, underside and mouth interior are reasonable reconstructions, not matches.
- **Hands are mitten nubs**, as in the reference. Fist, point, thumbs-up and grip are distinguishable mainly by the claw positions, not by long fingers.
- **Subtle expressions.** Sad, Angry and Worried are less readable than Happy, Excited, Surprised and Laughing. The floating brows are small and only raise, lower and tilt; there is no inner/outer brow split. Adding `Brow_Inner_Up` bones would strengthen them.
- **Arms above the head (125°)** deform cleanly but flatten the shoulder mass somewhat. The corrective helps but does not fully preserve volume at that extreme.
- **Close-ups of the nostrils** still show faint stretching inside the cavity walls, which comes from the snout's polar topology. The dark interior mask hides it at normal camera distances.
- **Blender-only shading.** Micro-roughness and bump do not exist in the Unity shader (deliberately: vertex colours only, no extra textures).
- **No eyelashes or teeth**, consistent with the reference.
- **No LOD chain** is shipped. The integration guide describes how to make one.
