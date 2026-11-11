using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Unity.Netcode;

namespace FartOnline.Game.Net
{
    /// <summary>
    /// Command-line driven multiplayer test harness for built players (does nothing without the -fo-net argument).
    ///   -fo-net host|join   -fo-addr 127.0.0.1   -fo-port 7777   -fo-name Bob
    ///   -fo-wait 1          host: number of remote clients to wait for before starting the run
    ///   -fo-secs 40         quit after this many seconds
    ///   -fo-pattern back|right|idle   where the bot sits and how it farts
    ///   -fo-delay 120 -fo-drop 0.1    client: simulated input latency (ms) and loss
    ///   -fo-log path        line-based log (state every second) for the test report
    ///   -fo-restart 3       host: restart the run this many seconds after a game over (endless)
    ///   -fo-net room        AWS-style room through the room service: -fo-rooms http://127.0.0.1:8080 (service URL),
    ///                       -fo-name bob (dev login "dev:bob", needs DEV_AUTH=1 on the service), -fo-code ABC123 to join
    ///                       an existing room (omit to create one; the code is logged as "ROOM CODE=..."), -fo-wait N
    ///                       (the leader starts once N players are in), -fo-voice tone (push a 440 Hz test tone through
    ///                       voice chat; every bot logs voice frames sent / received)
    /// The web build reads the same options from the page URL: index.html?fo-net=join&fo-addr=127.0.0.1&fo-secs=60
    /// (it logs to the browser console instead of a file).
    /// The bot plays through the same input path as a human (a scripted IThrustInputSource feeding the local seat).
    /// </summary>
    public class NetAutoTest : MonoBehaviour
    {
        public NetworkSessionManager sessionManager;
        public GameRunManager run;
        public SharedRaftController raft;
        NetworkRaftController netRaft => NetworkRaftController.Instance;

        string role, logPath, pattern = "back";
        float seconds = 40f;
        int wait = 1;
        StreamWriter log;
        BotInput bot;

        class BotInput : IThrustInputSource
        {
            public PlayerInputState state;
            public PlayerInputState ReadInput() => state;
        }

        static string[] args;

        static string Arg(string name, string def = null)
        {
            if (args == null)
            {
                var list = new System.Collections.Generic.List<string>(System.Environment.GetCommandLineArgs());
                string url = Application.absoluteURL ?? "";
                int q = url.IndexOf('?');
                if (q >= 0)
                    foreach (var kv in url.Substring(q + 1).Split('&'))
                    {
                        var parts = kv.Split('=');
                        if (parts.Length != 2) continue;
                        list.Add("-" + Uri.UnescapeDataString(parts[0]));
                        list.Add(Uri.UnescapeDataString(parts[1]));
                    }
                args = list.ToArray();
            }
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return def;
        }

        float restartAfter = -1f, gameOverAt = -1f;
        int gameOvers, restarts, lastSeed;

        void Start()
        {
            role = Arg("-fo-net");
            if (string.IsNullOrEmpty(role)) { enabled = false; return; }
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            logPath = Arg("-fo-log", Path.Combine(Application.persistentDataPath, "netlog_" + role + ".txt"));
            seconds = float.Parse(Arg("-fo-secs", "40"), System.Globalization.CultureInfo.InvariantCulture);
            wait = int.Parse(Arg("-fo-wait", "1"));
            pattern = Arg("-fo-pattern", role == "host" ? "back" : "right");
            NetworkRaftController.SimulatedInputDelayMs = int.Parse(Arg("-fo-delay", "0"));
            NetworkRaftController.SimulatedInputDrop = float.Parse(Arg("-fo-drop", "0"), System.Globalization.CultureInfo.InvariantCulture);
            sessionManager.playerName = Arg("-fo-name", role == "host" ? "HostBot" : "ClientBot");
            sessionManager.listenAddress = Arg("-fo-listen", sessionManager.listenAddress);
            restartAfter = float.Parse(Arg("-fo-restart", "-1"), System.Globalization.CultureInfo.InvariantCulture);
#if !UNITY_WEBGL || UNITY_EDITOR
            log = new StreamWriter(logPath, false) { AutoFlush = true };
#endif
            Log($"start role={role} pattern={pattern} delay={NetworkRaftController.SimulatedInputDelayMs}ms drop={NetworkRaftController.SimulatedInputDrop}");
            StartCoroutine(Run());
        }

        void Log(string s)
        {
            string line = $"{Time.realtimeSinceStartup:F1}s {s}";
            log?.WriteLine(line);
            Debug.Log("[NetAutoTest] " + line);
        }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(1f);
            ushort port = ushort.Parse(Arg("-fo-port", "7777"));
            bot = new BotInput();
            if (role == "room")
            {
                if (Arg("-fo-voice") == "tone") { FartOnline.Game.Online.VoiceChat.TestTone = true; }
                string svc = Arg("-fo-rooms", "http://127.0.0.1:8080").TrimEnd('/');
                string code = Arg("-fo-code", "");
                string body = string.IsNullOrEmpty(code) ? "{}" : "{\"code\":\"" + code + "\"}";
                FartOnline.Game.Online.RoomTicket ticket = null;
                using (var req = new UnityEngine.Networking.UnityWebRequest(svc + (string.IsNullOrEmpty(code) ? "/rooms" : "/rooms/join"), "POST"))
                {
                    req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                    req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", "Bearer dev:" + sessionManager.playerName);
                    yield return req.SendWebRequest();
                    ticket = JsonUtility.FromJson<FartOnline.Game.Online.RoomTicket>(req.downloadHandler.text ?? "{}");
                    if (ticket == null || string.IsNullOrEmpty(ticket.ticket)) { Log("ROOM REQUEST FAILED " + req.responseCode + " " + req.downloadHandler.text); Quit(); yield break; }
                }
                Log($"ROOM CODE={ticket.code} host={ticket.host}:{ticket.port} tls={ticket.tls}");
                if (!sessionManager.JoinRoom(ticket)) { Log("JOIN FAILED"); Quit(); yield break; }
                float until = Time.realtimeSinceStartup + 20f;
                while (!NetworkManager.Singleton.IsConnectedClient && Time.realtimeSinceStartup < until) yield return null;
                Log(NetworkManager.Singleton.IsConnectedClient ? "connected" : "CONNECT TIMEOUT " + sessionManager.CurrentRoom);
                while (netRaft == null && Time.realtimeSinceStartup < until) yield return null;
                if (netRaft != null) netRaft.SetLocalInput(bot); else Log("NO RAFT SYNC OBJECT");
                until = Time.realtimeSinceStartup + 40f;
                while (netRaft != null && netRaft.PlayerCount < wait && Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForSeconds(1f);
                Log($"players={(netRaft != null ? netRaft.PlayerCount : 0)} leader={sessionManager.IsLeader} state={run.State}");
                if (sessionManager.IsLeader && run.State == RunState.Lobby) { Log("leader starts the run"); netRaft.RequestRunCommandRpc(2); }
            }
            else if (role == "host")
            {
                if (!sessionManager.Host("127.0.0.1", port)) { Log("HOST FAILED"); Quit(); yield break; }
                var seat = netRaft.LocalSeat;
                seat.Occupy(seat.PlayerName, seat.PlayerColor, bot, true, false, seat.OwnerClientId);   // bot drives the host seat
                float until = Time.realtimeSinceStartup + 40f;
                while (netRaft != null && netRaft.PlayerCount < wait + 1 && Time.realtimeSinceStartup < until) yield return null;
                Log($"players={netRaft.PlayerCount}, starting run");
                sessionManager.StartRun();
            }
            else
            {
                if (!sessionManager.Join(Arg("-fo-addr", "127.0.0.1"), port)) { Log("JOIN FAILED"); Quit(); yield break; }
                float until = Time.realtimeSinceStartup + 20f;
                while (!NetworkManager.Singleton.IsConnectedClient && Time.realtimeSinceStartup < until) yield return null;
                Log(NetworkManager.Singleton.IsConnectedClient ? "connected" : "CONNECT TIMEOUT");
                while (netRaft == null && Time.realtimeSinceStartup < until) yield return null;
                if (netRaft != null) netRaft.SetLocalInput(bot); else Log("NO RAFT SYNC OBJECT");
            }

            float start = Time.realtimeSinceStartup, nextLog = 0f;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                float t = Time.realtimeSinceStartup - start;
                Drive(t);
                if (t >= nextLog) { nextLog += 1f; Snapshot(); }
                WatchEndless();
                yield return null;
            }
            Log("END " + Summary());
            sessionManager.Leave();
            yield return new WaitForSeconds(0.5f);
            Quit();
        }

        void Drive(float t)
        {
            var seat = netRaft != null ? netRaft.LocalSeat : null;
            if (seat == null || pattern == "idle") { bot.state.fartHeld = false; bot.state.move = 0f; return; }
            float target = pattern == "back" ? 180f : 90f;                      // "right": sits on the right side (pushes left)
            if (t > 20f && pattern == "right") target = 200f;                    // later walks round to the back to help
            float d = Mathf.DeltaAngle(seat.Angle, target);
            bot.state.move = Mathf.Abs(d) > 6f ? -Mathf.Sign(d) : 0f;
            bool fart = Mathf.Abs(d) < 15f && (t % 2.2f) < 1.4f;
            if (fart && !bot.state.fartHeld) bot.state.tapSeq++;
            bot.state.fartHeld = fart;
            if (Mathf.FloorToInt(t) % 9 == 8 && Mathf.Repeat(t, 1f) < 0.02f) bot.state.brakeSeq++;
        }

        /// <summary>Endless: logs every game over (with the replicated crash reason) and river change; the host restarts.</summary>
        void WatchEndless()
        {
            if (!run.Endless) return;
            if (run.RunSeed != lastSeed) { lastSeed = run.RunSeed; Log($"river seed={run.RunSeed} chunks={run.endless.ChunkCount} rows={(run.endless.Planner != null ? run.endless.Planner.Rows.Count : 0)}"); }
            if (run.State == RunState.GameOver)
            {
                if (gameOverAt < 0f)
                {
                    gameOverAt = Time.realtimeSinceStartup;
                    gameOvers++;
                    Log($"GAME OVER #{gameOvers} score={run.Stats.score:F0} crash='{run.Stats.crashReason}' seed={run.RunSeed}");
                }
                if (restartAfter >= 0f && Time.realtimeSinceStartup - gameOverAt > restartAfter && gameOvers > restarts)
                {
                    if (role == "host") { restarts++; Log($"host restarts the run (#{restarts})"); run.RestartRun(); }
                    else if (role == "room" && sessionManager.IsLeader) { restarts++; Log($"leader restarts the run (#{restarts})"); netRaft.RequestRunCommandRpc(1); }
                }
            }
            else gameOverAt = -1f;
        }

        string Summary()
        {
            var nm = NetworkManager.Singleton;
            return $"state={run.State} seed={run.RunSeed} score={run.Stats.score:F0} clock={run.ObstacleClock:F2} gameOvers={gameOvers} elapsed={run.Stats.elapsed:F1} cp={run.Stats.checkpointIndex} wipeouts={run.Stats.wipeouts} farts={run.Stats.thrustActivations} " +
                   (netRaft != null ? $"players={netRaft.PlayerCount} inputsSent={netRaft.InputsSent} burstsReceived={netRaft.BurstsReceived} posesReceived={netRaft.PosesReceived} " : "sync=none ") +
                   $"connected={(nm != null && nm.IsListening)}";
        }

        void Snapshot()
        {
            var p = raft.transform.position;
            var sb = new System.Text.StringBuilder();
            foreach (var s in raft.seats.Seats)
                if (s.IsOccupied)
                    sb.Append($" [{s.seatIndex + 1}:{s.PlayerName} a={s.Angle:F0} {(s.Thrust.State.sustaining ? "FART" : "-")} gas={s.Thrust.State.gas01:F2}]");
            var nm = NetworkManager.Singleton;
            string rtt = nm != null && nm.IsConnectedClient && !nm.IsServer
                ? nm.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().GetCurrentRtt(NetworkManager.ServerClientId) + "ms" : "-";
            var voice = FartOnline.Game.Online.VoiceChat.Instance;
            string v = voice != null ? $" voiceSent={voice.FramesSent} voiceRecv={voice.FramesReceived}" : "";
            if (voice != null && FartOnline.Game.Online.VoiceChat.TestTone) voice.PushHeld = Mathf.FloorToInt(Time.realtimeSinceStartup) % 4 < 2;   // talk 2 s, pause 2 s
            Log($"pos=({p.x:F2},{p.y:F2},{p.z:F2}) v={raft.Body.linearVelocity.magnitude:F2} kinematic={raft.Body.isKinematic} rtt={rtt} {Summary()}{sb}{v}");
        }

        void Quit()
        {
            log?.Flush();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
