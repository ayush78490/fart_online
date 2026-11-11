using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FartOnline.Environment;
using FartOnline.Water;
using FartOnline.CameraRig;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Builds the TropicalRaftCourse scene from the RiverPath + CourseSettings. Every step is deterministic
    /// (fixed seed) and idempotent: re-running replaces only the generated objects under the course root.
    /// Menu: Tools > Tropical Course.
    /// </summary>
    public static class CourseGenerator
    {
        public const string Root = "Assets/Environment";
        public const string ScenePath = "Assets/Scenes/TropicalRaftCourse.unity";
        public const string SettingsPath = Root + "/Settings/TropicalCourseSettings.asset";
        public const string MeshDir = Root + "/Meshes/Generated";
        public const string MatDir = Root + "/Materials";
        public const string RootName = "TropicalRaftCourse";

        // ------------------------------------------------------------------ assets
        public static CourseSettings Settings()
        {
            MeshKit.EnsureFolder(Root + "/Settings");
            var s = AssetDatabase.LoadAssetAtPath<CourseSettings>(SettingsPath);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<CourseSettings>();
                AssetDatabase.CreateAsset(s, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            return s;
        }

        public static Material Mat(string name, Color color, float smoothness = 0.25f)
        {
            MeshKit.EnsureFolder(MatDir);
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", smoothness);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        // ------------------------------------------------------------------ scene
        /// <summary>Opens (or creates) the course scene. Refuses if other open scenes have unsaved changes.</summary>
        public static bool OpenScene()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path == ScenePath) return true;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("[TropicalCourse] Save your open scenes first; refusing to switch scenes and lose changes.");
                    return false;
                }
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            else
            {
                var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(sc, ScenePath);
            }
            return true;
        }

        public static GameObject CourseRoot()
        {
            var go = GameObject.Find(RootName);
            if (go == null) go = new GameObject(RootName);
            return go;
        }

        public static Transform Child(Transform parent, string name, bool clear = false)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            if (clear)
                for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
            return t;
        }

        public static RiverPath River(bool resetLayout = false)
        {
            var root = CourseRoot().transform;
            var t = Child(root, "River");
            var path = t.GetComponent<RiverPath>(); if (path == null) path = t.gameObject.AddComponent<RiverPath>();
            if (resetLayout || path.points == null || path.points.Count < 2)
            {
                path.points = CourseLayout.Default();
                EditorUtility.SetDirty(path);
            }
            path.raftClearanceHalfWidth = Settings().MinRaftPassage * 0.5f;
            path.Invalidate();
            return path;
        }

        // ------------------------------------------------------------------ geometry
        public static void BuildTerrainAndWater()
        {
            var set = Settings();
            var path = River();
            var root = CourseRoot().transform;
            MeshKit.EnsureFolder(MeshDir);

            var terrainMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Canyon.mat") ?? Mat("M_Canyon_Graybox", new Color(0.78f, 0.47f, 0.30f));
            var waterMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_River_Main.mat") ?? Mat("M_River_Graybox", new Color(0.10f, 0.62f, 0.68f), 0.8f);

            // waterfalls first: their wet zones darken the rock they run over
            var falls = new List<WaterfallBuilder.Built>();
            var wet = new List<RiverTerrainBuilder.WetZone>();
            foreach (var spec in WaterfallBuilder.DefaultSpecs(path))
            {
                var b = WaterfallBuilder.Build(path, set, spec);
                if (b == null) continue;
                falls.Add(b);
                wet.Add(new RiverTerrainBuilder.WetZone { bottom = b.impact, top = b.top, radius = Mathf.Max(3f, b.spec.width * 0.6f + 2.5f) });
            }

            var terrainRoot = Child(root, "Terrain", true);
            var colMeshes = RiverTerrainBuilder.BuildTerrain(path, set, 48, true, wet);
            int ti = 0;
            foreach (var m in RiverTerrainBuilder.BuildTerrain(path, set, 48, false, wet))
            {
                var mesh = MeshKit.SaveAsset(m, MeshDir + "/Terrain_" + ti.ToString("00") + ".asset");
                var col = MeshKit.SaveAsset(colMeshes[Mathf.Min(ti, colMeshes.Count - 1)], MeshDir + "/TerrainCol_" + ti.ToString("00") + ".asset");
                var go = new GameObject("Terrain_" + ti.ToString("00"));
                go.transform.SetParent(terrainRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = terrainMat;
                go.AddComponent<MeshCollider>().sharedMesh = col;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                           StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
                ti++;
            }
            var waterRoot = Child(root, "Water", true);
            int wi = 0;
            foreach (var m in RiverTerrainBuilder.BuildWater(path, set))
            {
                var mesh = MeshKit.SaveAsset(m, MeshDir + "/Water_" + wi.ToString("00") + ".asset");
                var go = new GameObject("Water_" + wi.ToString("00"));
                go.transform.SetParent(waterRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = waterMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                wi++;
            }
            var fallRoot = Child(root, "Waterfalls", true);
            var sheetMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Waterfall.mat");
            var partMat = WaterfallBuilder.ParticleMaterial();
            foreach (var b in falls) WaterfallBuilder.CreateObject(fallRoot, b, sheetMat, partMat);
            // remove stale chunk assets from a previous, longer layout
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(p);
                if ((n.StartsWith("Terrain_") && int.TryParse(n.Substring(8), out int a) && a >= ti) ||
                    (n.StartsWith("TerrainCol_") && int.TryParse(n.Substring(11), out int c2) && c2 >= ti) ||
                    (n.StartsWith("Water_") && int.TryParse(n.Substring(6), out int b) && b >= wi))
                    AssetDatabase.DeleteAsset(p);
            }
            Debug.Log($"[TropicalCourse] terrain chunks {ti}, water spans {wi}, waterfalls {falls.Count}, river length {path.Length:F0} m");
        }

        // ------------------------------------------------------------------ raft + camera + light
        public static GameObject BuildRaft()
        {
            var set = Settings();
            var path = River();
            var root = CourseRoot().transform;
            var old = root.Find("TestRaft");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var start = path.SampleAtDistance(30f);
            var raft = new GameObject("TestRaft");
            raft.transform.SetParent(root, false);
            raft.transform.position = start.position + Vector3.up * 0.15f;
            raft.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(start.tangent, Vector3.up), Vector3.up);
            var rb = raft.AddComponent<Rigidbody>();
            rb.mass = 450f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var box = raft.AddComponent<BoxCollider>();
            box.size = new Vector3(set.raftSize.x, 0.55f, set.raftSize.y);
            box.center = new Vector3(0f, 0.05f, 0f);
            var buoy = raft.AddComponent<RaftBuoyancy>();
            buoy.river = path;
            float hx = set.raftSize.x * 0.43f, hz = set.raftSize.y * 0.43f;
            // float points at the bottom of the logs; buoyancy 22 -> equilibrium submersion g/22 = 0.45 m, deck rides at the surface
            buoy.buoyancy = 22f;
            buoy.floatPoints = new[] { new Vector3(-hx, -0.45f, -hz), new Vector3(hx, -0.45f, -hz), new Vector3(-hx, -0.45f, hz), new Vector3(hx, -0.45f, hz), new Vector3(0f, -0.45f, 0f) };
            raft.AddComponent<TestRaft>();

            // deck visual: logs + cross beams (graybox, replaced by the wooden raft prefab later)
            var wood = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Wood.mat") ?? Mat("M_Wood_Graybox", new Color(0.55f, 0.36f, 0.20f));
            var deck = new GameObject("Deck").transform;
            deck.SetParent(raft.transform, false);
            int logs = 7;
            for (int i = 0; i < logs; i++)
            {
                var log = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(log.GetComponent<Collider>());
                log.name = "Log_" + i;
                log.transform.SetParent(deck, false);
                float x = Mathf.Lerp(-set.raftSize.x * 0.5f + 0.3f, set.raftSize.x * 0.5f - 0.3f, i / (float)(logs - 1));
                log.transform.localPosition = new Vector3(x, 0f, 0f);
                log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                log.transform.localScale = new Vector3(0.58f, set.raftSize.y * 0.5f, 0.58f);
                log.GetComponent<Renderer>().sharedMaterial = wood;
            }
            // four pig passengers (task 1 asset)
            var pig = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Pig/PF_Pig.prefab");
            if (pig != null)
            {
                Vector2[] seats = { new Vector2(-0.95f, -0.95f), new Vector2(0.95f, -0.95f), new Vector2(-0.95f, 0.95f), new Vector2(0.95f, 0.95f) };
                for (int i = 0; i < 4; i++)
                {
                    var p = (GameObject)PrefabUtility.InstantiatePrefab(pig, raft.transform);
                    p.name = "Passenger_" + (i + 1);
                    p.transform.localPosition = new Vector3(seats[i].x, 0.3f, seats[i].y);
                    p.transform.localRotation = Quaternion.Euler(0f, i % 2 == 0 ? -12f : 12f, 0f);
                    p.transform.localScale = Vector3.one * 0.85f;
                }
            }
            var spawn = Child(root, "Respawn_Start");
            spawn.position = raft.transform.position;
            spawn.rotation = raft.transform.rotation;
            raft.GetComponent<TestRaft>().respawnPoint = spawn;
            return raft;
        }

        public static void BuildCameraAndLight(GameObject raft)
        {
            var root = CourseRoot().transform;
            var camT = root.Find("GameplayCamera");
            if (camT == null)
            {
                var c = new GameObject("GameplayCamera", typeof(Camera), typeof(AudioListener));
                c.tag = "MainCamera";
                c.transform.SetParent(root, false);
                camT = c.transform;
            }
            var cam = camT.GetComponent<Camera>();
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 900f;
            var chase = camT.GetComponent<RaftChaseCamera>(); if (chase == null) chase = camT.gameObject.AddComponent<RaftChaseCamera>();
            chase.target = raft.transform;
            chase.river = River();
            chase.SnapToTarget();

            var sun = root.Find("Sun");
            if (sun == null)
            {
                var l = new GameObject("Sun", typeof(Light));
                l.transform.SetParent(root, false);
                sun = l.transform;
            }
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;
            sun.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = light;
        }

        // ------------------------------------------------------------------ markers
        public static void BuildSectionMarkers()
        {
            var path = River();
            var root = Child(CourseRoot().transform, "Graybox_Markers", true);
            var mat = Mat("M_Graybox_Marker", new Color(1f, 0.85f, 0.1f));
            string last = null;
            foreach (var s in path.Samples)
            {
                if (s.section == last) continue;
                last = s.section;
                var m = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(m.GetComponent<Collider>());
                m.name = "SectionStart_" + s.section;
                m.transform.SetParent(root, false);
                m.transform.position = s.position + s.right * (s.width * 0.5f + 1.5f) + Vector3.up * 2f;
                m.transform.localScale = new Vector3(0.4f, 2f, 0.4f);
                m.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        // ------------------------------------------------------------------ dressing
        public static void BuildDressing()
        {
            var set = Settings();
            var path = River();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PropFactory.PrefabDir + "/Rocks/PF_Rock_Boulder_A.prefab") == null)
                PropFactory.RebuildLibrary();
            var root = Child(CourseRoot().transform, "Dressing");
            DressingPlacer.Scatter(path, set, root);
            DressingPlacer.Background(path, set, CourseRoot().transform, AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Canyon.mat"));
        }

        [MenuItem("Tools/Tropical Course/Rebuild Dressing and Background")]
        public static void RebuildDressingMenu()
        {
            if (!OpenScene()) return;
            BuildDressing();
            SaveScene();
        }

        // ------------------------------------------------------------------ entry points
        [MenuItem("Tools/Tropical Course/Build Graybox Scene")]
        public static void BuildGraybox()
        {
            if (!OpenScene()) return;
            River();
            BuildTerrainAndWater();
            var raft = BuildRaft();
            BuildCameraAndLight(raft);
            BuildSectionMarkers();
            BuildDressing();
            SaveScene();
        }

        [MenuItem("Tools/Tropical Course/Reset Layout To Default")]
        public static void ResetLayout()
        {
            if (!OpenScene()) return;
            River(true);
            BuildTerrainAndWater();
            SaveScene();
        }

        public static void SaveScene()
        {
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[TropicalCourse] scene saved: " + ScenePath);
        }
    }
}
