using UnityEditor;
using UnityEngine;
using FartOnline.Environment;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Creates / updates the shared environment materials from CourseSettings. One small coherent palette, few
    /// materials (shared by every generated object), all on Env/StylizedSurface or Env/StylizedWater.
    /// Menu: Tools > Tropical Course > Rebuild Materials.
    /// </summary>
    public static class MaterialLibrary
    {
        const string Dir = CourseGenerator.MatDir;

        static Material Get(string name, string shader)
        {
            MeshKit.EnsureFolder(Dir);
            string path = Dir + "/" + name + ".mat";
            var sh = Shader.Find(shader);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.enableInstancing = true;
            return m;
        }

        static void Mode(Material m, string mode)
        {
            foreach (var k in new[] { "_MODE_ROCK", "_MODE_FOLIAGE", "_MODE_WOOD" }) m.DisableKeyword(k);
            m.EnableKeyword("_MODE_" + mode.ToUpperInvariant());
            m.SetFloat("_Mode", mode == "Rock" ? 0 : mode == "Foliage" ? 1 : 2);
        }

        static Material Rock(string name, Color a, Color b, Color dark, Color wet, Color grass, float grassAmount, float darkAmount, float smooth)
        {
            var m = Get(name, "Env/StylizedSurface");
            Mode(m, "Rock");
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_ColorA", a); m.SetColor("_ColorB", b);
            m.SetColor("_ColorDark", dark); m.SetColor("_ColorWet", wet); m.SetColor("_GrassColor", grass);
            m.SetFloat("_GrassAmount", grassAmount);
            m.SetFloat("_DarkAmount", darkAmount);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Cull", 2);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Foliage(string name, Color c, float translucency, float wind)
        {
            var m = Get(name, "Env/StylizedSurface");
            Mode(m, "Foliage");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Translucency", translucency);
            m.SetFloat("_WindStrength", wind);
            m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_Cull", 0);                     // leaves are single sheets, lit on both sides
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Wood(string name, Color c, float smooth)
        {
            var m = Get(name, "Env/StylizedSurface");
            Mode(m, "Wood");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Cull", 2);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Water(string name, CourseSettings s, float clarity, float turbulenceFoam, bool radial)
        {
            var m = Get(name, "Env/StylizedWater");
            m.SetColor("_DeepColor", s.waterDeep);
            m.SetColor("_ShallowColor", s.waterShallow);
            m.SetColor("_FoamColor", s.waterFoam);
            m.SetFloat("_Clarity", clarity);
            m.SetFloat("_TurbulenceFoam", turbulenceFoam);
            m.SetFloat("_RadialFlow", radial ? 1f : 0f);
            if (radial) m.EnableKeyword("_RADIAL_FLOW"); else m.DisableKeyword("_RADIAL_FLOW");
            m.renderQueue = radial ? 2991 : 2990;            // foam discs draw after the river they float on
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Tools/Tropical Course/Rebuild Materials")]
        public static void Rebuild()
        {
            var s = CourseGenerator.Settings();
            // canyon walls: full palette, grass on ledges/plateaus
            Rock("M_Canyon", s.rockRed, s.rockOrange, s.rockDark, s.rockWet, s.grass, 1f, 0.45f, 0.16f);
            // prop rocks: a few tints of the same palette (all instancing-friendly)
            Rock("M_Rock_Red", s.rockRed, Color.Lerp(s.rockRed, s.rockOrange, 0.4f), s.rockDark, s.rockWet, s.grass, 0.6f, 0.4f, 0.18f);
            Rock("M_Rock_Orange", s.rockOrange, Color.Lerp(s.rockOrange, new Color(0.95f, 0.75f, 0.5f), 0.35f), s.rockDark, s.rockWet, s.grass, 0.6f, 0.35f, 0.18f);
            Rock("M_Rock_Dark", s.rockDark, Color.Lerp(s.rockDark, s.rockRed, 0.35f), s.rockDark * 0.7f, s.rockWet, s.grass, 0.3f, 0.5f, 0.2f);
            var wet = Rock("M_Rock_Wet", s.rockWet, Color.Lerp(s.rockWet, s.rockDark, 0.5f), s.rockWet * 0.7f, s.rockWet * 0.8f, s.grass, 0f, 0.4f, 0.55f);
            wet.SetFloat("_WetSmoothness", 0.72f);
            Foliage("M_Grass", s.grass, 0.3f, 0.06f);
            Foliage("M_Tropical_Leaves", s.leaves, 0.4f, 0.09f);
            Wood("M_Wood", new Color(0.62f, 0.40f, 0.22f), 0.22f);
            Wood("M_Wood_Wet", new Color(0.40f, 0.26f, 0.15f), 0.5f);
            Wood("M_Wood_Painted", Color.white, 0.3f);                   // hazard paint: colour comes from vertex colours
            // water
            Water("M_River_Main", s, 0.6f, 0.45f, false);
            Water("M_River_Shallow", s, 0.8f, 0.5f, false).SetFloat("_DepthFade", 3.2f);   // calmer, clearer variant
            var foam = Water("M_River_Foam", s, 0.4f, 1.3f, false);                         // whitewater variant (rapids)
            foam.SetFloat("_NormalScale", 0.45f);
            var wf = Get("M_Waterfall", "Env/Waterfall");
            wf.SetColor("_Color", Color.Lerp(s.waterShallow, Color.white, 0.35f));
            wf.SetColor("_DeepColor", s.waterDeep);
            wf.SetColor("_FoamColor", s.waterFoam);
            EditorUtility.SetDirty(wf);
            WaterfallBuilder.ParticleMaterial();
            AssetDatabase.SaveAssets();
            Debug.Log("[TropicalCourse] materials rebuilt in " + Dir);
        }
    }
}
