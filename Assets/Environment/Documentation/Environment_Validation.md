# Tropical raft course: environment validation

**Status: 2026-10-30.** Every item below was checked in the live Unity 6000.5.6f1 editor through MCP, or in a player build. Items not verified are listed separately and are not claimed.

## Verified

| Check | Result | How |
|---|---|---|
| Scripts compile | 0 errors | Editor console after each refresh |
| Scene generation | `TropicalRaftCourse.unity` rebuilds deterministically: 10 terrain chunks, 2 water spans, 9 waterfalls, 563 dressing props, 90×125 background cells with 260 trees, obstacles with 5 checkpoints | Generator log lines |
| Raft clearance | Narrowest route water is 9.5 m. The round game raft is 4.8 m across; the old square test raft had a 5.94 m diagonal | `CourseSettings.MinRaftPassage`, obstacle builder log |
| Basin boulder | Radius 4.5 m, leaving room on both sides | Obstacle builder log |
| Obstacles readable | Gates, beams, boulder, sweeper, kicker, pendulums and finish visible from river level | Preview sheet `ArtSource/Environment/Previews/sheet_obstacles.png` |
| Buoyancy and current | Raft floats level at the waterline and is carried by the current | Play-test (Phase 2) and the game's physics self-test |
| Collisions | Canyon wall impact registered; the raft stays in play | `PhysicsSelfTest`: "Pushing into the canyon wall..." PASS |
| Hazard below the course | A fall triggers a wipeout and a respawn at the last checkpoint | `PhysicsSelfTest` PASS |
| Jump (section E) | **Fixed during validation.** The dry span was about 15 m (impossible) because a segment is dry if either end is dry; the layout was changed to about 7 m. Now all six probe attempts land in the basin | `Assets/Game/Tests/Results/JumpProbe.md` |
| Full course completable | A single AI crew member finished the whole course | `Assets/Game/Tests/Results/CourseRun.md` |
| Water shader stability | Fixed striping caused by per-vertex speed in the flow phase, and turbulence banding | Isolation renders (Phase 4) |
| Lighting and atmosphere | `LightingBuilder` (menu *Apply Lighting and Atmosphere*): procedural sky, warm sun, trilight ambient, linear haze 140–700 m, global volume (bloom clamped to 3, neutral tonemapping, +14 saturation, vignette, warm white balance), realtime reflection probe. Game-view captures checked | `ArtSource/Game/Previews/sheet_lit2.png` |
| Shader NaNs | **Fixed during validation.** Black slivers on waterfall sheets and canyon silhouettes came from `normalize()` of zero-length vectors (degenerate edge triangles; grazing-angle bump). The waterfall, rock and water shaders now use `SafeNormalize`; the camera has Stop NaN on. Isolated by A/B captures (`ArtifactProbe`) | `ArtSource/Game/Previews/sheet_artifact.png`, `sheet_lit2.png` |
| GPU stability | The D3D12 device-removed crash during preview capture was avoided by running D3D11 and dropping forced synchronous shader compilation | Editor runs since |

## Not verified / not done

- **Phase 8 optimisation:** props have LOD culling, but no profiler capture or target-hardware frame budget has been measured. The only timing measured: about 3.7 ms average editor frame while four players fart (`VisualProbe`).
- **Audio:** hooks exist (`EnvironmentAudioHooks`, game `GameAudio`); the clips are synthesised placeholders.
- **Final preview set:** obstacle previews and game screenshots exist (`ArtSource/Environment/Previews`, `ArtSource/Game/Previews`). A full section-by-section beauty set after a lighting pass has not been rendered.
