using System.Collections.Generic;
using Concentus.Enums;
using Concentus.Structs;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FartOnline.Game.Online
{
    /// <summary>
    /// Real-time voice chat for online rooms, carried by the game's own connection (no extra servers, no fees):
    ///  - the microphone is read at 16 kHz mono and cut into 20 ms frames, each compressed with Opus (Concentus,
    ///    pure C#) at ~16 kbit/s into ~40 bytes;
    ///  - a client sends its frames to the server ("fo.vu", unreliable); the server forwards each one to every other
    ///    player ("fo.vd", with the speaker's client id). A dedicated AWS room only forwards; a LAN host also plays
    ///    and speaks itself;
    ///  - each remote speaker gets a <see cref="VoiceSpeaker"/> (jitter buffer + packet-loss concealment);
    ///  - modes (Settings, saved): Push to talk (TALK button / hold V), Open mic (with a level gate), Off;
    ///    any player can be muted locally (tap their card).
    /// Android asks for the microphone permission the first time voice is used. The web build has no voice yet
    /// (Unity's Microphone class and streamed audio clips are not available in WebGL).
    /// </summary>
    public class VoiceChat : MonoBehaviour
    {
        public enum Mode { PushToTalk, OpenMic, Off }
        public const int SampleRate = 16000, FrameSamples = 320;      // 20 ms
        const string ModeKey = "fo.voice.mode", UpMessage = "fo.vu", DownMessage = "fo.vd";
        const int MaxPacket = 200;
        const float OpenMicGate = 0.012f, OpenMicHangover = 0.35f;

        public static VoiceChat Instance { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool Supported => false;
#else
        public static bool Supported => true;
#endif

        public static Mode CurrentMode
        {
            get => (Mode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, 2);
            set { PlayerPrefs.SetInt(ModeKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Held by the TALK button / V key.</summary>
        public bool PushHeld { get; set; }
        public bool Transmitting { get; private set; }
        public bool MicDenied { get; private set; }
        public float MicLevel { get; private set; }
        /// <summary>Tests (NetAutoTest -fo-voice tone): send a 440 Hz tone instead of the microphone.</summary>
        public static bool TestTone;
        public int FramesSent { get; private set; }
        public int FramesReceived { get; private set; }
        float toneDue;
        long toneSample;

        NetworkManager nm;
        CustomMessagingManager registeredOn;
        OpusEncoder encoder;
        AudioClip mic;
        string micDevice;
        int micRead;
        float[] micChunk = new float[4096];
        readonly short[] frame = new short[FrameSamples];
        int frameFill;
        readonly byte[] packet = new byte[MaxPacket];
        byte seq;
        float lastLoud = -10f;
        bool permissionAsked;
        readonly Dictionary<ulong, VoiceSpeaker> speakers = new Dictionary<ulong, VoiceSpeaker>();
        readonly HashSet<ulong> muted = new HashSet<ulong>();
        readonly List<ulong> targets = new List<ulong>();
        byte[] scratch = new byte[MaxPacket];

        void Awake()
        {
            Instance = this;
            nm = GetComponentInParent<NetworkManager>();
            if (nm == null) nm = NetworkManager.Singleton;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StopMic();
        }

        bool Online => nm != null && nm.IsListening && !nm.ShutdownInProgress;
        bool IsDedicatedServer => Online && nm.IsServer && !nm.IsClient;

        void Update()
        {
            if (nm == null) nm = NetworkManager.Singleton;
            RegisterHandlers();
            if (!Online) { Transmitting = false; StopMic(); ClearSpeakers(); return; }
            if (IsDedicatedServer || !Supported || CurrentMode == Mode.Off) { Transmitting = false; StopMic(); return; }

            if (TestTone) { SendTone(); return; }
            if (!EnsurePermission()) { Transmitting = false; return; }
            if (mic == null) StartMic();
            if (mic == null) { Transmitting = false; return; }
            Capture();
        }

        void Capture()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)

            // read whatever the microphone wrote since last frame
            int pos = Microphone.GetPosition(micDevice);
            int total = mic.samples;
            int available = (pos - micRead + total) % total;
            bool wantSend = CurrentMode == Mode.OpenMic || PushHeld;
            float peak = 0f;
            while (available > 0)
            {
                int n = Mathf.Min(available, micChunk.Length, total - micRead);
                if (n <= 0) break;
                if (micChunk.Length < n) micChunk = new float[n];
                mic.GetData(micChunk, micRead);
                for (int i = 0; i < n; i++)
                {
                    float v = micChunk[i];
                    float a = v < 0f ? -v : v;
                    if (a > peak) peak = a;
                    frame[frameFill++] = (short)Mathf.Clamp(v * 32767f, -32768f, 32767f);
                    if (frameFill == FrameSamples)
                    {
                        frameFill = 0;
                        float rms = Rms(frame);
                        if (rms > OpenMicGate) lastLoud = Time.unscaledTime;
                        bool gateOpen = CurrentMode != Mode.OpenMic || Time.unscaledTime - lastLoud < OpenMicHangover;
                        if (wantSend && gateOpen) EncodeAndSend();
                    }
                }
                micRead = (micRead + n) % total;
                available -= n;
            }
            MicLevel = Mathf.Lerp(MicLevel, peak, 0.5f);
            Transmitting = wantSend && (CurrentMode != Mode.OpenMic || Time.unscaledTime - lastLoud < OpenMicHangover);
#endif
        }

        void SendTone()
        {
            EnsureEncoder();
            bool wantSend = CurrentMode == Mode.OpenMic || PushHeld;
            Transmitting = wantSend;
            toneDue += Time.unscaledDeltaTime * SampleRate;
            while (toneDue >= FrameSamples)
            {
                toneDue -= FrameSamples;
                for (int i = 0; i < FrameSamples; i++, toneSample++)
                    frame[i] = (short)(6000 * System.Math.Sin(2 * System.Math.PI * 440 * toneSample / SampleRate));
                if (wantSend) EncodeAndSend();
            }
        }

        void EnsureEncoder()
        {
            if (encoder != null) return;
            encoder = OpusEncoder.Create(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = 16000;
            encoder.Complexity = 5;                                // light on phone CPUs
            encoder.UseInbandFEC = true;
            encoder.PacketLossPercent = 10;
        }

        static float Rms(short[] s)
        {
            double sum = 0;
            for (int i = 0; i < s.Length; i++) { double v = s[i] / 32768.0; sum += v * v; }
            return (float)System.Math.Sqrt(sum / s.Length);
        }

        // ------------------------------------------------------------------ microphone
        bool EnsurePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone)) { MicDenied = false; return true; }
            if (!permissionAsked)
            {
                permissionAsked = true;
                var cb = new UnityEngine.Android.PermissionCallbacks();
                cb.PermissionDenied += _ => MicDenied = true;
                cb.PermissionGranted += _ => MicDenied = false;
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
            }
            return false;
#else
            return true;
#endif
        }

        void StartMic()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (Microphone.devices.Length == 0) { MicDenied = true; return; }
            micDevice = Microphone.devices[0];
            mic = Microphone.Start(micDevice, true, 1, SampleRate);
            if (mic == null) return;
            micRead = 0;
            frameFill = 0;
            EnsureEncoder();
            if (mic.frequency != SampleRate) Debug.LogWarning($"[Voice] microphone runs at {mic.frequency} Hz, expected {SampleRate}");
            Debug.Log($"[Voice] microphone '{micDevice}' started");
#endif
        }

        void StopMic()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (mic == null) return;
            Microphone.End(micDevice);
            mic = null;
            Debug.Log("[Voice] microphone stopped");
#endif
        }

        // ------------------------------------------------------------------ network
        void RegisterHandlers()
        {
            var cmm = Online ? nm.CustomMessagingManager : null;
            if (cmm == registeredOn) return;
            registeredOn = cmm;
            if (cmm == null) return;
            cmm.RegisterNamedMessageHandler(UpMessage, OnUp);
            cmm.RegisterNamedMessageHandler(DownMessage, OnDown);
        }

        void EncodeAndSend()
        {
            int len;
            try { len = encoder.Encode(frame, 0, FrameSamples, packet, 0, MaxPacket); }
            catch { return; }
            if (len <= 0) return;
            seq++;
            FramesSent++;
            if (nm.IsServer) Forward(nm.LocalClientId, seq, packet, len);     // LAN host: straight to everyone
            else
            {
                using (var w = new FastBufferWriter(len + 8, Allocator.Temp))
                {
                    w.WriteValueSafe(seq);
                    w.WriteValueSafe((ushort)len);
                    w.WriteBytesSafe(packet, len);
                    nm.CustomMessagingManager.SendNamedMessage(UpMessage, NetworkManager.ServerClientId, w, NetworkDelivery.Unreliable);
                }
            }
        }

        /// <summary>Server: a client's frame arrived; pass it on (and play it if this server is also a player).</summary>
        void OnUp(ulong sender, FastBufferReader r)
        {
            if (!nm.IsServer) return;
            if (!r.TryBeginRead(3)) return;
            r.ReadValue(out byte s);
            r.ReadValue(out ushort len);
            if (len == 0 || len > MaxPacket || !r.TryBeginRead(len)) return;
            r.ReadBytes(ref scratch, len);
            Forward(sender, s, scratch, len);
            if (nm.IsClient) Play(sender, s, scratch, len);
        }

        void Forward(ulong sender, byte s, byte[] data, int len)
        {
            targets.Clear();
            foreach (var id in nm.ConnectedClientsIds)
                if (id != sender && id != NetworkManager.ServerClientId) targets.Add(id);
            if (targets.Count == 0) return;
            using (var w = new FastBufferWriter(len + 16, Allocator.Temp))
            {
                w.WriteValueSafe(sender);
                w.WriteValueSafe(s);
                w.WriteValueSafe((ushort)len);
                w.WriteBytesSafe(data, len);
                nm.CustomMessagingManager.SendNamedMessage(DownMessage, targets, w, NetworkDelivery.Unreliable);
            }
        }

        /// <summary>Client: someone else's frame, relayed by the server.</summary>
        void OnDown(ulong _, FastBufferReader r)
        {
            if (!r.TryBeginRead(11)) return;
            r.ReadValue(out ulong speaker);
            r.ReadValue(out byte s);
            r.ReadValue(out ushort len);
            if (len == 0 || len > MaxPacket || !r.TryBeginRead(len)) return;
            r.ReadBytes(ref scratch, len);
            Play(speaker, s, scratch, len);
        }

        void Play(ulong speaker, byte s, byte[] data, int len)
        {
            if (speaker == nm.LocalClientId || IsDedicatedServer || !Supported || CurrentMode == Mode.Off) return;
            if (!speakers.TryGetValue(speaker, out var sp) || sp == null)
            {
                var go = new GameObject("VoiceSpeaker_" + speaker);
                go.transform.SetParent(transform, false);
                sp = go.AddComponent<VoiceSpeaker>();
                sp.Init(speaker);
                speakers[speaker] = sp;
            }
            sp.Volume = muted.Contains(speaker) ? 0f : 1f;
            sp.Push(s, data, len);
            FramesReceived++;
        }

        void ClearSpeakers()
        {
            if (speakers.Count == 0) return;
            foreach (var sp in speakers.Values) if (sp != null) Destroy(sp.gameObject);
            speakers.Clear();
            muted.Clear();
        }

        // ------------------------------------------------------------------ UI queries
        /// <summary>True while this client's voice was heard in the last 0.3 s (or it's us, transmitting).</summary>
        public bool IsTalking(ulong clientId)
        {
            if (!Online) return false;
            if (clientId == nm.LocalClientId) return Transmitting;
            return speakers.TryGetValue(clientId, out var sp) && sp != null && Time.unscaledTime - sp.LastHeard < 0.3f;
        }

        public bool IsMuted(ulong clientId) => muted.Contains(clientId);

        public void ToggleMute(ulong clientId)
        {
            if (!muted.Remove(clientId)) muted.Add(clientId);
            if (speakers.TryGetValue(clientId, out var sp) && sp != null) sp.Volume = muted.Contains(clientId) ? 0f : 1f;
        }
    }
}
