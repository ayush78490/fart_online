using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using FartOnline.Game;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Builds Fart Online as an installable web app (WebGL + PWA) for phones, tablets and desktops.
    /// Menu: Tools > Fart Online > Build Web App (WebGL). Output: D:\FOBuild\Web (serve the folder over http(s);
    /// gzip with decompression fallback, so any static host works, e.g. itch.io, GitHub Pages, Netlify).
    /// - Template Assets/WebGLTemplates/FartOnline: full-window responsive canvas, loading screen, manifest,
    ///   service worker (offline cache), capped device-pixel-ratio on mobiles.
    /// - App icons rendered from the game's own UI (Lilita One logo on the wood board).
    /// - Quality: WebGL uses the "Mobile" level; its URP asset gets the depth and opaque textures the water needs,
    ///   no MSAA, shorter shadows and a 0.85 render scale.
    /// </summary>
    public static class WebBuilder
    {
        public const string OutputDir = @"D:\FOBuild\Web";
        const string TemplateDir = "Assets/WebGLTemplates/FartOnline";
        const string MobileRP = "Assets/Settings/Mobile_RPAsset.asset";

        [MenuItem("Tools/Fart Online/Build Web App (WebGL)")]
        public static void BuildWeb()
        {
            Configure();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { EndlessBuilder.EndlessScenePath },
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            long size = 0;
            if (Directory.Exists(OutputDir)) foreach (var f in Directory.GetFiles(OutputDir, "*", SearchOption.AllDirectories)) size += new FileInfo(f).Length;
            Debug.Log($"[FartOnline] web build {report.summary.result}: {report.summary.totalErrors} errors, {size / (1024 * 1024)} MB on disk, {report.summary.totalTime.TotalSeconds:F0} s -> {OutputDir}");
        }

        [MenuItem("Tools/Fart Online/Web/Configure WebGL Settings + Icons")]
        public static void Configure()
        {
            PlayerSettings.productName = EndlessBuilder.ProductName;
            PlayerSettings.WebGL.template = "PROJECT:FartOnline";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;       // works on hosts without gzip headers
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            PlayerSettings.runInBackground = false;

            // WebGL / mobile quality level: the water shader needs the depth and opaque textures
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRP);
            if (rp != null)
            {
                rp.supportsCameraDepthTexture = true;
                rp.supportsCameraOpaqueTexture = true;
                rp.msaaSampleCount = 1;
                rp.shadowDistance = 35f;
                rp.renderScale = 0.85f;
                rp.upscalingFilter = UpscalingFilterSelection.Linear;   // "Auto" picked FSR, unsupported on WebGL (disabled all post-processing)
                EditorUtility.SetDirty(rp);
            }
            // The scene's only light is the sun, so additional-light shadows cost nothing to drop. On WebGL their
            // placeholder shadow map breaks every lit draw (texture/sampler mismatch), so both assets turn them off.
            foreach (var path in new[] { MobileRP, "Assets/Settings/PC_RPAsset.asset" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            WebPostProcessData();
            RenderIcons();
            AssetDatabase.SaveAssets();
            Debug.Log("[FartOnline] WebGL settings configured (template FartOnline, gzip + fallback, data caching)");
        }

        /// <summary>
        /// URP turns off ALL post-processing when any shader in the renderer's PostProcessData is unsupported, and the FSR
        /// upscaler (EASU) is unsupported on WebGL. The mobile/web renderer gets its own copy of the data with EASU
        /// replaced by the final-post shader (FSR is never selected: the upscaling filter is Linear), so bloom and colour
        /// grading run in the browser.
        /// </summary>
        static void WebPostProcessData()
        {
            const string copy = "Assets/Settings/PostProcessData_Web.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Mobile_Renderer.asset");
            if (rd == null) return;
            if (AssetDatabase.LoadAssetAtPath<PostProcessData>(copy) == null)
                AssetDatabase.CopyAsset("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset", copy);
            var data = AssetDatabase.LoadAssetAtPath<PostProcessData>(copy);
            if (data == null) { Debug.LogWarning("[FartOnline] could not copy URP PostProcessData"); return; }
            var dso = new SerializedObject(data);
            var easu = dso.FindProperty("shaders.easuPS");
            var final = dso.FindProperty("shaders.finalPostPassPS");
            if (easu != null && final != null) { easu.objectReferenceValue = final.objectReferenceValue; dso.ApplyModifiedPropertiesWithoutUndo(); }
            var rso = new SerializedObject(rd);
            rso.FindProperty("postProcessData").objectReferenceValue = data;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(rd);
        }

        // ------------------------------------------------------------------ icons
        static readonly Color Brown = UiKit.Hex(0x4A2C17);

        public static void RenderIcons()
        {
            string dir = TemplateDir + "/TemplateData/icons";
            Directory.CreateDirectory(dir);
            RenderIcon(dir + "/icon-192.png", 192, false);
            RenderIcon(dir + "/icon-512.png", 512, false);
            RenderIcon(dir + "/icon-maskable-512.png", 512, true);
            RenderIcon(TemplateDir + "/thumbnail.png", 128, false);
            AssetDatabase.Refresh();
        }

        /// <summary>The logo on the wood board, rendered with the game's UI kit (maskable: logo inside the safe zone).</summary>
        static void RenderIcon(string path, int size, bool maskable)
        {
            var camGo = new GameObject("_IconCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Brown;
            cam.cullingMask = 1 << 5;
            var canvasGo = new GameObject("_IconCanvas", typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
            canvasGo.layer = 5;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            cam.targetTexture = rt;
            canvas.scaleFactor = size / 512f;

            var root = UiKit.Stretch(canvas.transform, "Root");
            float inset = maskable ? 70f : 10f;
            var board = UiKit.Rect(root, "Board", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-inset * 2f, -inset * 2f));
            UiKit.Skin(board, "Panel_Main", null, maskable ? 0.45f : 0.55f);
            float k = maskable ? 0.72f : 0.9f;
            UiKit.Label(root, "Fart", "FART", Mathf.RoundToInt(190 * k), TextAnchor.MiddleCenter, UiKit.Accent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-8f * k, 70f * k), new Vector2(500f, 200f));
            var online = UiKit.Label(root, "Online", "ONLINE", Mathf.RoundToInt(132 * k), TextAnchor.MiddleCenter, UiKit.Cream, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(6f * k, -80f * k), new Vector2(500f, 160f));
            online.transform.localRotation = Quaternion.Euler(0f, 0f, 3f);
            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 5; t.gameObject.hideFlags = HideFlags.HideAndDontSave; }

            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(canvasGo);
            Object.DestroyImmediate(camGo);
        }
    }
}
