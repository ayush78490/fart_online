using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Device performance log (phones and tablets only, not the editor): every 5 s writes one line to the log
    /// ("adb logcat -s Unity") with the average FPS, the worst frame, the quality level, the screen and the run state:
    ///   [FartPerf] fps 58.9 avg | worst 24.1 ms | 1% low 41.0 fps | Mobile 1080x2400 | Playing 312 m
    /// Costs nothing measurable; lets a phone test be judged from a USB cable.
    /// </summary>
    public class PerfLog : MonoBehaviour
    {
        const float Window = 5f;
        readonly float[] frames = new float[2048];
        int count;
        float started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Application.isMobilePlatform || Application.isEditor) return;
            var go = new GameObject("PerfLog");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfLog>();
        }

        void Start() => started = Time.realtimeSinceStartup;

        void Update()
        {
            if (count < frames.Length) frames[count++] = Time.unscaledDeltaTime;
            float now = Time.realtimeSinceStartup;
            if (now - started < Window || count == 0) return;
            float sum = 0f, worst = 0f;
            for (int i = 0; i < count; i++) { sum += frames[i]; worst = Mathf.Max(worst, frames[i]); }
            System.Array.Sort(frames, 0, count);
            float p99 = frames[Mathf.Clamp((int)(count * 0.99f), 0, count - 1)];
            var run = FindAnyObjectByType<GameRunManager>();
            string state = run != null ? run.State + " " + run.Stats.score.ToString("F0") + " m" : "-";
            Debug.Log($"[FartPerf] fps {count / sum:F1} avg | worst {worst * 1000f:F1} ms | 1% low {1f / Mathf.Max(1e-4f, p99):F1} fps | " +
                      $"cpu {AdaptiveResolution.CpuMs:F1} gpu {AdaptiveResolution.GpuMs:F1} ms | scale {AdaptiveResolution.Scale:F2} | " +
                      $"{QualitySettings.names[QualitySettings.GetQualityLevel()]} {Screen.width}x{Screen.height} safe {Screen.safeArea} | {state}");
            count = 0;
            started = now;
        }
    }
}
