using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Menu Tools > Fart Online > Build Linux Dedicated Server: the headless room server that runs on AWS
    /// (Linux x86_64, Dedicated Server subtarget: no rendering, no audio). Output D:\FOBuild\Server\FartOnlineServer.x86_64,
    /// then upload it to the server as described in Backend/README.md (section 3). Needs Unity's "Linux Dedicated Server Build Support"
    /// module. The same scene as the game; the process becomes a room server when started with -fo-dedicated.
    /// </summary>
    public static class ServerBuilder
    {
        public const string OutputDir = @"D:\FOBuild\Server";
        public const string OutputPath = OutputDir + @"\FartOnlineServer.x86_64";

        [MenuItem("Tools/Fart Online/Build Linux Dedicated Server")]
        public static void BuildLinuxServer()
        {
            Directory.CreateDirectory(OutputDir);
            PlayerSettings.productName = EndlessBuilder.ProductName;
            var t0 = System.DateTime.Now;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { EndlessBuilder.EndlessScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[FartOnline] linux server build {s.result}: {s.totalErrors} errors, {s.totalSize / (1024 * 1024)} MB, {(System.DateTime.Now - t0).TotalSeconds:F0} s -> {OutputPath}");
        }
    }
}
