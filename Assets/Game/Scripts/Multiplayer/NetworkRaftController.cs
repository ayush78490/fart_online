using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FartOnline.Game.Net
{
    /// <summary>
    /// Network face of the one shared raft. It lives on a small spawned sync object (PF_NetRaftSync, registered as a
    /// network prefab) that the host spawns when a session starts; it finds the scene's raft and replicates it.
    /// Server-authoritative:
    ///  - only the server simulates SharedRaftController / RaftRecoveryManager / GameRunManager
    ///    (their authority delegate returns IsServer while the session runs); clients make the raft kinematic;
    ///  - clients send their own <see cref="NetInput"/> each tick (unreliable; presses carried by sequence counters);
    ///  - the server writes the raft pose, seat occupancy, per-seat state (angle, gas, farting...) and the run state
    ///    into NetworkVariables; clients play the pose back 100 ms behind server time with interpolation
    ///    (short extrapolation from the velocity if a packet is late);
    ///  - discrete bursts and impacts are RPCs, so each client spawns its own particles and sounds.
    /// Nothing about particles is ever networked.
    /// </summary>
    public class NetworkRaftController : NetworkBehaviour
    {
        public struct NetPose : INetworkSerializeByMemcpy, System.IEquatable<NetPose>
        {
            public double time;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public bool Equals(NetPose o) => time == o.time;
        }

        [Tooltip("How often clients resend their input even when unchanged (s)")] public float inputKeepAlive = 0.1f;
        [Tooltip("Client playback delay behind server time (s)")] public float interpolationDelay = 0.1f;
        [Tooltip("Max time a client extrapolates when packets are late (s)")] public float maxExtrapolation = 0.25f;

        /// <summary>Test harness: simulated client→server input latency (ms) and loss (0..1).</summary>
        public static int SimulatedInputDelayMs;
        public static float SimulatedInputDrop;

        public static NetworkRaftController Instance { get; private set; }

        readonly NetworkVariable<NetSeatInfo> seat0 = new NetworkVariable<NetSeatInfo>();
        readonly NetworkVariable<NetSeatInfo> seat1 = new NetworkVariable<NetSeatInfo>();
        readonly NetworkVariable<NetSeatInfo> seat2 = new NetworkVariable<NetSeatInfo>();
        readonly NetworkVariable<NetSeatInfo> seat3 = new NetworkVariable<NetSeatInfo>();
        readonly NetworkVariable<NetSeatState> state0 = new NetworkVariable<NetSeatState>();
        readonly NetworkVariable<NetSeatState> state1 = new NetworkVariable<NetSeatState>();
        readonly NetworkVariable<NetSeatState> state2 = new NetworkVariable<NetSeatState>();
        readonly NetworkVariable<NetSeatState> state3 = new NetworkVariable<NetSeatState>();
        readonly NetworkVariable<NetRunState> runState = new NetworkVariable<NetRunState>();
        readonly NetworkVariable<NetPose> pose = new NetworkVariable<NetPose>();
        /// <summary>Dedicated rooms: the client that leads (starts / restarts runs). ulong.MaxValue = nobody.</summary>
        readonly NetworkVariable<ulong> leader = new NetworkVariable<ulong>(ulong.MaxValue);

        SharedRaftController raft;
        RaftRecoveryManager recovery;
        FartFeedbackController feedback;
        GameRunManager run;
        CheckpointManager checkpoints;
        NetworkVariable<NetSeatInfo>[] infos;
        NetworkVariable<NetSeatState>[] states;
        IThrustInputSource localInput;
        NetInput lastSent;
        float lastSendTime;
        readonly Queue<(float time, NetInput input)> delayed = new Queue<(float, NetInput)>();
        readonly List<NetPose> poses = new List<NetPose>();

        public int InputsSent { get; private set; }
        public int BurstsReceived { get; private set; }
        public int PosesReceived { get; private set; }
        public SharedRaftController Raft => raft;
        public ulong LeaderClientId => leader.Value;
        public void ServerSetLeader(ulong id) { if (IsServer) leader.Value = id; }
        public int PlayerCount { get { int n = 0; foreach (var i in infos) if (i.Value.occupied && !i.Value.ai) n++; return n; } }

        void Awake()
        {
            infos = new[] { seat0, seat1, seat2, seat3 };
            states = new[] { state0, state1, state2, state3 };
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            raft = FindAnyObjectByType<SharedRaftController>();
            recovery = raft.GetComponent<RaftRecoveryManager>();
            feedback = raft.GetComponent<FartFeedbackController>();
            run = FindAnyObjectByType<GameRunManager>();
            checkpoints = FindAnyObjectByType<CheckpointManager>();
            raft.authority = () => IsServer;
            raft.Body.isKinematic = !IsServer;                 // clients follow the replicated pose
            raft.Body.interpolation = IsServer ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            raft.AnyBurst += OnServerBurst;
            recovery.Impact += OnServerImpact;
            if (!IsServer)
            {
                foreach (var v in infos) v.OnValueChanged += (_, __) => ApplySeatInfos();
                pose.OnValueChanged += (_, p) => { poses.Add(p); PosesReceived++; if (poses.Count > 32) poses.RemoveAt(0); };
                ApplySeatInfos();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (raft == null) return;
            raft.authority = null;                              // back to offline play
            raft.Body.isKinematic = false;
            raft.Body.interpolation = RigidbodyInterpolation.Interpolate;
            raft.AnyBurst -= OnServerBurst;
            recovery.Impact -= OnServerImpact;
            if (Instance == this) Instance = null;
        }

        /// <summary>The input this machine's own player produces (sent to the server; the host's seat reads it directly).</summary>
        public void SetLocalInput(IThrustInputSource source) => localInput = source;

        // ------------------------------------------------------------------ server: seats
        public PlayerSeatController ServerAssignSeat(ulong clientId, string playerName, IThrustInputSource hostLocal)
        {
            if (!IsServer) return null;
            var existing = raft.seats.SeatOf(clientId);
            if (existing != null) return existing;
            var seat = raft.seats.FirstFree();
            if (seat == null) return null;
            seat.Occupy(playerName, RaftSeatManager.PlayerColors[seat.seatIndex], hostLocal, hostLocal != null, false, clientId);
            WriteSeatInfos();
            return seat;
        }

        public void ServerReleaseSeat(ulong clientId)
        {
            if (!IsServer) return;
            var seat = raft.seats.SeatOf(clientId);
            if (seat == null) return;
            seat.Vacate();                         // stops that player's thrust immediately; the raft keeps its state
            WriteSeatInfos();
        }

        public void ServerClearSeats()
        {
            raft.seats.VacateAll();
            if (IsServer) WriteSeatInfos();
        }

        public void WriteSeatInfos()
        {
            if (!IsServer) return;
            for (int i = 0; i < 4; i++)
            {
                var s = raft.seats.Seats[i];
                infos[i].Value = new NetSeatInfo { occupied = s.IsOccupied, ai = s.IsAI, clientId = s.OwnerClientId, name = s.PlayerName ?? "" };
            }
        }

        // ------------------------------------------------------------------ clients: seats
        void ApplySeatInfos()
        {
            ulong me = NetworkManager.LocalClientId;
            for (int i = 0; i < 4; i++)
            {
                var info = infos[i].Value;
                var s = raft.seats.Seats[i];
                bool mine = info.occupied && !info.ai && info.clientId == me;
                if (info.occupied && (!s.IsOccupied || s.OwnerClientId != info.clientId || s.IsLocal != mine))
                    s.Occupy(info.name.ToString(), RaftSeatManager.PlayerColors[i], null, mine, info.ai, info.clientId);
                else if (!info.occupied && s.IsOccupied) s.Vacate();
            }
        }

        public PlayerSeatController LocalSeat
        {
            get { if (raft == null) return null; foreach (var s in raft.seats.Seats) if (s.IsOccupied && s.IsLocal) return s; return null; }
        }

        // ------------------------------------------------------------------ input
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void SubmitInputRpc(NetInput input, RpcParams rpcParams = default)
        {
            var seat = raft.seats.SeatOf(rpcParams.Receive.SenderClientId);
            if (seat != null) seat.SetRemoteInput(input.ToState());     // a client can only ever drive its own seat
        }

        // ------------------------------------------------------------------ events
        void OnServerBurst(ThrustEvent e)
        {
            if (IsServer) BurstRpc(NetBurst.From(e));
        }

        [Rpc(SendTo.NotServer)]
        void BurstRpc(NetBurst b)
        {
            var e = b.ToEvent();
            BurstsReceived++;
            if (e.seat >= 0 && e.seat < 4) raft.seats.Seats[e.seat].Thrust.RaiseReplicatedBurst(e);
        }

        void OnServerImpact(float strength, Vector3 point, bool heavy)
        {
            if (IsServer) ImpactRpc(strength, point, heavy);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void ImpactRpc(float strength, Vector3 point, bool heavy)
        {
            if (feedback != null) feedback.OnImpact(strength, point, heavy);
        }

        /// <summary>Client → server: reset to checkpoint (0), restart (1), start from the lobby (2) or back to the lobby
        /// (3), subject to the session's rule (dedicated rooms: the leader only).</summary>
        [Rpc(SendTo.Server)]
        public void RequestRunCommandRpc(byte command, RpcParams rpcParams = default)
        {
            var session = NetworkSessionManager.Instance;
            if (session == null || !session.ClientMayCommand(rpcParams.Receive.SenderClientId)) return;
            switch (command)
            {
                case 0: run.RestartFromCheckpoint(); break;
                case 1: run.RestartRun(); break;
                case 2: if (run.State == RunState.Lobby) session.StartRun(); break;
                case 3: if (run.State != RunState.Lobby) run.EnterLobby(); break;
            }
        }

        // ------------------------------------------------------------------ tick
        void FixedUpdate()
        {
            if (!IsSpawned || raft == null) return;
            if (IsServer)
            {
                pose.Value = new NetPose
                {
                    time = NetworkManager.ServerTime.Time,
                    position = raft.Body.position,
                    rotation = raft.Body.rotation,
                    velocity = raft.Body.linearVelocity,
                };
                for (int i = 0; i < 4; i++)
                {
                    var ns = NetSeatState.From(raft.seats.Seats[i].Thrust.State);
                    if (!ns.Equals(states[i].Value)) states[i].Value = ns;
                }
                var st = run.Stats;
                var rs = new NetRunState
                {
                    state = (byte)run.State,
                    elapsed = Mathf.Round(st.elapsed * 10f) / 10f,
                    checkpoint = (sbyte)st.checkpointIndex,
                    wipeouts = (ushort)st.wipeouts,
                    thrusts = (ushort)Mathf.Min(st.thrustActivations, ushort.MaxValue),
                    progress = (byte)Mathf.RoundToInt(st.progress01 * 255f),
                    countdown = Mathf.Ceil(run.CountdownRemaining * 10f) / 10f,
                    seed = run.RunSeed,
                    score = Mathf.Floor(st.score),
                    crash = Crash(st.crashReason),
                };
                if (!rs.Equals(runState.Value)) runState.Value = rs;
            }
            else if (localInput != null)
            {
                var ni = NetInput.From(localInput.ReadInput());
                if (!ni.Equals(lastSent) || Time.unscaledTime - lastSendTime > inputKeepAlive)
                {
                    if (SimulatedInputDrop <= 0f || Random.value >= SimulatedInputDrop)
                        delayed.Enqueue((Time.unscaledTime + SimulatedInputDelayMs / 1000f, ni));
                    lastSent = ni;
                    lastSendTime = Time.unscaledTime;
                }
                while (delayed.Count > 0 && delayed.Peek().time <= Time.unscaledTime)
                {
                    SubmitInputRpc(delayed.Dequeue().input);
                    InputsSent++;
                }
            }
        }

        void Update()
        {
            if (!IsSpawned || IsServer || raft == null) return;
            if (NetworkManager == null || !NetworkManager.IsConnectedClient || NetworkManager.ShutdownInProgress) return;   // leaving: stop mirroring
            PlayPose();
            for (int i = 0; i < 4; i++)
            {
                var s = raft.seats.Seats[i];
                var st = states[i].Value.ToState();
                s.Thrust.ApplyReplicatedState(st);
                float shown = Mathf.LerpAngle(s.Angle, st.angle, 1f - Mathf.Exp(-Time.deltaTime * 18f));
                raft.seats.SetReplicatedAngle(s, shown);
            }
            var r = runState.Value;
            run.ApplyReplicatedEndless(r.seed, r.score);
            run.Stats.crashReason = r.crash.ToString();
            run.ApplyReplicated((RunState)r.state, r.elapsed, r.checkpoint, r.wipeouts, r.thrusts, r.progress / 255f, r.countdown);
            checkpoints.ApplyReplicated(r.checkpoint, (RunState)r.state == RunState.Finished);
        }

        static Unity.Collections.FixedString64Bytes Crash(string s)
        {
            if (string.IsNullOrEmpty(s)) return default;
            if (s.Length > 40) s = s.Substring(0, 40);
            return new Unity.Collections.FixedString64Bytes(s);
        }

        /// <summary>Clients: interpolate the raft pose 100 ms behind the server clock (extrapolate briefly if late).</summary>
        void PlayPose()
        {
            if (poses.Count == 0) return;
            double t = NetworkManager.ServerTime.Time - interpolationDelay;
            Vector3 p; Quaternion q;
            int i = poses.Count - 1;
            while (i > 0 && poses[i - 1].time > t) i--;
            if (i > 0 && poses[i].time >= t)
            {
                var a = poses[i - 1]; var b = poses[i];
                float k = b.time > a.time ? (float)((t - a.time) / (b.time - a.time)) : 1f;
                p = Vector3.Lerp(a.position, b.position, k);
                q = Quaternion.Slerp(a.rotation, b.rotation, k);
            }
            else
            {
                var last = poses[poses.Count - 1];
                float ahead = Mathf.Clamp((float)(t - last.time), 0f, maxExtrapolation);
                p = last.position + last.velocity * ahead;
                q = last.rotation;
            }
            while (poses.Count > 2 && poses[1].time < t - 0.5) poses.RemoveAt(0);
            raft.Body.position = p;
            raft.Body.rotation = q;
            raft.transform.SetPositionAndRotation(p, q);
        }
    }
}
