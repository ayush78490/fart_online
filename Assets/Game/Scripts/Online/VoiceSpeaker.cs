using Concentus.Structs;
using UnityEngine;

namespace FartOnline.Game.Online
{
    /// <summary>
    /// Plays one remote player's voice: Opus packets in, a small jitter buffer, a streaming AudioClip out.
    /// Missing packets (sequence gaps) are concealed by the decoder (up to 3 frames); the buffer starts playing once
    /// 60 ms are queued and drops the backlog if it grows past 300 ms (so a stalled connection never builds delay).
    /// </summary>
    public class VoiceSpeaker : MonoBehaviour
    {
        public const int SampleRate = VoiceChat.SampleRate, FrameSamples = VoiceChat.FrameSamples;
        const int Capacity = SampleRate;                  // 1 s ring buffer
        const int StartAt = FrameSamples * 3, MaxBacklog = SampleRate * 3 / 10;

        public ulong ClientId { get; private set; }
        public float LastHeard { get; private set; } = -10f;

        OpusDecoder decoder;
        readonly short[] pcm = new short[FrameSamples * 6];
        readonly float[] ring = new float[Capacity];
        int readPos, writePos, count;
        bool playing;
        int lastSeq = -1;
        readonly object gate = new object();
        AudioSource source;

        public void Init(ulong clientId)
        {
            ClientId = clientId;
            decoder = OpusDecoder.Create(SampleRate, 1);
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 1f;
            source.clip = AudioClip.Create("Voice_" + clientId, SampleRate, 1, SampleRate, true, OnRead);
            source.Play();
        }

        public float Volume { get => source != null ? source.volume : 1f; set { if (source != null) source.volume = value; } }

        /// <summary>Main thread: one Opus packet (20 ms) with its 8-bit sequence number.</summary>
        public void Push(byte seq, byte[] data, int length)
        {
            LastHeard = Time.unscaledTime;
            if (lastSeq >= 0)
            {
                int gap = (seq - lastSeq - 1) & 0xFF;
                if (gap > 0 && gap <= 3)
                    for (int i = 0; i < gap; i++) Write(decoder.Decode(null, 0, 0, pcm, 0, FrameSamples, false));   // conceal
                else if (gap > 200) { lastSeq = seq; return; }                                                    // stale / reordered
            }
            lastSeq = seq;
            int n;
            try { n = decoder.Decode(data, 0, length, pcm, 0, FrameSamples, false); }
            catch { return; }                                                                                     // corrupt packet
            Write(n);
        }

        void Write(int samples)
        {
            if (samples <= 0) return;
            lock (gate)
            {
                for (int i = 0; i < samples; i++)
                {
                    ring[writePos] = pcm[i] / 32768f;
                    writePos = (writePos + 1) % Capacity;
                    if (count < Capacity) count++;
                    else readPos = (readPos + 1) % Capacity;        // overflow: drop the oldest
                }
                if (count > MaxBacklog)                              // too much delay: skip ahead
                {
                    int drop = count - StartAt;
                    readPos = (readPos + drop) % Capacity;
                    count -= drop;
                }
            }
        }

        /// <summary>Audio thread.</summary>
        void OnRead(float[] data)
        {
            lock (gate)
            {
                if (!playing && count >= StartAt) playing = true;
                for (int i = 0; i < data.Length; i++)
                {
                    if (playing && count > 0)
                    {
                        data[i] = ring[readPos];
                        readPos = (readPos + 1) % Capacity;
                        count--;
                    }
                    else
                    {
                        data[i] = 0f;
                        playing = false;                             // underrun: wait for the buffer to refill
                    }
                }
            }
        }
    }
}
