using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FartOnline.Game.Tests;

namespace FartOnline.GameEditor
{
    /// <summary>Menu entries that open the game scene, enter Play Mode and start a play-mode self-test.</summary>
    [InitializeOnLoad]
    static class GameTestMenu
    {
        const string PendingKey = "FartOnline.PendingSelfTest";

        static GameTestMenu() => EditorApplication.playModeStateChanged += OnPlayMode;

        [MenuItem("Tools/Fart Online/Tests/Run Physics Self-Test")]
        static void RunPhysics() => Launch("physics");

        [MenuItem("Tools/Fart Online/Tests/Run Visual Probe (VFX + poses)")]
        static void RunVisual() => Launch("visual");

        [MenuItem("Tools/Fart Online/Tests/Run HUD + AI Probe")]
        static void RunHud() => Launch("hud");

        [MenuItem("Tools/Fart Online/Tests/Run UI Probe (screens at 1080p)")]
        static void RunUi() => Launch("ui", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Tests/Run Online UI Probe (account, friends, invite)")]
        public static void RunOnlineUi() => Launch("onlineui", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Tests/Run Responsive Probe (phone, tablet, desktop)")]
        static void RunResponsive() => Launch("responsive", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Tests/Run Endless Probe (layout check + AI runs)")]
        static void RunEndless() => Launch("endless", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Tests/Run Endless Crash Check only")]
        static void RunEndlessCrash() => Launch("endlesscrash", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Tests/Run Look Probe (gameplay + hazard close-ups)")]
        public static void RunLook() => Launch("look", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Tests/Run Perf Probe (Mobile quality, 30 s)")]
        public static void RunPerf() => Launch("perf", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Tests/Run Course Run (AI crew 1 and 4)")]
        static void RunCourse() => Launch("course");

        [MenuItem("Tools/Fart Online/Tests/Run Jump Probe (section E)")]
        static void RunJump() => Launch("jump");

        [MenuItem("Tools/Fart Online/Tests/Run Course Run (AI crew 4 only)")]
        static void RunCourse4() => Launch("course4");

        [MenuItem("Tools/Fart Online/Tests/Run Artifact Probe (waterfalls)")]
        static void RunArtifact() => Launch("artifact");

        [MenuItem("Tools/Fart Online/Trailer/Record Footage (3 cycles of camera rigs)")]
        public static void RunTrailer() => Launch("trailer", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Trailer/Record Crashes (6 takes)")]
        public static void RunTrailerCrash() => Launch("trailercrash", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Trailer/Record Vertical 9x16 (menu to game over)")]
        public static void RunTrailerVert() => Launch("trailervert", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Trailer/Record Vertical Test (short)")]
        public static void RunTrailerVertTest() => Launch("trailerverttest", EndlessBuilder.EndlessScenePath);

        [MenuItem("Tools/Fart Online/Trailer/Record Test (1 short cycle)")]
        public static void RunTrailerTest() => Launch("trailertest", EndlessBuilder.EndlessScenePath);

        public static void Launch(string test) => Launch(test, GameBuilder.GameScenePath);

        public static void Launch(string test, string scenePath)
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (EditorSceneManager.GetActiveScene().path != scenePath)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene(scenePath);
            }
            SessionState.SetString(PendingKey, test);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            string test = SessionState.GetString(PendingKey, "");
            SessionState.EraseString(PendingKey);
            if (test == "physics") new GameObject("PhysicsSelfTest").AddComponent<PhysicsSelfTest>();
            if (test == "visual") new GameObject("VisualProbe").AddComponent<VisualProbe>();
            if (test == "hud") new GameObject("HudProbe").AddComponent<HudProbe>();
            if (test == "ui") new GameObject("UiProbe").AddComponent<UiProbe>();
            if (test == "onlineui") { UiProbe.OnlineScreens = true; new GameObject("UiProbe").AddComponent<UiProbe>(); }
            if (test == "responsive") new GameObject("ResponsiveProbe").AddComponent<ResponsiveProbe>();
            if (test == "endless") new GameObject("EndlessProbe").AddComponent<EndlessProbe>();
            if (test == "endlesscrash") { EndlessProbe.CrashCheckOnly = true; new GameObject("EndlessProbe").AddComponent<EndlessProbe>(); }
            if (test == "perf") new GameObject("PerfProbe").AddComponent<PerfProbe>();
            if (test.StartsWith("trailer")) { TrailerDirector.Vert = test.StartsWith("trailervert"); TrailerDirector.TestOnly = test.EndsWith("test"); TrailerDirector.CrashMode = test == "trailercrash"; new GameObject("TrailerDirector").AddComponent<TrailerDirector>(); }
            if (test == "look") new GameObject("LookProbe").AddComponent<LookProbe>();
            if (test == "course") new GameObject("CourseRunProbe").AddComponent<CourseRunProbe>();
            if (test == "jump") new GameObject("JumpProbe").AddComponent<JumpProbe>();
            if (test == "artifact") new GameObject("ArtifactProbe").AddComponent<ArtifactProbe>();
            if (test == "course4") { CourseRunProbe.OverrideCrews = new[] { 4 }; new GameObject("CourseRunProbe").AddComponent<CourseRunProbe>(); }
        }
    }
}
