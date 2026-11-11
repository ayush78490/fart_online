using System.IO;
using UnityEditor;
using UnityEngine;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Release builds for the public website (Website/ in the repo, deployed to the game server).
    ///  - Tools > Fart Online > Build Windows Release: a non-development Windows player in D:\FOBuild\Windows.
    ///  - Tools > Fart Online > Build All for Website: Linux server, Windows, Android APK and WebGL in one go, so the
    ///    server and every client come from the same code and the same backend settings
    ///    (Assets/Game/Resources/FartOnlineBackend.asset). Takes about an hour (four platform switches).
    /// Website/README.md says how the outputs are packed and uploaded.
    /// </summary>
    public static class ReleaseBuilder
    {
        public const string WindowsDir = @"D:\FOBuild\Windows";
        public const string WindowsExe = WindowsDir + @"\FartOnline.exe";

        [MenuItem("Tools/Fart Online/Build Windows Release")]
        public static void BuildWindowsRelease()
        {
            if (Directory.Exists(WindowsDir)) Directory.Delete(WindowsDir, true);   // no stale files in the download
            Directory.CreateDirectory(WindowsDir);
            PlayerSettings.productName = EndlessBuilder.ProductName;
            var t0 = System.DateTime.Now;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { EndlessBuilder.EndlessScenePath },
                locationPathName = WindowsExe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[FartOnline] windows release build {s.result}: {s.totalErrors} errors, {s.totalSize / (1024 * 1024)} MB, {(System.DateTime.Now - t0).TotalSeconds:F0} s -> {WindowsExe}");
        }

        [MenuItem("Tools/Fart Online/Build All for Website")]
        public static void BuildAll()
        {
            var t0 = System.DateTime.Now;
            Debug.Log("[FartOnline] website builds: start");
            ServerBuilder.BuildLinuxServer();
            BuildWindowsRelease();
            AndroidBuilder.BuildApk();
            WebBuilder.BuildWeb();
            Debug.Log($"[FartOnline] website builds: done in {(System.DateTime.Now - t0).TotalMinutes:F0} min");
        }
    }
}
