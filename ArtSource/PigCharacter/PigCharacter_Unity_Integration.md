# Pig character: Unity integration guide

Written for: Unity developers integrating the pig into `fartonline` (Unity 6000.5, URP 17.5).

## 1. What is in the project

```
Assets/Characters/Pig/
  Models/PigCharacter_Game.fbx      skinned mesh + Pig_Rig skeleton + 35 clips + 23 blend shapes
  Shaders/PigSkin.shader            FartOnline/PigSkin (URP)
  Textures/T_Pig_SkinDetail.png     baked greyscale detail (URP/Lit fallback)
  Textures/T_Pig_SkinMask.png       baked PigMask RGB (optional, for custom shaders)
  Scripts/PigFaceController.cs      facial + cartoon controls, expression presets, visemes
  Scripts/PigSecondaryMotion.cs     bounded spring follow-through + impact squash
  Editor/PigCharacterImporter.cs    enforces the import settings below
  Editor/PigCharacterSetup.cs       creates materials, PigCharacter.controller and PF_Pig.prefab
  Editor/PigCharacterValidation.cs  Tools > Pig Character > Run Validation (also batch mode)
ArtSource/PigCharacter/             Blender sources, scripts, renders, docs (outside Assets on purpose)
```

On first import (when the editor regains focus and recompiles), `PigCharacterSetup` runs once. It creates:
- `Materials/M_Pig_*.mat`,
- `PigCharacter.controller`,
- `PF_Pig.prefab`.

It also re-imports the FBX so the material remaps apply. You can re-run it from **Tools > Pig Character > Setup Materials, Controller and Prefab**.

## 2. Import settings (applied automatically)

| Setting | Value | Why |
|---|---|---|
| Scale | Scale Factor 1, Use File Scale on | 1 Blender unit = 1 m. The pig is 1.56 m tall to the top of the ears, with feet at y = 0 |
| Bake Axis Conversion | on | Blender Z-up becomes Unity Y-up in the data (no −90° X rotation). The exporter yaws the armature 180°, so the pig faces **+Z**; the `Pig_Rig` child node therefore carries a static 180° Y rotation. Leave it as is |
| Rig | **Generic**, avatar created from this model | the proportions (huge belly, 0.2 m legs, mitten hands) do not fit the Humanoid avatar and would distort retargeting; all clips are authored for this skeleton |
| Optimize Game Objects | off | the face controller and springs need the bone transforms |
| Blend Shapes | on, normals Calculate | 23 shapes |
| Skin Weights | Standard (4 bones) | weights were limited to 4 and normalised in Blender |
| Normals / Tangents | Import / Mikk | |
| Materials | Import via material description, remapped to `Materials/M_Pig_*` | |
| Animation compression | Keyframe Reduction, 0.1 error | keeps blend-shape curves precise |
| Loop | `Idle_Breathing`, `Walk`, `Run` | the clips were baked with an exact closing frame |

## 3. Skeleton

41 exported bones: `Root` plus 40 deform bones. All clips animate in place; the root stays at the origin, except that `Jump` and `Land` move `Root` vertically.

```
Root
└ Hips ─ Spine ─ Chest ─ Neck ─ Head ─ {Eye_L/R, Lid_Up_L/R, Lid_Low_L/R, Brow_L/R, Ear_L/R ─ Ear_Tip_L/R}
  │        │       └ Shoulder_L/R ─ UpperArm ─ LowerArm ─ Hand ─ {Thumb, Index, Finger}
  │        └ Belly                (jiggle bone)
  ├ Thigh_L/R ─ Shin ─ Foot      (hoof)
  └ Tail_1 ─ Tail_2
```

The Blender control bones (IK targets, poles, eye target, `CTRL_Face`, `CTRL_Body`) are not exported. Their effect is baked into every clip.

## 4. Animation clips

| Clip | Frames @30 fps | Loop | Notes |
|---|---|---|---|
| Idle_Breathing | 72 | yes | breathing, belly squash/stretch, a blink |
| Walk | 32 | yes | in place, ~0.15 m stride, hip sway, arm swing, belly/ear follow-through |
| Run | 20 | yes | lean, pumping arms, contact squash |
| Jump | 60 | | anticipation squash, stretch, ~0.42 m airtime, landing squash, belly bounce |
| Land | 36 | | landing from air, squash and settle |
| Squat | 62 | | down and up |
| Look_LeftRight / Look_Up | 70 / 50 | | head and eyes (eyes lead) |
| Blink / Smile / Laugh / Surprised | 12 / 42 / 62 / 48 | | facial |
| Talk | 60 | | viseme sequence REST, MBP, AA, L, EE, OH, FV … |
| Ear_FollowThrough | 48 | | fast head shake, ears lag and settle |
| Wave / Hand_Gestures | 58 / 126 | | open, fist, point, thumbs-up, grip |
| Expression_Transitions | 232 | | all 10 presets blended in sequence |
| RefPose | 1 | | the pose of the reference image |
| FACE_Rest, FACE_* (16) | 1 each | | data for `PigFaceController`; keep them out of your state machine |

The generated `PigCharacter.controller` has one state per gameplay clip, with `Idle_Breathing` as the default. Add your own transitions and parameters. Everything is plain Generic Mecanim, so blend trees, layers and Timeline all work.

## 5. Facial controls at runtime: `PigFaceController`

The control names and ranges match the Blender rig's `CTRL_Face` / `CTRL_Body` properties.

| Control | Range | Implemented by |
|---|---|---|
| Blink_L, Blink_R, Blink_Both, Squint_L/R, Eyes_Wide | 0..1 | lid / eye bones (data from FACE_* clips) |
| Eye_Look_X, Eye_Look_Y | −1..1 | eye bones (about ±29° yaw, ±23° pitch) |
| Brow_Raise_L/R, Brow_Furrow | 0..1 | brow bones |
| Ear_Tilt_L/R | −1..1 | ear bones (±35°) |
| Smile, Smile_L, Smile_R, Frown, Mouth_Open, Mouth_Wide, Mouth_O, Cheek_Raise, Snout_Compress, Snout_Widen | 0..1 | blend shapes |
| V_AA, V_EE, V_OH, V_FV, V_MBP, V_L, V_REST | 0..1 | blend shapes (visemes) |
| Squash, Stretch, Belly_Squash, Belly_Stretch | 0..1 | blend shapes × `Cartoon_Intensity` |
| Cartoon_Intensity | 0..2 | scales squash/stretch and the runtime springs |

```csharp
var face = pig.GetComponent<FartOnline.Pig.PigFaceController>();
face.SetExpression("Happy", 1f);          // presets blend, so combine freely
face.SetExpression("Surprised", 0.4f);
face.Blink_Both = 1f;                      // still works on top of the expression
face.Eye_Look_X = -0.6f;
face.SetViseme("V_AA", 0.8f);              // drive from your lip-sync source
face.SetExpression("Happy", 0f);           // remove
```

Presets: Neutral, Happy, Excited, Sad, Angry, Surprised, Confused, Sleepy, Laughing, Worried. Their values are identical to `ArtSource/PigCharacter/exports/PigExpressions.json`.

**How it combines with the Animator:** the controller runs in `LateUpdate` (execution order 50). It takes whatever the Animator wrote that frame as the base, then:
- for blend shapes, uses the larger of the animated value and the control value,
- for bones, multiplies the pose delta on top.

With no Animator it falls back to the rest pose, so values never accumulate.

**Why the bone controls are data-driven:** bone axis conventions change in the Blender → FBX → Unity (right- to left-handed) conversion. Each bone control therefore ships as a single-frame `FACE_*` clip at its extreme, which the controller samples once in `Awake`. No axis is hard-coded, and the Unity result matches Blender exactly.

**Shoulder correctives:** `Corr_Shoulder_Up_L/R` are driven from the upper-arm angle, as the Blender drivers do. They are already baked into every clip.

## 6. Secondary motion: `PigSecondaryMotion`

This component adds damped springs to `Ear_Tip_L/R`, `Belly` and `Tail_2`. They react to world acceleration, so gameplay motion (being pushed, turning fast, physics) gets follow-through. They are bounded and always settle.

`AddImpact(strength)` kicks a squash spring, for example on landing: `AddImpact(Mathf.Clamp01(fallSpeed / 6f))`.

The authored clips already contain baked follow-through, so `globalGain` defaults to 0.6. Set it to 0 if you want only the baked motion.

## 7. Materials and recolouring

See `PigCharacter_Materials.md`. In short: submesh 0 is the skin, and `_SkinColor` (or `_BaseColor` on the URP/Lit fallback) is the only colour control. Use a `MaterialPropertyBlock` for per-pig colours.

## 8. Performance

| Item | Value |
|---|---|
| Triangles | 30,758 (one SkinnedMeshRenderer, 5 submeshes) |
| Vertices | 15,425 |
| Bones | 41 (40 deform), 4 weights per vertex |
| Blend shapes | 23 (sparse: most only touch the face region) |
| Materials | 5, one custom (vertex colours, no textures needed) |
| Transparency | none |

For crowds, consider:
- an LOD1 made with Blender's Decimate (Planar) on the joined `Pig_Mesh` in `PigCharacter_AnimationTests.blend` (blend shapes survive collapse decimation poorly, so drop the facial shapes in LOD1),
- turning off `PigSecondaryMotion`,
- `SkinnedMeshRenderer.quality = 2 bones` at distance.

## 9. Validation

*Tools > Pig Character > Run Validation* re-checks the import (scale, facing, bones, blend shapes, clips, materials, shader, face controller) and renders previews to `ArtSource/PigCharacter/renders/unity_preview_*.png`. It works in a temporary scene and restores your open scenes afterwards. It refuses to run while any open scene has unsaved changes.

## 10. Re-exporting from Blender

Open `ArtSource/PigCharacter/PigCharacter_Master.blend` and run `scripts/pig_13_export.py` (or `run_export()` in it). It rebuilds the joined game mesh, bakes the textures, lays the clips out as NLA strips, writes `Assets/Characters/Pig/Models/PigCharacter_Game.fbx`, re-imports it to check it, and saves `PigCharacter_AnimationTests.blend`. Unity re-imports with the same settings automatically.

Two export settings matter and must not be changed:
- **Export from NLA strips, not "All Actions".** The facial blend shapes are driven by rig properties, and only NLA-strip takes sample every object. In "All Actions" mode the blend-shape curves silently disappear.
- **Only Deform Bones on, Add Leaf Bones off.**
