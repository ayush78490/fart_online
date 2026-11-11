using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Android build (menu Tools > Fart Online > Build Android APK). Package com.fartonline.game, IL2CPP, ARM64,
    /// Vulkan + GLES3, internet permission (online play), the "Mobile" quality level (the per-platform default).
    /// Output: D:\FOBuild\Android\FartOnline.apk. Install with: adb install -r D:\FOBuild\Android\FartOnline.apk
    /// Switches the editor's active build target to Android first (slow the first time: textures re-import).
    /// </summary>
    public static class AndroidBuilder
    {
        public const string OutputPath = @"D:\FOBuild\Android\FartOnline.apk";
        public const string PackageId = "com.fartonline.game";

        public static void Configure()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, PackageId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            // played sideways: landscape only, either way round
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.Android.optimizedFramePacing = true;      // Swappy: even 60 fps pacing on 90/120 Hz screens
            PlayerSettings.enableFrameTimingStats = true;            // CPU/GPU ms for AdaptiveResolution and PerfLog
            EditorUserBuildSettings.buildAppBundle = false;
            MobilePipeline();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/WebGLTemplates/FartOnline/TemplateData/icons/icon-512.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Mobile URP asset (phones and the web build): no opaque copy (the mobile water doesn't refract),
        /// render scale 0.8, no HDR, 25 m single-cascade shadows.</summary>
        public static void MobilePipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (urp == null) return;
            urp.supportsCameraOpaqueTexture = false;
            urp.supportsCameraDepthTexture = false;           // mobile water and gas don't read scene depth (FO_MOBILE)
            urp.renderScale = 0.8f;
            urp.supportsHDR = false;
            urp.shadowDistance = 25f;
            urp.shadowCascadeCount = 1;
            EditorUtility.SetDirty(urp);
        }

        [MenuItem("Tools/Fart Online/Build Android APK")]
        public static void BuildApk()
        {
            Configure();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var t0 = System.DateTime.Now;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var s = report.summary;
            long size = File.Exists(OutputPath) ? new FileInfo(OutputPath).Length : 0;
            Debug.Log($"[FartOnline] android build {s.result}: {s.totalErrors} errors, {size / (1024 * 1024)} MB, {(System.DateTime.Now - t0).TotalSeconds:F0} s -> {OutputPath}");
        }
    }
}
