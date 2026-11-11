using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FartOnline.Game
{
    /// <summary>
    /// On-device rendering diagnosis, only when launched with the intent extra fo_diag:
    ///   adb shell am start -n com.fartonline.game/com.unity3d.player.UnityPlayerGameActivity --es fo_diag 1
    /// At the main menu it measures FPS for a few seconds while switching one rendering feature off at a time
    /// (post-processing, shadows, water, canyon, all world geometry, render scale, depth/opaque copies) and logs
    ///   [FartDiag] &lt;step&gt;: fps X (frame Y ms)
    /// to logcat, so the expensive part can be found on the phone itself.
    /// </summary>
    public class DeviceDiag : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !Application.isMobilePlatform || !Requested()) return;
            var go = new GameObject("DeviceDiag");
            DontDestroyOnLoad(go);
            go.AddComponent<DeviceDiag>();
        }

        static bool Requested()
        {
#if UNITY_ANDROID
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = act.Call<AndroidJavaObject>("getIntent"))
                    return !string.IsNullOrEmpty(intent.Call<string>("getStringExtra", "fo_diag"));
            }
            catch (System.Exception e) { Debug.Log("[FartDiag] intent read failed: " + e.Message); }
#endif
            return false;
        }

        static readonly FrameTiming[] ft = new FrameTiming[1];

        static IEnumerator Measure(string step, float seconds = 4f)
        {
            yield return new WaitForSecondsRealtime(1.5f);          // settle (shader warm-up, pipeline rebuild)
            int frames = 0, gpuN = 0;
            double gpu = 0, cpuMain = 0;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                frames++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
                {
                    cpuMain += ft[0].cpuMainThreadFrameTime;
                    if (ft[0].gpuFrameTime > 0) { gpu += ft[0].gpuFrameTime; gpuN++; }
                }
                yield return null;
            }
            float dt = (Time.realtimeSinceStartup - t0) / Mathf.Max(1, frames);
            Debug.Log($"[FartDiag] {step}: fps {1f / dt:F1} (frame {dt * 1000f:F1} ms) gpu {(gpuN > 0 ? gpu / gpuN : 0):F1} ms main {cpuMain / Mathf.Max(1, frames):F1} ms");
        }

        static bool MatHas(Renderer r, string s) { foreach (var m in r.sharedMaterials) if (m != null && m.name.Contains(s)) return true; return false; }

        /// <summary>Hides the renderers <paramref name="pick"/> selects, measures, and shows them again.</summary>
        static IEnumerator Without(string step, System.Func<Renderer, bool> pick)
        {
            var hidden = new List<Renderer>();
            foreach (var r in FindObjectsByType<Renderer>())
                if (r.enabled && pick(r)) { r.enabled = false; hidden.Add(r); }
            yield return Measure($"{step} ({hidden.Count} renderers)");
            foreach (var r in hidden) if (r != null) r.enabled = true;
        }

        IEnumerator Start()
        {
            Debug.Log($"[FartDiag] device {SystemInfo.deviceModel} | gpu {SystemInfo.graphicsDeviceName} | api {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion} | " +
                      $"quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} | screen {Screen.width}x{Screen.height} | target fps {Application.targetFrameRate} | vsync {QualitySettings.vSyncCount}");
            // gameplay: wait for the autoplay run (fo_auto) to be under way; menu: just settle
            float waitUntil = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < waitUntil)
            {
                var run = FindAnyObjectByType<GameRunManager>();
                if (run != null && run.State == RunState.Playing && run.Stats.score > 15f) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            var ar = FindAnyObjectByType<AdaptiveResolution>();
            if (ar != null) ar.enabled = false;                       // hold the scale fixed while measuring
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.renderScale = 0.65f;
            var cam = Camera.main;
            var data = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            Debug.Log($"[FartDiag] urp {(urp != null ? urp.name : "none")} scale {(urp != null ? urp.renderScale : 0f)} depthTex {(urp != null && urp.supportsCameraDepthTexture)} opaqueTex {(urp != null && urp.supportsCameraOpaqueTexture)} hdr {(urp != null && urp.supportsHDR)} msaa {(urp != null ? urp.msaaSampleCount : 0)} post {(data != null && data.renderPostProcessing)}");
            int tris = 0, skinnedTris = 0;
            foreach (var r in FindObjectsByType<Renderer>())
            {
                if (!r.enabled || !r.isVisible) continue;
                Mesh m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                if (m == null || r.isPartOfStaticBatch) continue;
                int t = 0;
                for (int k = 0; k < m.subMeshCount; k++) t += (int)m.GetIndexCount(k) / 3;
                tris += t;
                if (r is SkinnedMeshRenderer) skinnedTris += t;
            }
            Debug.Log($"[FartDiag] visible triangles ~{tris} (skinned {skinnedTris}; static-batched meshes not counted)");

            yield return Measure("1 baseline");

            var lights = FindObjectsByType<Light>();
            var shadowModes = new Dictionary<Light, LightShadows>();
            foreach (var l in lights) { shadowModes[l] = l.shadows; l.shadows = LightShadows.None; }
            yield return Measure("2 shadows off");
            foreach (var kv in shadowModes) if (kv.Key != null) kv.Key.shadows = kv.Value;

            yield return Without("3 pigs hidden", r => r is SkinnedMeshRenderer);
            yield return Without("4 water hidden", r => MatHas(r, "River") || MatHas(r, "Waterfall"));
            yield return Without("5 canyon hidden", r => MatHas(r, "Canyon"));
            yield return Without("6 particles hidden", r => r is ParticleSystemRenderer);
            yield return Without("7 foliage + decor hidden", r => r.gameObject.layer != 5 && !(r is SkinnedMeshRenderer) && !(r is ParticleSystemRenderer)
                                                              && !MatHas(r, "River") && !MatHas(r, "Canyon") && !MatHas(r, "Waterfall")
                                                              && r.GetComponentInParent<Rigidbody>() == null && !r.name.Contains("Obs") && r.GetComponentInParent<FartOnline.Game.Endless.EndlessHazard>() == null);
            yield return Without("8 hazards hidden", r => r.GetComponentInParent<FartOnline.Game.Endless.EndlessHazard>() != null);

            var canvases = new List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>()) if (c.enabled && c.isRootCanvas) { c.enabled = false; canvases.Add(c); }
            yield return Measure("9 UI hidden");
            foreach (var c in canvases) c.enabled = true;

            if (urp != null)
            {
                urp.renderScale = 0.5f;
                yield return Measure("10 render scale 0.5");
                urp.renderScale = 0.8f;
                yield return Measure("11 render scale 0.8");
                urp.renderScale = 0.65f;
            }
            if (cam != null)
            {
                cam.enabled = false;
                yield return Measure("12 world camera off (UI only)");
                cam.enabled = true;
            }
            yield return Measure("13 baseline again");
            if (ar != null) ar.enabled = true;
            Debug.Log("[FartDiag] DONE");
        }
    }
}
