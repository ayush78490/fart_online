# HANDOFF CONTEXT: fartonline (read this first when continuing in a new session)

Written for: a Claude session on another account picking up this work cold.
Kept current: updated after every completed phase. **Last updated: 2026-11-11: Task 14 (AI crew removed from the game) done, see section 3m. Before that: Task 13 (trailer video) done, see section 3l. Before that, 2026-11-07 (evening): Task 12 (accounts, friends, invites, AWS-ready dedicated rooms, voice chat) implemented and tested locally; WAITING for the user's Supabase Project URL + anon key + access token (see section 3k). Before that: Task 11 (Android 60 fps, landscape, harder start) done. Editor active build target is now STANDALONE (the Linux server build was last); the Android build switches back automatically.**

---

## 0. How to resume

1. Read this whole file, then `ArtSource/PigCharacter/PigCharacter_Validation.md` (pig) and `Assets/Environment/Documentation/` (environment, once it exists).
2. Check the tools are reachable (section 1).
3. Continue at **"Next step"** in section 4. Update this file after each phase.

## 1. Environment and tools

- **Project:** `D:\unityProjects\fartonline`. Unity **6000.5.6f1**, **URP 17.5** (`Assets/Settings/PC_RPAsset.asset`, which has the depth and opaque textures enabled; its shadow bias is 0.1 / 0.5). Input System only (`activeInputHandler: 1`). Not a git repo.
- **Packages:** no Cinemachine, no Splines, no ProBuilder. MCP for Unity (`com.coplaydev.unity-mcp`) is installed.
- **Unity MCP:** MCP for Unity 3.4.11 at `http://127.0.0.1:58421/mcp` (streamable HTTP). If the session has no `mcp__unity...` tools, use the client in the repo:
  ```
  python Tools/umcp.py tools                          # list tools
  python Tools/umcp.py schema <tool>                  # input schema
  python Tools/umcp.py call <tool> '<json args>'      # e.g. refresh_unity, read_console, execute_menu_item, execute_code
  python Tools/umcp.py read mcpforunity://editor/state
  ```
  Things learned about this server:
  - `execute_code` needs `{"action":"execute","code":"...; return x;"}` and uses the CodeDom compiler (C# 6: no tuples or `$""` features).
  - A domain reload (`refresh_unity` with compile) or an FBX re-import temporarily disconnects it. Poll `mcpforunity://editor/state` until `data.advice.ready_for_tools` is true.
  - Always `read_console` for errors after compiling.
- **Blender:** 4.5.2 LTS with the MCP addon on port 9876 (`mcp__blender__*` tools). The addon is an older protocol, but `execute_blender_code` and `look` work.
- **Headless Unity validation** can't run against this project while the editor is open (project lock). It worked on a copy, but the scratchpad path is too long for Windows MAX_PATH; use a short path like `D:\PigVal`. Prefer validating in the live editor through MCP.
- **Backup** taken before the environment work: `D:\unityProjects\fartonline_backups\fartonline_before_environment_20261030_0312.zip`.

## 2. Completed: pig character (task 1)

Full details are in `ArtSource/PigCharacter/*.md`. Summary:
- **Blender sources:** `ArtSource/PigCharacter/PigCharacter_Master.blend` (editable parts, rig, 35 actions) and `PigCharacter_AnimationTests.blend` (joined game mesh with the clips on an NLA timeline).
- **Scripts:** `ArtSource/PigCharacter/scripts/pig_00…13_*.py`. `pig_build_all.py` rebuilds stages 01–11; then run `pig_12_animations.py` (`bake_all()`) and `pig_13_export.py` (`run_export()` writes the FBX, bakes textures, re-imports to verify and saves the blends).
- **Unity:** `Assets/Characters/Pig/`
  - FBX: 30,766 tris, 41 bones, 23 blend shapes, 35 clips, 5 materials, PigMask vertex colours.
  - `FartOnline/PigSkin` URP shader.
  - `PigFaceController` and `PigSecondaryMotion`.
  - Editor: `PigCharacterImporter`, `PigCharacterSetup`, `PigCharacterValidation` (menu *Tools > Pig Character > …*).
  - `PF_Pig.prefab`, `PigCharacter.controller`.
- **Validation:** *Tools > Pig Character > Run Validation* gives **all checks passed** in the live editor. Report: `Assets/Characters/Pig/Validation/UnityValidationReport.txt`.
- **Key decisions and gotchas:**
  - The exporter yaws the armature 180° for the export only, so the pig faces Unity +Z.
  - Clips are exported from NLA strips so blend shapes are sampled.
  - Bone-driven face controls in Unity are data-driven from single-frame `FACE_*` clips.
  - AO must be baked against the REST pose; the ears needed their normals flipped.
- **Open item (not yet done):** a Play Mode animation test of the pig in Unity. Only Jump, Walk and Gestures were watched in Blender. Planned: play every clip through the Animator, record frame strips, test transitions and loop seams, and run `PigSecondaryMotion` with `AddImpact`.

## 3. Current task: Tropical Raft Course environment (task 2)

The user's full prompt is the "UNITY ENVIRONMENT GENERATION MASTER PROMPT: Stylized Tropical Raft Obstacle-Course Environment", inspired by *Fart Together* (Steam app 5117550). All geometry must be original.

Required:
- Six sections: A jungle lagoon start, B narrow canyon, C waterfall basin, D rapids S-curve, E water ramp and jump, F final arena with finish.
- Spline-driven river, procedural canyon, custom water shader, waterfalls, vegetation, wood structures, obstacles (gates, pendulums, sweepers, moving barriers, rocks, low beams, ramps, checkpoints, finish).
- Test raft for 4 characters, chase camera, audio hooks, editor window "Tropical Course Generator", deterministic seeds.
- Scene `Assets/Scenes/TropicalRaftCourse.unity`; docs `Environment_README.md` and `Environment_Validation.md`; folders under `Assets/Environment/`.
- Phases 1–10, reporting after each.

### Design decisions made
- Own Catmull-Rom spline (`RiverPath`) instead of installing the Splines package; own chase camera instead of Cinemachine.
- Everything generated by editor code in `Assets/Environment/Scripts/**` and saved as meshes and prefabs (deterministic seed), not one giant script.
- Water: custom URP shader using the depth texture (already enabled) for shallow colour and foam, plus vertex colours baked by the river mesh builder. No paid or extra packages.
- Raft passengers: 4 × `PF_Pig` (reusing task 1).

### Phase status
| Phase | Status | Notes |
|---|---|---|
| 1 Inspect project | DONE | see section 1; backup taken |
| 2 Graybox route, scale, raft clearance | DONE | 475 m route, 10 terrain chunks, 2 water spans; narrowest route water 9.5 m vs raft diagonal 5.94 m (≈1.8 m per side); Play Mode test: raft floats level at the surface, carried by the current |
| 3 Canyon meshes and banks | DONE | `Env/StylizedSurface` shader (Rock/Foliage/Wood modes, texture-free); `MaterialLibrary` (menu *Rebuild Materials*); 12 wall rows, ledges, cracks, world-space displacement; separate collision meshes (`TerrainCol_XX`); shore vegetation only on real beaches |
| 4 Water shader, river mesh, waterfalls, foam | DONE | `Env/StylizedWater` (depth colour, refraction, flow-map normals and foam, foam spots), `Env/Waterfall`; `WaterfallBuilder` (9 falls: 3 basin, 2 canyon trickles, 2 arena, chute lip, 47 m landmark), wet zones on the terrain, `WaterFoamSpots` global array; materials M_River_Main/Shallow/Foam, M_Waterfall, M_Foam (particles) |
| 5 Rocks, vegetation, wood, background | MOSTLY DONE | `Editor/PropFactory.cs` (menu *Rebuild Prop Library*): 6 rocks and 16 vegetation prefabs in `Assets/Environment/Prefabs/{Rocks,Vegetation}`. `Editor/DressingPlacer.cs` (menu *Rebuild Dressing and Background*): 562 dressing props plus the background ground grid and 260 trees. **Pending:** (a) verify the background-hole fix (inside vertices slide onto the corridor boundary; written but NOT yet run); (b) re-capture previews; (c) wooden structures (start dock/gate) come in Phase 6 |
| 6 Obstacles, colliders, checkpoints, finish | IN PROGRESS | `Editor/ObstacleBuilder.cs` written (menu *Rebuild Obstacles*: start dock and gate, canyon gate, 2 slalom low beams, basin boulder, S-curve sweeper, chute warning posts and kicker ramp, 2 pendulum logs, moving barrier, finish gate with chequered banner, FinishGate trigger, confetti, finish dock, 5 checkpoints, HazardZone below the course; `M_Wood_Painted` added to MaterialLibrary). Runtime scripts written: `Scripts/Obstacles/{KinematicObstacle,Pendulum,RotatingSweeper,MovingBarrier,Checkpoint (+CourseProgress),FinishGate,HazardZone}.cs` and `Scripts/Environment/EnvironmentAudioHooks.cs`. Still to do: `Editor/ObstacleBuilder.cs`, which builds the wooden obstacle prefabs (rounded beams via `PropFactory.RoundedBox`, red/white hazard stripes via vertex colour) and places them per section (plan in section 4) |
| 7 Lighting, atmosphere, camera | TODO | |
| 8 Optimisation | TODO | |
| 9 Play-test and fixes | TODO | |
| 10 Save, verify, previews, docs | TODO | |


## 3b. NEW TASK 3 (current focus): "SHARED RAFT FART-PHYSICS GAME IN UNITY"

The user pasted a long master prompt (in the transcript of the session started 2026-10-30 ~06:30) and said: "read the HANDOFF_CONTEXT.md and continue the implementation". Summary of the prompt:
- **Goal:** a playable physics raft obstacle-course game inspired by *Fart Together*. One shared raft, 1 to 4 players, each player's directional fart thrust applied at their seat (`AddForceAtPosition`-equivalent: F_total = ΣF_i, τ_total = Σ r_i × F_i), cartoon gas VFX, single-player with Solo Assist (Off/Standard/High) and optional AI crew, online 2–4-player co-op (server-authoritative), obstacles A–I (canyon gate, pendulum, sweeper, S-curve rapids, waterfall+rock, ramp+gap, overhead beam, checkpoint, finish), `RaftRecoveryManager`, `GameRunManager` state machine (MainMenu, Lobby, Ready, Countdown, Playing, Checkpoint, Wipeout, Respawning, Finished, Paused, Disconnected), HUD, audio hooks, `RaftGameplay` input action map (ThrustForward/Backward/Left/Right, FartBurst, ChargeFart, Brake, Interact, Pause, CameraLook).
- **Structure:** `Assets/Game/{Characters,Raft,VFX/{Fart,Water,Impacts},Environment,Obstacles,Scripts/{Input,Physics,Players,Multiplayer,GameFlow,UI,Camera},Audio,Scenes,Settings,Tests,Documentation}`.
- **Deliverables:** playable scene, `PF_SharedRaft`, seat prefabs, `PF_FartBurstVFX` + variants (VFX_FartTap/Normal/Charged/Brake/Side/Cooldown), obstacle prefabs, single-player, online co-op, input config, UI and audio hooks, and docs: setup guide, physics tuning guide, multiplayer architecture guide, test report. Never claim multiplayer works unless tested; state the networking package's limits.
- **Phases:** 1 single-player physics prototype, 2 VFX + animation, 3 obstacles + checkpoints, 4 solo balancing, 5 multiplayer, 6 visual polish, 7 performance and regression tests.
- The environment task (section 3) is folded in: its course, obstacles and art become the game's level.

### Task 3 decisions
- Namespace `FartOnline.Game` (runtime), `FartOnline.GameEditor` (editor). No asmdefs (everything is in Assembly-CSharp).
- Input is network-agnostic: `PlayerInputState` (push direction in raft space plus sequence counters for tap, charge and brake edges). `IThrustInputSource` covers local, network and AI sources.
- **Push-direction semantics:** "ThrustLeft" pushes the RAFT left, and the gas leaves to the right.
- Seat layouts by crew size (`RaftSeatManager`): solo = centre-front (0, 0.75), duo = left/right, trio = front plus rear corners, quad = corners.
- Physics: `SharedRaftController` sums the seat forces and applies the combined force and torque once per FixedUpdate (authority only). Sustained thrust uses ForceMode.Force; tap, charged and brake bursts use one impulse.
- Characters: `PF_Pig` reused, parented to seats with no colliders. `CharacterThrustAnimator` adds a procedural sit and lean on top of the Idle clip, plus face presets.
- Networking plan: Netcode for GameObjects + Unity Transport, host-authoritative, direct IP. Relay and Lobby are NOT possible because the project is not linked to UGS (the editor log shows 401 on the project ID).
- **MCP bridge after an editor relaunch:** `Assets/Game/Scripts/Editor/McpBridgeKeepAlive.cs` now starts the bridge automatically after every domain reload. If MCP is down, focus Unity (or touch a script to force a reload).

### USER DESIGN CHANGE (2026-10-30 ~08:00, overrides the original prompt's raft/controls)
"make the raft circular not rectangular or square and player can change their position by left right keys A and D and F key will be used for fart as thrust. in the circular craft the players can slightly change their position and accordingly the thrust will work."
Implemented as:
- **Round raft** (`RaftTuning.raftRadius` 2.4 m), players sit on a circle (`seatRadius` 1.65 m) **facing the centre**. Seat position = rim angle (degrees clockwise from the bow: 0 front, 90 right, 180 back).
- **A/D** walk along the rim (`seatMoveSpeed` 95°/s, half speed while farting; D = character's right = angle decreases). Players cannot pass each other (`minSeatSpacing` 42°).
- **F** = fart: press = puff impulse (Tap), hold = sustained stream after `sustainDelay` 0.18 s. Push direction = from the seat toward the centre (gas goes outward behind the player), so sitting at the back pushes forward, on the right pushes left. `RimMath` in ThrustTypes.cs has Outward/Push/AngleForPush.
- Extra (kept from the prompt): Shift hold+release = charged fart, S = brake burst, R = reset to checkpoint (when stuck), Esc = pause. KeyboardRight scheme: ←/→, Enter, RShift, ↓, Backspace, P. Gamepad: left stick/D-pad, South, RT, East/LT, North, Start.
- `PlayerInputState` is now {move, fartHeld, charging, tapSeq, chargeSeq, brakeSeq}; `SeatThrustState` has angle + moving (replicate these for multiplayer).
- Thrust passes through the centre, so it causes no yaw by design; rotation comes from collisions and the current. This is documented as a deliberate consequence of the user's design.

### Task 3 progress
- [x] Physics core written and compiled: `Assets/Game/Scripts/Physics/{ThrustTypes,RaftTuning,FartResourceController,FartThrustController,SharedRaftController,RaftRecoveryManager}.cs` and `Players/{PlayerSeatController,RaftSeatManager,CharacterThrustAnimator}.cs`. `Environment/.../Water/WaterCurrentController.cs` added; `RaftBuoyancy` multiplies flow by it.
- [x] Course triggers now go through `Obstacles/ICourseRaft.cs` (TestRaft and RaftRecoveryManager implement it). `Checkpoint.AnyActivated`, `FinishGate.AnyFinished`, `SetActivated`/`Celebrate`/`ResetGate`; HazardZone calls `Wipeout`.
- [x] `GameFlow/{GameRunManager,CheckpointManager,GameSession}.cs`, `Input/LocalPlayerInput.cs` + `Assets/Game/Settings/RaftGameplay.inputactions` (schemes KeyboardLeft/KeyboardRight/Gamepad), `Players/AICrewInput.cs`, `VFX/{FartBurstVFX,FartVFXPool,FartFeedbackController}.cs`, shader `Assets/Game/VFX/Fart/GasCloud.shader`, `Audio/{GameAudio,ProceduralAudio}.cs` (synthesised placeholder clips), `Camera/RaftCameraShake.cs`, `Physics/RaftDebugGizmos.cs`.
- [x] `Editor/GameBuilder.cs`: menu *Tools > Fart Online > Build Everything* generates RaftTuning.asset, gas materials/meshes, `PF_FartBurstVFX` + `Variants/VFX_Fart{Normal,Tap,Charged,Brake,Side,Cooldown}`, `Characters/PF_PlayerSeat.prefab`, `Raft/Prefabs/PF_SharedRaft.prefab`, and the scene **`Assets/Game/Scenes/RaftCourse_Game.unity`** (copy of the environment scene, TestRaft removed, `Game/SharedRaft` + `Game/GameSystems`). Extension hook `GameBuilder.GameSceneExtras` for HUD/network builders.
- [x] **Phase 1 verified:** `Tests/PhysicsSelfTest.cs` (menu *Tools > Fart Online > Tests > Run Physics Self-Test*): **17/17 PASS** (report `Assets/Game/Tests/Results/PhysicsSelfTest.md`). Lesson: components read by SharedRaftController (DefaultExecutionOrder -50) must use lazy GetComponent properties.
- [x] **Round raft verified:** PhysicsSelfTest **19/19 PASS** (back seat → +2.97 m/s forward, 0 yaw; D walks −47° in 0.5 s; right seat pushes left; front seat reverses; two at the back combine; opposite seats cancel; spacing 42° enforced; diagonal; tap/charged/brake; gas exhaustion; canyon wall impact; fall wipeout → respawn; capsize).
- [x] **Phase 2 VFX fixed and verified** (`Tests/VisualProbe.cs`, menu *Run Visual Probe*; screenshots `ArtSource/Game/Previews/`). Gas was rendering black: particle meshes must have ONLY position/normal/uv (extra vertex colours/tangents/uv2 replace the per-particle colour stream), and GPU instancing is off on gas renderers. Four players firing: ~590 live particles, ~3.7 ms per frame in the editor.
- [x] Seated pose: the pig's legs are mostly inside its round belly, so a sit barely shows. Using a splayed thigh pose (thighPitch −75, splay 40, shin 20); crew scale 0.72. Known limitation.
- [x] **HUD + menus** (`UI/UiKit.cs`, `UI/GameplayHUD.cs`, added by GameBuilder): main menu (Play Solo, Assist toggle, AI crew 0–3, Local Co-op 2/4, Host Online, address + Join, Controls, Quit), timer/CP/progress, player cards (gas, state), radar (seat dots seen from the camera + push arrow), banners, tips, pause, results, lobby, controls screen. Verified with `Tests/HudProbe.cs` (menu *Run HUD + AI Probe*): 2 AI crew moved the raft 79 m in 25 s, 0 wipeouts.
- [x] **Phase 5 multiplayer DONE (Netcode for GameObjects 2.13.3 + Unity Transport, direct IP, port 7777):**
  - `Multiplayer/NetworkTypes.cs` (NetInput 5 B, NetSeatInfo, NetSeatState, NetRunState, NetBurst; memcpy-serialised).
  - `Multiplayer/NetworkRaftController.cs` lives on the spawned prefab **`Assets/Game/Raft/Prefabs/PF_NetRaftSync.prefab`** (registered in `Assets/Game/Settings/NetworkPrefabs.asset`). The host spawns it and it finds the scene raft. Server-authoritative: the raft pose goes through a NetworkVariable and clients interpolate 100 ms behind server time (kinematic body); seat info, seat states and run state are NetworkVariables; bursts and impacts are RPCs; input is an unreliable RPC with sequence counters and a 0.1 s keep-alive; there's a run-command RPC (reset / restart, `RestartRule` HostOnly|AnyPlayer).
  - **LESSONS:** (1) An in-scene NetworkObject with `EnableSceneManagement=false` is NOT matched on clients: the client destroyed its raft. So the scene raft has NO NetworkObject; replication goes through the spawned proxy. (2) `NetworkRigidbody` forces the body kinematic until spawned, which broke offline play; it's not used. (3) NetworkManager must be a scene ROOT object, otherwise a modal "Invalid Nested NetworkManager" dialog blocks the editor (and MCP).
  - `Multiplayer/NetworkSessionManager.cs`: Host/Join/Leave, connection approval (max 4, name in the payload, "The raft is full (4 players)"), seat by clientId, client leave frees the seat and the run continues, host leave sends every client to `Disconnected` (no host migration), status line with ping, HUD callbacks. `listenAddress` 0.0.0.0 by default (127.0.0.1 for tests, avoiding the firewall popup).
  - Online pause = local overlay only (`GameplayHUD.ToggleLocalPause`); restart/reset from clients goes through an RPC.
  - **Test harness** `Multiplayer/NetAutoTest.cs` (args `-fo-net host|join -fo-listen -fo-addr -fo-port -fo-wait N -fo-secs -fo-pattern back|right|idle -fo-delay ms -fo-drop p -fo-log file`). Player build: menu *Tools > Fart Online > Build Windows Player (development)* → `D:\FOBuild\TootRaft.exe` (~25 s incremental). Tests run headless: `TootRaft.exe -batchmode -nographics -logFile x -fo-net host ...`.
  - **Verified with real processes (logs in ArtSource/Game/NetTests/net2..net5):** (a) 2 players: client input reaches the server, the client's raft pose matches the host's, about 30 poses/s, bursts replicated, RTT 33 ms on loopback, client leave → host continues; (b) 3 players + host quits → both clients "Disconnected due to host shutting down" (bug fixed: clients' state now ends in `Disconnected`); (c) client with 150 ms input delay + 10% input loss: its farts still drive the raft (~49 m in 17 s); (d) host + 4 clients: 4 seated, the 5th rejected "The raft is full (4 players)"; seat spacing keeps the crowd at 117/159/201/243°. No exceptions in any player log.
  - Not tested: real internet/NAT (direct IP needs port forwarding; Relay unavailable without UGS), server→client packet loss (the NGO transport simulator is deprecated and needs the Multiplayer Tools package), long sessions (>1 min).
- [x] **Docs written:** `Assets/Game/Documentation/{Project_Setup_Guide,Physics_Tuning_Guide,Multiplayer_Architecture,Test_Report}.md` and `Assets/Environment/Documentation/{Environment_README,Environment_Validation}.md`.
- [x] **Course fixes from the traversal tests:**
  - (1) Section E gap was about 15 m dry (a river segment is wet only if BOTH ends are wet). `CourseLayout` now has the dry point at z 293.5 flanked by wet 290 and 297 (dry span about 7 m). `RaftBuoyancy.fastWaterGrip` 0.3 makes the chute carry the raft. `Tests/JumpProbe.cs`: 6/6 land.
  - (2) Canyon: `LowBeam_1` is now `OverheadBeam_1` (full width, bottom 1.5 m; Obstacle G); `LowBeam_2` reach is 40% of the width.
  - The environment scene was regenerated (Reset Layout, Dressing, Obstacles), then the game scene.
- [x] **Course run** (`Tests/CourseRunProbe.cs`, menus *Run Course Run (AI crew 1 and 4)* / *(AI crew 4 only)*): solo AI FINISHED in 185 s with 0 wipeouts; 4 AI FINISHED in 100 s with 0 wipeouts. Physics self-test still 19/19 after all changes.
- [x] Gas streams blowing toward the chase camera fade to 40% (`FartFeedbackController.cameraFacingOpacity`).
- [ ] REMAINING (optional / polish):
  - (a) DONE 10:30: `Assets/Environment/Scripts/Editor/LightingBuilder.cs` (menu *Tools > Tropical Course > Apply Lighting and Atmosphere*; profile `Assets/Environment/Settings/PP_TropicalCourse.asset`, sky `M_Sky_Tropical`). Bloom clamp 3 / threshold 1.3, camera stopNaN. Shader NaN fix (SafeNormalize in Waterfall, StylizedSurface, StylizedWater). **LESSON: never `GetComponent<T>() ?? AddComponent<T>()` in editor code: a missing component is a fake null.** Re-apply lighting before *Build Game Scene* if the environment scene is regenerated by *Build Graybox Scene*.
  - (b) Chase camera tuning for the round raft (the camera follows the river; consider a slightly higher, closer framing).
  - (c) Soak and regression: repeated restarts, long online session, real gamepads.
  - (d) Jump balancing: even a drift clears the gap now; only a slowed raft fails.
  - (e) Rebuild the player build after any change (menu) and re-run the NetAutoTest commands (see the multiplayer section).
  - (f) Old open item: pig Play Mode animation check.
- Polish backlog: gas streams from back-seated players blow toward the chase camera and can hide the view (reduce stream size/opacity near the camera); the Game view is small in screenshots (880×406).

## 3c. Task 4 (DONE 2026-10-30 23:45): UI redesign and sound packs

The user asked: "redesign the UI via unity mcp ... the assets are in `C:\Users\Ayush\Downloads\DEMO_Cozy_UI_Pack_doboui` and `C:\Users\Ayush\Downloads\Free_version` ... also use sound packs find free sfx on internet and use that."

- **Licences**
  - Both sprite packs are free or demo versions and are **not cleared for commercial use** (see `Assets/Game/UI/UI_CREDITS.md`).
  - Every sound is CC0 from OpenGameArt (`Assets/Game/Audio/AUDIO_CREDITS.md`). Kenney's own site blocks scripted downloads; OpenGameArt hosts the same packs.
  - Fonts are SIL OFL: Lilita One, and Fredoka SemiBold (a static instance cut from the variable font with fontTools).
- **Skin assets and import**
  - Sprites are renamed copies in `Assets/Game/UI/Resources/UI/*.png` (Panel_Main, Pill_Brown, Btn_Green_*, Btn_Red_*, Btn_Wood, Btn_White, Plank_Wide, Note_Paper, Container1, Slot, Badge_Green, Circle_On/Off, Arrow_A/B, Icon_*, …), with the fonts in `Fonts/`.
  - `Scripts/Editor/UiAudioImport.cs` (an AssetPostprocessor) sets the sprite import settings and the **9-slice borders table**.
  - It also sets audio import: music and the river loop stream as Vorbis, SFX are forced to mono.
  - Menu *Tools > Fart Online > Reimport UI and Audio* re-applies the rules.
- **`UI/UiKit.cs` (rewritten)**
  - Skin helpers: `S(name)` sprite loader; `Skin()` 9-slice with a border scale.
  - Panels and controls: `Board()` wooden panel with a title tab and pop-in; `Button()` in Green/Red/Wood/Cream styles with hover and press scale and sounds; `IconButton`; `Selector` (arrow buttons); `Slider`; `KeyCap`; `Note`.
  - Fill bars: `UiBar` with rounded fills (white capsule tinted, because tinting the brown fill sprite looks olive).
  - Motion components: `UiButtonFx`, `UiPop`, `UiPunch`, `UiIdle`, plus a generated sunburst.
- **`UI/GameplayHUD.cs` (rebuilt, same public API)**
  - Title screen: logo on the left, menu board on the right.
  - HUD: timer pill, CP badge, progress bar with knob, wooden player cards with gas bars, radar disc, punch banners, parchment tips and prompts, pause button.
  - Other screens: pause; settings (master, music and SFX volume, saved in PlayerPrefs); how-to-play keycap table; results (time pill, stat chips, per-player rows); lobby (4 seat slots). The HUD hides on results.
- **Audio**
  - `Audio/GameAudio.cs` loads clips per cue from `Resources/SFX/<Cue>/`, falling back to ProceduralAudio. New cues: UIHover, UIBack, UIOpen.
  - Music crossfades between the menu ukulele track and the gameplay tropical loop (`SetMusic`). The river ambience bed is switched with `SetAmbience`.
  - Each seat has a held fart stream (`FartStream()`), called from `FartFeedbackController.Update`. The Sustained burst no longer plays a one-shot.
  - Volumes: `MasterVolume`, `MusicVolume`, `SfxVolume`.
  - `Audio/ObstacleWhoosh.cs` is auto-attached to pendulums and sweepers. The HUD now plays the checkpoint jingle.
- **Verification**
  - `Tests/UiProbe.cs` (menu *Tools > Fart Online > Tests > Run UI Probe (screens at 1080p)*) writes `ArtSource/Game/Previews/UI/30..37_*.png`. All screens checked.
  - Runtime check: the menu music crossfades to gameplay music, the river bed plays, recorded farts play, and 3 whoosh trackers attach. No console errors.
  - Player rebuilt (222 MB, 0 errors). Net loopback with host and client OK (31 poses/s, no exceptions).
- **Lessons**
  - In URP, `Camera.Render()` ignores camera stacks, and a base camera with clear "Nothing" still clears. The probe therefore renders the UI over black and over white and composites it by difference matting in linear space.
  - Recompiling in Play Mode wipes the HUD's non-serialized references (NullReference spam in `GameplayHUD.Update`). Stop Play Mode before `refresh_unity`.
- **Follow-ups**
  - Listen-test the chosen clips: they were picked by name and length, not by ear. Swap within the same packs if needed.
  - Buy the full UI packs before any commercial release.

## 3d. Task 5 (2026-10-31): endless runner "Fart Online"

The user asked: "add more obstacles and make the game as infinite running. the obstacles should be more and when the raft will collide with obstacle the game will be over. make sure every route should have a possible way to pass the obstacle and the game name is Fart online not toot raft".

- **Full design doc:** `Assets/Game/Documentation/Endless_Mode.md` (read it first).
- **Scene:** `Assets/Game/Scenes/FartOnline_Endless.unity` (first in the build). Menu *Tools > Fart Online > Build Endless Game (prefabs + scene)* copies the game scene, strips the course, adds `EndlessWorld/EndlessRiver`, sets productName "Fart Online", and puts the scene first in the build. The player builds `D:\FOBuild\FartOnline.exe` from the endless scene.
- **Code**
  - `Scripts/Endless/{EndlessRiver,ObstaclePlanner,EndlessHazard}.cs` and `Scripts/Editor/EndlessBuilder.cs` (11 obstacle prefabs in `Assets/Game/Endless/Prefabs`).
  - `MeshKit.cs` (with `Noise`) and `RiverTerrainBuilder.cs` moved to `Assets/Environment/Scripts/Shared` (now runtime; asset saving is `#if UNITY_EDITOR`).
  - `RiverPath`: `distanceOffset`, `TrimFront`, `DistanceAtT`, `StartDistance`, empty-path guards.
  - `KinematicObstacle.ClockSource` and `ApplyPoseNow`.
  - `GameRunManager`: `RunState.GameOver` (appended), `endless`, `RunSeed`, `Stats.score`, `crashReason`, `BestScore` (PlayerPrefs `fo.endless.best`), `ObstacleClock`, `ApplyReplicatedEndless`. Start builds a menu river.
  - `RaftRecoveryManager`: `endlessMode`, `Crash()`, `Crashed` (EndlessHazard contact or any wipeout).
  - `NetRunState` gained `seed` and `score`.
  - `AICrewInput` follows `EndlessRiver.SafeLaneX`.
  - HUD: distance, best and milestone banners, game over board, "FART ONLINE" logo, endless copy.
- **Tests:** `Tests/EndlessProbe.cs` (menu *Run Endless Probe*): physical sweep of the safe path plus AI runs. Report in `Assets/Game/Tests/Results/EndlessProbe.md`. `GameTestMenu.Launch(test, scenePath)`: the UI and endless probes use the endless scene; the older tests still use the course scene.
- **Results (final, with the pack):** see `Assets/Game/Tests/Results/EndlessProbe.md`.
- **Lesson:** an endless scene starts with an empty RiverPath, so every query must handle fewer than 2 samples.
- **Obstacle pack (user request, 2026-10-31):** "Hyper-Casual Obstacle Pack" (EnsArts) from the Asset Store cache, `%APPDATA%/Unity/Asset Store-5.x/EnsArts/3D ModelsProps/Hyper-Casual Obstacle Pack.unitypackage`.
  - Only `Assets/Obstacle Pack` was imported, by unpacking the .unitypackage with its .meta files. The package also contains a `Packages/manifest.json` that would overwrite ours; it was deliberately skipped. A backup of our manifest is in the session scratchpad.
  - Materials are already URP/Lit.
  - The mapping table is in `Endless_Mode.md`. `EndlessRiver.Fit/Stretch/RopeFence` place the models; new planner kinds are Press, SpikeTrap and Buzzsaw. Density was raised (rows 30 m down to 19 m, more fill and moving obstacles).
- **Lessons**
  - Pack colliders can be bigger than their meshes, so fit them using renderer and collider bounds.
  - Destroyed chunks keep their colliders until the end of the frame. `DestroyChunk` deactivates them first; the probe found this as false violations on the first seed.
  - The crash check aims fences at their tip, because a fence's middle can be against the bank, where the raft runs aground.
  - Launching a test while Play Mode is running cancels the launch: stop Play Mode first.
  - Probes now save and restore the player's best score (`fo.endless.best`).

## 3e. Task 6 (2026-10-31): mobile/tablet responsive + web app

The user asked: "make the game mobile and tab responsive and compile it for web app."

- **Full doc:** `Assets/Game/Documentation/Web_and_Mobile.md`.
- **Code**
  - `Input/TouchInput.cs`: on-screen hold buttons; feeds `TouchInput.Owner`, the first LocalPlayerInput.
  - `GameplayHUD.UpdateResponsive` adapts the canvas scaler (1920x1080 or 1080x1920 reference, matching the limiting side) and lays out the menu, HUD, boards and touch controls for portrait or landscape.
  - The web build hides online play (`IsWeb`), and Quit becomes Fullscreen. Settings gained Touch buttons (Auto/On/Off) and Fullscreen.
  - `UiPop.SetBase` and `UiIdle.SetBasePosition` let layouts move animated elements.
  - `RaftChaseCamera` widens its FOV and pulls back on tall screens.
  - `GameFlow/PlatformBoot.cs` forces the Mobile quality level on WebGL.
- **Web**
  - `Scripts/Editor/WebBuilder.cs`, menu *Tools > Fart Online > Build Web App (WebGL)*, builds to `D:\FOBuild\Web`: gzip with decompression fallback, data caching.
  - Template `Assets/WebGLTemplates/FartOnline`: PWA manifest, service worker, icons rendered from the UI, loading screen, full-window canvas.
  - Move `*_BurstDebugInformation_DoNotShip` out of the output folder before deploying.
- **Web-only fixes (lessons)**
  1. Additional-light shadows are now off on both pipeline assets. Their placeholder shadow map made WebGL reject every lit custom-shader draw, so the canyon, raft and pigs were invisible.
  2. The Mobile pipeline upscaling filter is Linear (Auto picked FSR).
  3. `Mobile_Renderer` uses `Assets/Settings/PostProcessData_Web.asset` (EASU swapped for FinalPost), because URP turns off all post-processing when any shader in that data is unsupported.
- **Tests**
  - *Run Responsive Probe* (phone, tablet and desktop renders in `ArtSource/Game/Previews/Responsive`).
  - A puppeteer-core plus Chrome harness (scratchpad `webtest/`), serving with `python -m http.server 18765` (port 8765 is taken by another app on this PC).
- **Caveats**
  - The editor's active build target is now **WebGL**. Switch back to Windows (Build Profiles) before the desktop player build or the desktop-scene tests.
  - WebGL builds take about 35 to 45 minutes.

## 3f. Task 7 (2026-10-31): animated obstacles, curvy river, smooth fart physics

The user asked for obstacles similar to the *Fart Together* trailer, a river that changes direction, and smooth fart force.

- **Research:** trailer frames were pulled from Steam's DASH segments (per-segment MP4s, contact sheets made with puppeteer). The reference is in the session scratchpad `ref/`.
- **What was built:** see `Assets/Game/Documentation/Endless_Mode.md`, section "Animated hazards and river shape".
- **Lesson:** keep one MonoBehaviour per file, named after the class.
- **Probe results with the 62-degree bends:** layout 0 violations (5 seeds); AI 3 and AI 1 reached their caps; crash check 13/13 (new kinds included).
- **Final results:**
  - Probe with the 85-degree bends: 0 violations, AI caps reached, 13/13 crash checks.
  - Physics self-test: 19/19.
  - Web rebuilt (`D:\FOBuild\Web`) and checked in Chrome.
- **Not yet done:** rebuild the desktop player (switch the target to Windows first).

## 3g. Task 8 (2026-10-31): "make the obstacles more dangerous and implement the multiplayer functionality"

- **Hazards**
  - New kinds:
    - FireJet: `FireJetObstacle`, bank flamethrower, warning pilot, then a 1.5 s jet trigger.
    - Whirlpool: `WhirlpoolPull` plus `SpinObstacle`; pulls the raft into a spiked grinder.
    - Mortar: `MortarObstacle`, bank cannon, red target ring, ball and splash triggers.
  - Prefabs come from `AnimatedObstacleBuilder` (`PF_Obs_FireJet/Whirlpool/Mortar`). Glow materials `M_Glow_*` use URP Unlit with HDR colour.
  - Danger ramp: `ObstaclePlanner.HazardSpeed` up to 1.8x, `Overdrive` past 3 km (rows 16 m apart, 80% moving), HUD "DANGER LEVEL n" every 500 m.
  - The pack flamethrowers were removed from the bank scenery.
  - See `Endless_Mode.md`, "Deadlier hazards".
- **Probe:** layout 0 violations on 5 seeds to 4.2 km (triggers and whirlpool pull included); AI 3 reached 900 m and AI 1 reached 600 m; crash check 16/16.
  - The fire and whirlpool sizes were reduced slightly after this probe run (more of them get placed). The probe must be re-run.
- **Multiplayer**
  - Changed files and what they do:
    - `NetworkSessionManager`: `useWebSockets` (default on), `HostAddress`/`HostRoomCode`/`SessionInfo`, `PlayerName` in PlayerPrefs.
    - `Multiplayer/RoomCode.cs`: room codes, e.g. 127.0.0.1 = `B9S-AAAB`.
    - `NetRunState.crash`: replicates the crash reason.
    - HUD: name box, web shows Join (no Host), the address is pre-filled from the page URL, lobby join-info pill, game over board online (host: Play Again/Lobby; client: Waiting/Leave).
    - `NetAutoTest`: `-fo-restart`, reads URL params on WebGL.
  - Test: `Tools/net_endless_test.sh` passes (see `Multiplayer_Architecture.md`, logs `ArtSource/Game/NetTests/net7_endless_ws`).
  - Desktop player rebuilt (`D:\FOBuild\FartOnline.exe`, WebSockets).
- **Final results:**
  - Probe re-run after the size tweak: 0 violations on 5 seeds, AI 3 reached 900 m, AI 1 reached 600 m, crash check 16/16.
  - The web app was rebuilt (`D:\FOBuild\Web`, 1635 s).
  - Browser join test passed (`ArtSource/Game/NetTests/net8_web_client`).
- **Notes**
  - Whirlpools are still rare (about 10 per 4 km) because they need a lot of room.
  - Endless mode has no "stranded" timeout. A raft beached on a bank stays until the crew farts off it (seen with the test bots).
- The editor's active build target is WebGL; desktop builds switch automatically, but slowly.

## 3h. Task 9 (2026-11-05): harder, bright cartoon look, mobile controller

The user shared two reference images (a bright cartoon rafting river with red spiked rollers, spiked sea mines and a bridge with hanging spiked logs) and asked: "make difficulty harder like these images ... make the game aesthetic like that. add mobile controller button."

- **Full notes:** `Endless_Mode.md`, section "Spiked logs, harder ramp and the bright look".
- **New code**
  - `Scripts/Editor/SpikeHazardBuilder.cs`: prefabs PF_Obs_SpikeSwing, PF_Obs_SpikeMine, PF_Obs_SpikeLogRoll, PF_Obs_SpikeBridge and PF_Obs_SpikeCurtain, plus the decor PF_Deco_CliffBlocks_A/B/C, PF_Deco_Fence and PF_Deco_CascadeSplash.
  - `Environment/Scripts/Obstacles/VisualSpin.cs`.
  - `Scripts/Editor/BrightLookBuilder.cs`.
  - `Input/TouchJoystick.cs`.
  - `Tests/LookProbe.cs` (menu *Run Look Probe*, screenshots in `ArtSource/Game/Previews/Look`).
- **Changed code**
  - `ObstaclePlanner`: 4 new kinds appended (SpikeSwing, MineField, SpikeLogRoll, SpikeBridge), a harder curve with full difficulty at 1.5 km, and up to 3 obstacles per side.
  - `EndlessRiver`: spawning for the new kinds, plus `Decorate` and `Cascade`.
  - `EndlessBuilder.WireSpikesAndDecor`, and the menu *Endless > Rebuild Spiked Hazards + Decor (in place)*.
  - `GameBuilder`: `RaftMesh` is now a plank deck, `AddRaftTube` adds the inflatable ring, and there's the menu *Restyle Raft (inflatable ring)*.
  - `LightingBuilder.Apply` chains the bright look; `SkyPath` and `ProfilePath` are now public.
  - HUD: the thumbstick replaces the walk arrows, a PAD toggle button sits next to pause, and the status pill moved to x = -230.
  - `TouchInput` Auto mode also shows the controller on web pages opened on touchscreen devices.
  - `EndlessProbe` crash check now covers 20 kinds.
- **Verified 2026-11-05**
  - Endless probe:
    - Safe path: 0 violations on 5 seeds to 4.2 km. The min gap is 6.9 m.
    - AI runs: AI 3 reached its 900 m cap and AI 1 reached its 600 m cap.
    - Crash check: 20 of 20 hazard kinds end the run.
  - Look probe screenshots (`ArtSource/Game/Previews/Look`) checked: bright palette, mines, rolling log, bridge, bank waterfalls, inflatable raft, thumbstick and PAD button.
  - The look probe's SpikeSwing close-up frames the edge of the river instead of the hazard. This is a camera placement problem in the probe only.
  - **LESSON:** stop Play Mode before any script edit or refresh. Probes leave Play Mode running, and a recompile in Play Mode breaks the run with NullReferenceExceptions.
  - The WebGL rebuild succeeded (`D:\FOBuild\Web`, 35 MB, 307 s, 0 errors). It has not been opened in a browser yet. The desktop player was not rebuilt.

## 3i. Task 10 (2026-11-05, later): tight river, deadly banks, wait gates, mobile performance

The user's feedback was in two messages:
1. "The river is too wide / too much room ... at the beach of the river there should be roller obstacles ... make it difficult ... increase the fart thrust power with smoothness."
2. "Add some vegetation on the side of the river ... add more obstacles so that sometimes the player has to wait to pass ... optimise the game, currently it's too laggy. It should run smoothly on mobile devices."

The full notes are in `Endless_Mode.md`, in the sections "Tight river, deadly banks, stronger thrust" and "Wait gates, lush banks and mobile performance". In short:
- **River and banks**
  - The river is 15 m wide (it was 18).
  - Both banks are lined with deadly spiked rollers (`PF_Obs_EdgeRoller`, `EndlessRiver.EdgeRollers`, `edgeGuard` 1.75).
  - The safe gap is the raft's diameter + 1.8 m, down to + 1.0 m (floor + 0.9).
  - `ObstaclePlanner.Plug` closes the other open water with mines.
- **Thrust:** it went up about 37%, with longer rise and fall times (`RaftTuning.asset`). The physics self-test passes 19/19.
- **Wait gates:** `PF_Obs_WaitGate` and `WaitGateObstacle`, under spike bridges past 180 m. They're open 2.6 s out of every 5.7 s.
- **Performance**
  - `ToyMerge` merges each hazard's coloured parts into one mesh.
  - Static batching per chunk, and chunks are built over two frames.
  - On the Mobile quality level: shorter build-ahead, no bloom, no pig shadows, HDR off, render scale 0.8. Low-poly bank rollers.
  - `Tests/PerfProbe.cs`: draw calls went from 827 to 327, renderers from 832 to 478.
- **AI crew:** reachable spots, crew spread and nearest-seat pushing, predictive lane keeping with fart pulses, and waiting at wait gates.
- **BUG FOUND AND FIXED:** `FartOnline_Endless.unity` had 11 river chunks and 16 path points saved into it, so every run spawned on top of an old river. Its rollers killed the raft in mid-river. The scene was cleaned, `EndlessRiver.Awake` now deletes any leftover chunks, and `RebuildSpikesInPlace` strips them before saving.
- **LESSON:** a probe leaves Play Mode on. Always check `is_playing` before stopping Play Mode or compiling, otherwise a running probe gets killed.
- **Verified (02:55)**
  - Endless probe:
    - Safe path: 0 violations on 5 seeds to 4.2 km.
    - Wait gates: 109, all with an open window at least 0.78 s longer than the raft needs.
    - AI runs: AI 3 ran the full 5-minute cap (853 m) and AI 1 reached its 600 m cap.
    - Crash check: every kind and the bank edge end the run. Whirlpools no longer fit the narrow river, so none are planned.
  - Physics self-test: 19/19.
  - Wait gate checked visually (closed and open).
  - Web app rebuilt (`D:\FOBuild\Web`, 33 MB, 0 errors), but not yet tested in a browser on a phone. The desktop player was not rebuilt.

## 3j. Task 11 (2026-11-07): phone test over adb, landscape, 60 fps, harder from the start

The user asked: "test the game in mobile device via adb and optimize it for running smoothly. and the game should be played horizontally in portrait mode, not vertically." Then, mid-task: "make the game more difficult from starting, add sharp turns, add more animated obstacles, add more traps from starting."

- **Device:** OnePlus CPH2467 (Snapdragon 695 / Adreno 619, 2400x1080 at 120 Hz, Android 15), adb serial `2d1522a1`. adb is `D:\android-sdk\platform-tools\adb.exe` (on PATH).
- **Build:** *Tools > Fart Online > Build Android APK* writes `D:\FOBuild\Android\FartOnline.apk` (about 7 min incremental). `adb install -r` it. The editor's active build target is **Android**.
- **Landscape lock:** `AndroidBuilder.Configure` and `PlatformBoot` allow landscape left and right only. I read "horizontally" as landscape. The HUD's landscape layout and safe area were checked on the phone.
- **Performance:** the full write-up is in `Web_and_Mobile.md`, section "Android build, landscape lock and phone performance".
  - Gameplay went from 45 fps (17.4 ms GPU) to **60 fps (13.9 ms GPU)**.
  - Fixes: noise-texture lookups (`MobileNoise`) replace hash noise in the FO_MOBILE shader paths; a low-poly mobile pig mesh (`PigMobileMeshBuilder`, `MobileMeshSwap`; 30.8k to 9.2k triangles); `AdaptiveResolution` (scale 0.5 to 0.8, starting at about 720p); Swappy frame pacing.
  - The Unity editor log is `Logs/Editor.log` in the project, not `%LOCALAPPDATA%`.
- **Device tools:**
  - `--es fo_auto 3`: hands-free AI run (`DeviceAutoPlay`).
  - `--es fo_diag 1`: per-feature GPU attribution during gameplay (`DeviceDiag`).
  - `adb logcat -s Unity | grep FartPerf`: fps, CPU, GPU and scale every 5 s.
- **Harder start:** see `Endless_Mode.md`, section "Hard from the start".
  - River: `maxTurnPerPoint` is 28 (in the scene too), and bends start after about 25 m.
  - Difficulty floor is 0.3. The first row is at 75 m. 72% of obstacles are animated at the start. Hazards run 1.6x faster at the start. Up to 3 traps per side.
  - Wait gates start at 150 m. The AI waits from 30 m out.
- **Verified:**
  - Endless probe layout: 0 violations on 5 seeds to 4.2 km, and 121 wait gates passable.
  - Crash check: 22/22. It now retries up to 3 rivers per kind, because sharp bends can carry a thrustless raft past small targets.
  - AI runs now crash early (AI 3 at 123 m on a wait gate, AI 1 at 61 m on a buzzsaw). The game is much harder; the AI crew is weaker than a human here.
- **Not done:** the web and desktop builds were not rebuilt with these changes.

## 3k. Task 12 (2026-11-07): accounts, friends, invites, dedicated rooms on AWS, voice chat

The user asked:
- "how can i deploy the backend on aws for playing multiplayer games";
- add friends by username;
- Supabase for user credentials, and invites to play together;
- real-time voice chat for multiplayer, using free resources;
- single player without login; multiplayer needs an account.

Their answers to my questions: they will give the Supabase URL and keys; sign-up is email + password + username; free-tier t3.micro; voice goes through the game server. LATER: **"don't do it with AWS, just give me the instructions for deployment, don't make any script for that"**. So the deploy scripts were deleted; `Backend/README.md` section 3 has manual console and SSH steps. Never run anything against their AWS account. (Their configured AWS CLI keys fail with AuthFailure anyway.)

- **Read first:** `Backend/README.md` (architecture, Supabase setup, manual AWS steps, costs, security) and `Multiplayer_Architecture.md`, last section.
- **New code:**
  - `Online/{BackendConfig,OnlineTypes,AccountService,VoiceChat,VoiceSpeaker}.cs`
  - `UI/{OnlineUI,UiHoldButton}.cs`
  - `Multiplayer/{RoomTicketVerifier,DedicatedServerBoot}.cs`
  - Editor: `{ServerBuilder,BackendSettingsMenu,OnlineSelfTest}.cs`
  - `Assets/Plugins/Concentus` (Opus, BSD licence)
  - `Backend/supabase/schema.sql`, `Backend/room-service/{server.js,package.json,.env.example}`
  - `Tools/room_local_test.sh`
- **Changed code:**
  - `NetworkSessionManager`: dedicated mode, `JoinRoom`, leader, VoiceChat attached.
  - `NetworkRaftController`: leader NetworkVariable, run commands 2 and 3.
  - `GameplayHUD`: the menu online section is now Play Online + Friends with a badge; lobby Invite button; the leader client gets the host's game-over buttons; cards show TALKING / muted and can be tapped to mute; voice setting; `Toast`.
  - `NetAutoTest`: a `room` role with `-fo-voice tone`.
  - `UiProbe` and `GameTestMenu`: the online UI probe.
- **Verified:**
  - Online self-test: all pass.
  - Local rooms, both with the Windows build and with the Linux build in WSL: create, join by code, leader start and restart, wrong code 404, no login 401, duplicate login rejected, voice relayed both ways at about 0.3% loss.
  - Online UI probe screenshots checked.
- **Next steps:**
  1. Get the Supabase URL, anon key and personal access token from the user.
  2. Run the schema through the Management API.
  3. Fill in Backend Settings (Tools > Fart Online > Online > Backend Settings).
  4. Test sign-up, login, friends and invites end to end, with two Windows players on different accounts against the local room service (TLS=0, DEV_AUTH=0, real Supabase).
  5. Rebuild the APK and test the account screens and the mic on the phone.
- **Lessons:**
  - `execute_code` (CodeDom) can't compile code that touches netstandard1.0 assemblies such as Concentus, or editor-assembly types: call editor types by reflection (`Type.GetType("...OnlineSelfTest, Assembly-CSharp-Editor")`).
  - Another app on this PC uses port 8080: use 18080 for local tests.
  - A `FartOnline.Environment` namespace shadows `System.Environment`: write `System.Environment`.
  - WSL Ubuntu can run the Linux server build from `/mnt/d` (pass the secret with `WSLENV`).
  - Long inline heredocs with quotes break the Bash tool: write scripts to a file first.


## 3l. Task 13 (2026-11-10/11): 20-second trailer video (DONE)

The user asked for a studio-style 20 s trailer with sound effects only (no voiceover).

- **Output:** `ArtSource/Trailer/FartOnline_Trailer.mp4` (1920x1080, 60 fps, H.264 + AAC, 20.0 s, ~33 MB).
- **Filming:** `Assets/Game/Tests/TrailerDirector.cs`, menu *Tools > Fart Online > Trailer > Record Footage / Record Crashes / Record Test*. Four AI pigs play the endless game; the gameplay camera renders to a 2560x1440 texture (HUD not filmed) through 9 rigs (chase, lowfront, orbit, top, side, flyby, hazard, pigface, pigrear) on a fixed 60 fps capture clock. Frames go to `ArtSource/Trailer/raw` (7,200 frames, 3.4 GB) and `rawcrash` (4,443 frames, 2.2 GB), each with a `log.csv` (frame, shot, rig, state, score, event). Crash mode disables the AI after 3.5 s so the raft drifts into a hazard, then films 170 frames at timeScale 0.35.
- **Editing:** `Tools/trailer/make_trailer.py` (renderer: grade, punch-in, captions in LilitaOne, logo end card, SFX mix, pipes to ffmpeg) and `Tools/trailer/edit.py` (the cut: EDIT segments and CUES sound list). `python Tools/trailer/make_trailer.py` renders; `preview` writes a contact sheet; `audio` only remixes the WAV.
- **ffmpeg** was installed with winget (Gyan.FFmpeg 9.0.2); it is at `%LOCALAPPDATA%/Microsoft/WinGet/Links/ffmpeg.exe` and is not on the Git Bash PATH.
- **Found while filming (not fixed):**
  1. Some hazard effect renders **magenta (missing shader)** sparks for a few frames at a time, e.g. raw frames 1150-1185, 2182, 4580-4600 (near the press / at the water line). The trailer avoids those frames.
  2. With four AI crew (no human seat) the raft **stalled at about 133-158 m for over a minute** (shots 8-27 in `raw/log.csv`) without crashing, probably at a wait gate.
- The raw footage (5.6 GB) can be deleted once the user is happy with the cut; D: had about 8 GB free afterwards.


### 3l-b. Vertical 9:16 trailer (2026-11-11, DONE)

The user then asked for a 9:16 version with cinematic shots of obstacles hitting and being survived, from the start canvas to the game over canvas.

- **Output:** `ArtSource/Trailer/FartOnline_Trailer_9x16.mp4` (1080x1920, 60 fps, 30.0 s, ~50 MB). Order: main menu with a tap on Play Solo, countdown, HUD play, five dodge shots, three slow-motion hits, a last HUD run into a barricade with the CRASH! banner and the GAME OVER board, logo card.
- **Filming:** `TrailerDirector.Vert` (menu *Tools > Fart Online > Trailer > Record Vertical 9x16*), footage in `ArtSource/Trailer/rawvert` (9,129 frames, 2.9 GB, `log.csv`). The HUD is filmed by switching its canvas to ScreenSpaceCamera on the filming camera, with `GameplayHUD.ViewportOverride` 1080x1920 and `TouchInput.ForceForTests` (portrait layout, touch buttons). Seat 1 is an AI occupied as the local player so its card reads "(you)".
- **Editing:** `TRAILER_EDIT=edit_vertical.py python Tools/trailer/make_trailer.py` (the edit file sets OUT_W/OUT_H/OUT_NAME/SRC_DIR; new segment keys `tap`, `grade`).
- The menu in the video shows the line "Online accounts are not configured in this build" (true until Supabase is set up, section 3k).
- D: had about 5 GB free after this; raw, rawcrash and rawvert together are 8.5 GB.

## 3m. Task 14 (2026-11-11): AI crew removed from the game (DONE)

The user asked: "remove the ai crew thing only the real player can play the game."

- `GameSession.StartSolo(SoloAssist)` now seats only the human (the `crew` parameter, `aiCrew`, `aiSkill` and `AddAI` are gone). `EndLocal` removes any child input driver (`IThrustInputSource`), not just bots.
- Main menu: the AI CREW selector is gone; SOLO ASSIST is centred on the board.
- Deleted `GameFlow/DeviceAutoPlay.cs` (the phone `fo_auto` AI run).
- `AICrewInput.cs` moved (same GUID) to `Assets/Game/Tests/` and is now a test-only bot. Probes and the trailer recorder use `Tests/TestCrew.StartSolo(session, assist, crew, skill)` / `TestCrew.Add(...)`.
- `FartOnline_Endless.unity` had three leftover `AICrew_2/3/4` objects and `aiCrew: 3` saved in it; removed and saved.
- Verified in Play Mode: solo run = 1 occupied seat, 0 bots, no AI CREW label, no console errors. Builds (APK, web, desktop) were NOT rebuilt.

## 3n. Task 15 (2026-11-11): git + EC2 deployment (IN PROGRESS)

- `.gitignore` added (raw trailer frames, Library, builds, Android leftovers `/Data` `/.utmp` `/build`, IDE files, `.env`). Committable size ~433 MB, largest file 48 MB. The repo must stay PRIVATE (Asset Store obstacle pack + demo UI packs).
- EC2: `ubuntu@16.170.139.212` (t3.micro, Ubuntu 26.04, 908 MB RAM, no swap, 3.3 GB disk free). SSH key `~/.ssh/nextHome.pem` in the **Ubuntu** WSL distro (the default WSL distro docker-desktop is broken: use `wsl -d Ubuntu`; from Git Bash set `MSYS_NO_PATHCONV=1`). The instance ALSO runs the user's nextHome app: nginx on 80/443 (`/etc/nginx/sites-enabled/nexthome`, `nexthome-api.duckdns.org` -> node :3000). So Caddy is NOT used; the game uses an nginx site in `/etc/nginx/conf.d/fartonline.conf` + a certbot certificate for `16-170-139-212.sslip.io`. Node 22 and certbot already installed. Repo cloned sparse (Backend only) at `~/fartonline` via deploy key alias.
- Supabase project `qsvyhyxlnxizsxbifvrq` (URL + anon key now in `FartOnlineBackend.asset`; room service URL `https://16-170-139-212.sslip.io/api`). Schema verified (tables answer 200). Email confirmation is ON.
- The instance is in **eu-north-1 (Stockholm)**, account 147303436266, `i-0e07809f236a59139` (the user's console was on us-east-1, so they couldn't see it).
- **Server set up (2026-11-11 15:20 UTC, at the user's request):** 1 GB swap; user `fartonline`; `/opt/fartonline/{server,room-service,web}` (server = the Nov 7 Linux build, uploaded by the user to `~/Server` then moved); `/etc/fartonline/rooms.env` (MAX_ROOMS=1, TLS=1, ticket secret generated on the server); `fartonline-rooms.service` enabled and running; certbot cert for `16-170-139-212.sslip.io` (registered without email); `/etc/nginx/conf.d/fartonline.conf` (80->443 redirect, `/api/` -> :8080, `/` -> /opt/fartonline/web, `listen 7800 ssl` -> :17800 WebSocket). `https://16-170-139-212.sslip.io/api/health` returns ok from outside. nextHome unaffected (its root returns 404 directly from :3000 too).
- **Website LIVE (2026-11-11 22:10 IST): https://16-170-139-212.sslip.io/** — source in `Website/` (see `Website/README.md`): landing page (hero loop from clean raw trailer frames, download cards with "best for you", how-to-play SVGs, real control table, gallery, modes, FAQ), `/play/` = WebGL build, `/downloads/FartOnline.apk` (54 MB, served as application/vnd.android.package-archive) + `/downloads/FartOnline-Windows.zip` (52 MB, release build) + `info.json`; `/api/health` drives the "Servers online" badge. New editor menus: *Build Windows Release* (`D:\FOBuild\Windows`) and *Build All for Website* (`ReleaseBuilder.cs`). Staging folder `D:\FOBuild\Site`. The new Linux server build (same code) is deployed too. nginx site updated (downloads as attachments, /assets cached 7 d; backup `/etc/nginx/fartonline.conf.bak`). Verified with Playwright + installed Chrome: the live web game loads (~80 s with software GL), menu shows, Play Solo starts a run, 0 console errors. Port 7800 now reachable (502 when no room = expected).
- **LESSONS:** builds failed with OutOfMemory / "paging file is too small" because C: had 0.2 GB free; cleaned Temp VS-installer leftovers, `~/.gradle/caches`, npm cache (C: now ~12 GB free). Never run ffmpeg encodes during a Unity build. Python on Windows writes CRLF: always `sed -i 's/\r$//'` scripts before running them on the server. WebGL with decompression fallback outputs `.unityweb` (not `.gz`).
- **Moving the website to Vercel (user request, 2026-11-11 evening):** deploy through the Vercel DASHBOARD from GitHub (no CLI), Root Directory `Website`, preset Other, no build. `Website/` now contains EVERYTHING deployed: page, `play/` (WebGL build with `<base href="/play/">` added), `downloads/` (APK + Windows zip + info.json; `.gitignore` has `!/Website/downloads/*.apk`), `vercel.json` (download headers, `/api/*` rewrite to the EC2 room service). Committed locally; **the user pushes it themselves** (they declined my push). The EC2 nginx copy of the site still works but is no longer the main site.
- **Supabase email links went to http://localhost:3000** (default Site URL). Fix = Authentication > URL Configuration: Site URL = the Vercel URL, Redirect URLs += `https://<vercel>/**` (needs the user's dashboard or a personal access token). The site's `js/site.js` now handles the redirect hash: "Email confirmed!" or an expired-link message with a "Send me a new link" form (`/auth/v1/resend`). **Supabase's built-in mailer only delivers to the project's team members (~2/hour)**: public players need custom SMTP or "Confirm email" turned OFF. Deleting an unconfirmed user cascades to the profile (frees the username).
- **Remaining (older):** port 7800 was blocked by the security group (now open) (the user must add Custom TCP 7800 from 0.0.0.0/0 in eu-north-1); check whether 16.170.139.212 is an Elastic IP; pending reboot ("System restart required"); rebuild Linux server + APK + web together and upload web to /opt/fartonline/web.

## 4. Next step (environment task; superseded by 3b for ordering)

1. **Unity status (04:33):**
   - The D3D12 editor crashed with GPU device removal (04:15 during the captures, and twice more on restart at 04:27). Unity was on the RTX 4050, not the iGPU.
   - The editor was relaunched with **`-force-d3d11`**: `"C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe" -projectPath D:\unityProjects\fartonline -force-d3d11`. It loaded fine (`SampleScene` open, no compile errors), but the **MCP for Unity bridge did not reconnect**. The user needs to focus the editor and, if needed, use *Window > MCP for Unity > Start / Connect*.
   - `EnvironmentCapture` no longer forces synchronous shader compilation (it renders warm-up frames, 960x540 with 2× MSAA). `StylizedSurface` rock bump now uses screen-space derivatives (cheaper).
2. Refresh and compile (the Phase 6 scripts, ObstacleBuilder and shader changes are not compiled yet), then fix errors. Then open `Assets/Scenes/TropicalRaftCourse.unity`.
3. Run *Rebuild Dressing and Background* to apply the hole fix, then check the overview.
4. Run `ObstacleBuilder.Build()` (*Rebuild Obstacles*) and check it visually. Plan as implemented:
   - **A:** wooden start gate, start dock beside the spawn, checkpoint 0.
   - **B:** narrow canyon gate (posts leaving about 6.5 m), two half-width low beams alternating sides (slalom), checkpoint.
   - **C:** big central boulder (passages of at least 9 m each side), checkpoint.
   - **D:** rotating sweeper on a bank post at the S-curve middle (arm about 4.5 m), checkpoint.
   - **E:** striped warning posts at the chute start, kicker rock wedge at the chute lip (launches the raft over the dry gap), checkpoint in the landing basin, HazardZone under the course.
   - **F:** two pendulum logs, a moving barrier, the finish gate with FinishGate, confetti and a finish dock.
5. Then Phase 7 (lighting: sky, fog, volume, reflection probe; camera tuning), Phase 8 (optimisation and profiling), Phase 9 (full-course play-test; add an autopilot raft driver for testing), and Phase 10 (docs `Assets/Environment/Documentation/Environment_README.md` and `Environment_Validation.md`, final previews).

### Lessons from Phase 4 (don't repeat these)
- **Shader flow scrolling:** never `frac(_Time.y * perVertexSpeed)`. The editor `_Time.y` is huge, so the phase changes hundreds of times along the river (stripes). Use a time-only phase (`FlowPhase`) and let speed scale the offset distance; scroll coordinates must stay bounded.
- Per-row vertex attributes (turbulence) must be smoothed along the river, or they show as stripes.
- Waterfall foam discs looked like hard saucers because the water writes opaque colour. Impact foam is now drawn by the river shader through the `_FoamSpots` global array.
- To debug the look, render A/B variants by toggling material properties: `Tools/ucode.py <file.cs>` runs a C# snippet in the editor, and `EnvironmentCapture.Render(name, pos, look, fov, w, h)` writes `ArtSource/Environment/Previews/<name>.png`. Composite and view them with Blender's `look` tool.

### Environment code written so far (all under `Assets/Environment/Scripts/`)
- `Environment/RiverPath.cs`: centripetal Catmull-Rom spline of `RiverPoint`s (position.y = water height, width, bankFlat, wallHeight, wallRun with negative = overhang, hasWater, flowSpeed, section). Cached arc-length samples, `Closest`, `TryGetWater`, `SampleAtDistance`, and gizmos (centreline, edges, raft clearance, section labels).
- `Environment/CourseSettings.cs`: ScriptableObject at `Assets/Environment/Settings/TropicalCourseSettings.asset` (seed, scales, densities, palette).
- `Environment/TestRaft.cs`: paddling (W/S, A/D, Q/E, gamepad) and respawn. `Water/RaftBuoyancy.cs`: 5-point buoyancy plus current drag (buoyancy 22, float points y = -0.45).
- `Camera/RaftChaseCamera.cs`: damped follow, river look-ahead, spherecast wall avoidance.
- `Editor/CourseLayout.cs`: the default route (sections A–F with start and end coves); the jump gap point has `hasWater=false`.
- `Editor/MeshKit.cs` (mesh helper, `OrientRange`, `SaveAsset`, deterministic `Noise`) and `Editor/RiverTerrainBuilder.cs` (one continuous cross-section sweep for bed, banks, walls and plateau, chunked every 48 samples, with end caps; water ribbon per wet span).
- `Editor/CourseGenerator.cs`: menu *Tools > Tropical Course > Build Graybox Scene* and *Reset Layout To Default*. Builds the scene `Assets/Scenes/TropicalRaftCourse.unity` with root `TropicalRaftCourse` (River, Terrain, Water, TestRaft with 4× PF_Pig passengers, GameplayCamera, Sun, Graybox_Markers).
- `Editor/EnvironmentCapture.cs`: menu *Tools > Tropical Course > Capture Previews*, writing PNGs to `ArtSource/Environment/Previews/`.
- `Tools/playtest_raft.sh [secs] [interval]`: enters Play Mode through MCP (sets `runInBackground` at runtime), probes the raft and stops.
- `Tools/ucode.py <snippet.cs>`: runs a C# method-body file through `execute_code` (CodeDom C# 6).
- Phase 3/4 additions:
  - `Shaders/StylizedSurface.shader`, `StylizedWater.shader`, `Waterfall.shader`.
  - `Editor/MaterialLibrary.cs` (menu *Rebuild Materials*) and `Editor/WaterfallBuilder.cs`.
  - `Water/Waterfall.cs` and `Water/WaterFoamSpots.cs`.
  - `RiverTerrainBuilder` now has `SidePoints`, `WetZone` and the collision meshes.
  - A full rebuild is `MaterialLibrary.Rebuild(); CourseGenerator.BuildTerrainAndWater(); CourseGenerator.SaveScene();`. That doesn't touch the raft or camera; *Build Graybox Scene* rebuilds everything.

## 5. Log (newest last)
- 2026-10-30 03:12: Environment Phase 1 done. Project inspected, backup zip created, `Tools/umcp.py` copied into the repo, this file created.
- 2026-10-30: Environment Phase 2 done. Graybox scene built and verified (previews, clearance query, Play Mode float/current test). Fixed a variable-length terrain profile crash, a deck that floated too low, and Play Mode throttling while unfocused.
- 2026-10-30: Phase 3 done (stylized canyon shader and materials, ledges and cracks, collision meshes). Phase 4 done (water, waterfalls, foam spots). Fixed water striping (flow phase depended on per-vertex speed) and turbulence noise.
- 2026-10-30 04:25: Phase 5 props, dressing and background built (562 props, 260 background trees). Phase 6 runtime scripts written. Unity crashed (GPU device removed) during the preview capture; it was restarted, and the MCP session was waiting to reconnect.
- 2026-10-30 04:33: ObstacleBuilder written. Unity relaunched with -force-d3d11 after repeated D3D12 device-removed crashes; waiting for the MCP bridge to reconnect.
- 2026-10-30 07:00: Task 3 (the game) started. MCP bridge restored with McpBridgeKeepAlive. All environment Phase 6 code and the new physics core compile.
- 2026-10-30 07:35: Game Phase 1 done. PF_SharedRaft, seats, VFX prefabs and the game scene built; physics self-test 17/17.
- 2026-10-30 08:40: Round raft redesign (user request: A/D walk the rim, F farts), VFX fix, HUD/menus. Tests: physics 19/19, visual probe, HUD + AI probe.
- 2026-10-30 09:05: Online multiplayer implemented (NGO 2.13.3, proxy sync object) and verified with headless player builds: 2, 3 and 4 players, 5th rejected, latency/loss on input, client leave, host leave.
- 2026-10-30 09:55: Docs written; section E gap and canyon beams fixed; AI course runs finish (solo 185 s, 4-crew 100 s); physics 19/19.
- 2026-10-30 10:35: Lighting/atmosphere pass, bloom and NaN fixes, gas fades when facing the camera; player rebuilt; network regression and physics 19/19 OK.
- 2026-10-30 23:45: Task 4 done. UI redesigned (Bubble Bits + Cozy UI sprites, Lilita One / Fredoka fonts) and a CC0 audio library added (farts, splashes, wood, jingles, UI, music, river loop). UI probe screenshots, runtime audio check, player rebuild and net regression OK.
- 2026-10-31: Task 5 (endless "Fart Online") implemented: endless river, 11 obstacle types, planner with a safe-gap guarantee, game over on hit, HUD, network seed/score, rename. Layout probe 0 violations on 5 seeds.
- 2026-10-31 01:40: Hyper-Casual Obstacle Pack integrated as the obstacles and traps (3 new trap kinds, higher density). Probe: 0 violations, AI reaches caps, 9/9 crash checks. Player FartOnline.exe built; net loopback OK.
- 2026-10-31: Task 6: responsive layouts (phones and tablets, portrait and landscape), touch controls, WebGL PWA build. Fixed WebGL invisible objects (additional-light shadows) and post-processing (FSR). Chrome desktop and phone tests pass.
- 2026-10-31 04:40: Task 7: animated hazards, curvy river with drops, smooth gas physics. Probe passed (before the bend increase).
- 2026-10-31 05:10: Task 7 verified with the bigger bends; physics 19/19; web rebuilt and clean in Chrome.
- 2026-10-31 21:30: Task 8 done. Fire jets, whirlpools, cannons and the danger ramp added; multiplayer moved to WebSockets with room codes and names, endless state synced; desktop and web builds rebuilt; probe, desktop 3-player and browser-join tests pass.
- 2026-11-05: Task 9 implemented: spiked hazards, a harder ramp, the bright look, the inflatable raft and the thumbstick. Endless probe passes (0 violations, 20/20 crashes); web app rebuilt.
- 2026-11-05 02:40: Task 10 implemented: 15 m river, deadly banks, stronger thrust, wait gates, vegetation, perf pass (827 to 327 draw calls), AI crew fixes, stale scene chunks removed.
- 2026-11-07: Task 11: Android phone test over adb (OnePlus CPH2467), landscape lock, 45 to 60 fps (noise textures, mobile pig mesh, adaptive resolution), harder start with sharp turns and more animated traps; probe layout and crash checks pass.
- 2026-11-07 evening: Task 12: Supabase accounts, friends, invites, dedicated room servers + room service (AWS-ready, manual deploy guide), Opus voice chat; local tests with Windows and Linux (WSL) servers pass; waiting for Supabase keys.
- 2026-11-11: Task 14: AI crew removed from the game (menu selector, StartSolo bots, DeviceAutoPlay, leftover scene objects); bot driver kept as test-only in Assets/Game/Tests.
