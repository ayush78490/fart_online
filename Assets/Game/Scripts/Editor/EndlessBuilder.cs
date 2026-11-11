using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using FartOnline.Environment;
using FartOnline.EnvironmentEditor;
using FartOnline.Game;
using FartOnline.Game.Endless;
using FartOnline.Obstacles;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Builds the endless game ("Fart Online"): the obstacle prefabs (original procedural wood, rock, barrels, crates,
    /// buoys; hazards painted red/white) and the scene <c>Assets/Game/Scenes/FartOnline_Endless.unity</c>, a copy of
    /// the game scene with the fixed course removed and an <see cref="EndlessRiver"/> added. Sets the product name
    /// and puts the endless scene first in the build.
    /// Menu: Tools > Fart Online > Build Endless Game.
    /// All obstacle prefabs use river space: +Z downstream, +X river right, y = 0 at the water surface.
    /// </summary>
    public static class EndlessBuilder
    {
        public const string Dir = GameBuilder.Root + "/Endless";
        public const string PrefabDir = Dir + "/Prefabs";
        public const string MeshDir = Dir + "/Meshes";
        public const string EndlessScenePath = GameBuilder.Root + "/Scenes/FartOnline_Endless.unity";
        public const string ProductName = "Fart Online";
        const string EnvPrefabs = "Assets/Environment/Prefabs";

        static readonly Color Red = new Color(0.86f, 0.18f, 0.12f), White = new Color(0.95f, 0.93f, 0.88f);
        static readonly Color Rope = new Color(0.62f, 0.5f, 0.32f), Wood = Color.white;
        static Material wood, paint;

        static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/" + n + ".mat");

        [MenuItem("Tools/Fart Online/Build Endless Game (prefabs + scene)")]
        public static void BuildAll()
        {
            BuildPrefabs();
            BuildScene();
        }

        // ------------------------------------------------------------------ geometry helpers
        static void Beam(MeshKit k, Vector3 a, Vector3 b, float thick, Color tint, float stripe = 0f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            Quaternion rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.9f ? Vector3.forward : Vector3.up);
            if (stripe <= 0f) { PropFactory.RoundedBox(k, (a + b) * 0.5f, new Vector3(thick, thick, len), thick * 0.3f, tint, rot); return; }
            int n = Mathf.Max(1, Mathf.RoundToInt(len / stripe));
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                PropFactory.RoundedBox(k, Vector3.Lerp(a, b, (t0 + t1) * 0.5f), new Vector3(thick, thick, len / n + 0.02f), thick * 0.3f, i % 2 == 0 ? Red : White, rot, 2);
            }
        }

        static GameObject Visual(Transform parent, string name, MeshKit k, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(k.Build(name), MeshDir + "/" + name + ".asset");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        static GameObject Save(GameObject root)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/" + root.name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject Root(string name, string hazard)
        {
            var go = new GameObject(name);
            if (hazard != null) go.AddComponent<EndlessHazard>().label = hazard;
            return go;
        }

        static T Kinematic<T>(GameObject go) where T : KinematicObstacle
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return go.AddComponent<T>();
        }

        // ------------------------------------------------------------------ prefabs
        [MenuItem("Tools/Fart Online/Endless/Rebuild Obstacle Prefabs")]
        public static void BuildPrefabs()
        {
            MeshKit.EnsureFolder(PrefabDir);
            MeshKit.EnsureFolder(MeshDir);
            wood = M("M_Wood");
            paint = M("M_Wood_Painted");
            float hw = 7.5f;                 // EndlessRiver.waterWidth / 2

            // floating barrels: 4 barrels on their sides, all inside a 1.45 m circle
            {
                var go = Root("PF_Obs_Barrels", "floating barrels");
                var k = new MeshKit();
                Vector3[] at = { new Vector3(-0.55f, 0f, -0.45f), new Vector3(0.6f, 0f, -0.35f), new Vector3(-0.1f, 0f, 0.6f), new Vector3(0.1f, 0.62f, -0.05f) };
                float[] yaw = { 20f, -35f, 80f, 5f };
                for (int i = 0; i < at.Length; i++)
                {
                    Quaternion r = Quaternion.Euler(0f, yaw[i], 0f);
                    Vector3 axis = r * Vector3.right * 0.5f;
                    var path = new List<Vector3> { at[i] - axis + Vector3.up * 0.15f, at[i] + Vector3.up * 0.15f, at[i] + axis + Vector3.up * 0.15f };
                    PropFactory.Tube(k, path, 0.4f, 0.4f, 10, i % 2 == 0 ? Red : White, i % 2 == 0 ? White : Red, 0f, 0f, true);
                    var holder = new GameObject("Col_" + i);
                    holder.transform.SetParent(go.transform, false);
                    holder.transform.localPosition = at[i] + Vector3.up * 0.15f;
                    holder.transform.localRotation = r;
                    var cap = holder.AddComponent<CapsuleCollider>();
                    cap.direction = 0; cap.radius = 0.42f; cap.height = 1.0f + 0.84f;
                }
                Visual(go.transform, "Obs_Barrels", k, paint);
                Save(go);
            }
            // crate stack: two crates and one on top, inside a 1.25 m circle
            {
                var go = Root("PF_Obs_Crates", "a crate stack");
                var k = new MeshKit();
                (Vector3 c, float yaw)[] crates = { (new Vector3(-0.3f, 0.2f, 0f), 10f), (new Vector3(0.35f, 0.2f, 0.12f), -15f), (new Vector3(0f, 1.15f, 0.05f), 30f) };
                foreach (var (c, yaw) in crates)
                {
                    Quaternion r = Quaternion.Euler(0f, yaw, 0f);
                    PropFactory.RoundedBox(k, c, new Vector3(0.95f, 0.95f, 0.95f), 0.08f, new Color(0.85f, 0.65f, 0.4f), r, 2);
                    for (int s = -1; s <= 1; s += 2)   // slats
                        PropFactory.RoundedBox(k, c + r * new Vector3(0f, s * 0.3f, 0.49f), new Vector3(0.9f, 0.14f, 0.04f), 0.02f, new Color(0.6f, 0.42f, 0.25f), r, 1);
                    var holder = new GameObject("Col");
                    holder.transform.SetParent(go.transform, false);
                    holder.transform.localPosition = c;
                    holder.transform.localRotation = r;
                    holder.AddComponent<BoxCollider>().size = Vector3.one * 0.95f;
                }
                Visual(go.transform, "Obs_Crates", k, wood);
                Save(go);
            }
            // drifting log (6 m along X; the river scales X to the planned length)
            {
                var go = Root("PF_Obs_Log", "a drifting log");
                var k = new MeshKit();
                var path = new List<Vector3> { new Vector3(-3f, 0.12f, 0f), new Vector3(0f, 0.18f, 0f), new Vector3(3f, 0.12f, 0f) };
                PropFactory.Tube(k, path, 0.42f, 0.38f, 10, new Color(0.62f, 0.44f, 0.3f), new Color(0.55f, 0.38f, 0.26f), 0f, 0f, true);
                PropFactory.RoundedBox(k, new Vector3(0f, 0.6f, 0f), new Vector3(0.06f, 0.5f, 0.06f), 0.02f, Wood, Quaternion.identity, 1);   // flag pole
                PropFactory.RoundedBox(k, new Vector3(0.2f, 0.78f, 0f), new Vector3(0.36f, 0.22f, 0.03f), 0.02f, Red, Quaternion.identity, 1);
                Visual(go.transform, "Obs_Log", k, paint);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 0; cap.radius = 0.42f; cap.height = 6.0f; cap.center = new Vector3(0f, 0.15f, 0f);
                Save(go);
            }
            // buoy
            {
                var go = Root("PF_Obs_Buoy", "a buoy line");
                var k = new MeshKit();
                PropFactory.RoundedBox(k, new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.75f, 0.9f), 0.36f, Red, Quaternion.identity, 3);
                PropFactory.RoundedBox(k, new Vector3(0f, 0.55f, 0f), new Vector3(0.55f, 0.4f, 0.55f), 0.22f, White, Quaternion.identity, 3);
                PropFactory.RoundedBox(k, new Vector3(0f, 0.95f, 0f), new Vector3(0.08f, 0.5f, 0.08f), 0.03f, Red, Quaternion.identity, 1);
                Visual(go.transform, "Obs_Buoy", k, paint);
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = 0.46f; sc.center = new Vector3(0f, 0.2f, 0f);
                Save(go);
            }
            // rope between buoys (1 m along Z, scaled to length; no collider)
            {
                var go = Root("PF_Obs_BuoyRope", null);
                var k = new MeshKit();
                Beam(k, new Vector3(0f, 0.32f, -0.5f), new Vector3(0f, 0.32f, 0.5f), 0.06f, Rope);
                Visual(go.transform, "Obs_BuoyRope", k, wood, false);
                Save(go);
            }
            // gate post (painted), top flag
            {
                var go = Root("PF_Obs_GatePost", "a gate post");
                var k = new MeshKit();
                Beam(k, new Vector3(0f, -2f, 0f), new Vector3(0f, 4.8f, 0f), 0.6f, Wood, 0.7f);
                PropFactory.RoundedBox(k, new Vector3(0.45f, 4.5f, 0f), new Vector3(0.8f, 0.45f, 0.04f), 0.02f, Red, Quaternion.identity, 1);
                Visual(go.transform, "Obs_GatePost", k, paint);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = 0.3f; cap.height = 6.8f; cap.center = new Vector3(0f, 1.4f, 0f);
                Save(go);
            }
            // gate banner (1 m along X, scaled to the opening; high above the crew, no collider)
            {
                var go = Root("PF_Obs_GateBanner", null);
                var k = new MeshKit();
                int n = 8;
                for (int i = 0; i < n; i++)
                    PropFactory.RoundedBox(k, new Vector3(-0.5f + (i + 0.5f) / n, 4.3f, 0f), new Vector3(1f / n + 0.005f, 0.55f, 0.18f), 0.02f, i % 2 == 0 ? Red : White, Quaternion.identity, 1);
                Visual(go.transform, "Obs_GateBanner", k, paint);
                Save(go);
            }
            // pendulum frame spanning the river (posts on the banks, not hazards)
            const float ropeLen = 3.6f, pivotH = 4.55f;
            {
                var go = Root("PF_Obs_PendulumFrame", null);
                var k = new MeshKit();
                float span = hw + 1.2f;
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 p = new Vector3(sd * span, 0f, 0f);
                    Beam(k, p - Vector3.up * 2f, p + Vector3.up * (pivotH + 0.6f), 0.6f, Wood);
                    var holder = new GameObject("Col_Post");
                    holder.transform.SetParent(go.transform, false);
                    holder.transform.localPosition = p + Vector3.up * (pivotH * 0.5f - 0.7f);
                    holder.AddComponent<BoxCollider>().size = new Vector3(0.6f, pivotH + 2.6f, 0.6f);
                }
                Beam(k, new Vector3(-span - 0.4f, pivotH + 0.3f, 0f), new Vector3(span + 0.4f, pivotH + 0.3f, 0f), 0.55f, Wood);
                Visual(go.transform, "Obs_PendulumFrame", k, wood);
                Save(go);
            }
            // pendulum swing: pivot (root) -> Log child (5 m along X, scaled), swings along the flow
            {
                var go = Root("PF_Obs_PendulumSwing", "a swinging log");
                var pend = Kinematic<Pendulum>(go);
                pend.length = ropeLen; pend.amplitude = 45f; pend.period = 3.2f;
                var logGo = new GameObject("Log");
                logGo.transform.SetParent(go.transform, false);
                var kl = new MeshKit();
                Vector3 lp = Vector3.down * ropeLen;
                Beam(kl, lp - Vector3.right * 2.5f, lp + Vector3.right * 2.5f, 0.9f, Wood, 0.8f);
                Visual(logGo.transform, "Obs_SwingLog", kl, paint);
                var kr = new MeshKit();
                for (int sd = -1; sd <= 1; sd += 2)
                    PropFactory.Tube(kr, new List<Vector3> { Vector3.right * sd * 1.75f, lp + Vector3.right * sd * 1.75f }, 0.05f, 0.05f, 5, Rope, Rope, 0f, 0f);
                Visual(logGo.transform, "Obs_SwingRopes", kr, wood, false);
                var cap = logGo.AddComponent<CapsuleCollider>();
                cap.direction = 0; cap.radius = 0.46f; cap.height = 5f; cap.center = lp;
                Save(go);
            }
            // sweeper: bank post + arm (5 m along +Z, scaled) wiping +-60 degrees across the river
            {
                var go = Root("PF_Obs_Sweeper", "a sweeper arm");
                var k = new MeshKit();
                Beam(k, new Vector3(0f, -2f, 0f), new Vector3(0f, 2.2f, 0f), 0.9f, Wood);
                Visual(go.transform, "Obs_SweeperPost", k, wood);
                var holder = new GameObject("Col_Post");
                holder.transform.SetParent(go.transform, false);
                holder.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                holder.AddComponent<BoxCollider>().size = new Vector3(0.9f, 4.2f, 0.9f);
                var pivot = new GameObject("Pivot");
                pivot.transform.SetParent(go.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                var sw = Kinematic<RotatingSweeper>(pivot);
                sw.degreesPerSecond = 60f; sw.wipeArc = 120f; sw.reach = 5f;
                var arm = new GameObject("Arm");
                arm.transform.SetParent(pivot.transform, false);
                var ka = new MeshKit();
                Beam(ka, Vector3.zero, Vector3.forward * 5f, 0.55f, Wood, 0.8f);
                Visual(arm.transform, "Obs_SweeperArm", ka, paint);
                var bc = arm.AddComponent<BoxCollider>();
                bc.center = Vector3.forward * 2.5f; bc.size = new Vector3(0.55f, 0.55f, 5f);
                AnimatedObstacleBuilder.MalletHead(arm.transform, 5f);      // hammer arm
                Save(go);
            }
            // sliding log wall (moves along local +X; the river sets the travel)
            {
                var go = Root("PF_Obs_Barrier", "a sliding log wall");
                var mb = Kinematic<MovingBarrier>(go);
                mb.moveTime = 2.2f; mb.pauseTime = 1.0f;
                var k = new MeshKit();
                for (int i = 0; i < 3; i++)
                    Beam(k, new Vector3(0f, 0.15f + i * 0.45f, -1.5f), new Vector3(0f, 0.15f + i * 0.45f, 1.5f), 0.5f, Wood, 1f);
                Visual(go.transform, "Obs_BarrierLogs", k, paint);
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.55f, 0f); bc.size = new Vector3(0.6f, 1.4f, 3f);
                Save(go);
            }
            // spinner: a painted log turning on a buoy (Arm 5 m along X, scaled)
            {
                var go = Root("PF_Obs_Spinner", "a spinning log");
                var sw = Kinematic<RotatingSweeper>(go);
                sw.degreesPerSecond = 55f; sw.wipeArc = 0f; sw.reach = 2.5f;
                var k = new MeshKit();
                PropFactory.RoundedBox(k, new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.9f, 0.9f), 0.4f, White, Quaternion.identity, 3);
                Beam(k, new Vector3(0f, 0f, 0f), new Vector3(0f, 1.1f, 0f), 0.35f, Red);
                Visual(go.transform, "Obs_SpinnerHub", k, paint);
                var hub = go.AddComponent<CapsuleCollider>();
                hub.radius = 0.45f; hub.height = 1.4f; hub.center = new Vector3(0f, 0.4f, 0f);
                var arm = new GameObject("Arm");
                arm.transform.SetParent(go.transform, false);
                var ka = new MeshKit();
                Beam(ka, new Vector3(-2.5f, 0.75f, 0f), new Vector3(2.5f, 0.75f, 0f), 0.5f, Wood, 0.8f);
                Visual(arm.transform, "Obs_SpinnerArm", ka, paint);
                var bc = arm.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.75f, 0f); bc.size = new Vector3(5f, 0.5f, 0.5f);
                Save(go);
            }
            AnimatedObstacleBuilder.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("[FartOnline] endless obstacle prefabs built in " + PrefabDir);
        }

        public const string PackDir = "Assets/Obstacle Pack/Prefabs";
        static GameObject H(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(PackDir + "/" + name + ".prefab");
        static GameObject P(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + name + ".prefab");
        static GameObject E(string sub, string name) => AssetDatabase.LoadAssetAtPath<GameObject>(EnvPrefabs + "/" + sub + "/" + name + ".prefab");

        /// <summary>Spiked-log hazards and the bright bank decor (prefabs from <see cref="SpikeHazardBuilder"/>).</summary>
        public static void WireSpikesAndDecor(EndlessRiver e)
        {
            e.spikeSwing = P("PF_Obs_SpikeSwing");
            e.spikeMine = P("PF_Obs_SpikeMine");
            e.spikeLogRoll = P("PF_Obs_SpikeLogRoll");
            e.spikeBridge = P("PF_Obs_SpikeBridge");
            e.spikeCurtain = P("PF_Obs_SpikeCurtain");
            e.edgeRoller = P("PF_Obs_EdgeRoller");
            e.waitGate = P("PF_Obs_WaitGate");
            e.packPole = null;                  // the pack's hammer pole is 33k triangles: gates use the light procedural posts
            e.waterWidth = 15f;                 // tight river: only a little room around the obstacles
            e.edgeGuard = 1.75f;
            e.cliffBlocks = new[] { P("PF_Deco_CliffBlocks_A"), P("PF_Deco_CliffBlocks_B"), P("PF_Deco_CliffBlocks_C") };
            e.fence = P("PF_Deco_Fence");
            e.cascadeSplash = P("PF_Deco_CascadeSplash");
            e.cascadeMaterial = M("M_Waterfall");
            // more lush banks: extra plants on the shore and bushes on the plateau
            if (e.plateauProps != null && System.Array.IndexOf(e.plateauProps, E("Vegetation", "PF_Shrub_B")) < 0)
                e.plateauProps = e.plateauProps.Concat(new[] { E("Vegetation", "PF_Shrub_B"), E("Vegetation", "PF_BroadLeaf_A") }).ToArray();
            EditorUtility.SetDirty(e);
        }

        /// <summary>Rebuilds the spiked hazards and decor and wires them into the open endless scene (no scene rebuild).</summary>
        [MenuItem("Tools/Fart Online/Endless/Rebuild Spiked Hazards + Decor (in place)")]
        public static void RebuildSpikesInPlace()
        {
            if (EditorSceneManager.GetActiveScene().path != EndlessScenePath) EditorSceneManager.OpenScene(EndlessScenePath);
            BuildPrefabs();                     // gantry spans follow the river width; also rebuilds the spiked hazards
            var e = Object.FindAnyObjectByType<EndlessRiver>();
            for (int i = e.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(e.transform.GetChild(i).gameObject);   // never save a built river
            e.path.points.Clear();
            WireSpikesAndDecor(e);
            EditorSceneManager.MarkSceneDirty(e.gameObject.scene);
            EditorSceneManager.SaveScene(e.gameObject.scene);
            Debug.Log("[FartOnline] spiked hazards and decor wired into " + EndlessScenePath);
        }

        // ------------------------------------------------------------------ scene
        [MenuItem("Tools/Fart Online/Endless/Rebuild Endless Scene")]
        public static void BuildScene()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (sc.isDirty && sc.path != EndlessScenePath && sc.path != GameBuilder.GameScenePath)
                {
                    Debug.LogError("[FartOnline] save or discard the open scene changes first");
                    return;
                }
            }
            if (P("PF_Obs_Barrels") == null) BuildPrefabs();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameBuilder.GameScenePath) == null) GameBuilder.BuildGameScene();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(EndlessScenePath);
            if (!AssetDatabase.CopyAsset(GameBuilder.GameScenePath, EndlessScenePath)) { Debug.LogError("[FartOnline] could not copy the game scene"); return; }
            var scene = EditorSceneManager.OpenScene(EndlessScenePath, OpenSceneMode.Single);

            // strip the fixed course: keep the river object, start marker, camera, sun and post-processing
            var world = GameObject.Find(CourseGenerator.RootName).transform;
            world.name = "EndlessWorld";
            foreach (var n in new[] { "Terrain", "Water", "Graybox_Markers", "Waterfalls", "Dressing", "Background", "Gameplay", "ReflectionProbe" })
            {
                var t = world.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            var river = Object.FindAnyObjectByType<RiverPath>();
            river.points.Clear();
            river.drawGizmos = false;
            var start = world.Find("Respawn_Start");

            var raftGo = GameObject.Find("Game/SharedRaft");
            var run = Object.FindAnyObjectByType<GameRunManager>();
            var cpm = Object.FindAnyObjectByType<CheckpointManager>();
            cpm.checkpoints = new Checkpoint[0];
            cpm.finish = null;

            var eGo = new GameObject("EndlessRiver");
            eGo.transform.SetParent(world, false);
            var e = eGo.AddComponent<EndlessRiver>();
            e.path = river;
            e.settings = AssetDatabase.LoadAssetAtPath<CourseSettings>("Assets/Environment/Settings/TropicalCourseSettings.asset");
            e.target = raftGo.transform;
            e.startPoint = start;
            e.terrainMaterial = M("M_Canyon");
            e.waterMaterial = M("M_River_Main");
            e.shoreProps = new[] { E("Vegetation", "PF_Fern_A"), E("Vegetation", "PF_Fern_B"), E("Vegetation", "PF_GrassTuft_A"), E("Vegetation", "PF_GrassTuft_B"),
                                   E("Vegetation", "PF_BroadLeaf_A"), E("Vegetation", "PF_BroadLeaf_B"), E("Vegetation", "PF_Shrub_A"), E("Vegetation", "PF_Shrub_B") };
            e.plateauProps = new[] { E("Vegetation", "PF_Palm_A"), E("Vegetation", "PF_Palm_B"), E("Vegetation", "PF_Palm_C"),
                                     E("Vegetation", "PF_JungleTree_A"), E("Vegetation", "PF_JungleTree_B"), E("Vegetation", "PF_JungleTree_C"), E("Vegetation", "PF_Shrub_A") };
            e.rockProps = new[] { E("Rocks", "PF_Rock_Pebble_F"), E("Rocks", "PF_Rock_Slab_D"), E("Rocks", "PF_Rock_Boulder_C") };
            e.boulders = new[] { E("Rocks", "PF_Rock_Boulder_A"), E("Rocks", "PF_Rock_Boulder_B"), E("Rocks", "PF_Rock_Boulder_C") };
            e.barrels = P("PF_Obs_Barrels");
            e.crates = P("PF_Obs_Crates");
            e.log = P("PF_Obs_Log");
            e.buoy = P("PF_Obs_Buoy");
            e.buoyRope = P("PF_Obs_BuoyRope");
            e.gatePost = P("PF_Obs_GatePost");
            e.gateBanner = P("PF_Obs_GateBanner");
            e.pendulumFrame = P("PF_Obs_PendulumFrame");
            e.pendulumSwing = P("PF_Obs_PendulumSwing");
            e.sweeper = P("PF_Obs_Sweeper");
            e.barrier = P("PF_Obs_Barrier");
            e.spinner = P("PF_Obs_Spinner");
            e.raftRadius = GameBuilder.Tuning().raftRadius;
            // Hyper-Casual Obstacle Pack (EnsArts, Asset Store): fitted to the planner's footprints at runtime
            e.packCrystals = new[] { H("ice wall"), H("ice wall 2") };
            e.packMine = H("Bomb");
            e.packBarricade = H("barrier");
            e.packJersey = H("barrier2");
            e.packRopeFence = H("log_trap2");
            e.packPole = H("Hammer");
            e.packSpikeWall = H("obstacle spikewall");
            e.packBlade = H("blade");
            e.packBuzzsaw = H("blade spinner");
            e.packPress = H("PressTrap");
            e.packSpikeTrap = H("obstacle spike");
            e.spikeRoller = P("PF_Obs_SpikeRoller");
            e.starSpinner = P("PF_Obs_StarSpinner");
            e.puffer = P("PF_Obs_Puffer");
            e.tentacle = P("PF_Obs_Tentacle");
            e.windmill = P("PF_Obs_Windmill");
            e.fireJet = P("PF_Obs_FireJet");
            e.whirlpool = P("PF_Obs_Whirlpool");
            e.mortar = P("PF_Obs_Mortar");
            WireSpikesAndDecor(e);
            // bank scenery (no colliders); the flamethrowers became real fire jets (PF_Obs_FireJet)
            e.packBankTraps = new[] { H("obstacle poisonthrower"), H("obstacle crossbow") };
            run.endless = e;
            run.startPoint = start;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var list = EditorBuildSettings.scenes.Where(s => s.path != EndlessScenePath && s.path != GameBuilder.GameScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(EndlessScenePath, true));
            list.Insert(1, new EditorBuildSettingsScene(GameBuilder.GameScenePath, false));
            EditorBuildSettings.scenes = list.ToArray();
            PlayerSettings.productName = ProductName;
            Debug.Log("[FartOnline] endless scene built: " + EndlessScenePath);
        }
    }
}
