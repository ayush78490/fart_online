using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Fallback for cues without a recorded clip (the CC0 library in Resources/SFX normally covers all of them).
    /// Synthesises small placeholder clips at startup so every audio hook stays audible:fluttering low "brrt" farts (three variants), a charged sweep, an empty "pfft", splashes, wooden
    /// thunks, a creak, a whoosh, chimes, a slide-whistle wipeout and a finish fanfare. Deterministic (seeded).
    /// Replace any cue with real clips in the GameAudio inspector.
    /// </summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;
        static readonly Dictionary<AudioCue, AudioClip[]> cache = new Dictionary<AudioCue, AudioClip[]>();

        public static AudioClip[] ClipsFor(AudioCue cue)
        {
            if (cache.TryGetValue(cue, out var c)) return c;
            switch (cue)
            {
                case AudioCue.Fart: c = new[] { Fart("Fart_A", 0.42f, 92f, 31f, 1), Fart("Fart_B", 0.55f, 78f, 24f, 2), Fart("Fart_C", 0.34f, 108f, 38f, 3) }; break;
                case AudioCue.FartCharged: c = new[] { Sweep("FartCharged", 0.95f, 150f, 58f, 28f, 4) }; break;
                case AudioCue.FartEmpty: c = new[] { Noise("FartEmpty", 0.22f, 0.25f, 0.9f, 5) }; break;
                case AudioCue.Splash: c = new[] { Noise("Splash_A", 0.7f, 0.08f, 0.35f, 6), Noise("Splash_B", 0.55f, 0.06f, 0.45f, 7) }; break;
                case AudioCue.RaftCollision: c = new[] { Thunk("Thunk", 0.28f, 110f, 8) }; break;
                case AudioCue.RaftCollisionHeavy: c = new[] { Thunk("ThunkHeavy", 0.5f, 65f, 9) }; break;
                case AudioCue.WoodCreak: c = new[] { Creak("Creak", 0.65f, 10) }; break;
                case AudioCue.PendulumWhoosh: c = new[] { Noise("Whoosh", 0.8f, 0.4f, 0.15f, 11) }; break;
                case AudioCue.Checkpoint: c = new[] { Notes("Chime", new[] { 1046.5f, 1568f }, 0.16f, 0.5f, 0f) }; break;
                case AudioCue.Wipeout: c = new[] { Slide("Wipeout", 0.85f, 900f, 180f) }; break;
                case AudioCue.Respawn: c = new[] { Notes("Respawn", new[] { 523f, 659f, 784f }, 0.08f, 0.2f, 0f) }; break;
                case AudioCue.Finish: c = new[] { Notes("Fanfare", new[] { 523f, 659f, 784f, 1046.5f, 784f, 1046.5f }, 0.14f, 0.6f, 0.35f) }; break;
                case AudioCue.Countdown: c = new[] { Notes("Beep", new[] { 660f }, 0.15f, 0.1f, 0.2f) }; break;
                case AudioCue.CountdownGo: c = new[] { Notes("Go", new[] { 990f }, 0.35f, 0.25f, 0.2f) }; break;
                case AudioCue.UIClick: c = new[] { Thunk("Click", 0.04f, 1800f, 12) }; break;
                default: c = new AudioClip[0]; break;
            }
            cache[cue] = c;
            return c;
        }

        static AudioClip Make(string name, float[] data)
        {
            float peak = 1e-4f;
            foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            for (int i = 0; i < data.Length; i++) data[i] = data[i] / peak * 0.85f;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Env(float t, float len, float attack, float release) =>
            Mathf.Clamp01(t / attack) * Mathf.Clamp01((len - t) / release);

        /// <summary>Low buzzing tone with a lip-flutter amplitude modulation and a bit of breath noise.</summary>
        static AudioClip Fart(string name, float len, float f0, float flutter, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(len * Rate);
            var d = new float[n];
            float phase = 0f, lp = 0f, jitter = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                if (i % 200 == 0) jitter = (float)(rng.NextDouble() - 0.5) * 0.25f;
                float f = f0 * (1f + jitter) * Mathf.Lerp(1.08f, 0.85f, t / len);
                phase += f / Rate;
                float saw = 2f * (phase - Mathf.Floor(phase + 0.5f));
                float flap = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * flutter * t + Mathf.Sin(t * 13f));
                float noise = (float)(rng.NextDouble() * 2 - 1) * 0.25f;
                float x = (saw * flap + noise * flap);
                lp += (x - lp) * 0.18f;                                 // soften the buzz
                d[i] = lp * Env(t, len, 0.015f, 0.12f);
            }
            return Make(name, d);
        }

        static AudioClip Sweep(string name, float len, float fa, float fb, float flutter, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(len * Rate);
            var d = new float[n];
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, k = t / len;
                float f = Mathf.Lerp(fa, fb, k * k);
                phase += f / Rate;
                float saw = 2f * (phase - Mathf.Floor(phase + 0.5f));
                float flap = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * flutter * (1f + k) * t);
                float x = saw * flap + (float)(rng.NextDouble() * 2 - 1) * 0.3f * flap;
                lp += (x - lp) * 0.22f;
                d[i] = lp * Env(t, len, 0.03f, 0.2f);
            }
            return Make(name, d);
        }

        /// <summary>Filtered noise burst: lowpass factor 0..1 (small = darker), decay shape.</summary>
        static AudioClip Noise(string name, float len, float lowpass, float attackFrac, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(len * Rate);
            var d = new float[n];
            float lp = 0f, hp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rng.NextDouble() * 2 - 1);
                lp += (x - lp) * lowpass;
                hp = lp - hp * 0.02f;
                float env = t < len * attackFrac ? t / (len * attackFrac) : Mathf.Exp(-(t - len * attackFrac) * 6f / len);
                d[i] = hp * env * Mathf.Clamp01((len - t) / 0.03f);
            }
            return Make(name, d);
        }

        static AudioClip Thunk(string name, float len, float f, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(len * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float body = Mathf.Sin(2f * Mathf.PI * f * t * (1f - t * 0.6f)) * Mathf.Exp(-t * 14f / len);
                float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 120f);
                d[i] = body + click * 0.6f;
            }
            return Make(name, d);
        }

        static AudioClip Creak(string name, float len, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(len * Rate);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 320f + 140f * Mathf.Sin(t * 7f) + (float)rng.NextDouble() * 30f;
                phase += f / Rate;
                float pulse = (phase - Mathf.Floor(phase)) < 0.12f ? 1f : -0.1f;
                d[i] = pulse * (0.5f + 0.5f * Mathf.Sin(t * 41f)) * Env(t, len, 0.05f, 0.2f);
            }
            return Make(name, d);
        }

        static AudioClip Slide(string name, float len, float fa, float fb)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, k = t / len;
                float f = Mathf.Lerp(fa, fb, k) * (1f + 0.03f * Mathf.Sin(t * 40f));
                phase += f / Rate;
                d[i] = Mathf.Sin(2f * Mathf.PI * phase) * Env(t, len, 0.02f, 0.15f);
            }
            return Make(name, d);
        }

        /// <summary>Sequence of notes (sine + a little square for brightness), each with an exponential tail.</summary>
        static AudioClip Notes(string name, float[] freqs, float step, float tail, float square)
        {
            float len = step * freqs.Length + tail;
            int n = (int)(len * Rate);
            var d = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = (int)(k * step * Rate);
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    float s = Mathf.Sin(2f * Mathf.PI * freqs[k] * t);
                    float sq = Mathf.Sign(s) * square;
                    d[i] += (s * (1f - square) + sq) * Mathf.Exp(-t * 5f) * Mathf.Clamp01(t / 0.005f);
                }
            }
            return Make(name, d);
        }
    }
}
