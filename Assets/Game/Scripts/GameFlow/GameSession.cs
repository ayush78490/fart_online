using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FartOnline.Game
{
    public enum SessionMode { None, Solo, LocalCoop, OnlineHost, OnlineClient }

    /// <summary>
    /// Starts and ends play sessions on this machine: solo (one human), local co-op (2–4 people on
    /// one computer: KeyboardLeft, KeyboardRight, gamepads) and, through the network layer, online sessions.
    /// It seats players, wires local input to the run (pause, interact/reset) and hands the run to GameRunManager.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public InputActionAsset inputActions;
        public SharedRaftController raft;
        public RaftRecoveryManager recovery;
        public GameRunManager run;
        public Camera gameplayCamera;
        public SoloAssist soloAssist = SoloAssist.Standard;
        [Tooltip("Development: skip the menu and start solo on Play")] public bool autoStartSolo = false;

        public SessionMode Mode { get; private set; }
        public readonly List<LocalPlayerInput> LocalPlayers = new List<LocalPlayerInput>();
        public event Action<SessionMode> ModeChanged;

        public static GameSession Instance { get; private set; }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (autoStartSolo) StartSolo(soloAssist);
        }

        // ------------------------------------------------------------------ local sessions

        /// <summary>Solo run: one real player alone on the raft (no AI crew).</summary>
        public void StartSolo(SoloAssist assist)
        {
            EndLocal();
            soloAssist = assist;
            AddLocalPlayer(0, "", null, "Player 1");
            raft.SetAssist(assist);
            SetMode(SessionMode.Solo);
            run.PrepareRun();
            run.StartCountdown();
        }

        /// <summary>Local co-op with explicit schemes, e.g. {"KeyboardLeft","KeyboardRight","Gamepad"}. Gamepads are taken in order.</summary>
        public void StartLocalCoop(int players)
        {
            EndLocal();
            players = Mathf.Clamp(players, 1, 4);
            var pads = Gamepad.all;
            int pad = 0;
            for (int i = 0; i < players; i++)
            {
                string scheme; InputDevice dev = null;
                if (i == 0) scheme = "KeyboardLeft";
                else if (i == 1 && pads.Count < players - 1) scheme = "KeyboardRight";
                else if (pad < pads.Count) { scheme = "Gamepad"; dev = pads[pad++]; }
                else scheme = i == 1 ? "KeyboardRight" : "KeyboardLeft";
                AddLocalPlayer(i, scheme, dev, "Player " + (i + 1));
            }
            raft.SetAssist(SoloAssist.Off);
            SetMode(SessionMode.LocalCoop);
            run.PrepareRun();
            run.StartCountdown();
        }

        public void EndSession()
        {
            EndLocal();
            SetMode(SessionMode.None);
            run.ReturnToMenu();
        }

        /// <summary>Used by the network layer: creates the local input for this machine's own player.</summary>
        public LocalPlayerInput CreateLocalInputOnly(string scheme)
        {
            var go = new GameObject("LocalInput_Net");
            go.transform.SetParent(transform, false);
            var input = go.AddComponent<LocalPlayerInput>();
            input.Init(inputActions, scheme, null, raft.transform, gameplayCamera);
            Wire(input);
            LocalPlayers.Add(input);
            return input;
        }

        public void SetModeExternal(SessionMode m) => SetMode(m);

        void SetMode(SessionMode m)
        {
            Mode = m;
            run.Networked = m == SessionMode.OnlineHost || m == SessionMode.OnlineClient;
            ModeChanged?.Invoke(m);
        }

        LocalPlayerInput AddLocalPlayer(int seatIndex, string scheme, InputDevice device, string name)
        {
            var go = new GameObject("LocalInput_P" + (seatIndex + 1));
            go.transform.SetParent(transform, false);
            var input = go.AddComponent<LocalPlayerInput>();
            input.Init(inputActions, scheme, device, raft.transform, gameplayCamera);
            Wire(input);
            LocalPlayers.Add(input);
            var seat = raft.seats.Seats[seatIndex];
            seat.Occupy(name, RaftSeatManager.PlayerColors[seatIndex], input, true, false, 0);
            return input;
        }

        void Wire(LocalPlayerInput input)
        {
            input.PausePressed += TogglePause;
            input.InteractPressed += Interact;
        }

        GameplayHUD hud;
        GameplayHUD Hud => hud != null ? hud : (hud = FindAnyObjectByType<GameplayHUD>());
        bool Online => Mode == SessionMode.OnlineHost || Mode == SessionMode.OnlineClient;

        public void TogglePause()
        {
            if (Online) { if (Hud != null) Hud.ToggleLocalPause(); return; }   // never freeze a shared online run
            if (run.State == RunState.Paused) run.Resume(); else run.Pause();
        }

        /// <summary>Interact: reset to checkpoint when stuck, replay after finishing.</summary>
        public void Interact()
        {
            if (run.State == RunState.Finished && (Mode == SessionMode.Solo || Mode == SessionMode.LocalCoop)) { run.RestartRun(); return; }
            if (!recovery.StuckPrompt) return;
            if (Online && Hud != null && Hud.RunCommand != null) Hud.RunCommand(0); else run.RestartFromCheckpoint();
        }

        void EndLocal()
        {
            foreach (var p in LocalPlayers) if (p != null) Destroy(p.gameObject);
            LocalPlayers.Clear();
            // any other input driver parented here (test bots, leftovers saved into a scene) goes too
            foreach (Transform c in transform) if (c.GetComponent<IThrustInputSource>() != null) Destroy(c.gameObject);
            raft.seats.VacateAll();
        }
    }
}
