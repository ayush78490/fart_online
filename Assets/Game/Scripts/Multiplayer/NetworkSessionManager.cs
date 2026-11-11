using System;
using System.Collections.Generic;
using System.Text;
using FartOnline.Game.Online;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FartOnline.Game.Net
{
    /// <summary>
    /// Online co-op session flow on top of Netcode for GameObjects + Unity Transport (direct IP, default port 7777):
    /// host / join → lobby (seats assigned by client id, up to 4) → host starts → countdown → play → results →
    /// restart or leave. The host is the server: it runs the only raft simulation. Clients are approved only while a
    /// seat is free; their name travels in the connection payload. A leaving client's seat is freed and its thrust
    /// stops while everyone else keeps playing. If the host leaves the session ends for everyone (no host migration:
    /// NGO does not provide it for a client-hosted server) and clients return to the menu with a message.
    /// Relay / Lobby services are not used (the project is not linked to Unity Gaming Services).
    /// Transport: WebSockets by default (<see cref="useWebSockets"/>), so browser players (the WebGL web app, which can
    /// only join) and desktop players share one session. The host shows a <see cref="RoomCode"/> made from its LAN
    /// address; the Join box takes the code or a plain address.
    ///
    /// Internet play (AWS): <see cref="StartDedicated"/> runs this scene as a headless DEDICATED server for one room
    /// (started by Backend/room-service, which also signs the join tickets). There is no local player: every seat
    /// belongs to a signed-in client whose ticket (connection payload) is checked by <see cref="RoomTicketVerifier"/>.
    /// The room owner (or the first player in) is the LEADER: only the leader starts, restarts or returns to the
    /// lobby (run commands 2 and 3); leadership passes on when the leader leaves. An empty room shuts itself down.
    /// Clients reach it with <see cref="JoinRoom"/> (wss:// through Caddy in production).
    /// </summary>
    public class NetworkSessionManager : MonoBehaviour
    {
        public enum RestartRule { HostOnly, AnyPlayer }

        public NetworkManager network;
        [Tooltip("PF_NetRaftSync: spawned by the host, replicates the scene raft")] public NetworkRaftController netRaftPrefab;
        public SharedRaftController raft;
        public GameSession session;
        public GameRunManager run;
        public GameplayHUD hud;
        [Tooltip("Who may restart / reset the shared run when players disagree")] public RestartRule restartRule = RestartRule.HostOnly;
        public int maxPlayers = 4;
        public string playerName = "";
        [Tooltip("Address the host listens on (0.0.0.0 = all interfaces for LAN / port-forwarded play; 127.0.0.1 = this PC only)")]
        public string listenAddress = "0.0.0.0";
        [Tooltip("WebSocket transport (lets the web app join; desktop players use it too). Off = UDP, desktop only")]
        public bool useWebSockets = true;
        const string NameKey = "fo.player.name";

        public static NetworkSessionManager Instance { get; private set; }

        readonly Dictionary<ulong, string> names = new Dictionary<ulong, string>();
        readonly Dictionary<ulong, RoomTicketVerifier.Ticket> tickets = new Dictionary<ulong, RoomTicketVerifier.Ticket>();
        string ticketSecret;
        ulong leader = ulong.MaxValue;
        float startedAt, emptySince;
        bool everJoined;
        /// <summary>Dedicated server: quit after the room has been empty this long (s), or if nobody ever joined.</summary>
        public const float EmptyQuitSeconds = 90f, NeverJoinedQuitSeconds = 180f;
        /// <summary>This process is a headless dedicated server for one room.</summary>
        public bool Dedicated { get; private set; }
        /// <summary>The room code (dedicated server, or a client that joined through the room service).</summary>
        public string CurrentRoom { get; private set; } = "";
        /// <summary>True when this machine leads the session (a LAN host, or the leader client of an AWS room).</summary>
        public bool IsLeader => IsOnline && (network.IsServer && !Dedicated || netRaft != null && netRaft.LeaderClientId == network.LocalClientId);
        LocalPlayerInput localInput;
        string lastError = "";

        UnityTransport Transport => network.GetComponent<UnityTransport>();
        NetworkRaftController netRaft => NetworkRaftController.Instance;
        int RemotePlayerCount { get { int n = 0; foreach (var s in raft.seats.Seats) if (s.IsOccupied && !s.IsAI) n++; return n; } }
        public bool IsOnline => network != null && network.IsListening;
        /// <summary>Host: the LAN address and room code friends use to join (empty when not hosting).</summary>
        public string HostAddress { get; private set; } = "";
        public string HostRoomCode { get; private set; } = "";
        /// <summary>Client: the address this machine joined.</summary>
        public string JoinedAddress { get; private set; } = "";
        public ushort Port { get; private set; } = RoomCode.DefaultPort;

        public string PlayerName
        {
            get => playerName;
            set
            {
                string v = (value ?? "").Trim();
                if (v.Length > 16) v = v.Substring(0, 16);
                if (v.Length == 0) return;
                playerName = v;
                PlayerPrefs.SetString(NameKey, v);
            }
        }

        void Awake()
        {
            Instance = this;
            if (GetComponent<VoiceChat>() == null) gameObject.AddComponent<VoiceChat>();
            if (string.IsNullOrEmpty(playerName)) playerName = PlayerPrefs.GetString(NameKey, "");
            if (string.IsNullOrEmpty(playerName)) playerName = "Pig " + Random.Range(10, 99);
            hud.PlayerNameAccess = () => playerName;
            hud.PlayerNameChanged = n => PlayerName = n;
            hud.SessionInfo = SessionInfo;
            hud.HostRequested = (a, p) => Host(a, p);
            hud.JoinRequested = (a, p) => Join(a, p);
            hud.LeaveRequested = Leave;
            hud.ConnectionStatus = StatusText;
            hud.CanStartOnline = () => IsOnline && run.State == RunState.Lobby && (network.IsServer || IsLeader);
            hud.StartOnlineRequested = () => { if (network.IsServer) StartRun(); else if (netRaft != null) netRaft.RequestRunCommandRpc(2); };
            hud.IsRoomLeader = () => IsLeader;
            hud.RunCommand = SendRunCommand;
            network.OnClientConnectedCallback += OnClientConnected;
            network.OnClientDisconnectCallback += OnClientDisconnected;
            network.OnTransportFailure += OnTransportFailure;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (network == null) return;
            network.OnClientConnectedCallback -= OnClientConnected;
            network.OnClientDisconnectCallback -= OnClientDisconnected;
            network.OnTransportFailure -= OnTransportFailure;
        }

        // ------------------------------------------------------------------ host / join / leave
        public bool Host(string address, ushort port)
        {
            if (IsOnline) return false;
            session.EndSession();
            Dedicated = false;
            CurrentRoom = "";
            Transport.UseWebSockets = useWebSockets;
            Transport.UseEncryption = false;
            Transport.SetConnectionData("127.0.0.1", port, listenAddress);
            network.NetworkConfig.ConnectionApproval = true;
            network.ConnectionApprovalCallback = Approve;
            names.Clear();
            if (!network.StartHost()) { lastError = "Could not host on port " + port + " (is another game hosting?)"; return false; }
            Port = port;
            HostAddress = listenAddress == "127.0.0.1" ? "127.0.0.1" : RoomCode.LocalLanAddress();
            HostRoomCode = string.IsNullOrEmpty(HostAddress) ? "" : RoomCode.Encode(HostAddress, port);
            session.SetModeExternal(SessionMode.OnlineHost);
            var sync = Instantiate(netRaftPrefab);
            sync.GetComponent<NetworkObject>().Spawn();
            localInput = session.CreateLocalInputOnly("");
            netRaft.SetLocalInput(localInput);
            netRaft.ServerAssignSeat(network.LocalClientId, playerName + " (host)", localInput);
            run.EnterLobby();
            Debug.Log($"[Net] hosting on port {port} ({(useWebSockets ? "WebSockets" : "UDP")}), LAN address {HostAddress}, room code {HostRoomCode}");
            return true;
        }

        public bool Join(string address, ushort port)
        {
            if (IsOnline) return false;
            session.EndSession();
            if (RoomCode.TryParse(address, out string a, out ushort p)) { address = a; if (p != RoomCode.DefaultPort) port = p; }
            CurrentRoom = "";
            Transport.UseWebSockets = useWebSockets;
            Transport.UseEncryption = false;
            Transport.SetConnectionData(address, port);
            JoinedAddress = address;
            Port = port;
            network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(playerName);
            lastError = "";
            if (!network.StartClient()) { lastError = "Could not connect to " + address + ":" + port; return false; }
            session.SetModeExternal(SessionMode.OnlineClient);
            Debug.Log($"[Net] joining {address}:{port} ({(useWebSockets ? "WebSockets" : "UDP")})");
            return true;
        }

        /// <summary>Joins a room on the AWS game server with a ticket from the room service.</summary>
        public bool JoinRoom(RoomTicket t)
        {
            if (IsOnline || t == null || string.IsNullOrEmpty(t.ticket)) return false;
            session.EndSession();
            Transport.UseWebSockets = true;
            Transport.UseEncryption = t.tls;
            if (t.tls) Transport.SetClientSecrets(t.host);          // publicly trusted certificate (Let's Encrypt via Caddy)
            Transport.SetConnectionData(t.host, (ushort)t.port);
            JoinedAddress = t.host;
            Port = (ushort)t.port;
            CurrentRoom = t.code;
            network.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(t.ticket);
            lastError = "";
            if (!network.StartClient()) { lastError = "Could not connect to the game server"; return false; }
            session.SetModeExternal(SessionMode.OnlineClient);
            Debug.Log($"[Net] joining room {t.code} at {(t.tls ? "wss" : "ws")}://{t.host}:{t.port}");
            return true;
        }

        /// <summary>Headless dedicated server for one room (no local player). Needs FO_TICKET_SECRET in the environment.</summary>
        public bool StartDedicated(ushort port, string listen, string room)
        {
            ticketSecret = System.Environment.GetEnvironmentVariable("FO_TICKET_SECRET");
            if (string.IsNullOrEmpty(ticketSecret)) { Debug.LogError("[Net] FO_TICKET_SECRET is not set: refusing to start a room"); return false; }
            Dedicated = true;
            CurrentRoom = room;
            session.EndSession();
            Transport.UseWebSockets = true;
            Transport.UseEncryption = false;                         // TLS is terminated by Caddy in front of us
            Transport.SetConnectionData("127.0.0.1", port, string.IsNullOrEmpty(listen) ? "0.0.0.0" : listen);
            network.NetworkConfig.ConnectionApproval = true;
            network.ConnectionApprovalCallback = Approve;
            names.Clear();
            tickets.Clear();
            leader = ulong.MaxValue;
            if (!network.StartServer()) { Debug.LogError("[Net] could not listen on port " + port); return false; }
            Port = port;
            session.SetModeExternal(SessionMode.OnlineHost);
            var sync = Instantiate(netRaftPrefab);
            sync.GetComponent<NetworkObject>().Spawn();
            run.EnterLobby();
            startedAt = emptySince = Time.realtimeSinceStartup;
            everJoined = false;
            RoomLog("ready port=" + port + " room=" + room);
            return true;
        }

        static void RoomLog(string s)
        {
            string line = "[FOROOM] " + s;
            Debug.Log(line);
            Console.WriteLine(line);                 // the room service reads stdout
        }

        void SetLeader(ulong id)
        {
            leader = id;
            if (netRaft != null) netRaft.ServerSetLeader(id);
            if (id != ulong.MaxValue) Debug.Log($"[Net] room leader: client {id} ({(names.TryGetValue(id, out var n) ? n : "?")})");
        }

        void Update()
        {
            if (!Dedicated || !IsOnline) return;
            float now = Time.realtimeSinceStartup;
            if (RemotePlayerCount > 0) return;
            if (!everJoined ? now - startedAt > NeverJoinedQuitSeconds : now - emptySince > EmptyQuitSeconds)
            {
                RoomLog("closing (empty)");
                network.Shutdown();
                Application.Quit();
            }
        }

        public void Leave()
        {
            if (!IsOnline) return;
            bool wasServer = network.IsServer;
            if (wasServer && netRaft != null) netRaft.ServerClearSeats();
            network.Shutdown();
            CleanupLocal();
            HostAddress = HostRoomCode = JoinedAddress = "";
            Debug.Log("[Net] left the session" + (wasServer ? " (host: session closed for everyone)" : ""));
        }

        void CleanupLocal()
        {
            if (netRaft != null) netRaft.SetLocalInput(null);
            localInput = null;
            session.EndSession();
        }

        // ------------------------------------------------------------------ server side
        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse resp)
        {
            if (Dedicated) { ApproveTicket(req, resp); return; }
            int occupied = RemotePlayerCount;
            bool isHost = req.ClientNetworkId == network.LocalClientId;
            resp.CreatePlayerObject = false;
            resp.Approved = isHost || occupied < maxPlayers;
            resp.Reason = resp.Approved ? "" : "The raft is full (4 players)";
            if (resp.Approved && !isHost)
            {
                string n = req.Payload != null && req.Payload.Length > 0 ? Encoding.UTF8.GetString(req.Payload) : "Player";
                names[req.ClientNetworkId] = n.Length > 24 ? n.Substring(0, 24) : n;
            }
        }

        void ApproveTicket(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse resp)
        {
            resp.CreatePlayerObject = false;
            string t = req.Payload != null && req.Payload.Length > 0 && req.Payload.Length < 2048 ? Encoding.UTF8.GetString(req.Payload) : "";
            if (!RoomTicketVerifier.Verify(t, ticketSecret, CurrentRoom, out var info, out string err))
            {
                resp.Approved = false;
                resp.Reason = err;
                Debug.Log("[Net] rejected a connection: " + err);
                return;
            }
            foreach (var kv in tickets)
                if (kv.Value.UserId == info.UserId) { resp.Approved = false; resp.Reason = "You're already on this raft (another device?)"; return; }
            if (tickets.Count >= maxPlayers) { resp.Approved = false; resp.Reason = "The raft is full (4 players)"; return; }
            tickets[req.ClientNetworkId] = info;
            names[req.ClientNetworkId] = info.Username;
            resp.Approved = true;
        }

        void OnClientConnected(ulong id)
        {
            if (network.IsServer && Dedicated)
            {
                string n = names.TryGetValue(id, out var nm) ? nm : "Player " + id;
                var seat = netRaft.ServerAssignSeat(id, n, null);
                everJoined = true;
                bool owner = tickets.TryGetValue(id, out var tk) && tk.Owner;
                if (leader == ulong.MaxValue || owner) SetLeader(id);
                if (run.State == RunState.MainMenu) run.EnterLobby();
                Debug.Log($"[Net] {n} joined room {CurrentRoom} -> seat {(seat != null ? seat.seatIndex + 1 : -1)}");
                RoomLog("players=" + RemotePlayerCount);
                return;
            }
            if (network.IsServer)
            {
                if (id == network.LocalClientId) return;
                string n = names.TryGetValue(id, out var nm) ? nm : "Player " + id;
                var seat = netRaft.ServerAssignSeat(id, n, null);
                Debug.Log($"[Net] client {id} '{n}' joined -> seat {(seat != null ? seat.seatIndex + 1 : -1)}");
                if (run.State == RunState.MainMenu) run.EnterLobby();
            }
            else if (id == network.LocalClientId)
            {
                localInput = session.CreateLocalInputOnly("");
                if (netRaft != null) netRaft.SetLocalInput(localInput);
                else Debug.LogWarning("[Net] raft sync object not spawned yet");
                Debug.Log($"[Net] connected as client {id}");
            }
        }

        void OnClientDisconnected(ulong id)
        {
            if (network.IsServer && Dedicated)
            {
                netRaft.ServerReleaseSeat(id);
                names.Remove(id);
                tickets.Remove(id);
                if (leader == id)
                {
                    ulong next = ulong.MaxValue;
                    foreach (var k in tickets.Keys) if (k < next) next = k;
                    SetLeader(next);
                }
                int left = RemotePlayerCount;
                if (left == 0)
                {
                    emptySince = Time.realtimeSinceStartup;
                    if (run.State != RunState.Lobby) run.EnterLobby();      // ready for whoever comes next
                }
                Debug.Log($"[Net] client {id} left room {CurrentRoom}; {left} player(s) remain");
                RoomLog("players=" + left);
                return;
            }
            if (network.IsServer)
            {
                if (id == network.LocalClientId) return;
                netRaft.ServerReleaseSeat(id);
                names.Remove(id);
                Debug.Log($"[Net] client {id} left; seat freed, the run continues with {RemotePlayerCount} player(s)");
            }
            else
            {
                // we were disconnected (host left, kicked, or the network failed)
                lastError = string.IsNullOrEmpty(network.DisconnectReason) ? "Disconnected from the host" : network.DisconnectReason;
                Debug.Log("[Net] disconnected: " + lastError);
                network.Shutdown();
                localInput = null;
                CurrentRoom = "";
                session.EndSession();          // back to the menu state...
                run.Disconnect();              // ...shown as "DISCONNECTED" with the reason in the status line
            }
        }

        void OnTransportFailure()
        {
            lastError = "Network failure";
            Debug.LogWarning("[Net] transport failure");
            CleanupLocal();
            run.Disconnect();
        }

        public void StartRun()
        {
            if (!IsOnline || !network.IsServer) return;
            run.PrepareRun();
            run.StartCountdown();
        }

        public bool ClientMayCommand(ulong clientId) =>
            Dedicated ? clientId == leader : restartRule == RestartRule.AnyPlayer || clientId == NetworkManager.ServerClientId;

        void SendRunCommand(byte command)
        {
            if (!IsOnline || network.IsServer)
            {
                if (command == 0) run.RestartFromCheckpoint(); else run.RestartRun();
                return;
            }
            if (netRaft != null) netRaft.RequestRunCommandRpc(command);
        }

        /// <summary>Lobby line: where friends join (host) or where we are (client).</summary>
        string SessionInfo()
        {
            if (!IsOnline) return "";
            if (!string.IsNullOrEmpty(CurrentRoom))
                return "Room  " + CurrentRoom + "   ·   " + (IsLeader ? "invite friends, then press Start" : "the room leader starts the run");
            if (network.IsServer)
            {
                if (string.IsNullOrEmpty(HostAddress)) return "Friends join with this computer's address (port " + Port + ")";
                string addr = Port == RoomCode.DefaultPort ? HostAddress : HostAddress + ":" + Port;
                return "Room code  " + HostRoomCode + "   ·   address  " + addr;
            }
            return "Joined " + JoinedAddress + (Port == RoomCode.DefaultPort ? "" : ":" + Port);
        }

        string StatusText()
        {
            if (!IsOnline) return lastError;
            if (network.IsServer) return $"Hosting · {RemotePlayerCount} player(s)";
            if (!network.IsConnectedClient) return "Connecting...";
            ulong rtt = Transport.GetCurrentRtt(NetworkManager.ServerClientId);
            return string.IsNullOrEmpty(CurrentRoom) ? $"Online · ping {rtt} ms" : $"Room {CurrentRoom} · ping {rtt} ms";
        }
    }
}
