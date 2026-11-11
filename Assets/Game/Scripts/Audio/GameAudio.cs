using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Game
{
    public enum AudioCue
    {
        Fart, FartCharged, FartEmpty, Splash, RaftCollision, RaftCollisionHeavy, WoodCreak, PendulumWhoosh,
        Checkpoint, Wipeout, Respawn, Finish, Countdown, CountdownGo, UIClick,
        UIHover, UIBack, UIOpen
    }

    public enum MusicTrack { None, Menu, Gameplay }

    /// <summary>
    /// Event-driven sound hooks. Every cue has a volume and a minimum re-trigger interval so holding thrust or
    /// scraping a wall never spams sound. Clip lookup per cue: inspector clips, then every clip in
    /// <c>Resources/SFX/&lt;CueName&gt;/</c> (the bundled CC0 library, see Assets/Game/Audio/AUDIO_CREDITS.md), then a
    /// placeholder synthesised by <see cref="ProceduralAudio"/>. 3D one-shots come from a small pool of AudioSources.
    /// Also owns the music (menu / gameplay crossfade), the river ambience bed and one fart-stream voice per seat.
    /// Master / music / SFX volumes persist in PlayerPrefs.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        [System.Serializable]
        public class CueSettings
        {
            public AudioCue cue;
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume = 0.8f;
            [Tooltip("Minimum seconds between two plays of this cue (per seat for farts)")] public float minInterval = 0.08f;
            [Range(0f, 1f)] public float spatial = 0.7f;
            public Vector2 pitch = new Vector2(0.92f, 1.08f);
        }

        [Range(0f, 1f)] public float masterVolume = 0.9f;
        public CueSettings[] cues;
        public int voices = 16;
        [Tooltip("Seconds for music to crossfade between the menu and gameplay tracks")] public float musicFade = 1.2f;
        [Range(0f, 1f)] public float musicLevel = 0.42f;
        [Range(0f, 1f)] public float ambienceLevel = 0.35f;
        [Range(0f, 1f)] public float fartStreamLevel = 0.8f;

        public static GameAudio Instance { get; private set; }

        const string PrefMaster = "fo.vol.master", PrefMusic = "fo.vol.music", PrefSfx = "fo.vol.sfx";
        public static float MasterVolume { get => PlayerPrefs.GetFloat(PrefMaster, 0.9f); set { PlayerPrefs.SetFloat(PrefMaster, Mathf.Clamp01(value)); } }
        public static float MusicVolume { get => PlayerPrefs.GetFloat(PrefMusic, 0.7f); set { PlayerPrefs.SetFloat(PrefMusic, Mathf.Clamp01(value)); } }
        public static float SfxVolume { get => PlayerPrefs.GetFloat(PrefSfx, 1f); set { PlayerPrefs.SetFloat(PrefSfx, Mathf.Clamp01(value)); } }

        readonly Dictionary<AudioCue, CueSettings> map = new Dictionary<AudioCue, CueSettings>();
        readonly Dictionary<int, float> lastPlay = new Dictionary<int, float>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        int next;

        AudioSource[] music;          // two sources, crossfaded
        AudioClip menuClip, gameplayClip;
        MusicTrack track = MusicTrack.None;
        int activeMusic;
        AudioSource ambience;
        bool ambienceOn;
        AudioSource[] streams;
        AudioClip[] streamClips;
        readonly float[] streamTarget = new float[4];

        void Awake()
        {
            Instance = this;
            foreach (AudioCue c in System.Enum.GetValues(typeof(AudioCue))) map[c] = Defaults(c);
            if (cues != null) foreach (var c in cues) map[c.cue] = c;
            foreach (var c in map.Values)
            {
                if (c.clips != null && c.clips.Length > 0) continue;
                var loaded = Resources.LoadAll<AudioClip>("SFX/" + c.cue);
                c.clips = loaded.Length > 0 ? loaded : ProceduralAudio.ClipsFor(c.cue);
            }
            for (int i = 0; i < voices; i++)
            {
                var src = NewSource("Voice_" + i, false);
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 4f;
                src.maxDistance = 70f;
                pool.Add(src);
            }

            music = new[] { NewSource("Music_A", true), NewSource("Music_B", true) };
            foreach (var m in music) { m.spatialBlend = 0f; m.priority = 0; m.ignoreListenerPause = true; }
            menuClip = Resources.Load<AudioClip>("Music/Menu_UkuleleIsland");
            gameplayClip = Resources.Load<AudioClip>("Music/Gameplay_TropicalLoop");

            ambience = NewSource("RiverAmbience", true);
            ambience.spatialBlend = 0f;
            var river = Resources.LoadAll<AudioClip>("SFX/RiverLoop");
            if (river.Length > 0) ambience.clip = river[0];

            streamClips = Resources.LoadAll<AudioClip>("SFX/FartLoop");
            if (streamClips.Length == 0) streamClips = map[AudioCue.Fart].clips;
            streams = new AudioSource[4];
            for (int i = 0; i < streams.Length; i++)
            {
                var s = NewSource("FartStream_" + i, false);
                s.spatialBlend = 0.6f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 4f;
                s.maxDistance = 70f;
                streams[i] = s;
            }
        }

        void Start() => ObstacleWhoosh.Attach();

        AudioSource NewSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.volume = 0f;
            return src;
        }

        static CueSettings Defaults(AudioCue c)
        {
            var s = new CueSettings { cue = c, clips = new AudioClip[0] };
            switch (c)
            {
                case AudioCue.Fart: s.volume = 0.85f; s.pitch = new Vector2(0.9f, 1.12f); break;
                case AudioCue.FartCharged: s.volume = 1f; s.pitch = new Vector2(0.8f, 0.9f); break;
                case AudioCue.FartEmpty: s.volume = 0.6f; s.pitch = new Vector2(1.1f, 1.3f); break;
                case AudioCue.Splash: s.volume = 0.8f; break;
                case AudioCue.RaftCollision: s.volume = 0.7f; s.minInterval = 0.12f; break;
                case AudioCue.RaftCollisionHeavy: s.volume = 0.9f; s.minInterval = 0.25f; break;
                case AudioCue.WoodCreak: s.volume = 0.5f; s.minInterval = 0.6f; break;
                case AudioCue.PendulumWhoosh: s.volume = 0.6f; s.minInterval = 0.4f; s.spatial = 1f; break;
                case AudioCue.Checkpoint: case AudioCue.Finish: case AudioCue.Wipeout:
                    s.volume = 0.75f; s.spatial = 0f; s.pitch = Vector2.one; s.minInterval = 0.5f; break;
                case AudioCue.Respawn: s.volume = 0.5f; s.spatial = 0f; s.pitch = Vector2.one; break;
                case AudioCue.Countdown: s.volume = 0.6f; s.spatial = 0f; s.pitch = Vector2.one; s.minInterval = 0.3f; break;
                case AudioCue.CountdownGo: s.volume = 0.75f; s.spatial = 0f; s.pitch = Vector2.one; s.minInterval = 0.3f; break;
                case AudioCue.UIClick: s.volume = 0.55f; s.spatial = 0f; s.pitch = new Vector2(0.97f, 1.03f); s.minInterval = 0.04f; break;
                case AudioCue.UIHover: s.volume = 0.22f; s.spatial = 0f; s.pitch = new Vector2(0.97f, 1.05f); s.minInterval = 0.05f; break;
                case AudioCue.UIBack: s.volume = 0.5f; s.spatial = 0f; s.pitch = Vector2.one; break;
                case AudioCue.UIOpen: s.volume = 0.35f; s.spatial = 0f; s.pitch = Vector2.one; s.minInterval = 0.15f; break;
            }
            return s;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void Play(AudioCue cue, Vector3 position, float volume = 1f, int channel = 0)
        {
            if (Instance != null) Instance.PlayInternal(cue, position, volume, channel);
        }

        /// <summary>2D one-shot (menus, jingles): the listener position is used.</summary>
        public static void PlayUI(AudioCue cue, float volume = 1f)
        {
            if (Instance == null) return;
            var l = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Instance.PlayInternal(cue, l, volume, 0);
        }

        public static void SetMusic(MusicTrack t) { if (Instance != null) Instance.track = t; }
        public static void SetAmbience(bool on) { if (Instance != null) Instance.ambienceOn = on; }

        /// <summary>Held fart stream for one seat: plays a long recorded fart while on, fades out quickly when off.</summary>
        public static void FartStream(int seat, bool on, Vector3 position, float intensity = 1f)
        {
            if (Instance == null || seat < 0 || seat >= Instance.streams.Length) return;
            var s = Instance.streams[seat];
            s.transform.position = position;
            Instance.streamTarget[seat] = on ? Mathf.Clamp01(intensity) : 0f;
            if (on && !s.isPlaying && Instance.streamClips.Length > 0)
            {
                s.clip = Instance.streamClips[Random.Range(0, Instance.streamClips.Length)];
                s.pitch = Random.Range(0.94f, 1.06f) * (1f + seat * 0.06f);
                s.time = 0f;
                s.volume = 0f;
                s.Play();
            }
        }

        void PlayInternal(AudioCue cue, Vector3 position, float volume, int channel)
        {
            if (!map.TryGetValue(cue, out var s) || s.clips == null || s.clips.Length == 0) return;
            int key = (int)cue * 8 + channel;
            if (lastPlay.TryGetValue(key, out float t) && Time.unscaledTime - t < s.minInterval) return;
            lastPlay[key] = Time.unscaledTime;
            var src = pool[next];
            next = (next + 1) % pool.Count;
            src.transform.position = position;
            src.clip = s.clips[Random.Range(0, s.clips.Length)];
            src.volume = s.volume * volume * masterVolume * MasterVolume * SfxVolume;
            src.pitch = Random.Range(s.pitch.x, s.pitch.y) * (cue == AudioCue.Fart ? 1f + channel * 0.07f : 1f);
            src.spatialBlend = s.spatial;
            src.ignoreListenerPause = s.spatial == 0f;
            src.Play();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float master = masterVolume * MasterVolume;

            // music crossfade: the active source plays the wanted clip, the other fades out
            AudioClip want = track == MusicTrack.Menu ? menuClip : track == MusicTrack.Gameplay ? gameplayClip : null;
            var cur = music[activeMusic];
            if (cur.clip != want && want != null)
            {
                activeMusic = 1 - activeMusic;
                cur = music[activeMusic];
                cur.clip = want;
                cur.volume = 0f;
                cur.Play();
            }
            float step = dt / Mathf.Max(0.05f, musicFade);
            for (int i = 0; i < music.Length; i++)
            {
                var m = music[i];
                float target = i == activeMusic && want != null && m.clip == want ? musicLevel * master * MusicVolume : 0f;
                m.volume = Mathf.MoveTowards(m.volume, target, step * Mathf.Max(target, 0.3f));
                if (m.volume <= 0.0001f && i != activeMusic && m.isPlaying) m.Stop();
            }

            if (ambience.clip != null)
            {
                float target = ambienceOn ? ambienceLevel * master * SfxVolume : 0f;
                ambience.volume = Mathf.MoveTowards(ambience.volume, target, dt * 0.5f);
                if (ambience.volume > 0f && !ambience.isPlaying) ambience.Play();
                else if (ambience.volume <= 0f && ambience.isPlaying) ambience.Stop();
            }

            for (int i = 0; i < streams.Length; i++)
            {
                var s = streams[i];
                if (!s.isPlaying) continue;
                float target = streamTarget[i] * fartStreamLevel * master * SfxVolume;
                // fast attack, quick release so a released F key cuts the sound
                s.volume = Mathf.MoveTowards(s.volume, target, dt * (target > s.volume ? 8f : 6f));
                if (target <= 0f && s.volume <= 0.001f) s.Stop();
            }
        }
    }
}
