# Fart Online: project setup guide

A physics raft co-op game: up to four cartoon pigs sit on the rim of **one round raft** and steer it down a tropical canyon by farting. Each fart pushes the raft from where that player sits toward the centre of the raft.

## Requirements

- Unity **6000.5.6f1** with URP 17.5 (the project uses `Assets/Settings/PC_RPAsset.asset`; the depth and opaque textures must stay enabled for the water and gas shaders).
- Packages, all in `Packages/manifest.json`: Input System 1.20, **Netcode for GameObjects 2.13.3** (with Unity Transport), uGUI.
- No Cinemachine, Splines or ProBuilder. The camera, the river spline and all meshes are custom code.

## Play it

1. Open `Assets/Game/Scenes/RaftCourse_Game.unity`. It is scene 0 in the build settings.
2. Press Play. The main menu appears.
   - **Play Solo:** one human. *Assist* cycles Off / Standard / High; *AI crew* adds 0–3 visible AI pigs.
   - **Local Co-op (2/4):** player 1 uses the left side of the keyboard, player 2 the arrow keys, and players 3 and 4 use gamepads.
   - **Host Online / Join:** direct IP on port 7777. Enter `address` or `address:port` before pressing Join.

### Controls (also on the in-game Controls screen)

| Action | Player 1 | Player 2 | Gamepad |
|---|---|---|---|
| Walk around the rim | A / D | ← / → | Left stick / D-pad |
| Fart (press = puff, hold = stream) | F | Enter | South (A) |
| Charged fart | hold L-Shift, release | hold R-Shift | hold RT, release |
| Brake burst | S | ↓ | East (B) / LT |
| Reset to checkpoint (when stuck) | R | Backspace | North (Y) |
| Pause | Esc | P | Start |

The bindings live in `Assets/Game/Settings/RaftGameplay.inputactions`: action map `RaftGameplay`, control schemes `KeyboardLeft`, `KeyboardRight` and `Gamepad`.

## Regenerating content

Everything is built by editor code and is deterministic, so you can always rebuild it.

| Menu | What it rebuilds |
|---|---|
| Tools > Tropical Course > Reset Layout To Default / Rebuild Dressing and Background / Rebuild Obstacles | The environment scene `Assets/Scenes/TropicalRaftCourse.unity`: river, canyon, water, waterfalls, props and obstacles |
| Tools > Fart Online > Build Everything (assets + game scene) | `RaftTuning.asset` (kept if it exists), gas VFX prefabs, `PF_PlayerSeat`, `PF_SharedRaft`, `PF_NetRaftSync`, `NetworkPrefabs.asset`, and the game scene, which is a fresh copy of the environment scene plus the game systems |
| Tools > Fart Online > Build Windows Player (development) | `D:\FOBuild\FartOnline.exe` |

**Order after editing the course:** run the environment menus first, then *Build Game Scene*. The game scene is overwritten, so make lasting changes in the builders (`Assets/Game/Scripts/Editor/GameBuilder.cs`, `Assets/Environment/Scripts/Editor/*`), not in the scene.

## Folder map

```
Assets/Game/
  Characters/PF_PlayerSeat.prefab        seat + thrust + gas tank + pig + cushion + gas stream
  Raft/Prefabs/PF_SharedRaft.prefab      round raft (physics, buoyancy, 4 nested seats, splash/wake, gizmos)
  Raft/Prefabs/PF_NetRaftSync.prefab     the only network prefab (replicates the raft online)
  VFX/Fart/                              GasCloud.shader, M_GasCloud, PF_FartBurstVFX, Variants/VFX_Fart{Normal,Tap,Charged,Brake,Side,Cooldown}
  Settings/                              RaftTuning.asset, RaftGameplay.inputactions, NetworkPrefabs.asset
  Scenes/RaftCourse_Game.unity
  Scripts/{Physics,Players,Input,GameFlow,VFX,Audio,Camera,UI,Multiplayer,Editor}
  Tests/                                 play-mode probes + Results/*.md
  Documentation/                         this guide, Physics_Tuning_Guide, Multiplayer_Architecture, Test_Report
Assets/Environment/                      course generator, shaders, props (see Assets/Environment/Documentation)
Assets/Characters/Pig/                   the pig character (rig, 35 clips, face controller)
```

## Main components

| Component | Role |
|---|---|
| `SharedRaftController` | The one authoritative physics owner. Moves seats along the rim, sums every seat's force and torque, and applies the total once per physics step. Also handles stabilisation, the speed limit and solo assist. |
| `FartThrustController` / `FartResourceController` | One per seat: turns input into a force or impulse at the seat; holds the gas tank, cooldowns and exhaustion. |
| `RaftSeatManager` / `PlayerSeatController` | Seats on a circle facing the centre, starting layouts, minimum spacing, occupancy (local / remote / AI). |
| `RaftBuoyancy`, `WaterCurrentController` | Multi-point buoyancy against the river spline; current drag (stronger grip in fast water). |
| `RaftRecoveryManager` | Impacts, wipeouts (capsized / stranded / fell / hazard), respawn at the last checkpoint, and the stuck prompt. |
| `GameRunManager`, `CheckpointManager`, `GameSession` | Run state machine (MainMenu, Lobby, Ready, Countdown, Playing, Checkpoint, Wipeout, Respawning, Finished, Paused, Disconnected), timer and stats; checkpoints and progress; starting solo or co-op sessions. |
| `LocalPlayerInput`, `AICrewInput` | Input sources (`IThrustInputSource`) feeding seats. |
| `FartFeedbackController`, `FartVFXPool`, `FartBurstVFX` | Gas VFX, character reactions, splashes, wake and camera shake, all driven from replicated data. |
| `GameAudio`, `ProceduralAudio` | Event-driven sound with per-cue cooldowns; placeholder clips are synthesised at startup. |
| `GameplayHUD`, `UiKit` | Code-built HUD and menus. |
| `NetworkSessionManager`, `NetworkRaftController`, `NetAutoTest` | Online co-op (see `Multiplayer_Architecture.md`). |

## Known limitations

- Online play is direct IP only. Relay and Lobby need the project linked to Unity Gaming Services, which it isn't. Internet play needs port 7777 forwarded on the host. There is no host migration.
- The audio is synthesised placeholder audio; assign real clips per cue on `GameAudio`.
- The pig's legs are mostly hidden inside its belly, so the seated pose reads as "plopped on a cushion".
- The pig character has no dedicated raft clips. Leaning, straining, shuffling and recoil are procedural over the existing clips and face rig.
