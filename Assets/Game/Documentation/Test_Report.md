# Fart Online: test report

**Date:** 2026-10-30.

**Setup:** Unity 6000.5.6f1 (D3D11 editor), URP 17.5, NGO 2.13.3, Windows 11 with an RTX 4050 laptop GPU.

**Method:** every result below comes from automated play-mode probes, either in the editor (menu *Tools > Fart Online > Tests*) or in headless Windows player builds. The raw outputs are in `Assets/Game/Tests/Results/` and `ArtSource/Game/NetTests/`.

## 1. Physics: `PhysicsSelfTest`, 19/19 PASS

Results are in `Results/PhysicsSelfTest.md`. Measured on the round raft (radius 2.4 m, 450 kg, thrust 1500 N per player):

| Check | Measured |
|---|---|
| Fart from the back → forward, no spin | +2.97 m/s forward, −0.01 m/s sideways, yaw 0.00 rad/s |
| D / A walk the rim | 180° → 133° in 0.5 s (expected ≈ 132.5°), then back to 180° |
| Fart from the right side → raft moves left | −2.65 m/s sideways |
| Fart from the front → raft moves backward | −2.43 m/s |
| Two players at the back combine | +4.30 m/s, no spin |
| Opposite players cancel | 0.14 / 0.00 m/s |
| Players can't pass each other | gap held at 42.0° |
| Diagonal seat → diagonal push | +1.85 forward, −1.75 sideways m/s |
| F tap impulse / charged burst / brake | +1.33 m/s / +5.15 m/s / 1.98 → 0.80 m/s |
| Gas exhaustion blocks thrust, then regenerates | yes / gas 0.59 after the cooldown |
| Wall collision → impact event, no reset | 1 impact, 0 wipeouts |
| Fall below the course → wipeout → respawn at the checkpoint | "fell", thrust disabled, respawned 0.36 m from the checkpoint, state Playing |
| Capsize > 75° → wipeout | "capsized" |

The suite was re-run after the multiplayer components were added; the offline result is unchanged (19/19).

## 2. VFX and animation: `VisualProbe` and `HudProbe`

Screenshots are in `ArtSource/Game/Previews/`.

**Gas clouds**
- **Issue found and fixed:** the gas rendered black. The particle meshes carried their own vertex colours, tangents and UV2, which replaced the per-particle colour stream. Particle meshes are now position/normal/UV only, and GPU instancing is off for the gas renderers.
- Now: lime puffs with toon shading and soft edges.
- **Direction is correct:** the gas leaves outward behind the farting player, opposite to the push.

**Effect variants and pooling**
- Distinct tap, charged, brake and empty ("pfft") variants.
- Pooled: 36 instances, no runtime instantiation.

**Cost:** with four players streaming, about 590 live particles in the scene (waterfalls included), editor frame 3.7 ms average and 5.2 ms worst. This is not a target-hardware profile.

**Characters**
- Seated on cushions on the rim, facing the centre.
- They lean into the push, strain while charging, recoil, waddle while walking the rim, and react to impacts, wipeouts and the finish.
- **Limitation:** the pig's legs are mostly inside its belly, so the sit pose is subtle.

**HUD:** main menu, timer and checkpoint count with progress, player gas cards with state, raft radar with push arrow, banners, tips, pause menu. All captured and checked.

## 3. Obstacles and course: `JumpProbe`, `CourseRunProbe`

**Section E jump**
- **Issue found and fixed:** the jump was impossible. The dry span was about 15 m because a river segment is dry if either end point is dry. The layout now flanks the gap with wet points (dry span about 7 m).
- **Second fix:** fast water now grips the raft harder, so the chute carries it to the lip.
- **Result:** 6/6 attempts land in the basin: drift 5.4 m/s, stream 7.3 m/s, charged 5.8 m/s.
- **Balancing note:** even a drift clears the gap. Only a raft slowed by braking or backward thrust falls short.

**Canyon (section B)**
- **Issue found and fixed:** the solo AI stalled 5 times at the slalom low beam, which was sized for the old square raft.
- Changes: `LowBeam_1` is now a full-width **overhead beam** (bottom 1.5 m vs. crew top 1.3 m: Obstacle G), and `LowBeam_2`'s reach dropped from 45% to 40% of the width.

**Full course with AI-only crews**

Recorded in `Results/CourseRun.md`; run at 3× time scale with AI skill 0.6.

| Crew | Result |
|---|---|
| 1 AI (solo case: one player's thrust only) | **FINISHED in 185 s**, 0 wipeouts, all 5 checkpoints |
| 4 AI | **FINISHED in 100 s**, 0 wipeouts |

Before the fixes, the solo run did not finish: it stopped at 288 m of 475 m with 17 wipeouts at the gap.

The course is therefore completable by one player's thrust, with no hidden propulsion. The AI crew is a visible, optional proxy, not a human play-test.

## 4. Multiplayer: headless player builds on one PC (`NetAutoTest`)

Logs are in `ArtSource/Game/NetTests/net2..net5`.

| Test | Result |
|---|---|
| 2 players (host + client) | Client approved and seated. Client input reached the server (356 inputs), client farts drained its tank on the host and moved the raft. The client's interpolated raft matched the host's (≈ (11.1, 34.9) vs (11.5, 37.2) one log tick later). About 30 poses/s, 30 burst events replicated, RTT 33 ms (loopback). |
| Client leaves | Host freed the seat; the run continued. |
| 3 players, then the host quits | Both clients: "Disconnected due to host shutting down."<br>**Bug found and fixed:** clients' state stayed `Playing` because the sync object kept mirroring during shutdown. Now `Disconnected`, with the timer frozen. |
| Client with 150 ms input delay + 10% input loss | Its farts still drove the raft (about 49 m in 17 s). The host saw its seat farting and its gas draining. |
| Host + 4 clients | 4 seated; the 5th was rejected: "The raft is full (4 players)". The seat spacing kept the crowd at 117/159/201/243°. |
| Errors | No exceptions in any of the player logs. |

**Issues found and fixed during the multiplayer work:**
- In-scene NetworkObject not matched without scene management → replaced by the spawned `PF_NetRaftSync` proxy.
- `NetworkRigidbody` kept the raft kinematic offline → not used.
- Nested NetworkManager blocked the editor with a modal dialog → NetworkManager is now a scene root object.

## 5. Visual pass

- The lighting and atmosphere pass is applied (sky, haze, post volume, reflection probe).
- **Issue found and fixed:** bloom blew the canyon out to white, because extreme HDR values from the custom shaders were spreading. Bloom is now clamped (max 3, threshold 1.3) and the camera has Stop NaN on.
- **Issue found and fixed:** black slivers came from NaN normals in the waterfall shader (degenerate edge triangles) and the rock bump (grazing angles). These now use `SafeNormalize`.
- Gas streams blowing toward the chase camera fade to 40% opacity, so the river ahead stays visible.
- Final Game-view captures: `ArtSource/Game/Previews/sheet_lit2.png`.

## 6. Regression after all changes

- `PhysicsSelfTest`: 19/19.
- Player rebuilt (209 MB). 2-player network run passes with no exceptions; logs in `ArtSource/Game/NetTests/net6_regression`.

## 7. Not tested

- Real internet / NAT play.
- Server→client packet loss.
- Sessions longer than about 1 minute.
- Repeated level restarts in a soak loop.
- Local co-op with physical gamepads.
- A target-hardware performance profile.
- Human play-testing for feel. Tuning was checked with the scripted and AI probes above only.
