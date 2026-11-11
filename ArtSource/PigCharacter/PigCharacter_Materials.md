# Pig character: material architecture

Written for: technical artists and Unity developers who need to recolour, tweak or port the pig's materials.

## 1. Design rule

The skin colour is **one parameter** and it only ever multiplies the albedo:

```
Final skin albedo = Skin_Color × Detail
Detail            = (1 − PigMask.R × Patch_Darkness)          darker skin patches
                  × lerp(1, PigMask.G, Cavity_Strength)        baked cavity / ambient occlusion
                  × (1 + (PigMask.A − 0.5) × Variation)        low-frequency colour variation
then              lerp(albedo, Interior_Color, PigMask.B)      nostril / smile-crease interiors
```

`Detail` is greyscale, so it never adds hue. Roughness/smoothness, specular, coat and subsurface are separate inputs and nothing in the colour path feeds them. Changing `Skin_Color` from pink to blue, green, purple or yellow therefore keeps the same highlights, gloss, softness and patch pattern. This was checked in Unity: the validation step changes `_SkinColor` and compares every surface parameter.

## 2. Materials

| Material | Used on | Blender | Unity |
|---|---|---|---|
| `M_Pig_Skin` | body, snout, ears, eyelids, tail | Principled BSDF inside node group `NG_Pig_Skin` | `FartOnline/PigSkin` (custom URP shader) |
| `M_Pig_Skin_URPLit` | optional fallback for `M_Pig_Skin` | n/a | `Universal Render Pipeline/Lit` + `T_Pig_SkinDetail` |
| `M_Pig_Mouth` | mouth interior (mouth bag) | Principled, sRGB (0.42, 0.13, 0.15), roughness 0.45 | URP/Lit, smoothness 0.55 |
| `M_Pig_Eye_White` | eyeballs | Principled, near-white, roughness 0.12, coat 0.6 | URP/Lit, smoothness 0.88 |
| `M_Pig_Pupil` | pupils (faces of the eyeball) | Principled, near-black, roughness 0.08, coat 0.8 | URP/Lit, smoothness 0.92 |
| `M_Pig_Hoof` | foot hooves, hand claw tips, eyebrows | Principled, dark brown (0.26, 0.21, 0.19), roughness 0.32 | URP/Lit, smoothness 0.68 |

The exported game mesh has 5 materials and 5 submeshes. There is no separate `M_Pig_Snout` or `M_Pig_Nostril`. The snout uses the same skin, so a single `Skin_Color` recolours the whole pig. The nostril cavities get their dark colour from the `PigMask.B` interior mask instead of a hard material border. A per-face material split on the snout's polar topology produced a jagged sawtooth edge; the smooth mask fixed it.

## 3. PigMask vertex colours

Every skin mesh carries a `PigMask` colour attribute. It holds linear data, not colour, and is exported to FBX as linear vertex colours.

| Channel | Meaning | Source |
|---|---|---|
| R | darker skin patches (0 to 1) | noise-edged blobs placed from the reference (upper arms, right hip/leg) plus a few on the back |
| G | cavity / ambient occlusion (1 = open) | 64-ray hemisphere occlusion against the rest-pose parts (snout over chin, arms over torso and so on), with back-face pass-through and neighbour smoothing |
| B | interior mask | nostril depth, smile-crease line, mouth bag |
| A | low-frequency colour variation (0.1–0.9, 0.5 = neutral). **A = 0 flags geometry buried inside the body**: the snout back wall, parked eyelids and tail root | 3D noise sampled at rest, so it stays locked to the skin when deformed. The buried flag comes from a point-in-body test |

The masks are stored per vertex rather than in object-space procedural noise, which would swim when the character deforms.

## 4. Blender: `NG_Pig_Skin` inputs

`M_Pig_Skin` is a Color Attribute (`PigMask`) node feeding Separate Color, which feeds `NG_Pig_Skin`. To recolour, change `Skin_Color` on the group node.

| Input | Default | Notes |
|---|---|---|
| **Skin_Color** | sRGB (0.945, 0.615, 0.695) | the only colour control |
| Patch_Mask / Cavity / Interior_Mask / Variation_Noise | from PigMask R / G / B / A | wired automatically |
| Patch_Darkness | 0.16 | patches about 13% darker, measured from the reference |
| Cavity_Strength | 0.5 | |
| Variation | 0.06 | ±3% |
| Interior_Color | sRGB (0.30, 0.12, 0.10) | fixed dark brown, independent of skin colour |
| Roughness | 0.36 | |
| Micro_Roughness | 0.06 | fine roughness breakup (Blender only) |
| Specular_Level / IOR | 0.5 / 1.45 | |
| Coat_Weight / Coat_Roughness | 0.18 / 0.22 | the "toy plastic" glaze |
| Subsurface_Weight / Subsurface_Scale | 0.12 / 0.025 | radius (1.0, 0.35, 0.22). Kept low on purpose, because more looks waxy |
| Bump_Strength | 0.035 | micro bump (Blender only) |

The default pink was measured from `GoGoPig.jpg`: belly mid-tone (0.92, 0.63, 0.71) sRGB under the studio light. The lit result in the preview scene matches it closely; see `renders/cmp_08_refpose.png`.

## 5. Unity: `FartOnline/PigSkin` (URP 17, Unity 6000.5)

The shader uses URP's own `UniversalFragmentPBR`, so main and additional lights, shadows, light probes and APV, SSAO, fog, cluster (Forward+) lighting, light layers and cookies all work. It adds three restrained, colour-independent terms for the main light:
- a wrap-subsurface term,
- a soft coat lobe,
- an optional rim (off by default).

It has ShadowCaster, DepthOnly and DepthNormals passes, all properties live in `UnityPerMaterial`, and it is SRP Batcher compatible.

The ShadowCaster pass skips vertices with `PigMask.A = 0` (buried geometry), which would otherwise self-shadow the skin above it. `M_Pig_Mouth`, `M_Pig_Eye_White` and `M_Pig_Pupil` sit partly inside the head, so their ShadowCaster pass is disabled: they receive shadows but don't cast them.

### Blender to Unity parameter mapping

| Blender (`NG_Pig_Skin`) | Unity `FartOnline/PigSkin` | Unity `URP/Lit` fallback |
|---|---|---|
| Skin_Color | `_SkinColor` | `_BaseColor` |
| PigMask R/G/A × Patch_Darkness / Cavity_Strength / Variation | `_PatchDarkness`, `_CavityStrength`, `_Variation` (same vertex colours) | baked into `T_Pig_SkinDetail` (`_BaseMap`) |
| Interior_Mask → Interior_Color | `_InteriorColor` (vertex colour B) | baked as darkening only (cannot tint brown) |
| Roughness 0.36 | `_Smoothness` 0.64 (= 1 − roughness) | `_Smoothness` 0.64 |
| Specular_Level 0.5 | `_SpecularLevel` 0.5 | default dielectric |
| Coat_Weight / Coat_Roughness | `_CoatStrength` 0.18 / `_CoatSmoothness` 0.78 | none |
| Subsurface_Weight / radius | `_SSSStrength` 0.12, `_SSSColor`, `_SSSWrap` 0.5 | none |
| Micro_Roughness, Bump | none | none |

### Recolouring at runtime

```csharp
var mpb = new MaterialPropertyBlock();
renderer.GetPropertyBlock(mpb, 0);                // submesh 0 = skin
mpb.SetColor("_SkinColor", new Color(0.35f, 0.58f, 0.95f));   // _BaseColor for the URP/Lit fallback
renderer.SetPropertyBlock(mpb, 0);
```

A MaterialPropertyBlock (or a material instance) gives each pig its own colour. All skin variants share one material asset and one texture set; nothing is duplicated per colour.

## 6. Textures

| File | Size | Colour space | Use |
|---|---|---|---|
| `Assets/Characters/Pig/Textures/T_Pig_SkinDetail.png` | 1024² | sRGB | greyscale `Detail` for the URP/Lit fallback (UV atlas of the joined game mesh) |
| `Assets/Characters/Pig/Textures/T_Pig_SkinMask.png` | 1024² | linear | raw PigMask RGB baked to UV, for custom Shader Graphs or tools that cannot read vertex colours |

The primary shader needs no textures, because it reads the vertex colours.

## 7. Render-pipeline compatibility

- **URP (this project):** verified. The shader compiles with no errors in the live editor and in a headless Unity 6000.5.6f1 import, and renders through `PC_RPAsset` (see `renders/unity_preview_sheet.png`). `Mobile_RPAsset` was not exercised.
- **HDRP:** not targeted (the project is URP). Use HDRP/Lit with `_BaseColorMap = T_Pig_SkinDetail` and `_BaseColor = Skin_Color`, or rebuild the formula in Shader Graph.
- **Built-in pipeline:** not supported by `FartOnline/PigSkin`. Use the Standard shader with the same detail-map approach.

## 8. Preview lighting (Blender only)

`LIGHTING_PREVIEW` holds a neutral studio: a 260 W key, 90 W fill, 260 W rim, 60 W top, a grey world at 0.42, a grey ground and the `Standard` view transform. It is never exported.
