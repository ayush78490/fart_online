using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Platform start-up settings applied before the first scene loads. The web build always runs the "Mobile"
    /// quality level (Forward renderer, no additional-light shadows): WebGL rejects draws that bind URP's placeholder
    /// additional-light shadow map to a shadow sampler, which made every lit custom-shader object invisible.
    /// </summary>
    public static class PlatformBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int mobile = System.Array.IndexOf(QualitySettings.names, "Mobile");
            if (mobile >= 0 && QualitySettings.GetQualityLevel() != mobile) QualitySettings.SetQualityLevel(mobile, true);
#endif
            if (Application.isMobilePlatform) Application.targetFrameRate = 60;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            Screen.fullScreen = true;                 // immersive: hide the status bar over the HUD
            // the game is played sideways: landscape only, either way round (the player settings say the same;
            // this also covers an old install whose manifest still allowed portrait)
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            QualitySettings.vSyncCount = 0;           // phones pace with targetFrameRate (and Android frame pacing)
#endif
        }

        public static bool IsMobileQuality
        {
            get
            {
                int q = QualitySettings.GetQualityLevel();
                return q >= 0 && q < QualitySettings.names.Length && QualitySettings.names[q] == "Mobile";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad() => ApplyQualityTweaks();

        /// <summary>
        /// Extra savings when the "Mobile" quality level is active (phones, tablets, the web build): the river is built
        /// 170 m ahead instead of 230 m and kept 40 m behind (fog and the far plane hide the end), bloom is off (the
        /// costliest post effect on mobile GPUs; colour grading stays).
        /// </summary>
        public static void ApplyQualityTweaks()
        {
            // cheap shader paths for the procedural canyon and water (per-pixel noise there costs 0.6 s a frame on
            // an Adreno 619): see FO_MOBILE in Env/StylizedSurface and Env/StylizedWater
            if (IsMobileQuality) { MobileNoise.Bind(); Shader.EnableKeyword("FO_MOBILE"); }
            else { Shader.DisableKeyword("FO_MOBILE"); return; }
            var river = Object.FindAnyObjectByType<FartOnline.Game.Endless.EndlessRiver>();
            if (river != null) { river.buildAhead = 170f; river.keepBehind = 40f; river.spinEdgeRollers = false; }
            foreach (var v in Object.FindObjectsByType<UnityEngine.Rendering.Volume>())
            {
                var prof = v.profile;                        // runtime copy: the asset stays untouched
                if (prof != null && prof.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom)) bloom.active = false;
            }
            // the crew pigs are ~30k triangles each: no shadow pass for them on mobile (the raft still casts one)
            foreach (var sk in Object.FindObjectsByType<SkinnedMeshRenderer>())
                sk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var cam = Camera.main;
            if (cam != null) cam.farClipPlane = Mathf.Min(cam.farClipPlane, 210f);
            // SMAA is three extra full-screen passes: far too much for phones (the colour grading stays)
            var camData = cam != null ? cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
            if (camData != null)
            {
                camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
                // no post-processing on phones (measured 9 ms a frame on an Adreno 619); the canyon and water shaders
                // bake the grading in instead (MobileGrade under FO_MOBILE)
                camData.renderPostProcessing = false;
            }
            RenderSettings.fogStartDistance = Mathf.Min(RenderSettings.fogStartDistance, 70f);
            RenderSettings.fogEndDistance = Mathf.Min(RenderSettings.fogEndDistance, 190f);
        }
    }
}
