using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Lighting and atmosphere for the tropical course: procedural sky, warm sun, trilight ambient, distance haze,
    /// a global post-processing volume (bloom, neutral tonemapping, colour adjustments, vignette) and a reflection
    /// probe over the course. Menu: Tools > Tropical Course > Apply Lighting and Atmosphere.
    /// </summary>
    public static class LightingBuilder
    {
        public const string SkyPath = CourseGenerator.MatDir + "/M_Sky_Tropical.mat";
        public const string ProfilePath = CourseGenerator.Root + "/Settings/PP_TropicalCourse.asset";

        [MenuItem("Tools/Tropical Course/Apply Lighting and Atmosphere")]
        public static void ApplyMenu()
        {
            if (!CourseGenerator.OpenScene()) return;
            Apply();
            CourseGenerator.SaveScene();
        }

        public static void Apply()
        {
            var root = CourseGenerator.CourseRoot().transform;

            // sky
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, SkyPath); }
            sky.SetFloat("_SunSize", 0.05f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetColor("_SkyTint", new Color(0.42f, 0.66f, 0.95f));
            sky.SetColor("_GroundColor", new Color(0.55f, 0.48f, 0.38f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // sun
            var sunT = root.Find("Sun");
            if (sunT != null)
            {
                var sun = sunT.GetComponent<Light>();
                sun.color = new Color(1f, 0.93f, 0.8f);
                sun.intensity = 1.5f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.75f;
                sunT.rotation = Quaternion.Euler(52f, -38f, 0f);
                RenderSettings.sun = sun;
            }

            // ambient + haze (soft blue in the distance keeps the canyon layers readable)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.74f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.66f, 0.6f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.27f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.82f, 0.9f);
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 700f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            // post-processing volume
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            var bloom = Get<Bloom>(profile);
            // the custom water / waterfall shaders can output very bright (or NaN) HDR pixels at grazing angles:
            // clamp the bloom input hard and filter NaNs on the camera, otherwise the canyon blows out to white
            bloom.threshold.Override(1.3f);
            bloom.intensity.Override(0.25f);
            bloom.scatter.Override(0.55f);
            bloom.clamp.Override(3f);
            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);
            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);
            color.saturation.Override(14f);
            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            var wb = Get<WhiteBalance>(profile);
            wb.temperature.Override(6f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volT = root.Find("PostProcessVolume");
            if (volT == null) { volT = new GameObject("PostProcessVolume").transform; volT.SetParent(root, false); }
            var vol = GetOrAdd<Volume>(volT.gameObject);
            vol.isGlobal = true;
            vol.priority = 1;
            vol.sharedProfile = profile;

            var camT = root.Find("GameplayCamera");
            if (camT != null)
            {
                var data = GetOrAdd<UniversalAdditionalCameraData>(camT.gameObject);
                data.renderPostProcessing = true;
                data.stopNaN = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                camT.GetComponent<Camera>().farClipPlane = 900f;
            }

            // reflection probe over the middle of the course (water + wet rock pick up the sky and canyon colours)
            var path = CourseGenerator.River();
            var probeT = root.Find("ReflectionProbe");
            if (probeT == null) { probeT = new GameObject("ReflectionProbe").transform; probeT.SetParent(root, false); }
            var probe = GetOrAdd<ReflectionProbe>(probeT.gameObject);
            probeT.position = path.SampleAtDistance(path.Length * 0.5f).position + Vector3.up * 8f;
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128;
            probe.size = new Vector3(600f, 120f, 600f);
            probe.importance = 1;
            // the game uses the brighter cartoon look on top of this base (palette, sky, sun, grading)
            FartOnline.GameEditor.BrightLookBuilder.ApplyShared();
            FartOnline.GameEditor.BrightLookBuilder.ApplyScene();
            Debug.Log("[TropicalCourse] lighting and atmosphere applied (sky, sun, trilight ambient, haze, post volume, reflection probe)");
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet(out T c)) c = p.Add<T>(true);
            c.active = true;
            return c;
        }

        /// <summary>GetComponent or AddComponent. (Never use "??" with Unity objects: a missing component is a fake null in the editor.)</summary>
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
