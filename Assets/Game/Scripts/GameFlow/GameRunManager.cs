using System;
using System.Collections.Generic;
using UnityEngine;
using FartOnline.Obstacles;
using FartOnline.Game.Endless;

namespace FartOnline.Game
{
    public enum RunState : byte
    {
        MainMenu, Lobby, Ready, Countdown, Playing, Checkpoint, Wipeout, Respawning, Finished, Paused, Disconnected,
        /// <summary>Endless game: the raft hit an obstacle (or wiped out). Appended last: the byte value is replicated.</summary>
        GameOver
    }

    /// <summary>Per-run statistics shown on the results screen.</summary>
    [Serializable]
    public class RunStats
    {
        public float elapsed;
        public int checkpointIndex = -1;
        public int checkpointCount;
        public int wipeouts;
        public int thrustActivations;
        public float distance;
        public float progress01;
        public bool finished;
        /// <summary>Endless game: metres travelled down the river (the score).</summary>
        public float score;
        /// <summary>Endless game: what the raft crashed into.</summary>
        public string crashReason = "";
        public int[] activationsPerSeat = new int[4];
        public float[] thrustSecondsPerSeat = new float[4];

        public void Clear(int checkpoints)
        {
            elapsed = distance = progress01 = score = 0f;
            crashReason = "";
            checkpointIndex = -1;
            checkpointCount = checkpoints;
            wipeouts = thrustActivations = 0;
            finished = false;
            Array.Clear(activationsPerSeat, 0, 4);
            Array.Clear(thrustSecondsPerSeat, 0, 4);
        }
    }

    /// <summary>
    /// The run's explicit state machine. It owns the timer and the statistics, enables or disables thrust, and reacts
    /// to checkpoint / finish / wipeout events from the authoritative raft. Only legal transitions are accepted
    /// (see <see cref="Allowed"/>). Online, the server's instance is authoritative and the network layer mirrors
    /// <see cref="State"/>, <see cref="Stats"/> and the timer to clients through <see cref="ApplyReplicated"/>.
    /// It deliberately does not do UI, input, VFX or networking itself.
    /// </summary>
    public class GameRunManager : MonoBehaviour
    {
        public SharedRaftController raft;
        public RaftRecoveryManager recovery;
        public CheckpointManager checkpoints;
        [Tooltip("Where a new run starts")] public Transform startPoint;
        public float countdownSeconds = 3f;
        [Tooltip("Seconds the 'Checkpoint!' state lasts (thrust stays enabled)")] public float checkpointBanner = 1.5f;
        [Tooltip("Endless game: the infinite river (null on the fixed course)")] public EndlessRiver endless;

        public RunState State { get; private set; } = RunState.MainMenu;
        public RunStats Stats { get; } = new RunStats();
        public float CountdownRemaining { get; private set; }
        public bool IsAuthority => raft == null || raft.IsAuthority;
        /// <summary>True while a network session is active (pause must not stop time for others).</summary>
        public bool Networked { get; set; }

        public event Action<RunState, RunState> StateChanged;
        public event Action<Checkpoint> CheckpointReached;
        public event Action Finished;
        /// <summary>Endless game: the run ended (crash reason).</summary>
        public event Action<string> GameOver;

        public bool Endless => endless != null;
        /// <summary>Endless game: the seed of the current river (chosen by the authority, replicated to clients).</summary>
        public int RunSeed { get; private set; }
        /// <summary>Endless game: best score on this machine.</summary>
        public float BestScore { get; private set; }
        /// <summary>Endless game: this run beat the previous best.</summary>
        public bool NewBest { get; private set; }
        /// <summary>Clock for moving obstacles: the run time, smoothed on network clients (replicated in 0.1 s steps).</summary>
        public float ObstacleClock { get; private set; }
        const string BestKey = "fo.endless.best";

        RunState beforePause;
        float stateTime;
        Vector3 lastPos;

        static readonly Dictionary<RunState, RunState[]> Allowed = new Dictionary<RunState, RunState[]>
        {
            { RunState.MainMenu,     new[] { RunState.Lobby, RunState.Ready, RunState.Disconnected } },
            { RunState.Lobby,        new[] { RunState.Ready, RunState.MainMenu, RunState.Disconnected } },
            { RunState.Ready,        new[] { RunState.Countdown, RunState.Lobby, RunState.MainMenu, RunState.Disconnected } },
            { RunState.Countdown,    new[] { RunState.Playing, RunState.Paused, RunState.MainMenu, RunState.Lobby, RunState.Disconnected } },
            { RunState.Playing,      new[] { RunState.Checkpoint, RunState.Wipeout, RunState.Finished, RunState.GameOver, RunState.Paused, RunState.Ready, RunState.MainMenu, RunState.Lobby, RunState.Disconnected } },
            { RunState.Checkpoint,   new[] { RunState.Playing, RunState.Wipeout, RunState.Finished, RunState.GameOver, RunState.Paused, RunState.Ready, RunState.MainMenu, RunState.Lobby, RunState.Disconnected } },
            { RunState.Wipeout,      new[] { RunState.Respawning, RunState.GameOver, RunState.Paused, RunState.Ready, RunState.MainMenu, RunState.Lobby, RunState.Disconnected } },
            { RunState.Respawning,   new[] { RunState.Playing, RunState.GameOver, RunState.Paused, RunState.Ready, RunState.MainMenu, RunState.Lobby, RunState.Disconnected } },
            { RunState.Finished,     new[] { RunState.Ready, RunState.Lobby, RunState.MainMenu, RunState.Disconnected } },
            { RunState.GameOver,     new[] { RunState.Ready, RunState.Lobby, RunState.MainMenu, RunState.Disconnected } },
            { RunState.Paused,       new[] { RunState.Countdown, RunState.Playing, RunState.Checkpoint, RunState.Wipeout, RunState.Respawning, RunState.GameOver, RunState.Ready, RunState.MainMenu, RunState.Lobby, RunState.Disconnected } },
            { RunState.Disconnected, new[] { RunState.MainMenu } },
        };

        public static bool CanTransition(RunState from, RunState to) =>
            from == to || (Allowed.TryGetValue(from, out var list) && Array.IndexOf(list, to) >= 0);

        void Awake()
        {
            recovery.WipedOut += OnWipedOut;
            recovery.Respawned += OnRespawned;
            recovery.CheckpointReached += OnCheckpoint;
            recovery.Crashed += OnCrashed;
            recovery.endlessMode = Endless;
            BestScore = PlayerPrefs.GetFloat(BestKey, 0f);
            if (Endless) KinematicObstacle.ClockSource = () => ObstacleClock;
            FinishGate.AnyFinished += OnFinish;
            foreach (var s in raft.seats.Seats) s.Thrust.Burst += e => { if (IsAuthority && e.kind != ThrustKind.Cooldown) Stats.thrustActivations++; };
            ApplyThrustGate();
        }

        void Start()
        {
            // endless: a river for the title screen to float on (a run builds a fresh one with its own seed)
            if (Endless && endless.Planner == null)
            {
                endless.ResetRiver(UnityEngine.Random.Range(1, int.MaxValue));
                recovery.ResetForNewRun(endless.startPoint);
            }
        }

        void OnDestroy() => FinishGate.AnyFinished -= OnFinish;

        bool Go(RunState to)
        {
            if (State == to) return true;
            if (!CanTransition(State, to))
            {
                Debug.LogWarning($"[GameRun] illegal transition {State} -> {to}");
                return false;
            }
            var from = State;
            State = to;
            stateTime = 0f;
            ApplyThrustGate();
            StateChanged?.Invoke(from, to);
            return true;
        }

        void ApplyThrustGate()
        {
            if (raft != null) raft.thrustEnabled = State == RunState.Playing || State == RunState.Checkpoint;
        }

        // ------------------------------------------------------------------ commands (authority)

        public void EnterLobby() => Go(RunState.Lobby);
        public void ReturnToMenu() { Time.timeScale = 1f; Go(RunState.MainMenu); }
        public void Disconnect() { Time.timeScale = 1f; Go(RunState.Disconnected); }

        /// <summary>Puts the raft at the start with fresh stats; state Ready (players seated, waiting for countdown).</summary>
        public void PrepareRun()
        {
            if (!IsAuthority) return;
            Time.timeScale = 1f;
            if (Endless)
            {
                RunSeed = UnityEngine.Random.Range(1, int.MaxValue);
                endless.ResetRiver(RunSeed);
                startPoint = endless.startPoint;
                NewBest = false;
                ObstacleClock = 0f;
            }
            checkpoints.ResetAll();
            Stats.Clear(checkpoints.Count);
            recovery.ResetForNewRun(startPoint);
            foreach (var s in raft.seats.Seats) if (s.characterAnimator != null) s.characterAnimator.ResetPose();
            lastPos = raft.transform.position;
            CourseProgress.ResetRun();
            Go(RunState.Ready);
        }

        public void StartCountdown()
        {
            if (!IsAuthority) return;
            if (State != RunState.Ready) PrepareRun();
            CountdownRemaining = countdownSeconds;
            Go(RunState.Countdown);
        }

        /// <summary>Restart the whole level (results screen / pause menu).</summary>
        public void RestartRun() { PrepareRun(); StartCountdown(); }

        /// <summary>Respawn at the last checkpoint (pause menu / stuck prompt). Counts as a wipeout.</summary>
        public void RestartFromCheckpoint()
        {
            if (!IsAuthority) return;
            if (State == RunState.Paused) Resume();
            if (Endless) { if (State == RunState.GameOver) RestartRun(); return; }   // no checkpoints: R restarts after a crash
            if (State == RunState.Playing || State == RunState.Checkpoint) recovery.RequestRecovery();
        }

        public void Pause()
        {
            if (State == RunState.Paused || State == RunState.MainMenu || State == RunState.Lobby || State == RunState.Finished || State == RunState.GameOver) return;
            beforePause = State;
            if (Go(RunState.Paused) && !Networked) Time.timeScale = 0f;
        }

        public void Resume()
        {
            if (State != RunState.Paused) return;
            Time.timeScale = 1f;
            Go(beforePause);
        }

        // ------------------------------------------------------------------ events from the raft (authority)

        void OnCheckpoint(Checkpoint c)
        {
            if (!IsAuthority) return;
            if (c.index <= Stats.checkpointIndex) return;
            Stats.checkpointIndex = c.index;
            if (State == RunState.Playing) Go(RunState.Checkpoint);
            CheckpointReached?.Invoke(c);
        }

        void OnWipedOut(string reason)
        {
            if (!IsAuthority) return;
            Stats.wipeouts++;
            if (State == RunState.Paused) beforePause = RunState.Wipeout; else Go(RunState.Wipeout);
        }

        void OnRespawned()
        {
            if (!IsAuthority) return;
            if (State == RunState.Wipeout) { Go(RunState.Respawning); }
        }

        void OnCrashed(string reason)
        {
            if (!IsAuthority || State == RunState.GameOver) return;
            if (State != RunState.Playing && State != RunState.Checkpoint && State != RunState.Wipeout && State != RunState.Respawning && State != RunState.Paused) return;
            Time.timeScale = 1f;
            Stats.crashReason = reason;
            Stats.wipeouts++;
            RecordBest();
            Go(RunState.GameOver);
            GameOver?.Invoke(reason);
        }

        /// <summary>Saves the score as the best on this machine if it beats it.</summary>
        void RecordBest()
        {
            if (Stats.score > BestScore + 0.5f)
            {
                NewBest = true;
                BestScore = Stats.score;
                PlayerPrefs.SetFloat(BestKey, BestScore);
                PlayerPrefs.Save();
            }
        }

        void OnFinish(FinishGate g)
        {
            if (!IsAuthority || State == RunState.Finished) return;
            if (State != RunState.Playing && State != RunState.Checkpoint) return;
            Stats.finished = true;
            Stats.progress01 = 1f;
            Go(RunState.Finished);
            Finished?.Invoke();
        }

        // ------------------------------------------------------------------ tick

        void Update()
        {
            stateTime += Time.deltaTime;
            bool clockRuns = State == RunState.Playing || State == RunState.Checkpoint || State == RunState.Wipeout || State == RunState.Respawning;
            if (!IsAuthority)
            {
                // replicated elapsed time arrives in 0.1 s steps: advance locally and pull gently toward it
                if (clockRuns) ObstacleClock += Time.deltaTime;
                float err = Stats.elapsed - ObstacleClock;
                ObstacleClock = Mathf.Abs(err) > 0.4f ? Stats.elapsed : ObstacleClock + err * Mathf.Clamp01(Time.deltaTime * 2f);
                return;
            }
            switch (State)
            {
                case RunState.Countdown:
                    CountdownRemaining -= Time.deltaTime;
                    if (CountdownRemaining <= 0f) { CountdownRemaining = 0f; Go(RunState.Playing); }
                    break;
                case RunState.Checkpoint:
                    if (stateTime > checkpointBanner) Go(RunState.Playing);
                    break;
                case RunState.Respawning:
                    if (stateTime > 0.5f) Go(RunState.Playing);
                    break;
            }
            bool running = State == RunState.Playing || State == RunState.Checkpoint || State == RunState.Wipeout || State == RunState.Respawning;
            if (running)
            {
                Stats.elapsed += Time.deltaTime;
                Vector3 p = raft.transform.position;
                if (State == RunState.Playing || State == RunState.Checkpoint) Stats.distance += Vector3.Distance(p, lastPos);
                lastPos = p;
                Stats.progress01 = checkpoints.Progress01(p);
                if (Endless) Stats.score = Mathf.Max(Stats.score, endless.Score(p));
                foreach (var s in raft.seats.Seats)
                {
                    Stats.activationsPerSeat[s.seatIndex] = s.Thrust.Activations;
                    Stats.thrustSecondsPerSeat[s.seatIndex] = s.Thrust.ThrustSeconds;
                }
            }
            else lastPos = raft.transform.position;
            ObstacleClock = Stats.elapsed;
        }

        /// <summary>Network clients, before <see cref="ApplyReplicated"/>: the server's river seed and score. A new seed
        /// rebuilds the river so every client sees the same obstacles.</summary>
        public void ApplyReplicatedEndless(int seed, float score)
        {
            if (!Endless) return;
            if (seed != 0 && seed != endless.Seed)
            {
                RunSeed = seed;
                endless.ResetRiver(seed);
                NewBest = false;
            }
            Stats.score = score;
        }

        /// <summary>Network clients: mirror the server's state (no transition checks: the server already validated them).</summary>
        public void ApplyReplicated(RunState s, float elapsed, int checkpoint, int wipeouts, int thrusts, float progress, float countdown)
        {
            if (s != State)
            {
                var from = State;
                State = s;
                stateTime = 0f;
                if (s == RunState.GameOver) RecordBest();
                StateChanged?.Invoke(from, s);
            }
            Stats.elapsed = elapsed;
            Stats.checkpointIndex = checkpoint;
            Stats.wipeouts = wipeouts;
            Stats.thrustActivations = thrusts;
            Stats.progress01 = progress;
            Stats.finished = s == RunState.Finished;
            CountdownRemaining = countdown;
        }
    }
}
