# Fart Online backend: accounts, friends, invites, rooms and voice

Online play needs an account. Single player and local co-op never do.

## How the pieces fit

```
 Phone / PC / browser (Unity client)
   │ 1. sign up / log in, friends, invites        ┌──────────────────────────┐
   ├──────────────── HTTPS (REST) ───────────────►│ Supabase (free tier)     │
   │                                              │ Auth + Postgres + RLS     │
   │                                              └──────────────────────────┘
   │ 2. "create room" / "join ABC123"  + Supabase access token
   ├──────── HTTPS https://<ip>.sslip.io/api ─────► Caddy ─► room service (Node, :8080)
   │                                                           │ checks the token with Supabase,
   │◄──────────── room code + host + port + signed ticket ─────┘ starts one Unity server per room
   │
   │ 3. game + voice: WebSocket over TLS, ticket as the connection payload
   └──────── wss://<ip>.sslip.io:7800 ────────────► Caddy ─► Unity dedicated server (:17800)
                                                             checks the ticket (HMAC), runs the raft,
                                                             relays voice (Opus) between players
                         AWS EC2 (Ubuntu 24.04, one instance)
```

| Part | Where | What it does |
|---|---|---|
| `supabase/schema.sql` | Supabase | profiles (unique username), friendships, invites, row level security, RPC functions |
| `room-service/server.js` | EC2 | `POST /rooms`, `POST /rooms/join`, `GET /health`; starts/stops room servers; signs join tickets |
| Unity Linux dedicated server | EC2 | one process per room (up to 4 players); quits after 90 s empty |
| Caddy | EC2 | free Let's Encrypt certificate for `<elastic-ip>.sslip.io`; TLS for the API (443) and room ports (7800+) |
| Unity client | players | `Online/AccountService` (Supabase REST), `UI/OnlineUI` (account, play online, friends, invites), `Online/VoiceChat` |

**Why this design:**
- The free sslip.io hostname means you don't have to buy a domain. `1-2-3-4.sslip.io` resolves to `1.2.3.4`.
- Real certificates matter because browsers only open `wss://` from an `https://` page.
- Voice travels on the game connection, so it needs no extra servers or fees.
- The ticket secret lives only on the server.

## 1. Supabase (accounts, friends, invites)

1. Create a free project at <https://supabase.com>.
2. Run the schema:
   - **SQL Editor:** paste [`supabase/schema.sql`](supabase/schema.sql) and run it. It's safe to run again.
   - **Management API**, with a personal access token:
     ```
     curl -X POST https://api.supabase.com/v1/projects/<ref>/database/query \
       -H "Authorization: Bearer <access-token>" -H "Content-Type: application/json" \
       --data "$(jq -Rs '{query: .}' Backend/supabase/schema.sql)"
     ```
3. **Authentication > Sign In / Providers > Email.** Keep email + password on. If you turn off **Confirm email**, players can play right after signing up. If it's on, the game says "check your email" and they log in after confirming.
4. Copy the **Project URL** and the **anon public key** from **Project Settings > API**.
   - The anon key is meant to be public, because it ships in every build.
   - Never put the `service_role` key in the game.
5. In Unity, open **Tools > Fart Online > Online > Backend Settings** and paste them. This writes `Assets/Game/Resources/FartOnlineBackend.asset`.

## 2. Build the Linux server

1. Install Unity Hub's *Linux Dedicated Server Build Support* (and *Linux Build Support (IL2CPP)*). Both are already installed on this PC.
2. In Unity, open **Tools > Fart Online > Build Linux Dedicated Server**. The build goes to `D:\FOBuild\Server\FartOnlineServer.x86_64`.

## 3. Deploy to AWS (EC2), step by step in the AWS console

These steps use 2 rooms on a t3.micro. For more rooms, pick a bigger instance and widen the port range in steps 2 and 7.

### 3.1 Launch the instance

1. Open **EC2 > Instances > Launch instance**:
   - **Name:** `fartonline-game`.
   - **AMI:** *Ubuntu Server 24.04 LTS* (64-bit x86).
   - **Instance type:** `t3.micro` (1 GB, 2 rooms). `t3.small` (2 GB) handles about 5 rooms.
   - **Key pair:** *Create new key pair*, type ED25519, format `.pem`. Save the file, for example `C:\Users\<you>\.ssh\fartonline-key.pem`.
   - **Storage:** 16 GB gp3.
2. Under **Network settings > Edit**, create a security group named `fartonline-game` with these inbound rules:

   | Type | Port | Source | Why |
   |---|---|---|---|
   | SSH | 22 | My IP | your shell access only |
   | HTTP | 80 | 0.0.0.0/0 | Let's Encrypt certificate check |
   | HTTPS | 443 | 0.0.0.0/0 | the room API (`/api`) |
   | Custom TCP | 7800-7801 | 0.0.0.0/0 | one port per room (secure WebSockets) |

3. Launch the instance.
4. Go to **EC2 > Elastic IPs**: *Allocate Elastic IP address*, then *Actions > Associate*, and pick the instance.
   - The address must stay fixed, because the hostname and its certificate are tied to it.
   - Your hostname is the IP with dashes plus `.sslip.io`. For example, `3.91.20.7` gives `3-91-20-7.sslip.io`. This free DNS name needs no setup.

### 3.2 Upload the files

On your PC (PowerShell or Git Bash), `scp` comes with Windows:
```
scp -i ~/.ssh/fartonline-key.pem -r D:/FOBuild/Server ubuntu@<ELASTIC-IP>:~/server
scp -i ~/.ssh/fartonline-key.pem D:/unityProjects/fartonline/Backend/room-service/server.js ubuntu@<ELASTIC-IP>:~/
ssh -i ~/.ssh/fartonline-key.pem ubuntu@<ELASTIC-IP>
```
Leave out the `Fart Online_BurstDebugInformation_DoNotShip` folder if you like; the server doesn't need it.

### 3.3 Set up the server

Run the following on the instance, after `ssh`.

1. Install Node 22 and Caddy, and add swap (1 GB isn't enough for two Unity rooms without it):
   ```
   sudo apt-get update
   curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
   sudo apt-get install -y nodejs debian-keyring debian-archive-keyring apt-transport-https
   curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
   curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
   sudo apt-get update && sudo apt-get install -y caddy
   sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
   echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
   ```
2. Put the files in place under a service user:
   ```
   sudo useradd --system --home /opt/fartonline --shell /usr/sbin/nologin fartonline
   sudo mkdir -p /opt/fartonline/room-service /etc/fartonline
   sudo mv ~/server /opt/fartonline/server
   sudo mv ~/server.js /opt/fartonline/room-service/
   sudo chmod +x /opt/fartonline/server/FartOnlineServer.x86_64
   sudo chown -R fartonline:fartonline /opt/fartonline
   openssl rand -hex 32          # copy the output: it's your TICKET_SECRET
   ```
3. Create the configuration with `sudo nano /etc/fartonline/rooms.env` (all settings are described in [`room-service/.env.example`](room-service/.env.example)):
   ```
   PORT=8080
   BIND=127.0.0.1
   SUPABASE_URL=https://<ref>.supabase.co
   SUPABASE_ANON_KEY=<anon key>
   TICKET_SECRET=<the 64 hex characters from openssl>
   GAME_SERVER_BIN=/opt/fartonline/server/FartOnlineServer.x86_64
   GAME_SERVER_ARGS=-batchmode -nographics
   PUBLIC_HOST=<ip-with-dashes>.sslip.io
   PUBLIC_PORT_BASE=7800
   INTERNAL_PORT_BASE=17800
   MAX_ROOMS=2
   TLS=1
   DEV_AUTH=0
   ```
   Then lock the file down: `sudo chown root:fartonline /etc/fartonline/rooms.env && sudo chmod 640 /etc/fartonline/rooms.env`.
4. Create the service with `sudo nano /etc/systemd/system/fartonline-rooms.service`:
   ```
   [Unit]
   Description=Fart Online room service
   After=network-online.target
   Wants=network-online.target

   [Service]
   User=fartonline
   Group=fartonline
   EnvironmentFile=/etc/fartonline/rooms.env
   WorkingDirectory=/opt/fartonline/room-service
   ExecStart=/usr/bin/node /opt/fartonline/room-service/server.js
   Restart=always
   RestartSec=3
   KillMode=control-group
   NoNewPrivileges=true

   [Install]
   WantedBy=multi-user.target
   ```
5. Set up HTTPS and the room ports with `sudo nano /etc/caddy/Caddyfile`. Replace everything in it with:
   ```
   <ip-with-dashes>.sslip.io {
       handle_path /api/* {
           reverse_proxy 127.0.0.1:8080
       }
       respond / "Fart Online game server" 200
   }

   <ip-with-dashes>.sslip.io:7800 {
       reverse_proxy 127.0.0.1:17800
   }

   <ip-with-dashes>.sslip.io:7801 {
       reverse_proxy 127.0.0.1:17801
   }
   ```
   Add one more `:780N` block per extra room, matching `MAX_ROOMS` and the security group.
6. Start everything:
   ```
   sudo systemctl daemon-reload
   sudo systemctl enable --now fartonline-rooms
   sudo systemctl restart caddy
   curl http://127.0.0.1:8080/health            # {"ok":true,"rooms":0,...}
   ```
7. From your PC, open `https://<ip-with-dashes>.sslip.io/api/health` in a browser. It should show `{"ok":true,...}` with a valid padlock.

### 3.4 Point the game at it

1. In Unity, open **Tools > Fart Online > Online > Backend Settings**.
2. Set **Room Service Url** to `https://<ip-with-dashes>.sslip.io/api`.
3. Rebuild the APK, desktop or web builds.

### 3.5 Day-to-day running

- **Logs:**
  - `journalctl -u fartonline-rooms -f` shows the room service, with one line per room start, join and leave.
  - Each room server also writes to it.
- **New server build:**
  1. Upload the new `D:/FOBuild/Server` the same way.
  2. Run `sudo systemctl stop fartonline-rooms`.
  3. Replace `/opt/fartonline/server`, then `chmod +x` the binary and `chown` the folder as in 3.3 step 2.
  4. Run `sudo systemctl start fartonline-rooms`.
- **New room service:** copy `server.js` to `/opt/fartonline/room-service/`, then run `sudo systemctl restart fartonline-rooms`.
- **Stop paying:**
  1. Terminate the instance.
  2. Release the Elastic IP; an unattached one keeps costing money.
  3. Delete the security group and key pair.

**Costs (us-east-1, approximate):**

| Item | Cost |
|---|---|
| t3.micro | ~$7.50/month, or free for 12 months on an eligible new account |
| Public IPv4 (Elastic IP) | ~$3.60/month |
| Storage (16 GB gp3) | ~$1.30/month |
| Data transfer | 100 GB/month free, then ~$0.09/GB |

- A 4-player room uses roughly 15 to 40 MB per hour of game state and 30 MB per hour per talking player for voice.
- t3 instances are "burstable". A busy room above the CPU baseline spends CPU credits. In the default unlimited mode, sustained use costs a little extra.
- For many rooms, move to t3.small or a c7i instance and raise `MAX_ROOMS`. Open the extra ports in the security group too.

## 4. How players use it

1. On the main menu, **Play Online** (or **Friends**) asks the player to log in or sign up: email, password and a unique username.
2. **Friends:** add a friend by username, accept or decline requests, see who's online (green dot), or remove a friend.
3. **Play Online > Create Room:** the room service starts a game server, and the player lands in the lobby with a room code.
   - The lobby's **Invite** button opens the friends list. **Invite** sends that friend a pop-up: "bob invited you to raft together! Join / Later".
   - Friends can also type the room code under **Join**.
4. The room owner leads: they **Start**, **Play Again**, or take everyone back to the **Lobby**. If the leader leaves, the next player takes over.
5. **Voice chat:**
   - Hold **TALK** (on screen) or **V** (keyboard) to talk. **Settings > Voice chat** switches between Push to talk, Open mic and Off.
   - Tap a player's card to mute them. Cards show *TALKING* while someone speaks.
   - Android asks for microphone permission the first time.
   - The web build has no voice yet. Unity's WebGL player can't record or stream audio, so it would need a browser audio bridge.

## Security notes

- **Writes go through functions:**
  - Every table has row level security on.
  - Players may only read their own friendships and invites.
  - All writes go through `security definer` functions that check `auth.uid()`. For example, you can only invite accepted friends, and only accept requests sent to you.
- **The room service checks every request:**
  - It accepts only valid Supabase sessions; it looks each user up with the user's own token.
  - It rate-limits each user to 20 room requests a minute.
- **The room server checks every join:**
  - Join tickets are HMAC-SHA256 signed, tied to one room and valid for 2 minutes.
  - The room server rejects a second connection from the same account.
- **Secrets stay on the server:** the ticket secret lives in `/etc/fartonline/rooms.env` (mode 640). It never ships in a build.
- **Dev login is for local tests only:** `DEV_AUTH=1` accepts fake logins and refuses to start together with TLS.

## Local testing (no AWS, no Supabase)

`Tools/room_local_test.sh` runs the room service with `DEV_AUTH=1` and plain ws, using the Windows player as the room server. Two bots then create and join a room, play, restart runs, and send test-tone voice. It also checks that a wrong code, no login, and a duplicate login are refused. Logs go to `ArtSource/Game/NetTests/room1_local`.
