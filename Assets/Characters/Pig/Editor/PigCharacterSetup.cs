using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FartOnline.Pig.EditorTools
{
    /// <summary>
    /// Creates the pig materials, Animator controller and prefab. Runs automatically once after the FBX is first
    /// imported (and from Tools > Pig Character > Setup).
    /// </summary>
    [InitializeOnLoad]
    public static class PigCharacterSetup
    {
        public static readonly string[] FbxMaterialNames = { "M_Pig_Skin", "M_Pig_Mouth", "M_Pig_Eye_White", "M_Pig_Pupil", "M_Pig_Hoof" };
        const string Root = PigCharacterImporter.Root;
        public const string PrefabPath = Root + "/PF_Pig.prefab";
        public const string ControllerPath = Root + "/PigCharacter.controller";
        public static readonly Color SkinDefault = new Color(0.945f, 0.615f, 0.695f, 1f);   // sRGB, same as Blender

        static PigCharacterSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PigCharacterImporter.FbxPath) != null &&
                    (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null ||
                     AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/M_Pig_Skin.mat") == null))
                    Setup();
            };
        }

        [MenuItem("Tools/Pig Character/Setup Materials, Controller and Prefab")]
        public static void Setup()
        {
            CreateMaterials();
            AssetDatabase.ImportAsset(PigCharacterImporter.FbxPath, ImportAssetOptions.ForceUpdate);   // apply remaps
            var controller = CreateController();
            CreatePrefab(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[Pig] setup complete: materials, " + ControllerPath + ", " + PrefabPath);
        }

        static Material GetOrCreate(string name, Shader shader)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        public static void CreateMaterials()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var pig = Shader.Find("FartOnline/PigSkin");
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/T_Pig_SkinDetail.png");

            bool pigOk = pig != null && pig.isSupported && !ShaderUtil.ShaderHasError(pig);
            var skin = GetOrCreate("M_Pig_Skin", pigOk ? pig : lit);
            if (pigOk)
            {
                skin.SetColor("_SkinColor", SkinDefault);
                skin.SetFloat("_PatchDarkness", 0.16f);
                skin.SetFloat("_CavityStrength", 0.5f);
                skin.SetFloat("_Variation", 0.06f);
                skin.SetColor("_InteriorColor", new Color(0.30f, 0.12f, 0.10f));
                skin.SetFloat("_Smoothness", 0.64f);
                skin.SetFloat("_SpecularLevel", 0.5f);
                skin.SetFloat("_CoatStrength", 0.18f);
                skin.SetFloat("_CoatSmoothness", 0.78f);
                skin.SetColor("_SSSColor", new Color(1f, 0.35f, 0.22f));
                skin.SetFloat("_SSSStrength", 0.12f);
                skin.SetFloat("_SSSWrap", 0.5f);
            }
            else
            {
                Debug.LogWarning("[Pig] FartOnline/PigSkin unavailable - M_Pig_Skin falls back to URP/Lit + detail texture.");
                SetupLitSkin(skin, detail);
            }
            // guaranteed-compatible variant: URP/Lit, _BaseColor (= Skin_Color) x _BaseMap (= baked Detail)
            SetupLitSkin(GetOrCreate("M_Pig_Skin_URPLit", lit), detail);

            // the mouth bag and the eyeballs sit inside the head; their buried halves would self-shadow the skin,
            // so they receive shadows but do not cast them (baked AO already darkens around them)
            Lit(GetOrCreate("M_Pig_Mouth", lit), new Color(0.42f, 0.13f, 0.15f), 0.55f, castShadows: false);
            Lit(GetOrCreate("M_Pig_Eye_White", lit), new Color(0.97f, 0.965f, 0.96f), 0.88f, castShadows: false);
            Lit(GetOrCreate("M_Pig_Pupil", lit), new Color(0.035f, 0.03f, 0.03f), 0.92f, castShadows: false);
            Lit(GetOrCreate("M_Pig_Hoof", lit), new Color(0.26f, 0.21f, 0.19f), 0.68f);
            AssetDatabase.SaveAssets();
        }

        static void SetupLitSkin(Material m, Texture2D detail)
        {
            m.SetColor("_BaseColor", SkinDefault);
            if (detail != null) m.SetTexture("_BaseMap", detail);
            m.SetFloat("_Smoothness", 0.64f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
        }

        static void Lit(Material m, Color c, float smoothness, bool castShadows = true)
        {
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetShaderPassEnabled("ShadowCaster", castShadows);
            EditorUtility.SetDirty(m);
        }

        public static IEnumerable<AnimationClip> Clips() =>
            AssetDatabase.LoadAllAssetsAtPath(PigCharacterImporter.FbxPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"));

        static AnimatorController CreateController()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                       ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
            int i = 0;
            AnimatorState idle = null;
            foreach (var clip in Clips().Where(c => !c.name.StartsWith("FACE_")).OrderBy(c => c.name))
            {
                var st = sm.AddState(clip.name, new Vector3(300 + (i % 3) * 220, (i / 3) * 60, 0));
                st.motion = clip;
                if (clip.name == "Idle_Breathing") idle = st;
                i++;
            }
            if (idle != null) sm.defaultState = idle;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static void CreatePrefab(AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PigCharacterImporter.FbxPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = "PF_Pig";
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            var face = go.GetComponent<PigFaceController>() ?? go.AddComponent<PigFaceController>();
            face.skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
            face.facePoseClips = Clips().Where(c => c.name.StartsWith("FACE_")).ToArray();
            var sec = go.GetComponent<PigSecondaryMotion>() ?? go.AddComponent<PigSecondaryMotion>();
            sec.face = face;
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
        }
    }
}
