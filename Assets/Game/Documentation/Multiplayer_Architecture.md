# Fart Online: multiplayer architecture

**Stack:** Netcode for GameObjects (NGO) 2.13.3 + Unity Transport 2.x, using direct IP (default port 7777). It is the only networking framework in the project.

**Topology:** client-hosted and server-authoritative. The host's machine is both the server and a player, and it runs the only raft simulation.

## Ownership

| Thing | Simulated by | Clients get |
|---|---|---|
| Raft rigidbody, buoyancy, collisions | server (`SharedRaftController.authority = () => IsServer`) | pose (position, rotation, velocity, server time) in a NetworkVariable, played back 100 ms behind server time with interpolation; extrapolated up to 0.25 s if late; the body is kinematic on clients |
| Seat movement (A/D) and thrust (F) | server, from each client's input | per-seat `NetSeatState`: angle, moving, farting, push direction, gas, charge, exhausted |
| Seat occupancy and names | server | `NetSeatInfo` ×4 |
| Run state, timer, checkpoint, wipeouts, progress, countdown | server (`GameRunManager`) | `NetRunState`, mirrored through `GameRunManager.ApplyReplicated` / `CheckpointManager.ApplyReplicated` |
| Checkpoint, finish and hazard triggers | server only (`ICourseRaft.IsAuthoritative`) | flags and celebrations via the run state |
| Bursts (puff, charged, brake, empty) and impacts | server events | RPCs `BurstRpc` / `ImpactRpc`; each client spawns its own pooled VFX and sounds. **No particle data is ever sent.** |

## Objects

- **The scene raft (`Game/SharedRaft`) is a plain GameObject**, not a NetworkObject.
  - *Lesson:* with `EnableSceneManagement = false`, NGO does not match in-scene NetworkObjects, and clients destroyed their raft. Turning scene management on would reload the scene on join.
- **`PF_NetRaftSync`** is the only network prefab; it's registered in `Assets/Game/Settings/NetworkPrefabs.asset`.
  - The host spawns it in `NetworkSessionManager.Host`. On spawn, its `NetworkRaftController` finds the scene raft and takes over replication.
  - On despawn it restores offline play: authority back to local, body dynamic again.
- **`NetworkManager`** is a scene **root** object. NGO refuses nested managers with a modal editor dialog.
  - Config: `EnableSceneManagement = false`, `TickRate = 30`, `ConnectionApproval = true`, no player prefab.

## Input path

1. `LocalPlayerInput` (A/D/F...) produces a `PlayerInputState`: move −1..1, fartHeld, charging, plus three wrapping sequence counters for F presses, charge releases and brake presses.
2. A client packs it into `NetInput` (5 bytes) and sends `SubmitInputRpc` (unreliable) whenever it changes, and at least every 0.1 s as a keep-alive.
3. The server looks up the seat by the **sender's client id**, so a client can only drive its own seat, and stores the input on the seat.
4. Because presses are counters rather than flags, a lost or duplicated packet can't drop or double a fart.
5. The host's own seat reads its `LocalPlayerInput` directly.

## Thrust aggregation

The server's `SharedRaftController.FixedUpdate` does the following, once per physics step:
1. For each occupied seat, moves it along the rim (blocked by neighbours), then asks its `FartThrustController` for force and impulse at the seat's thrust origin.
2. Sums `F_total = Σ Fᵢ` and `τ_total = Σ rᵢ × Fᵢ`.
3. Applies them once.

There is no per-player speed bonus. The outcome comes from each player's position, timing and intensity.

## Transport, room codes and names (2026-10-31)

- **WebSockets** (`NetworkSessionManager.useWebSockets`, on by default) on port 7777. The web app can only join (browsers can't listen), so every platform uses the same transport and web and desktop players share one raft. Set it off for UDP (desktop only).
- **Room code** (`Multiplayer/RoomCode.cs`): the host's LAN IPv4 (plus the port, if it isn't 7777) in base 32 without look-alike letters, for example 192.168.1.23 becomes `DAL-SAJZ`. The lobby shows the code and the address. The Join box accepts a code, `address` or `address:port`. No server is involved; the code only says where the host is.
- **Player name:** the menu has a name box (saved in PlayerPrefs `fo.player.name`, up to 16 characters). It travels in the connection payload and appears on seats, cards and the lobby.

## Endless mode online

- The host picks the river seed. Clients rebuild the same river whenever the replicated seed changes, including after restarts.
- Moving obstacles run on `GameRunManager.ObstacleClock`: the host's run time, smoothed on clients. Every machine sees the same fire jets, cannon shots and spinners.
- Hazard contacts are judged on the host only. The game-over state, score and crash reason (`NetRunState.crash`) reach every client.
- **Game over board online:**
  - Host: **Play Again** starts a new river for everyone; **Lobby** takes the crew back to the lobby.
  - Clients: see "Waiting..." and can **Leave**.
- Players can join mid-run and get the current river and state.

## Session flow (`NetworkSessionManager`)

1. **Host:**
   - `StartHost` on `listenAddress` (0.0.0.0 by default; 127.0.0.1 for local tests).
   - Spawns `PF_NetRaftSync`, seats the host, and goes to `Lobby`.
2. **Join:**
   - `StartClient` to `address:port`, with the player name in the connection payload.
   - Approval rejects joins when 4 human seats are taken ("The raft is full (4 players)").
   - On connect, the server gives the client the first free seat.
3. **Start:** in the lobby the host presses Start, which calls `PrepareRun` then `StartCountdown`. Everyone sees the same countdown via the run state.
4. **Checkpoints and finish:** server-validated; clients see flags and the results screen.
5. **Restart / reset:** clients send `RequestRunCommandRpc`, which is allowed according to `RestartRule`.
   - **HostOnly** (default): only the host can restart or reset.
   - **AnyPlayer:** anyone can.
6. **Pause:** online it is a local overlay only. One player can't freeze the shared run.

### Disconnects

- **Client leaves or drops:**
  - The server frees the seat; that player's input is cleared at once, so their thrust stops.
  - The raft keeps its physical state and the others keep playing.
  - Rejoining = joining again; the player gets a free seat.
- **Host leaves:**
  - Every client gets "Disconnected due to host shutting down."
  - The run goes to `Disconnected` (menu with message).
  - **There is no host migration.** NGO doesn't provide it for client-hosted sessions, so the session ends.

## What was tested

Real Windows player builds, run headless on one PC; see `Test_Report.md` and the logs in `ArtSource/Game/NetTests/`:
- 2, 3 and 4 players;
- a 5th player rejected;
- a client leaving;
- the host leaving;
- 150 ms + 10% loss on client input.

### Endless online test (WebSockets, room code)

`Tools/net_endless_test.sh <outdir> 2 75` runs a headless host and 2 clients on one PC. The clients join with room code `B9S-AAAB` (127.0.0.1). The host bot drives forward and restarts 3 s after each game over. Logs: `ArtSource/Game/NetTests/net7_endless_ws/`.

Result:
- 3 players seated.
- Three runs were played, each with a new river. The seeds were identical on all three machines every time (208001197, then 224158230, then 316172587).
- Game over arrived on the clients at the same second, with the host's crash reason ("a spiked roller", "a barricade").
- Client obstacle clocks were within 0.1 s of the host's run time.
- Each client received about 2,070 poses and 85 bursts, and sent about 630 inputs.
- 0 exceptions in all three logs.

### Browser joins a desktop host

A headless desktop host was started, and Chrome (headless, puppeteer) opened the web build with `?fo-net=join&fo-addr=B9S-AAAB`. Logs and screenshots: `ArtSource/Game/NetTests/net8_web_client/`.

Result:
- The browser connected over WebSockets and got the host's river seed.
- It saw the game over ("windmill blades") and the host's restart, then rebuilt the new river.
- It sent 448 inputs and received 1,646 poses and 102 bursts. Its farts drove the shared raft.
- Ping 89 ms. No exceptions on the host and no errors in the browser.

## Not done / limits

- **Internet play (player-hosted mode):** NAT traversal and Relay are not available, because the project isn't linked to Unity Gaming Services. A player-hosted game needs the host to forward TCP 7777 (WebSockets), or a VPN/LAN, and its room codes encode the LAN address. **This is solved by the dedicated AWS rooms described in the last section**, which is now the online path in the menu.
- **Browsers:** the web app must be served over plain http to join (an https page may only open `wss://`, and the host doesn't serve TLS).
- **Firewall:** the first time the desktop app hosts, Windows asks to allow it on the network; allow private networks.
- **No client-side prediction.** Clients see the raft 100 ms in the past, plus their round-trip time for their own input. That's acceptable for a shared, slow-ish raft; the gas VFX for your own farts appear when the server confirms them (about one RTT).
- **Packet-loss simulation covered input only.** Server-to-client loss and latency weren't simulated: NGO's transport simulator is deprecated and now needs the Multiplayer Tools package.
- **No long-session soak test** beyond about 1 minute per run.


## Internet play: accounts, friends, invites, dedicated rooms and voice (2026-11-07)

Full guide and the AWS deployment steps: [`Backend/README.md`](../../../Backend/README.md).

- **Login rules:** single player and local co-op never need an account. Online play does: the menu's **Play Online** and **Friends** buttons open the login / sign-up board first.
- **Accounts (Supabase, `Online/AccountService.cs`):**
  - Players sign up with email, password and a unique username (`Backend/supabase/schema.sql`: profiles, friendships, invites, RLS and RPC functions).
  - The client uses plain REST through UnityWebRequest (no SDK), so the same code runs on Android, desktop and WebGL.
  - The session's refresh token is kept in PlayerPrefs.
  - Friends and invites are polled; an "online" dot comes from a 30 s heartbeat.
- **Screens (`UI/OnlineUI.cs`):**
  - ACCOUNT (log in or sign up);
  - PLAY ONLINE (create a room, or join one by code);
  - FRIENDS (add by username, accept or decline, invite to the current room, remove);
  - an invite pop-up (Join / Later);
  - a TALK button, plus V on a keyboard.
  - The lobby has an **Invite** button. Settings has a **Voice chat** selector (Push to talk / Open mic / Off).
  - Tapping a player card mutes that player.
- **Rooms:** `Backend/room-service/server.js` (Node, no dependencies) starts one Unity dedicated server per room and signs a join ticket (HMAC-SHA256, 2 minutes, tied to the room).
- **Dedicated mode:** `NetworkSessionManager.StartDedicated` runs a room server, launched with `-fo-dedicated` by `Multiplayer/DedicatedServerBoot.cs`.
  - It checks the ticket in the connection approval (`RoomTicketVerifier`) and rejects a second login of the same account.
  - The room owner (or the first player in) is the leader. Leader-only run commands: 2 = start, 3 = back to the lobby. Leadership passes on when the leader leaves.
  - The room quits after 90 s empty.
- **Joining:** clients connect with `JoinRoom(ticket)` over `wss://<elastic-ip>.sslip.io:7800+`; Caddy terminates TLS with a free Let's Encrypt certificate.
- **Voice (`Online/VoiceChat.cs`, `VoiceSpeaker.cs`; Concentus 1.1.7 in `Assets/Plugins/Concentus`):**
  - 16 kHz mono, 20 ms Opus frames at about 17 kbit/s (about 43 bytes each), sent as NGO named messages over unreliable delivery.
  - The server forwards each frame to the other players. Each listener runs a jitter buffer with packet-loss concealment.
  - Android asks for the microphone permission on first use. There is no voice in the WebGL build.
- **Builds:** *Tools > Fart Online > Build Linux Dedicated Server* writes `D:\FOBuild\Server` (123 MB).
- **Tests:**
  - *Tools > Fart Online > Tests > Run Online Self-Test*: an Opus round trip, plus Node-signed tickets checked in C# (valid, expired, wrong room, tampered, wrong secret). All pass.
  - *Run Online UI Probe*: screenshots `ArtSource/Game/Previews/UI/40-45`.
  - `Tools/room_local_test.sh` (add `SERVER=wsl` to run the Linux build in WSL): two bots create and join a room, the leader starts and restarts runs, and both send test-tone voice. It also checks that a wrong code (404), no login (401) and a duplicate login are refused.
  - Results with the Linux server in WSL (`ArtSource/Game/NetTests/room2_wsl_linux`): 751 voice frames sent and 749 received one way, 700 and 700 the other; 36 to 83 ms ping; runs in sync.
- **Not tested yet:**
  - against a live Supabase project (no keys yet);
  - on AWS (the user will deploy by hand);
  - voice on a real phone microphone.
