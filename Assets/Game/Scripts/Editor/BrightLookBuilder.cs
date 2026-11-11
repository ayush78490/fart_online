using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FartOnline.EnvironmentEditor;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Bright cartoon look for the endless river: saturated red-orange stone, vivid grass, turquoise water with lots of
    /// white foam, a clear blue sky, a warm strong sun and punchier colour grading. Writes the palette into
    /// TropicalCourseSettings and rebuilds the materials (so a later *Rebuild Materials* keeps it), then sets the sky,
    /// sun, ambient, fog and the shared post-processing profile, and applies the scene part to the open scene.
    /// Menu: Tools > Fart Online > Apply Bright Cartoon Look.
    /// </summary>
    public static class BrightLookBuilder
    {
        [MenuItem("Tools/Fart Online/Apply Bright Cartoon Look")]
        public static void ApplyMenu()
        {
            ApplyShared();
            ApplyScene();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[FartOnline] bright cartoon look applied");
        }

        /// <summary>Palette, materials, sky material and the post-processing profile (assets shared by every scene).</summary>
        public static void ApplyShared()
        {
            var s = CourseGenerator.Settings();
            s.rockRed = new Color(0.86f, 0.36f, 0.19f);
            s.rockOrange = new Color(0.99f, 0.60f, 0.30f);
            s.rockDark = new Color(0.58f, 0.28f, 0.16f);
            s.rockWet = new Color(0.44f, 0.25f, 0.17f);
            s.grass = new Color(0.40f, 0.80f, 0.18f);
            s.leaves = new Color(0.20f, 0.70f, 0.22f);
            s.leavesAlt = new Color(0.55f, 0.86f, 0.16f);
            s.waterDeep = new Color(0.0f, 0.52f, 0.76f);
            s.waterShallow = new Color(0.24f, 0.93f, 0.94f);
            s.waterFoam = new Color(1f, 1f, 1f);
            EditorUtility.SetDirty(s);
            MaterialLibrary.Rebuild();

            var water = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_River_Main.mat");
            water.SetFloat("_TurbulenceFoam", 0.75f);
            water.SetFloat("_EdgeFoam", 0.55f);
            water.SetFloat("_FoamDistance", 1.0f);
            water.SetFloat("_DepthFade", 2.8f);
            water.SetFloat("_SpecularStrength", 0.9f);
            EditorUtility.SetDirty(water);
            var fall = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_Waterfall.mat");
            fall.SetFloat("_Opacity", 0.9f);
            fall.SetFloat("_FoamTop", 0.7f);
            fall.SetFloat("_FoamBottom", 1f);
            EditorUtility.SetDirty(fall);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(LightingBuilder.SkyPath);
            if (sky != null)
            {
                sky.SetColor("_SkyTint", new Color(0.30f, 0.62f, 1f));
                sky.SetColor("_GroundColor", new Color(0.62f, 0.55f, 0.42f));
                sky.SetFloat("_AtmosphereThickness", 0.62f);
                sky.SetFloat("_Exposure", 1.35f);
                EditorUtility.SetDirty(sky);
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(LightingBuilder.ProfilePath);
            if (profile != null)
            {
                if (profile.TryGet(out ColorAdjustments color))
                {
                    color.postExposure.Override(0.22f);
                    color.contrast.Override(16f);
                    color.saturation.Override(30f);
                }
                if (profile.TryGet(out Vignette vignette)) vignette.intensity.Override(0.14f);
                if (profile.TryGet(out WhiteBalance wb)) { wb.temperature.Override(3f); wb.tint.Override(-2f); }
                if (profile.TryGet(out Bloom bloom)) bloom.intensity.Override(0.32f);
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Sun, ambient and fog of the open scene.</summary>
        public static void ApplyScene()
        {
            foreach (var l in Object.FindObjectsByType<Light>())
            {
                if (l.type != LightType.Directional) continue;
                l.color = new Color(1f, 0.95f, 0.86f);
                l.intensity = 1.8f;
                l.shadowStrength = 0.7f;
                l.transform.rotation = Quaternion.Euler(50f, -38f, 0f);
                EditorUtility.SetDirty(l);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.70f, 0.84f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.80f, 0.74f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.34f, 0.24f);
            RenderSettings.fogColor = new Color(0.74f, 0.88f, 1f);
            RenderSettings.fogStartDistance = 130f;
            RenderSettings.fogEndDistance = 650f;
        }
    }
}
