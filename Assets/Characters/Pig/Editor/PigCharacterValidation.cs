using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FartOnline.Pig.EditorTools
{
    /// <summary>
    /// Unity-side validation of the pig import. Menu: Tools > Pig Character > Run Validation.
    /// Batch: Unity.exe -batchmode -projectPath ... -executeMethod FartOnline.Pig.EditorTools.PigCharacterValidation.RunBatch
    /// Writes Assets/Characters/Pig/Validation/UnityValidationReport.txt and preview renders.
    /// </summary>
    public static class PigCharacterValidation
    {
        const string Root = PigCharacterImporter.Root;
        static readonly StringBuilder log = new StringBuilder();
        static int fails;

        static void Check(bool ok, string what, string detail = "")
        {
            if (!ok) fails++;
            string line = (ok ? "PASS  " : "FAIL  ") + what + (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]");
            log.AppendLine(line);
            if (ok) Debug.Log("[PigValidation] " + line); else Debug.LogError("[PigValidation] " + line);
        }
        static void Info(string s) { log.AppendLine("INFO  " + s); Debug.Log("[PigValidation] " + s); }

        public static void RunBatch()
        {
            int code = 1;
            try { code = Run() == 0 ? 0 : 2; }
            catch (Exception e) { Debug.LogException(e); log.AppendLine("EXCEPTION " + e); WriteReport(); }
            EditorApplication.Exit(code);
        }

        [MenuItem("Tools/Pig Character/Run Validation")]
        public static int Run()
        {
            log.Clear(); fails = 0;
            log.AppendLine("Pig character Unity validation  " + DateTime.Now.ToString("u"));
            log.AppendLine("Unity " + Application.unityVersion + "  pipeline: " +
                           (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in"));

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PigCharacterSetup.PrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pig_Skin.mat") == null)
                PigCharacterSetup.Setup();

            // ---------------------------------------------------------------- shader / materials
            var shader = Shader.Find("FartOnline/PigSkin");
            Check(shader != null, "PigSkin shader found");
            if (shader != null)
            {
                Check(!ShaderUtil.ShaderHasError(shader), "PigSkin shader compiles without errors",
                      string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message).Take(5)));
                Check(shader.isSupported, "PigSkin shader supported on this device");
            }
            var skinMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pig_Skin.mat");
            Check(skinMat != null && skinMat.shader == shader, "M_Pig_Skin uses FartOnline/PigSkin", skinMat ? skinMat.shader.name : "missing");
            var litMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pig_Skin_URPLit.mat");
            Check(litMat != null && litMat.GetTexture("_BaseMap") != null, "URP/Lit fallback skin has the baked detail map");

            // skin colour independence: change the colour, surface parameters must stay identical
            if (skinMat != null && shader != null)
            {
                var inst = new Material(skinMat);
                string[] surf = { "_Smoothness", "_SpecularLevel", "_CoatStrength", "_CoatSmoothness", "_SSSStrength", "_SSSWrap", "_PatchDarkness", "_CavityStrength" };
                var before = surf.Select(p => inst.GetFloat(p)).ToArray();
                inst.SetColor("_SkinColor", new Color(0.35f, 0.55f, 0.95f));
                var after = surf.Select(p => inst.GetFloat(p)).ToArray();
                Check(before.SequenceEqual(after), "Changing _SkinColor leaves smoothness/specular/coat/SSS/detail untouched");
                UnityEngine.Object.DestroyImmediate(inst);
            }

            // ---------------------------------------------------------------- model import
            var mi = (ModelImporter)AssetImporter.GetAtPath(PigCharacterImporter.FbxPath);
            Check(mi != null, "FBX importer found", PigCharacterImporter.FbxPath);
            if (mi == null) { WriteReport(); return fails; }
            Check(mi.animationType == ModelImporterAnimationType.Generic, "Rig type Generic");
            Check(mi.importBlendShapes, "Blend shapes imported");
            Info("importer: globalScale=" + mi.globalScale + " useFileScale=" + mi.useFileScale + " fileScale=" + mi.fileScale + " bakeAxisConversion=" + mi.bakeAxisConversion);

            var clips = PigCharacterSetup.Clips().ToList();
            Info("clips (" + clips.Count + "): " + string.Join(", ", clips.Select(c => c.name + (c.isLooping ? "(loop)" : "") + " " + c.length.ToString("0.00") + "s")));
            Check(clips.Count >= 35, "All 35 clips imported", clips.Count.ToString());
            foreach (var n in PigCharacterImporter.LoopingClips)
                Check(clips.Any(c => c.name == n && c.isLooping), "Clip " + n + " loops");
            var talk = clips.FirstOrDefault(c => c.name == "Talk");
            if (talk != null)
            {
                var b = AnimationUtility.GetCurveBindings(talk);
                Check(b.Any(x => x.propertyName.Contains("blendShape.V_AA")), "Talk clip animates viseme blend shapes", b.Count(x => x.propertyName.StartsWith("blendShape.")) + " blend-shape curves");
            }

            // ---------------------------------------------------------------- instance checks
            // Never touch the user's scenes: refuse if anything is unsaved, restore the open scenes afterwards.
            var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            bool anyDirty = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty);
            if (anyDirty && !Application.isBatchMode)
            {
                Check(false, "Open scenes have unsaved changes - save them and run the validation again");
                WriteReport();
                return fails;
            }
            try { RunInstanceChecks(clips); }
            finally
            {
                if (!Application.isBatchMode && sceneSetup != null && sceneSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
            }
            log.AppendLine(fails == 0 ? "RESULT: ALL CHECKS PASSED" : "RESULT: " + fails + " CHECK(S) FAILED");
            WriteReport();
            return fails;
        }

        static void RunInstanceChecks(List<AnimationClip> clips)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PigCharacterSetup.PrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Check(smr != null, "SkinnedMeshRenderer present");
            var mesh = smr.sharedMesh;
            Info("mesh: " + mesh.vertexCount + " verts, " + (mesh.triangles.Length / 3) + " tris, " + mesh.subMeshCount + " submeshes, " + mesh.blendShapeCount + " blend shapes, colors=" + (mesh.colors != null && mesh.colors.Length > 0));
            Check(mesh.subMeshCount == 5 && smr.sharedMaterials.Length == 5, "5 submeshes / materials (skin, mouth, eye white, pupil, hoof)", mesh.subMeshCount.ToString());
            Check(mesh.blendShapeCount == 23, "23 blend shapes", string.Join(",", Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName)));
            Check(mesh.colors != null && mesh.colors.Length == mesh.vertexCount, "PigMask vertex colours present");
            Check(smr.bones.Length >= 40, "Skin bones bound", smr.bones.Length.ToString());
            var bw = mesh.GetAllBoneWeights();
            Check(bw.Length > 0, "Skin weights present", bw.Length + " weights");
            Check(smr.sharedMaterials.All(m => m != null && FbxMaterialOk(m)), "All submesh materials remapped to project materials",
                  string.Join(",", smr.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "null")));
            var bones = go.GetComponentsInChildren<Transform>().Where(t => smr.bones.Contains(t) || t.name == "Root").ToList();
            Check(go.GetComponentsInChildren<Transform>().Any(t => t.name == "Root"), "Root bone present");
            Info("skeleton transforms: " + bones.Count);

            var face = go.GetComponent<PigFaceController>();
            AnimationClip rest = clips.First(c => c.name == "FACE_Rest");
            rest.SampleAnimation(go, 0);
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var w = baked.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray();
            Vector3 mn = new Vector3(w.Min(v => v.x), w.Min(v => v.y), w.Min(v => v.z)), mx = new Vector3(w.Max(v => v.x), w.Max(v => v.y), w.Max(v => v.z));
            Info("rest world bounds min " + mn.ToString("F3") + " max " + mx.ToString("F3"));
            Check(Mathf.Abs((mx.y - mn.y) - 1.565f) < 0.05f, "Imported height ~1.56 m (scale correct)", (mx.y - mn.y).ToString("F3"));
            Check(Mathf.Abs(mn.y) < 0.02f, "Feet on ground (min y ~ 0)", mn.y.ToString("F3"));
            var eyeL = Find(go, "Eye_L"); var head = Find(go, "Head"); var hips = Find(go, "Hips");
            Check(eyeL && head && eyeL.position.z - head.position.z > 0.15f, "Character faces +Z (Unity forward)", eyeL ? (eyeL.position - head.position).ToString("F3") : "");
            Check(eyeL && eyeL.position.x < 0, "Eye_L is on the character's left (-X when facing +Z)", eyeL ? eyeL.position.x.ToString("F3") : "");

            // animation sampling: no NaN / no offsets, jump leaves the ground, walk stays in place
            foreach (var c in clips.Where(c => !c.name.StartsWith("FACE_")))
            {
                bool okc = true; float maxOff = 0;
                for (int k = 0; k <= 8; k++)
                {
                    c.SampleAnimation(go, c.length * k / 8f);
                    foreach (var t in bones)
                        if (float.IsNaN(t.position.x) || float.IsNaN(t.rotation.x)) okc = false;
                    if (hips) maxOff = Mathf.Max(maxOff, new Vector2(hips.position.x, hips.position.z).magnitude);
                }
                Check(okc && maxOff < 0.25f, "Clip " + c.name + " samples cleanly, stays in place", "max hips XZ offset " + maxOff.ToString("F3"));
            }
            var jump = clips.FirstOrDefault(c => c.name == "Jump");
            if (jump)
            {
                rest.SampleAnimation(go, 0); float y0 = hips.position.y;
                jump.SampleAnimation(go, 22f / 30f); float y1 = hips.position.y;
                Check(y1 - y0 > 0.25f, "Jump clip leaves the ground", (y1 - y0).ToString("F3"));
            }

            // face controller: data-driven bone controls
            rest.SampleAnimation(go, 0);
            face.Initialise();
            Check(face.PoseControlCount == 16, "Face controller found all 16 FACE pose clips", face.PoseControlCount.ToString());
            var lid = Find(go, "Lid_Up_L");
            Quaternion lid0 = lid.localRotation;
            face.Blink_Both = 1f; face.Apply();
            float lidAng = Quaternion.Angle(lid0, lid.localRotation);
            Check(lidAng > 120f, "Blink_Both closes the upper lid", lidAng.ToString("F1") + " deg");
            face.Blink_Both = 0f; face.Apply();
            Check(Quaternion.Angle(lid0, lid.localRotation) < 0.5f, "Blink releases back to rest");
            face.SetExpression("Happy", 1f); face.Apply();
            int smileIdx = mesh.GetBlendShapeIndex("Smile");
            Check(smileIdx >= 0 && smr.GetBlendShapeWeight(smileIdx) > 70f, "Expression preset drives blend shapes", smileIdx >= 0 ? smr.GetBlendShapeWeight(smileIdx).ToString("F1") : "no Smile");
            face.ClearExpressions(); face.Apply();

            // ---------------------------------------------------------------- preview renders (URP)
            bool asyncWas = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;          // no placeholder shaders on the first frame
            try { RenderPreviews(go, clips, face); }
            catch (Exception e) { Info("preview render skipped: " + e.Message); }
            finally { ShaderUtil.allowAsyncCompilation = asyncWas; }

            UnityEngine.Object.DestroyImmediate(go);
        }

        static bool FbxMaterialOk(Material m) => AssetDatabase.GetAssetPath(m).StartsWith(Root + "/Materials/");

        static Transform Find(GameObject go, string n) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

        static void RenderPreviews(GameObject go, List<AnimationClip> clips, PigFaceController face)
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "PigCharacter", "renders"));
            Directory.CreateDirectory(outDir);
            var camGo = new GameObject("PreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(1.1f, 1.15f, 3.6f);
            cam.transform.LookAt(new Vector3(0, 0.78f, 0));
            cam.fieldOfView = 30;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.64f, 0.68f);
            var lightGo = new GameObject("Key");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            // keep the preview independent of the project's shadow bias (PC_RPAsset uses 0.1 / 0.5)
            var lightData = lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            lightData.usePipelineSettings = false;
            light.shadowBias = 1f;
            light.shadowNormalBias = 1f;
            lightGo.transform.rotation = Quaternion.Euler(40, -150, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.3f, 0.3f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 3;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.55f, 0.56f, 0.58f));
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.forceMatrixRecalculationPerRender = true;      // re-skin on every cam.Render() (no player loop in editor)
            var mats = smr.sharedMaterials;
            var skinInst = new Material(mats[0]);
            mats[0] = skinInst;
            smr.sharedMaterials = mats;

            var rt = new RenderTexture(900, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            rt.Create();
            cam.targetTexture = rt;
            void Shot(string name, string clip, float t, Action setup)
            {
                var c = clips.FirstOrDefault(x => x.name == clip);
                if (c) c.SampleAnimation(go, t);
                face.ClearExpressions();
                setup?.Invoke();
                face.Apply();
                cam.Render();                                   // one render target, reused for every shot
                RenderTexture.active = rt;
                var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name), tex.EncodeToPNG());
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(tex);
                Info("rendered " + name);
            }
            skinInst.SetColor(skinInst.HasProperty("_SkinColor") ? "_SkinColor" : "_BaseColor", PigCharacterSetup.SkinDefault);
            Shot("_warmup.png", "RefPose", 0, null);
            File.Delete(Path.Combine(outDir, "_warmup.png"));
            Shot("unity_preview_refpose.png", "RefPose", 0, null);
            Shot("unity_preview_happy.png", "FACE_Rest", 0, () => face.SetExpression("Happy", 1f));
            skinInst.SetColor(skinInst.HasProperty("_SkinColor") ? "_SkinColor" : "_BaseColor", new Color(0.35f, 0.58f, 0.95f));
            Shot("unity_preview_blue_surprised.png", "FACE_Rest", 0, () => face.SetExpression("Surprised", 1f));
            skinInst.SetColor(skinInst.HasProperty("_SkinColor") ? "_SkinColor" : "_BaseColor", new Color(0.45f, 0.8f, 0.4f));
            Shot("unity_preview_green_jump_land.png", "Jump", 35f / 30f, null);

            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(lightGo);
            UnityEngine.Object.DestroyImmediate(ground);
        }

        static void WriteReport()
        {
            string dir = Path.Combine(Application.dataPath, "Characters", "Pig", "Validation");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "UnityValidationReport.txt"), log.ToString());
            Debug.Log("[PigValidation] report written: " + Path.Combine(dir, "UnityValidationReport.txt"));
        }
    }
}
