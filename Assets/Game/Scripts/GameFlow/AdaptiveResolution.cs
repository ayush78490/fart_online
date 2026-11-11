using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FartOnline.Game
{
    /// <summary>
    /// Holds 60 fps on phones by moving URP's render scale (the 3D view only; the UI canvas always draws at native
    /// resolution). The game is fill-rate bound on mid-range phones: on an Adreno 619 (Snapdragon 695) at 2400x1080,
    /// half resolution runs at 60 fps where 0.8 ran at 45.
    ///  * starts at a ~720p internal resolution (short side 720 px), never above 0.8 or below <see cref="MinScale"/>;
    ///  * every second it counts frames that missed the 60 fps budget; two bad seconds in a row step down 0.05;
    ///  * eight clean seconds with GPU headroom step back up 0.05, but a step up that is followed by a drop within
    ///    12 s lowers the ceiling to the lower step (no oscillation);
    ///  * also samples FrameTimingManager (CPU and GPU ms, when the device supports it) for PerfLog.
    /// Only on phones/tablets running the "Mobile" quality level, never in the editor (the URP asset would change on disk).
    /// </summary>
    public class AdaptiveResolution : MonoBehaviour
    {
        public const float MinScale = 0.5f;
        public const float MaxScale = 0.8f;
        const float Step = 0.05f;
        const int TargetShortSide = 720;

        public static float Scale { get; private set; } = 1f;
        public static float CpuMs { get; private set; }
        public static float GpuMs { get; private set; }

        float ceiling;
        float windowStart;
        int frames, missed;
        float gpuSum, cpuSum;
        int gpuCount;
        int badWindows, goodWindows;
        float lastUpAt = -100f;
        float holdUntil;
        readonly FrameTiming[] timings = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !Application.isMobilePlatform || !PlatformBoot.IsMobileQuality) return;
            var go = new GameObject("AdaptiveResolution");
            DontDestroyOnLoad(go);
            go.AddComponent<AdaptiveResolution>();
        }

        static UniversalRenderPipelineAsset Urp => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        void Start()
        {
            int shortSide = Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));
            ceiling = Mathf.Clamp(Snap((float)TargetShortSide / shortSide), MinScale, MaxScale);
            Apply(ceiling);
            windowStart = Time.realtimeSinceStartup;
            holdUntil = windowStart + 3f;                       // shader warm-up and the first river chunks
        }

        static float Snap(float s) => Mathf.Round(s / Step) * Step;

        static void Apply(float s)
        {
            var urp = Urp;
            if (urp == null) return;
            urp.renderScale = s;
            Scale = s;
        }

        void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                if (timings[0].gpuFrameTime > 0.0) { gpuSum += (float)timings[0].gpuFrameTime; gpuCount++; }
                cpuSum += (float)timings[0].cpuFrameTime;
            }

            float now = Time.realtimeSinceStartup;
            int target = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
            frames++;
            if (Time.unscaledDeltaTime > 1.3f / target) missed++;
            if (now - windowStart < 1f) return;

            CpuMs = cpuSum / Mathf.Max(1, frames);
            GpuMs = gpuCount > 0 ? gpuSum / gpuCount : 0f;
            float missRate = missed / (float)Mathf.Max(1, frames);
            frames = missed = gpuCount = 0;
            gpuSum = cpuSum = 0f;
            windowStart = now;
            if (now < holdUntil) return;

            if (missRate > 0.08f) { badWindows++; goodWindows = 0; }
            else if (missRate == 0f) { goodWindows++; badWindows = 0; }
            else { badWindows = 0; goodWindows = 0; }

            float budget = 1000f / target;
            if (badWindows >= 2 && Scale > MinScale + 0.001f)
            {
                if (now - lastUpAt < 12f) ceiling = Mathf.Max(MinScale, Scale - Step);   // that step up was too much
                Apply(Mathf.Max(MinScale, Scale - Step));
                badWindows = 0;
                holdUntil = now + 2f;
                Debug.Log($"[FartPerf] render scale down to {Scale:F2} (missed {missRate:P0})");
            }
            else if (goodWindows >= 8 && Scale < ceiling - 0.001f && (GpuMs <= 0f || GpuMs < budget * 0.75f))
            {
                Apply(Mathf.Min(ceiling, Scale + Step));
                goodWindows = 0;
                lastUpAt = now;
                holdUntil = now + 2f;
                Debug.Log($"[FartPerf] render scale up to {Scale:F2}");
            }
        }
    }
}
