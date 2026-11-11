# Fart Online: endless river mode

Written for: developers working on Fart Online who need to change or debug the endless mode.

Fart Online is an endless runner. One round raft rides a river that never ends while the crew farts it around the obstacles. One hit and the run is over; the score is the distance travelled.

## Scenes and menus

| What | Where |
|---|---|
| Game scene (first in the build) | `Assets/Game/Scenes/FartOnline_Endless.unity` |
| Old fixed course (kept, not in the build) | `Assets/Game/Scenes/RaftCourse_Game.unity` |
| Rebuild prefabs and the scene | *Tools > Fart Online > Build Endless Game (prefabs + scene)* |
| Rebuild only the obstacle prefabs or the scene | *Tools > Fart Online > Endless > …* |
| Checks | *Tools > Fart Online > Tests > Run Endless Probe*, writes `Assets/Game/Tests/Results/EndlessProbe.md` |
| Windows player | *Tools > Fart Online > Build Windows Player (development)*, writes `D:\FOBuild\FartOnline.exe` |

The endless scene is a copy of the game scene with the fixed course removed (terrain, water, dressing, waterfalls, obstacles, checkpoints, finish). It keeps the raft, game systems, HUD, networking, camera, sun and post-processing, and adds `EndlessWorld/EndlessRiver`.

## How the river is made (`Scripts/Endless/EndlessRiver.cs`)

- **One growing spline.** The scene's `RiverPath` gets new control points ahead of the raft every 22 m: a seeded random walk of the heading, at most ±30° from straight. Points more than about 60 m behind are trimmed.
  - `RiverPath.distanceOffset` keeps every distance absolute after a trim. `TrimFront` re-anchors on the control point after the removed ones, because that's the first point whose distance doesn't change.
  - Buoyancy, current, the chase camera, the AI and the recovery code all keep working unchanged.
- **Chunks of 24 m** are built at runtime, about 230 m ahead of the raft. Each chunk has:
  - **Terrain:** the same cross-section the hand-made course uses (`RiverTerrainBuilder.SideProfile` and `Displace`, now runtime code in `Environment/Scripts/Shared`), with a MeshCollider.
  - **Water:** a ribbon with the same vertex colours the water shader expects.
  - **Dressing:** plants and pebbles on the banks, rocks at the foot of the walls, palms and jungle trees on the plateau.
  - **Obstacle rows.**

  Chunks more than 60 m behind the raft are destroyed together with their meshes.
- **Water:** 18 m wide. The current speeds up from 3.0 to 4.6 m/s over the first 3 km.
- **Determinism:** the seed decides everything (spline, dressing, obstacles). Online, the host picks the seed and sends it with the run state (`NetRunState.seed`), so every client builds the same river. Moving obstacles run on the run clock (`KinematicObstacle.ClockSource = GameRunManager.ObstacleClock`; clients smooth the replicated time), so they're in the same phase everywhere.

## Obstacles (`Scripts/Endless/ObstaclePlanner.cs`)

### Models come from the Hyper-Casual Obstacle Pack

The models are from the Hyper-Casual Obstacle Pack (EnsArts, Asset Store), imported to `Assets/Obstacle Pack`; credits are in `Assets/Game/UI/UI_CREDITS.md`. `EndlessBuilder` wires them into the `EndlessRiver` "pack" slots, and `EndlessRiver.Fit()` scales each model at runtime so that, at any rotation, its renderers **and colliders** fit inside the footprint the planner reserved.

| Planner kind | Pack model |
|---|---|
| Boulder, rock pair | ice wall / ice wall 2 |
| Barrels | three Bomb mines |
| Crates | barrier (barricade) |
| Log | barrier2 (stretched) |
| Buoy line | log_trap2 rope-fence segments |
| Gate | Hammer poles plus banner |
| Barrier | obstacle spikewall on the sliding body |
| Spinner | blade |
| Press | PressTrap |
| Spike trap | obstacle spike |
| Buzzsaw | blade spinner |

- Pendulum and sweeper keep the procedural wood prefabs in `Assets/Game/Endless/Prefabs`, which are also the fallback whenever a pack slot is empty.
- The flamethrowers, poison thrower and crossbow have no colliders, so they're scenery on the banks.
- Trigger colliders (the press head) also end the run: see `RaftRecoveryManager.OnTriggerEnter`.

### Obstacle kinds

There are 14 kinds. The static ones are: boulder, rock pair, mines, barricade, barrier, rope fence, and gate (with fences to both banks). The moving ones are: pendulum log (swings along the flow), sweeper arm (wipes ±60° from a bank post), sliding spiked wall, spinning blade, stamping press, pop-up spike trap and buzzsaw.

### The "always a way through" guarantee

The planner places obstacles in **rows**. Each row reserves a **safe gap**: its width is the raft diameter plus 4.4 m at the start, shrinking to plus 2.4 m at 3 km.

- Every obstacle has a conservative lateral footprint covering the whole sweep of its moving parts at any time. No footprint may overlap the gap. The planner rejects such placements and `Validate()` throws if one slips through.
- The safe path holds the gap's centre for 7 m before and after each row. That's longer than any obstacle reaches along the flow, plus the raft's radius.
- Between rows the path shifts sideways no more than the raft can travel in the time the current gives it: 2 m/s after 1.2 s of reaction time (`MaxShift`).
- Other lanes may be open too, or blocked only some of the time by moving obstacles.

As the distance grows, rows come closer together (30 m down to 19 m), gaps get narrower, and more of the obstacles move.

**Verified physically:** the endless probe sweeps a raft-sized capsule along the safe path for several seeds, from the start to full difficulty, at 52 moments of the obstacle clock. Every hazard collider it touches counts as a violation. The current result is in `Tests/Results/EndlessProbe.md`.

## Crashing and the run (`GameRunManager`, `RaftRecoveryManager`)

- Obstacles carry `EndlessHazard`. The first contact while the run is live raises `RaftRecoveryManager.Crashed`, and the run goes to the new state **`GameOver`** (appended to `RunState`, so the old byte values are unchanged).
- Capsizing, falling and being stranded also end the run. Banks and canyon walls only bounce.
- There are no checkpoints. The R key (Y on a gamepad) restarts after a crash, and the pause menu hides "Reset to Checkpoint".
- The score is metres along the river from the start (`RunStats.score`). The best score is saved per machine in PlayerPrefs (`fo.endless.best`).

## HUD

- The top pill shows the distance. The badge shows the best distance.
- The bar shows progress toward the best distance, or toward the next 250 m.
- A small pill shows the survival time.
- Every 250 m there's a "250 m! FASTER!" banner, and "NEW BEST!" appears when you beat your record.
- On a crash: a "CRASH!" banner and camera shake, then the **GAME OVER** board. It shows what you hit, the distance (with a NEW BEST badge when it applies), survival time, best and farts, plus Play Again and Main Menu buttons.

## AI crew

On the endless river the AI steers toward the guaranteed safe path a few metres ahead (`EndlessRiver.SafeLaneX`). It pushes sideways against its drift and brakes when the raft runs faster than the current.

## Tuning knobs

- **`EndlessRiver`:** `waterWidth`, `buildAhead`, `keepBehind`, `startDistance`, `firstObstacle`, `startFlow`, `maxFlow`.
- **`ObstaclePlanner`:** `Difficulty` (0 to 1 over 3 km), `Spacing`, `Gap`, `Hold`, `LateralSpeed`, `ReactionTime`, and the fill and moving-obstacle chances in `Next()`.
- After changing footprints or prefabs, re-run the endless probe.

## Animated hazards and river shape (2026-10-31)

These are inspired by the *Fart Together* trailer (watched frame by frame). The geometry is original and procedural (`Scripts/Editor/AnimatedObstacleBuilder.cs`, menu *Tools > Fart Online > Endless > Rebuild Animated Obstacles*). The materials are URP Lit toy colours in `Assets/Game/Endless/Materials`.

| Kind | Prefab | Motion (script, shared obstacle clock) | Footprint the planner reserves |
|---|---|---|---|
| BankRoller | PF_Obs_SpikeRoller | purple log with white spikes spinning along the bank (`SpinObstacle`, axis Z) | bank to 1.75 m into the river |
| StarSpinner | PF_Obs_StarSpinner | 4 spiked arms turning on a hub (`SpinObstacle`, axis Y) | arm + 0.55 m radius |
| Puffer | PF_Obs_Puffer | red spiky mine with eyes; inflates and deflates, bobs (`PulseObstacle`) | fully inflated radius |
| Tentacle | PF_Obs_Tentacle | 7-bone kraken arm by the bank, leans into the river with a curl wave (`TentacleObstacle`, bounded angles) | base + 4.2 m reach + 0.5 m |
| Windmill | PF_Obs_Windmill | tower on the shore, blades turning in the cross-river plane and dipping into the water (`SpinObstacle` on the hub) | bank to 3.6 m into the river |
| Sweeper | PF_Obs_Sweeper | now with a mallet head (hammer arm) | unchanged |

Each motion script is its own file (`SpinObstacle.cs`, `PulseObstacle.cs`, `TentacleObstacle.cs`). Unity can only attach a MonoBehaviour whose file name matches its class: with all three in one file, the prefabs got "missing script" components.

**River shape** (`EndlessRiver.AppendPoint`):
- **Bends:** planned sweeps of 40–85° that keep turning the same way until past 50°, then swing back. The river never runs upstream, so it can't loop over itself.
- **Turn rate:** at most 20° of turn per 22 m segment, so the canyon never folds.
- **Waterfall drops:** every 420–700 m there's a straight lip, then a steep chute 2.2–3.6 m lower over 7.5 m, with faster current.
- **Quiet zones:** no obstacle rows within 34 m of a drop (`ObstaclePlanner.IsQuiet` / `NextFreeDistance`).

**Smooth gas** (`SharedRaftController`, tuning in `RaftTuning`):
- The combined fart force eases in (`thrustRiseTime` 0.11 s) and fades out (`thrustFallTime` 0.24 s), so the stream swells and the raft glides on.
- Puffs, charged blasts and brakes go into a pressure reservoir released with `burstReleaseTime` 0.09 s: the same total impulse without a single-frame kick.
- Teleports and a disabled thrust gate clear the reservoir.

## Deadlier hazards and the danger ramp (2026-10-31)

Three timed killers, built by `AnimatedObstacleBuilder` (same menu) and placed by the planner like every other kind:

| Kind | Prefab | What it does (shared obstacle clock) | Deadly part | Footprint the planner reserves |
|---|---|---|---|---|
| FireJet | PF_Obs_FireJet | Striped fuel tank on the bank. The pilot flame swells and flickers faster for 1.2 s (warning), then a jet of fire roars 3–5.2 m across the water for 1.5 s and dies back (`FireJetObstacle`). | Trigger capsule on the flame, only while the jet is longer than a third of its reach | Bank to nozzle (0.55 m) + 1.06 × jet length + 0.35 m |
| Whirlpool | PF_Obs_Whirlpool | Spinning dark-water rings with white foam spirals around a spiked grinder. Once the raft's rim enters the swirl, `WhirlpoolPull` drags it toward the grinder (up to 2.3 m/s²) and swings it round (1.4 m/s²). One player's sideways fart (about 2.6 m/s²) can still escape. | The grinder (solid capsule) | The whole swirl (radius 2.6–3.7 m), so the safe path never feels the pull |
| Mortar | PF_Obs_Mortar | Cannon on a wooden pier. A red target ring on the water tightens while it aims (1.1 s), the cannonball arcs over (1.35 s) and lands in the ring, and a splash column bursts up (`MortarObstacle`). | The ball on the last quarter of its fall and the first 0.3 s of the splash (triggers), both inside the ring | Target circle (1.35–1.8 m) + 0.3 m, out to the bank |

`RaftRecoveryManager.OnTriggerEnter` ends the run on any trigger under an `EndlessHazard`, as it already did for the press.

**Danger ramp** (`ObstaclePlanner`):
- `HazardSpeed(distance)`: every moving hazard in a row (clock-driven obstacles and pack animators) runs 1x at the start, 1.6x at 3 km and 1.8x at 6 km. Footprints are whole sweeps, so speed never affects the guarantee.
- Past 3 km (`Overdrive`): rows tighten from 19 m to 16 m apart (never under 2 × `Hold`), and up to 80% of obstacles move.
- The HUD announces **DANGER LEVEL n** every 500 m (`DangerLevel`), with distance banners between them.
- The pack's bank flamethrowers are no longer used as scenery: every flamethrower you see now really fires.

**Verification** (`Tests/EndlessProbe.cs`, report `Tests/Results/EndlessProbe.md`):
- The safe-path sweep now counts trigger hazards (fire, cannonball, splash) and checks that no whirlpool pull reaches the raft's rim. It runs to 4.2 km to cover the overdrive.
- The crash check covers 16 kinds. Fire jets and cannons hold the raft in the danger zone until they fire. The whirlpool starts the raft inside the swirl but clear of the grinder, so the pull has to drag it in.

## Hard mode tuning (2026-10-31, after "make the difficulties high")

| Setting | Before | Now |
|---|---|---|
| Full difficulty reached at | 3 km | **2 km** (`ObstaclePlanner.Difficulty`) |
| Row spacing (start → full → overdrive) | 30 → 19 → 16 m | **26 → 18 → 15.5 m** |
| Safe gap (start → full) | raft + 4.4 m → raft + 2.4 m | **raft + 3.6 m → raft + 2.2 m** (the guarantee's floor is raft + 2 m) |
| Moving obstacles | 30% → 65% → 80% | **45% → 75% → 85%** |
| Fill chance per side | 85% → 100% | **95% → 100%** |
| Hazard speed (`HazardSpeed`) | 1x → 1.6x → 1.8x | **1.15x → 1.8x (2 km) → 2.05x (5 km)** |
| Overdrive | 3–6 km | **2–5 km** |
| River current (`EndlessRiver.startFlow/maxFlow`) | 3 → 4.6 m/s | **3.4 → 5.2 m/s** |
| Whirlpool pull (`WhirlpoolPull.strength`) | 2.3 m/s² | **2.7 m/s²** |

Thrust went up about 15% to match (`Assets/Game/Settings/RaftTuning.asset`; see `Physics_Tuning_Guide.md`).

## Spiked logs, harder ramp and the bright look (2026-11-05)

The user asked for a game that is harder and looks like two reference images: a bright cartoon river with red spiked rollers, spiked sea mines and a bridge with spiked logs. All the geometry is original and procedural, built by `Scripts/Editor/SpikeHazardBuilder.cs`. To rebuild, use *Tools > Fart Online > Endless > Rebuild Spiked Hazards + Decor (in place)*. That menu also wires the prefabs into the open endless scene; *Rebuild Animated Obstacles* rebuilds them as well.

### New hazards

| Kind | Prefab | What it does | Footprint the planner reserves |
|---|---|---|---|
| SpikeSwing | PF_Obs_SpikeSwing (hung under the pendulum gantry) | Red spiked roller on ropes. It swings 48° along the flow (period 2.8 s) and spins on its axle (`VisualSpin`). | Log length / 2 + 0.7 m on each side |
| MineField | PF_Obs_SpikeMine × 2–4 | Floating red mines with white spikes. They are spaced 1.75 m across the river, staggered ±0.9 m along it, and bob. | First mine − 0.9 m to last mine + 0.9 m |
| SpikeLogRoll | PF_Obs_SpikeLogRoll | Spiked roller lying across the stream. It spins while it slides sideways (`MovingBarrier`). | Start − (len / 2 + 0.5) to start + travel + len / 2 + 0.5 |
| SpikeBridge | PF_Obs_SpikeBridge + PF_Obs_SpikeCurtain | A plank bridge over the whole river (scenery, posts on the banks), with spiked logs hanging to 0.25 m above the water from each bank to the gap. Replaces 60% of gate rows. | Bank to the gap edge ± 0.7 m |

`VisualSpin` (`Environment/Scripts/Obstacles`) rotates only the visual mesh on the shared obstacle clock. The collider is a capsule on a non-spinning parent, so the spin never affects the physics.

### Harder ramp (`ObstaclePlanner`)

| Setting | Before | Now |
|---|---|---|
| Full difficulty reached at | 2 km | **1.5 km** |
| Row spacing (start → full → overdrive) | 26 → 18 → 15.5 m | **22 → 16.5 → 14.5 m** (14.5 = 2 × Hold + 0.5, the floor) |
| Safe gap (start → full) | raft + 3.6 m → raft + 2.2 m | **raft + 3.0 m → raft + 2.1 m** (the guarantee's floor is raft + 2 m) |
| Moving obstacles | 45% → 75% → 85% | **55% → 80% → 90%** |
| Obstacles per side | up to 2 | **up to 3** on wide sides |
| Hazard speed | 1.15x → 1.8x → 2.05x | **1.3x → 1.95x → 2.3x** |
| Overdrive | 2–5 km | **1.5–4 km** |
| Danger level | +1 every 500 m | **+1 every 400 m** |

The guarantee is unchanged: every footprint is the whole sweep, the gap stays at least the raft's diameter + 2 m wide, and the endless probe still sweeps the safe path.

### Bright cartoon look (`Scripts/Editor/BrightLookBuilder.cs`, menu *Tools > Fart Online > Apply Bright Cartoon Look*)

- **Palette and materials:**
  - The palette lives in `TropicalCourseSettings`: saturated red-orange rock, vivid grass, turquoise water.
  - Applying the look rebuilds the materials through `MaterialLibrary.Rebuild`, so a later *Rebuild Materials* keeps the new palette.
  - The water has more foam.
- **Sky, light and grading:**
  - The sky is a clearer blue, with a warmer and stronger sun and brighter trilight ambient.
  - The post profile adds saturation (+30) and contrast (+16).
  - `LightingBuilder.Apply` calls it at the end, so re-applying the lighting keeps the look.
- **Bank decor (`EndlessRiver.Decorate`):**
  - Chunky stone block stacks with grass tops stand at the wall foot and on the cliff top. Wooden fences run along the plateau edge.
  - Runtime bank waterfalls (`Cascade`) hug the wall's outer envelope and end in a foam and mist splash.
  - The decor uses its own random stream, so it never changes the obstacle layout.
- **Raft:**
  - The raft is now an inflatable ring: a cream tube on a teal band with red handles and a yellow rope, around a green plank deck.
  - The rebuild menu is *Tools > Fart Online > Restyle Raft (inflatable ring)*. The physics hull is unchanged.

### On-screen controller (touch)

- **Stick:** the bottom-left walk arrows were replaced by a thumbstick (`Input/TouchJoystick.cs`).
  - Drag it left or right to walk around the rim. The input is analog, with a dead zone.
  - Flick it down to brake.
- **Action buttons:** FART, CHARGE and BRAKE stay at the bottom right.
- **Showing the controller:**
  - A new **PAD** button next to pause shows or hides the controller on any device. It sets the same option as *Settings > Touch buttons*.
  - In Auto mode, the web build also shows the controller when the device has a touchscreen.

## Tight river, deadly banks, stronger thrust (2026-11-05, second pass)

The user's feedback: "the river is too wide and there is too much room to pass ... at the beach of the river there should be roller obstacles ... make it difficult ... increase the fart thrust power with smoothness."

- **Narrower river.** `EndlessRiver.waterWidth` went from 18 m to **15 m**. The gantry and bridge spans follow it: `EndlessBuilder` and `SpikeHazardBuilder` use a half width of 7.5 m.
- **Deadly banks.** Both banks are lined with spinning red spiked rollers.
  - Prefab: `PF_Obs_EdgeRoller`, 6 m segments placed every 6.3 m along the flow, with a static capsule collider. Touching one ends the run with "a spiked bank roller".
  - They reach 1.25 m into the water. `EndlessRiver.edgeGuard` (1.75 m) keeps the safe gap clear of them.
  - Segments are removed within ±3.5 m of a row whose bank-anchored obstacle reaches that bank (sweepers, fences, bridges, cannons and so on: `BankAnchored`). Floating hazards may overlap the rollers, since both are deadly.
- **One tight passage per row.**
  - The safe gap is now the raft's diameter + **1.8 m** at the start, down to + **1.0 m** at full difficulty, so each side has 0.5 to 0.9 m to spare. The planner's floor is + 0.9 m (`ObstaclePlanner.MinGapMargin`).
  - After the normal fill, `ObstaclePlanner.Plug` closes every other stretch of open water at least 1.8 m wide with a line of spiked mines. Moving hazards still leave timing windows elsewhere.
- **Stronger, smooth thrust** (`Assets/Game/Settings/RaftTuning.asset`):

| Setting | Before | Now |
|---|---|---|
| `thrust` (per player) | 1750 N | **2400 N** |
| `maxCombinedForce` | 4900 N | **6600 N** |
| `tapImpulse` | 750 | **1000** |
| `chargedImpulseMin` / `Max` | 1150 / 3000 | **1550 / 4000** |
| `brakeImpulse` | 1500 | **2000** |
| `thrustRiseTime` (swell-in) | 0.11 s | **0.16 s** |
| `thrustFallTime` (glide-out) | 0.24 s | **0.30 s** |
| `burstReleaseTime` | 0.09 s | **0.14 s** |
| `maxSpeed` | 10.5 m/s | **11.5 m/s** |

  The physics self-test passes 19/19. One back-seat player now adds 4.4 m/s forward (it was 3.0).
- **AI crew.** `AICrewInput` also corrects from the raft's current offset to the safe lane, damps sideways drift more, and shoves away from an edge that is getting close.
- **Probe.** The crash check adds a "Bank edge" case: the raft is drifted into the bank rollers.

## Wait gates, lush banks and mobile performance (2026-11-05, third pass)

The user's feedback: "add some vegetation on the side of the river ... add more obstacles so that sometimes the player has to wait to pass ... optimise the game, currently it's too laggy. The game should run smoothly on mobile devices."

### Wait gates (`ObstacleKind.WaitGate`, prefab `PF_Obs_WaitGate`, motion `WaitGateObstacle`)

- **Where:**
  - Past 180 m, 40–65% of spike-bridge rows close their only gap with a red spiked log on a lift between two striped posts.
  - The log is down for 2.2 s, rises in 0.45 s, stays up for **2.6 s** (bottom about 2.6 m above the water, clear of the crew), and drops in 0.45 s.
  - Players have to hold the raft back (brake, or fart from the front seat) and go when it lifts. It shudders just before it moves.
- **Timing:** the danger ramp never speeds up wait gates (`EndlessRiver.SpeedUp` skips them), so the open window stays as tested.
- **Probe:**
  - The safe-path sweep ignores the lift. Instead, it checks that every gate's open window is longer than the raft needs to pass (raft diameter + the log's 1.7 m depth, at the slowest current there).
  - The crash check holds the raft under a gate until it drops.
- **AI:** `AICrewInput.MustWait` holds the raft upstream when the gate ahead won't stay open for the whole pass.

### Lusher banks

- 7 shore plants per side per 24 m chunk (it was 3).
- 4 bushes along each cliff top, so the canyon rim reads green.
- The existing palms, jungle trees, block stacks and fences.
- All of it is static-batched (see below), so the extra plants cost almost no draw calls.

### Performance (`Tests/PerfProbe.cs`, menu *Run Perf Probe (Mobile quality, 30 s)*, results in `Tests/Results/PerfProbe.md`)

| Change | Where |
|---|---|
| Each hazard's toy-coloured parts merge into one vertex-coloured mesh (`M_Toy_VertexColor`, Env/StylizedSurface Foliage mode, no wind). A spiked roller goes from 4 draws to 1. | `Editor/ToyMerge.cs`, called by `AnimatedObstacleBuilder.Save` |
| Each chunk's terrain, plants, rocks, block stacks, fences and bank waterfalls go under a `Static` child and are combined with `StaticBatchingUtility`. The combined meshes are destroyed with the chunk (no leak). | `EndlessRiver.Batch` |
| Bank rollers are 9 m, low-poly, and without posts. On mobile they don't spin and join the static batch. | `SpikeHazardBuilder.EdgeRoller`, `EndlessRiver.spinEdgeRollers` |
| Bank rollers are laid along each bank's own curve (a segment per 7.5 m of bank, stretched to its chord), so they never poke into the river on bends. | `EndlessRiver.EdgeRollers` |
| While playing, a chunk is built over two frames (geometry and decor, then obstacles and batching), one step per frame, so there are no frame spikes. | `EndlessRiver.StartChunk` / `FinishChunk` |
| Gate poles use the procedural post. The pack's hammer pole was 33k triangles. | `EndlessBuilder.WireSpikesAndDecor` |
| **Mobile quality level** (phones, tablets, the web build): <ul><li>The river is built 170 m ahead and kept 40 m behind (it was 230 / 60), with fog at 70–190 m and the far plane at 210 m.</li><li>Bloom is off.</li><li>The crew pigs cast no shadows.</li><li>Shadow distance is 25 m, render scale 0.8, HDR is off, and the opaque copy is downsampled 2x.</li></ul> | `PlatformBoot.ApplyQualityTweaks`, `Assets/Settings/Mobile_RPAsset.asset` |
| An endless river accidentally saved into the scene is now removed at start. 11 stale chunks were found in the scene; their rollers killed the raft in mid-river. The in-place rebuild menu never saves chunks. | `EndlessRiver.Awake`, `EndlessBuilder.RebuildSpikesInPlace` |

Measured with the Mobile quality level in the editor, AI crew of 3:

| | Draw calls | SetPass | Triangles | Active renderers | Worst frame |
|---|---|---|---|---|---|
| Before | 827 | 82 | 727k | 832 | 40 ms |
| After | 327 | 86 | 607k | 478 | 23 ms |

Editor frame times include editor overhead. The draw-call, triangle and renderer counts carry over to devices. The 4 crew pigs (about 31k triangles each) are now the biggest single cost. A lower-poly LOD for them is the next step if phones still struggle.

### AI crew (now TEST-ONLY: removed from the game on 2026-11-11; `Tests/AICrewInput.cs` + `Tests/TestCrew.cs` drive the probes)

- **Reachable spot:** a bot aims for the closest spot it can actually reach (players can't pass each other on the rim). The old code let every bot queue behind the idle player's seat, so nobody farted.
- **Spread and nearest:** bots spread evenly around the rim, and only the one nearest the needed push direction walks to it. Any bot whose spot already pushes roughly the right way farts too.
- **Lane keeping:** it predicts where the raft will be about 2 s ahead, with a dead band, and uses measured fart pulses instead of long streams, which overshoot with the stronger thrust.

## Hard from the start: sharp turns, more animated hazards, more traps (2026-11-07)

The user asked: "make the game more difficult from starting, add sharp turns, add more animated obstacles, add more traps from starting."

- **Sharp turns (`EndlessRiver.AppendPoint`):**
  - `maxTurnPerPoint` went from 20 to 28 degrees per 22 m control point. That's a turn radius of about 45 m. The canyon walls reach about 22 m from the centreline, so they never fold. Only the hidden back plateau, out to about 48 m, can overlap slightly on the inner side.
  - Bends start after control point 3 instead of 4, so the first turn comes about 25 m after the start line.
  - 90% of bends are sweeps (was 80%), of 50 to 85 degrees (was 40 to 85), held for 2 to 4 points (was 3 to 6). Each point turns 85 to 100% of the maximum (was 70 to 100%).
  - The scene's `EndlessRiver` was updated to match (`maxTurnPerPoint` 28, `firstObstacle` 75).
- **Difficulty from the first metre (`ObstaclePlanner`):**
  - `Difficulty` now starts at `StartDifficulty` = 0.3 and reaches 1 at 1.5 km. Before, it started at 0. This also means a narrower safe gap and a faster current from the start (3.9 m/s instead of 3.4).
  - The first obstacle row is at 75 m (was 115), so 30 m after the start line.
  - Row spacing is `Lerp(20.5, 16, d)`, about 19 m at the start. It was 22 m.
  - Animated share of obstacles: 72% at the start, 88% at 1.5 km, 95% in overdrive. It was 55 / 80 / 90%.
  - Moving hazards run at 1.6x speed at the start, 2.0x at 1.5 km and 2.35x at 4 km.
  - More traps per side: up to 3 when there is more than 8 m of room (60% chance), and 2 when there is more than 4.5 m (90% chance).
  - Closed spike-bridge or gate rows have a 20 to 32% chance (was 16 to 30%).
  - Wait gates appear from 150 m along the river (was 180 m).
- **AI crew:** starts holding back for a closed wait gate 30 m before it (was 22 m), because the current is faster now.
- **Guarantee unchanged:** every row still keeps a raft-sized safe gap that no hazard footprint enters, at any time. The endless probe re-verifies this (see `Tests/Results/EndlessProbe.md`).
