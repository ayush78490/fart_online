using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using FartOnline.Game;
using FartOnline.Environment;
using FartOnline.Water;
using FartOnline.CameraRig;
using FartOnline.EnvironmentEditor;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Generates every game asset from code (deterministic, re-runnable):
    ///  - Assets/Game/Settings/RaftTuning.asset (kept if it exists)
    ///  - gas materials + puff/ring meshes, PF_FartBurstVFX and the variants VFX_FartTap/Normal/Charged/Brake/Side/Cooldown
    ///  - PF_PlayerSeat (seat, thrust, gas tank, pig character, cushion, thrust origin, gas stream)
    ///  - PF_SharedRaft (wooden raft, physics, buoyancy, four nested seats, splash/wake effects, debug gizmos)
    ///  - the playable scene Assets/Game/Scenes/RaftCourse_Game.unity (copy of the environment scene + game systems)
    /// Menu: Tools > Fart Online > ...
    /// </summary>
    public static class GameBuilder
    {
        public const string Root = "Assets/Game";
        public const string SettingsDir = Root + "/Settings";
        public const string TuningPath = SettingsDir + "/RaftTuning.asset";
        public const string InputPath = SettingsDir + "/RaftGameplay.inputactions";
        public const string RaftDir = Root + "/Raft";
        public const string RaftPrefabPath = RaftDir + "/Prefabs/PF_SharedRaft.prefab";
        public const string SeatPrefabPath = Root + "/Characters/PF_PlayerSeat.prefab";
        public const string FartDir = Root + "/VFX/Fart";
        public const string ImpactDir = Root + "/VFX/Impacts";
        public const string WaterFxDir = Root + "/VFX/Water";
        public const string MeshDir = Root + "/Raft/Meshes";
        public const string GameScenePath = Root + "/Scenes/RaftCourse_Game.unity";
        const string PigPath = "Assets/Characters/Pig/PF_Pig.prefab";

        [MenuItem("Tools/Fart Online/Build Everything (assets + game scene)")]
        public static void BuildEverything()
        {
            BuildAssets();
            BuildGameScene();
        }

        [MenuItem("Tools/Fart Online/Build Assets (raft, seats, VFX)")]
        public static void BuildAssets()
        {
            foreach (var d in new[] { SettingsDir, RaftDir + "/Prefabs", RaftDir + "/Materials", RaftDir + "/Physics", MeshDir, FartDir, ImpactDir, WaterFxDir,
                                      Root + "/Characters", Root + "/Scenes", Root + "/Audio", Root + "/Documentation", Root + "/Tests", Root + "/Obstacles/Prefabs" })
                MeshKit.EnsureFolder(d);
            Tuning();
            var vfx = BuildFartVFX();
            var seat = BuildSeatPrefab();
            BuildRaftPrefab(seat);
            AssetDatabase.SaveAssets();
            Debug.Log("[FartOnline] assets built: tuning, " + vfx.Count + " gas VFX prefabs, PF_PlayerSeat, PF_SharedRaft");
        }

        // ------------------------------------------------------------------ settings
        public static RaftTuning Tuning()
        {
            var t = AssetDatabase.LoadAssetAtPath<RaftTuning>(TuningPath);
            if (t != null) return t;
            t = ScriptableObject.CreateInstance<RaftTuning>();
            AssetDatabase.CreateAsset(t, TuningPath);
            return t;
        }

        // ------------------------------------------------------------------ materials and meshes
        static Material Mat(string path, string shader, System.Action<Material> setup)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            else m.shader = Shader.Find(shader);
            setup?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material GasMaterial() => Mat(FartDir + "/M_GasCloud.mat", "FartOnline/GasCloud", m =>
        {
            m.SetColor("_BaseColor", new Color(0.74f, 0.93f, 0.34f));
            m.SetColor("_ShadeColor", new Color(0.38f, 0.64f, 0.24f));
            m.SetFloat("_Opacity", 0.86f);
            m.SetFloat("_EdgeSoftness", 1.1f);
            m.enableInstancing = false;
        });

        static Material RingMaterial() => Mat(FartDir + "/M_GasRing.mat", "FartOnline/GasCloud", m =>
        {
            m.SetColor("_BaseColor", new Color(0.92f, 1f, 0.7f));
            m.SetColor("_ShadeColor", new Color(0.8f, 0.95f, 0.55f));
            m.SetFloat("_Opacity", 0.75f);
            m.SetFloat("_EdgeSoftness", 0.15f);
        });

        static Material SparkMaterial() => WaterfallBuilder.ParticleMaterial();   // soft dot, alpha blended, tinted by particle colour

        static Mesh PuffMesh()
        {
            string p = MeshDir + "/Mesh_GasPuff.asset";
            PropFactory.Icosphere(2, out var vs, out var ts);
            var k = new MeshKit();
            foreach (var v in vs) k.Add(v.normalized * 0.5f, new Vector2(v.x, v.y), Color.white);
            k.tri.AddRange(ts);
            k.OrientOutward(0, k.tri.Count, Vector3.zero);
            return MeshKit.SaveAsset(ParticleMesh(k, "Mesh_GasPuff"), p);
        }

        static Mesh RingMesh()
        {
            string p = MeshDir + "/Mesh_GasRing.asset";
            var k = new MeshKit();
            int seg = 40;
            for (int side = 0; side < 2; side++)
            {
                int t0 = k.tri.Count;
                int[] inner = new int[seg + 1], outer = new int[seg + 1];
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    inner[i] = k.Add(d * 0.36f, new Vector2(i / (float)seg, 0f), Color.white);
                    outer[i] = k.Add(d * 0.5f, new Vector2(i / (float)seg, 1f), Color.white);
                }
                k.Strip(inner, outer);
                k.OrientRange(t0, k.tri.Count, side == 0 ? Vector3.back : Vector3.forward);
            }
            return MeshKit.SaveAsset(ParticleMesh(k, "Mesh_GasRing"), p);
        }

        /// <summary>Mesh for particle rendering: position, normal and UV only. Extra attributes (vertex colours,
        /// tangents, UV2) collide with the particle vertex streams and replace the per-particle colour.</summary>
        static Mesh ParticleMesh(MeshKit k, string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(k.v);
            m.SetUVs(0, k.uv);
            m.SetTriangles(k.tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ particle helpers
        static ParticleSystem PS(Transform parent, string name, Mesh mesh, Material mat, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            main.startRotation3D = false;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enableGPUInstancing = false;            // GasCloud has no particle-instancing path: keep per-particle vertex colours
            if (mesh != null)
            {
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = mesh;
                r.alignment = ParticleSystemRenderSpace.World;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static void Burst(ParticleSystem ps, int min, int max, float time = 0f)
        {
            var em = ps.emission;
            em.enabled = true;
            em.SetBursts(new[] { new ParticleSystem.Burst(time, (short)Mathf.Max(1, min), (short)Mathf.Max(1, max)) });
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = angle;
            sh.radius = radius;
            sh.radiusThickness = 1f;
        }

        static void Grow(ParticleSystem ps, float from, float to, bool easeOut = true)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var c = easeOut ? new AnimationCurve(new Keyframe(0f, from, 0f, (to - from) * 3f), new Keyframe(1f, to, 0f, 0f))
                            : AnimationCurve.Linear(0f, from, 1f, to);
            sol.size = new ParticleSystem.MinMaxCurve(1f, c);
        }

        static void Fade(ParticleSystem ps, Color a, Color b, float holdAlpha = 0.6f)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, holdAlpha), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        static void Drag(ParticleSystem ps, float drag)
        {
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.limit = 50f;
            lv.drag = drag;
            lv.multiplyDragByParticleSize = false;
        }

        static void Wobble(ParticleSystem ps, float strength)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = strength;
            n.frequency = 0.8f;
            n.scrollSpeed = 0.4f;
            n.quality = ParticleSystemNoiseQuality.Low;
        }

        static void Main(ParticleSystem ps, Vector2 life, Vector2 speed, Vector2 size, Color color, float gravity = 0f)
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            m.startColor = color;
            m.gravityModifier = gravity;
        }

        // ------------------------------------------------------------------ gas VFX
        struct GasSpec
        {
            public string name; public ThrustKind kind; public float count, size, speed; public bool ring, sparkles, wisps; public Color tint; public float life;
        }

        static readonly GasSpec[] Specs =
        {
            new GasSpec { name = "PF_FartBurstVFX", kind = ThrustKind.Sustained, count = 1f, size = 1f, speed = 1f, ring = true, sparkles = true, wisps = true, tint = Color.white, life = 1f },
            new GasSpec { name = "VFX_FartNormal", kind = ThrustKind.Sustained, count = 1f, size = 1f, speed = 1f, ring = true, sparkles = true, wisps = true, tint = Color.white, life = 1f },
            new GasSpec { name = "VFX_FartTap", kind = ThrustKind.Tap, count = 0.55f, size = 0.75f, speed = 1.2f, ring = true, sparkles = false, wisps = false, tint = new Color(1f, 1f, 0.92f), life = 0.75f },
            new GasSpec { name = "VFX_FartCharged", kind = ThrustKind.Charged, count = 1.9f, size = 1.55f, speed = 1.45f, ring = true, sparkles = true, wisps = true, tint = new Color(0.9f, 1f, 0.72f), life = 1.3f },
            new GasSpec { name = "VFX_FartBrake", kind = ThrustKind.Brake, count = 1.1f, size = 1.05f, speed = 1.2f, ring = true, sparkles = false, wisps = true, tint = new Color(1f, 0.95f, 0.62f), life = 1f },
            new GasSpec { name = "VFX_FartSide", kind = ThrustKind.Sustained, count = 0.8f, size = 0.9f, speed = 1.1f, ring = false, sparkles = true, wisps = true, tint = new Color(0.92f, 1f, 0.88f), life = 0.9f },
            new GasSpec { name = "VFX_FartCooldown", kind = ThrustKind.Cooldown, count = 0.3f, size = 0.6f, speed = 0.35f, ring = false, sparkles = false, wisps = false, tint = new Color(0.72f, 0.78f, 0.7f, 0.7f), life = 1.2f },
        };

        static Dictionary<string, FartBurstVFX> BuildFartVFX()
        {
            var puff = PuffMesh();
            var ring = RingMesh();
            var gas = GasMaterial();
            var ringMat = RingMaterial();
            var spark = SparkMaterial();
            var result = new Dictionary<string, FartBurstVFX>();
            foreach (var s in Specs)
            {
                var root = new GameObject(s.name);
                var fx = root.AddComponent<FartBurstVFX>();
                fx.kind = s.kind;
                fx.lifetime = 2.2f * s.life + 0.3f;
                Color light = s.tint, mid = Color.Lerp(s.tint, new Color(0.8f, 0.9f, 0.7f, s.tint.a), 0.5f);

                fx.gasCore = PS(root.transform, "GasCore", puff, gas, 16);
                Main(fx.gasCore, new Vector2(0.3f, 0.45f) * s.life, new Vector2(1.5f, 3f) * s.speed, new Vector2(0.35f, 0.55f) * s.size, light);
                Burst(fx.gasCore, Mathf.RoundToInt(3 * s.count), Mathf.RoundToInt(5 * s.count));
                Cone(fx.gasCore, 12f, 0.05f);
                Grow(fx.gasCore, 0.8f, 1.9f);
                Fade(fx.gasCore, light, mid, 0.4f);
                Drag(fx.gasCore, 3f);

                fx.gasCloud = PS(root.transform, "GasCloudParticles", puff, gas, 48);
                Main(fx.gasCloud, new Vector2(0.8f, 1.3f) * s.life, new Vector2(2f, 4.5f) * s.speed, new Vector2(0.25f, 0.6f) * s.size, light);
                Burst(fx.gasCloud, Mathf.RoundToInt(8 * s.count), Mathf.RoundToInt(14 * s.count));
                Cone(fx.gasCloud, 24f, 0.08f);
                Grow(fx.gasCloud, 0.6f, 2.2f);
                Fade(fx.gasCloud, light, mid, 0.55f);
                Drag(fx.gasCloud, 2.6f);
                Wobble(fx.gasCloud, 0.35f);

                fx.expandingCloud = PS(root.transform, "ExpandingCloud", puff, gas, 4);
                Main(fx.expandingCloud, new Vector2(1.0f, 1.3f) * s.life, new Vector2(0.8f, 1.2f) * s.speed, new Vector2(0.6f, 0.75f) * s.size, new Color(light.r, light.g, light.b, 0.75f * light.a));
                Burst(fx.expandingCloud, Mathf.Max(1, Mathf.RoundToInt(1 * s.count)), Mathf.Max(1, Mathf.RoundToInt(2 * s.count)), 0.04f);
                Cone(fx.expandingCloud, 5f, 0.02f);
                Grow(fx.expandingCloud, 0.7f, 3.6f);
                Fade(fx.expandingCloud, light, mid, 0.35f);
                Drag(fx.expandingCloud, 1.5f);

                if (s.wisps)
                {
                    fx.trailingWisps = PS(root.transform, "TrailingWisps", puff, gas, 40);
                    Main(fx.trailingWisps, new Vector2(0.6f, 1.0f) * s.life, new Vector2(0.6f, 1.6f) * s.speed, new Vector2(0.12f, 0.24f) * s.size, light);
                    var m = fx.trailingWisps.main; m.duration = 0.35f;
                    var em = fx.trailingWisps.emission; em.enabled = true; em.rateOverTime = 34f * s.count;
                    Cone(fx.trailingWisps, 35f, 0.06f);
                    Grow(fx.trailingWisps, 1f, 2.2f);
                    Fade(fx.trailingWisps, light, mid, 0.5f);
                    Drag(fx.trailingWisps, 1.8f);
                    Wobble(fx.trailingWisps, 0.5f);
                }
                if (s.ring)
                {
                    fx.shockRing = PS(root.transform, "ShockRing", ring, ringMat, 2);
                    Main(fx.shockRing, new Vector2(0.28f, 0.28f), new Vector2(0.8f, 0.8f) * s.speed, new Vector2(0.4f, 0.4f) * s.size, new Color(0.85f, 1f, 0.6f, 0.8f));
                    var m = fx.shockRing.main; m.startRotation = 0f;
                    Burst(fx.shockRing, 1, 1);
                    Cone(fx.shockRing, 0f, 0.001f);
                    Grow(fx.shockRing, 0.5f, 3.2f);
                    Fade(fx.shockRing, Color.white, Color.white, 0.3f);
                    fx.shockRing.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
                }
                if (s.sparkles)
                {
                    fx.sparkles = PS(root.transform, "SmallDebrisOrSparkles", null, spark, 24);
                    Main(fx.sparkles, new Vector2(0.4f, 0.7f), new Vector2(2f, 5f) * s.speed, new Vector2(0.06f, 0.13f), Color.white, -0.15f);
                    Burst(fx.sparkles, Mathf.RoundToInt(6 * s.count), Mathf.RoundToInt(10 * s.count));
                    Cone(fx.sparkles, 45f, 0.1f);
                    Fade(fx.sparkles, Color.white, Color.white, 0.5f);
                    Drag(fx.sparkles, 2f);
                }
                if (s.kind == ThrustKind.Cooldown)
                {
                    var m = fx.gasCore.main; m.gravityModifier = -0.05f;
                }
                var src = root.AddComponent<AudioSource>();       // optional per-effect sound slot (GameAudio plays the default cues)
                src.playOnAwake = false;
                src.spatialBlend = 0.7f;

                string dir = s.name.StartsWith("VFX_") ? FartDir + "/Variants" : FartDir;
                MeshKit.EnsureFolder(dir);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, dir + "/" + s.name + ".prefab");
                Object.DestroyImmediate(root);
                result[s.name] = prefab.GetComponent<FartBurstVFX>();
            }
            return result;
        }

        static ParticleSystem GasStream(Transform parent)
        {
            var ps = PS(parent, "GasStream", PuffMesh(), GasMaterial(), 160);
            Main(ps, new Vector2(0.55f, 0.9f), new Vector2(3f, 5f), new Vector2(0.18f, 0.34f), Color.white);
            var m = ps.main; m.loop = true; m.duration = 1f;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            Cone(ps, 14f, 0.05f);
            Grow(ps, 0.8f, 2.4f);
            Fade(ps, Color.white, new Color(0.85f, 0.95f, 0.75f), 0.5f);
            Drag(ps, 2.2f);
            Wobble(ps, 0.4f);
            var iv = ps.inheritVelocity; iv.enabled = true; iv.mode = ParticleSystemInheritVelocityMode.Current; iv.curve = 0.6f;
            m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            return ps;
        }

        // ------------------------------------------------------------------ seat prefab
        static GameObject BuildSeatPrefab()
        {
            var tuning = Tuning();
            var seat = new GameObject("PF_PlayerSeat");
            var res = seat.AddComponent<FartResourceController>();
            res.tuning = tuning;
            var thrust = seat.AddComponent<FartThrustController>();
            thrust.tuning = tuning;
            var psc = seat.AddComponent<PlayerSeatController>();

            // cushion (tinted with the player colour)
            var painted = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_Wood_Painted.mat");
            var k = new MeshKit();
            PropFactory.RoundedBox(k, new Vector3(0f, 0.05f, 0f), new Vector3(0.7f, 0.1f, 0.7f), 0.045f, Color.white, Quaternion.identity);
            var cushionMesh = MeshKit.SaveAsset(k.Build("Mesh_SeatCushion"), MeshDir + "/Mesh_SeatCushion.asset");
            var cushion = new GameObject("Cushion");
            cushion.transform.SetParent(seat.transform, false);
            cushion.AddComponent<MeshFilter>().sharedMesh = cushionMesh;
            var cr = cushion.AddComponent<MeshRenderer>();
            cr.sharedMaterial = painted;

            // character: pig seated on the cushion
            var anchor = new GameObject("CharacterAnchor").transform;
            anchor.SetParent(seat.transform, false);
            var pigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PigPath);
            GameObject pig = null;
            const float scale = 0.72f;
            if (pigPrefab != null)
            {
                pig = (GameObject)PrefabUtility.InstantiatePrefab(pigPrefab, anchor);
                pig.name = "Character_Pig";
                pig.transform.localScale = Vector3.one * scale;
                foreach (var c in pig.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                foreach (var rb in pig.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
                var hips = pig.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Hips");
                float hipsY = hips != null ? pig.transform.InverseTransformPoint(hips.position).y * scale : 0.45f;
                // hips rest just above the cushion top (0.1); the pelvis is roughly 0.14 m deep at this scale
                anchor.localPosition = new Vector3(0f, 0.1f + 0.12f - hipsY, 0.04f);
            }

            var anim = seat.AddComponent<CharacterThrustAnimator>();
            anim.thrust = thrust;
            if (pig != null)
            {
                anim.animator = pig.GetComponentInChildren<Animator>();
                anim.face = pig.GetComponentInChildren<FartOnline.Pig.PigFaceController>();
                anim.secondary = pig.GetComponentInChildren<FartOnline.Pig.PigSecondaryMotion>();
                if (anim.animator != null) anim.animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            anim.colorRenderers = new Renderer[] { cr };
            // the animator's root defines the character frame: move the component's frame reference to the anchor
            psc.characterAnchor = anchor;
            psc.character = anchor.gameObject;
            psc.characterAnimator = anim;

            var origin = new GameObject("ThrustOrigin").transform;
            origin.SetParent(seat.transform, false);
            origin.localPosition = new Vector3(0f, 0.12f + tuning.thrustOriginHeight, -0.36f);
            thrust.thrustOrigin = origin;

            GasStream(seat.transform).transform.localPosition = origin.localPosition;

            var prefab = PrefabUtility.SaveAsPrefabAsset(seat, SeatPrefabPath);
            Object.DestroyImmediate(seat);
            return prefab;
        }

        // ------------------------------------------------------------------ raft prefab
        /// <summary>
        /// Round raft deck: painted green planks trimmed to a circle on a dark base disc, with a small yellow hub. The
        /// inflatable tube around it is a separate child (<see cref="AddRaftTube"/>) so it can use smooth toy plastic.
        /// Vertex colours drive M_Wood_Painted (wood grain x colour).
        /// </summary>
        static Mesh RaftMesh(float R)
        {
            var k = new MeshKit();
            float rd = R - 0.3f;
            Color[] cols = { new Color(0.46f, 0.74f, 0.42f), new Color(0.40f, 0.68f, 0.38f), new Color(0.52f, 0.78f, 0.45f) };
            const float w = 0.42f, gap = 0.045f;
            int n = Mathf.FloorToInt(2f * rd / (w + gap));
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * (w + gap);
                float half = Mathf.Sqrt(Mathf.Max(0.04f, rd * rd - x * x)) - 0.05f;
                PropFactory.RoundedBox(k, new Vector3(x, 0.14f, 0f), new Vector3(w, 0.1f, 2f * half), 0.035f, cols[i % cols.Length], Quaternion.identity, 1);
            }
            // two cross battens under the planks and the base disc that closes the gaps
            foreach (float z in new[] { -0.9f, 0.9f })
                PropFactory.RoundedBox(k, new Vector3(0f, 0.05f, z), new Vector3(2f * Mathf.Sqrt(rd * rd - z * z) - 0.1f, 0.08f, 0.22f), 0.03f, new Color(0.36f, 0.24f, 0.15f), Quaternion.identity, 1);
            var disc = new List<Vector3> { new Vector3(0f, -0.06f, 0f), new Vector3(0f, 0.06f, 0f) };
            PropFactory.Tube(k, disc, rd, rd, 32, new Color(0.22f, 0.36f, 0.25f), new Color(0.22f, 0.36f, 0.25f), 0f, 0f, false);
            int t1 = k.tri.Count, top = k.Add(new Vector3(0f, 0.06f, 0f), Vector2.zero, new Color(0.22f, 0.36f, 0.25f));
            int[] ring = new int[33];
            for (int s2 = 0; s2 <= 32; s2++) { float a = s2 / 32f * Mathf.PI * 2f; ring[s2] = k.Add(new Vector3(Mathf.Cos(a) * rd, 0.06f, Mathf.Sin(a) * rd), Vector2.zero, new Color(0.22f, 0.36f, 0.25f)); }
            for (int s2 = 0; s2 < 32; s2++) { k.tri.Add(top); k.tri.Add(ring[s2]); k.tri.Add(ring[s2 + 1]); }
            k.OrientRange(t1, k.tri.Count, Vector3.up);
            // centre hub (round lid)
            var hub = new List<Vector3> { new Vector3(0f, 0.18f, 0f), new Vector3(0f, 0.26f, 0f) };
            PropFactory.Tube(k, hub, 0.42f, 0.38f, 20, new Color(0.96f, 0.76f, 0.26f), new Color(0.98f, 0.8f, 0.3f), 0f, 0f, false);
            int t2 = k.tri.Count, hc = k.Add(new Vector3(0f, 0.26f, 0f), Vector2.zero, new Color(1f, 0.84f, 0.36f));
            int[] cap = new int[21];
            for (int s2 = 0; s2 <= 20; s2++) { float a = s2 / 20f * Mathf.PI * 2f; cap[s2] = k.Add(new Vector3(Mathf.Cos(a) * 0.38f, 0.26f, Mathf.Sin(a) * 0.38f), Vector2.zero, new Color(1f, 0.84f, 0.36f)); }
            for (int s2 = 0; s2 < 20; s2++) { k.tri.Add(hc); k.tri.Add(cap[s2]); k.tri.Add(cap[s2 + 1]); }
            k.OrientRange(t2, k.tri.Count, Vector3.up);
            var m = k.Build("Mesh_SharedRaft");
            m.RecalculateNormals();
            return m;
        }

        /// <summary>
        /// Inflatable ring around the deck: a fat cream tube on a teal under-band, six red grab handles and a yellow rope
        /// looping between them. Visual only (the physics hull is the prism). Replaces an existing "RaftTube" child.
        /// </summary>
        public static void AddRaftTube(Transform raft, float R)
        {
            var old = raft.Find("RaftTube");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("RaftTube").transform;
            root.SetParent(raft, false);
            float rc = R - 0.3f, tr = 0.32f;
            var tube = new MeshKit(); var band = new MeshKit(); var handles = new MeshKit(); var rope = new MeshKit();
            var ring = new List<Vector3>(); var ring2 = new List<Vector3>();
            for (int i = 0; i <= 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                ring.Add(new Vector3(Mathf.Sin(a) * rc, 0.24f, Mathf.Cos(a) * rc));
                ring2.Add(new Vector3(Mathf.Sin(a) * (rc + 0.02f), 0.08f, Mathf.Cos(a) * (rc + 0.02f)));
            }
            PropFactory.Tube(tube, ring, tr, tr, 18, Color.white, Color.white, 0f, 0f, false);
            PropFactory.Tube(band, ring2, tr * 0.92f, tr * 0.92f, 18, Color.white, Color.white, 0f, 0f, false);
            const int nh = 6;
            for (int h = 0; h < nh; h++)
            {
                float a = (h + 0.5f) / nh * Mathf.PI * 2f;
                Vector3 outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 c = outward * (rc + tr * 0.55f) + Vector3.up * 0.3f;
                PropFactory.RoundedBox(handles, c, new Vector3(0.42f, 0.3f, 0.2f), 0.08f, Color.white, Quaternion.LookRotation(outward), 2);
                // rope sagging to the next handle along the outside of the tube
                float a2 = (h + 1.5f) / nh * Mathf.PI * 2f;
                var path = new List<Vector3>();
                for (int j = 0; j <= 12; j++)
                {
                    float t = j / 12f;
                    float aa = Mathf.Lerp(a, a2, t);
                    Vector3 o = new Vector3(Mathf.Sin(aa), 0f, Mathf.Cos(aa));
                    path.Add(o * (rc + tr + 0.03f) + Vector3.up * (0.3f - 0.2f * Mathf.Sin(t * Mathf.PI)));
                }
                PropFactory.Tube(rope, path, 0.04f, 0.04f, 6, Color.white, Color.white, 0f, 0f, false);
            }
            Part(root, "Raft_Tube", tube, AnimatedObstacleBuilder.Toy("RaftCream", new Color(0.98f, 0.93f, 0.8f), 0.6f));
            Part(root, "Raft_TubeBand", band, AnimatedObstacleBuilder.Toy("RaftTeal", new Color(0.24f, 0.7f, 0.66f), 0.55f));
            Part(root, "Raft_Handles", handles, AnimatedObstacleBuilder.Toy("RaftRed", new Color(0.86f, 0.22f, 0.18f), 0.5f));
            Part(root, "Raft_Rope", rope, AnimatedObstacleBuilder.Toy("RaftRope", new Color(0.96f, 0.76f, 0.22f), 0.2f), false);
        }

        static void Part(Transform parent, string name, MeshKit k, Material m, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = k.Build(name);
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(mesh, MeshDir + "/Mesh_" + name + ".asset");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Restyles the existing raft prefab (deck mesh + inflatable tube) without rebuilding seats, VFX or scenes.</summary>
        [MenuItem("Tools/Fart Online/Restyle Raft (inflatable ring)")]
        public static void RestyleRaft()
        {
            var tuning = Tuning();
            var contents = PrefabUtility.LoadPrefabContents(RaftPrefabPath);
            var visual = contents.transform.Find("RaftVisual");
            visual.GetComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(RaftMesh(tuning.raftRadius), MeshDir + "/Mesh_SharedRaft.asset");
            AddRaftTube(contents.transform, tuning.raftRadius);
            PrefabUtility.SaveAsPrefabAsset(contents, RaftPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.SaveAssets();
            Debug.Log("[FartOnline] raft restyled: plank deck + inflatable tube");
        }

        /// <summary>Convex prism (n-gon cylinder) for MeshCollider hulls.</summary>
        static Mesh Prism(string name, float radius, float y0, float y1, int n = 20)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Sin(a) * radius, y0, Mathf.Cos(a) * radius));
                v.Add(new Vector3(Mathf.Sin(a) * radius, y1, Mathf.Cos(a) * radius));
            }
            for (int i = 0; i < n; i++)
            {
                int a0 = i * 2, a1 = a0 + 1, b0 = ((i + 1) % n) * 2, b1 = b0 + 1;
                t.AddRange(new[] { a0, a1, b1, a0, b1, b0 });
                if (i > 0 && i < n - 1) { t.AddRange(new[] { 0, b0, a0 }); t.AddRange(new[] { 1, a1, b1 }); }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return MeshKit.SaveAsset(m, MeshDir + "/" + name + ".asset");
        }

        static ParticleSystem Splash(Transform parent, string name, int max, float speed, float size)
        {
            var ps = PS(parent, name, null, WaterfallBuilder.ParticleMaterial(), max);
            Main(ps, new Vector2(0.6f, 1.1f), new Vector2(speed * 0.5f, speed), new Vector2(size * 0.5f, size), new Color(1f, 1f, 1f, 0.9f), 1.3f);
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.6f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            Grow(ps, 0.6f, 1.6f);
            Fade(ps, Color.white, new Color(0.85f, 0.95f, 1f), 0.5f);
            return ps;
        }

        static void BuildRaftPrefab(GameObject seatPrefab)
        {
            var tuning = Tuning();
            float R = tuning.raftRadius;
            var root = new GameObject("PF_SharedRaft");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = tuning.mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var raftRoot = new GameObject("RaftRoot").transform;           // logical root marker (pivot = deck centre at the waterline)
            raftRoot.SetParent(root.transform, false);

            var visual = new GameObject("RaftVisual");
            visual.transform.SetParent(root.transform, false);
            var mesh = MeshKit.SaveAsset(RaftMesh(R), MeshDir + "/Mesh_SharedRaft.asset");
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_Wood_Painted.mat");
            AddRaftTube(root.transform, R);

            // physics hull (separate from the visual) + crew clearance volume so low beams catch heads, not just logs
            var phys = new GameObject("RaftPhysicsBody");
            phys.transform.SetParent(root.transform, false);
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(RaftDir + "/Physics/PM_RaftHull.asset");
            if (pm == null)
            {
                pm = new PhysicsMaterial("PM_RaftHull") { dynamicFriction = 0.15f, staticFriction = 0.2f, bounciness = 0.15f,
                                                           frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Average };
                AssetDatabase.CreateAsset(pm, RaftDir + "/Physics/PM_RaftHull.asset");
            }
            var hull = phys.AddComponent<MeshCollider>();
            hull.sharedMesh = Prism("Col_RaftHull", R, -0.3f, 0.36f);
            hull.convex = true;
            hull.sharedMaterial = pm;
            var crew = new GameObject("CrewClearance");
            crew.transform.SetParent(phys.transform, false);
            var crewHull = crew.AddComponent<MeshCollider>();
            crewHull.sharedMesh = Prism("Col_CrewClearance", tuning.seatRadius + 0.3f, 0.36f, 1.3f, 16);
            crewHull.convex = true;
            crewHull.sharedMaterial = pm;

            var buoy = root.AddComponent<RaftBuoyancy>();
            buoy.buoyancy = tuning.buoyancy;
            var points = new Vector3[9];
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                points[i] = new Vector3(Mathf.Sin(a) * R * 0.78f, -0.45f, Mathf.Cos(a) * R * 0.78f);
            }
            points[8] = new Vector3(0f, -0.45f, 0f);
            buoy.floatPoints = points;
            var bp = new GameObject("BuoyancyPoints").transform;
            bp.SetParent(root.transform, false);
            for (int i = 0; i < points.Length; i++) { var p = new GameObject("FloatPoint_" + i).transform; p.SetParent(bp, false); p.localPosition = points[i]; }

            var seats = root.AddComponent<RaftSeatManager>();
            seats.tuning = tuning;
            for (int i = 0; i < 4; i++)
            {
                var s = (GameObject)PrefabUtility.InstantiatePrefab(seatPrefab, root.transform);
                s.name = "PlayerSeat_0" + (i + 1);
                var psc = s.GetComponent<PlayerSeatController>();
                psc.seatIndex = i;
                s.GetComponent<FartThrustController>().seatIndex = i;
                s.transform.Find("ThrustOrigin").name = "ThrustOrigin_0" + (i + 1);
                seats.seats[i] = psc;
                psc.SetAngle(seats.quadAngles[i], tuning.seatRadius, seats.seatHeight);
            }

            var raft = root.AddComponent<SharedRaftController>();
            raft.tuning = tuning;
            raft.seats = seats;
            var rec = root.AddComponent<RaftRecoveryManager>();

            var splashRoot = new GameObject("SplashEffects").transform;
            splashRoot.SetParent(root.transform, false);
            var impact = Splash(splashRoot, "ImpactSplash", 80, 4f, 0.45f);
            var landing = Splash(splashRoot, "LandingSplash", 160, 6f, 0.7f);
            var wakeRoot = new GameObject("WakeEffects").transform;
            wakeRoot.SetParent(root.transform, false);
            wakeRoot.localPosition = new Vector3(0f, 0.03f, -R - 0.2f);
            var wake = PS(wakeRoot, "Wake", null, WaterfallBuilder.ParticleMaterial(), 120);
            Main(wake, new Vector2(1.2f, 1.8f), new Vector2(0.1f, 0.4f), new Vector2(0.5f, 0.9f), new Color(1f, 1f, 1f, 0.45f));
            var wm = wake.main; wm.loop = true; wm.duration = 1f; wm.playOnAwake = true;
            var wsh = wake.shape; wsh.enabled = true; wsh.shapeType = ParticleSystemShapeType.Box; wsh.scale = new Vector3(R * 1.5f, 0.05f, 0.3f);
            Grow(wake, 0.8f, 2.2f, false);
            Fade(wake, Color.white, Color.white, 0.3f);
            wake.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            var audio = new GameObject("RaftAudio");
            audio.transform.SetParent(root.transform, false);
            var loop = audio.AddComponent<AudioSource>();       // hook for a water/creak loop (optional clip)
            loop.playOnAwake = false;
            loop.loop = true;
            loop.spatialBlend = 1f;

            var fb = root.AddComponent<FartFeedbackController>();
            fb.raft = raft;
            fb.recovery = rec;
            fb.impactSplash = impact;
            fb.landingSplash = landing;
            fb.wake = wake;
            for (int i = 0; i < 4; i++) fb.seatStreams[i] = seats.seats[i].transform.Find("GasStream").GetComponent<ParticleSystem>();

            var dbg = new GameObject("RaftDebugGizmos");
            dbg.transform.SetParent(root.transform, false);
            dbg.AddComponent<RaftDebugGizmos>().raft = raft;

            PrefabUtility.SaveAsPrefabAsset(root, RaftPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ game scene
        [MenuItem("Tools/Fart Online/Build Game Scene")]
        public static void BuildGameScene()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty &&
                    UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path != GameScenePath)   // the game scene is regenerated anyway
                {
                    Debug.LogError("[FartOnline] save or discard the open scene changes first (no dialog is shown so this can run unattended)");
                    return;
                }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RaftPrefabPath) == null) BuildAssets();
            MeshKit.EnsureFolder(Root + "/Scenes");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // release the old game scene before replacing it
            AssetDatabase.DeleteAsset(GameScenePath);
            if (!AssetDatabase.CopyAsset(CourseGenerator.ScenePath, GameScenePath)) { Debug.LogError("[FartOnline] could not copy the environment scene"); return; }
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

            var course = GameObject.Find(CourseGenerator.RootName).transform;
            var test = course.Find("TestRaft");
            if (test != null) Object.DestroyImmediate(test.gameObject);
            var markers = course.Find("Graybox_Markers");
            if (markers != null) markers.gameObject.SetActive(false);
            var river = Object.FindAnyObjectByType<RiverPath>();
            var start = course.Find("Respawn_Start");

            var game = new GameObject("Game").transform;
            var raftGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RaftPrefabPath), game);
            raftGo.name = "SharedRaft";
            raftGo.transform.SetPositionAndRotation(start.position, start.rotation);
            var raft = raftGo.GetComponent<SharedRaftController>();
            raftGo.GetComponent<RaftBuoyancy>().river = river;
            var rec = raftGo.GetComponent<RaftRecoveryManager>();
            rec.respawnPoint = start;

            var current = GetOrAdd<WaterCurrentController>(river.gameObject);
            current.strength = Tuning().currentStrength;

            var systems = new GameObject("GameSystems").transform;
            systems.SetParent(game, false);
            var cpm = systems.gameObject.AddComponent<CheckpointManager>();
            cpm.river = river;
            cpm.checkpoints = Object.FindObjectsByType<FartOnline.Obstacles.Checkpoint>(FindObjectsInactive.Include).OrderBy(c => c.index).ToArray();
            cpm.finish = Object.FindAnyObjectByType<FartOnline.Obstacles.FinishGate>();
            var run = systems.gameObject.AddComponent<GameRunManager>();
            run.raft = raft;
            run.recovery = rec;
            run.checkpoints = cpm;
            run.startPoint = start;

            var cam = course.Find("GameplayCamera").GetComponent<Camera>();
            cam.GetComponent<RaftChaseCamera>().target = raftGo.transform;
            if (cam.GetComponent<RaftCameraShake>() == null) cam.gameObject.AddComponent<RaftCameraShake>();

            var session = systems.gameObject.AddComponent<GameSession>();
            session.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            session.raft = raft;
            session.recovery = rec;
            session.run = run;
            session.gameplayCamera = cam;

            var audioGo = new GameObject("GameAudio");
            audioGo.transform.SetParent(systems, false);
            audioGo.AddComponent<GameAudio>();

            var poolGo = new GameObject("FartVFXPool");
            poolGo.transform.SetParent(systems, false);
            var pool = poolGo.AddComponent<FartVFXPool>();
            FartBurstVFX V(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(FartDir + "/Variants/" + n + ".prefab").GetComponent<FartBurstVFX>();
            pool.variants = new[]
            {
                new FartVFXPool.Variant { kind = ThrustKind.Sustained, lateral = false, prefab = V("VFX_FartNormal"), count = 6 },
                new FartVFXPool.Variant { kind = ThrustKind.Sustained, lateral = true, prefab = V("VFX_FartSide"), count = 6 },
                new FartVFXPool.Variant { kind = ThrustKind.Tap, lateral = false, prefab = V("VFX_FartTap"), count = 8 },
                new FartVFXPool.Variant { kind = ThrustKind.Tap, lateral = true, prefab = V("VFX_FartSide"), count = 4 },
                new FartVFXPool.Variant { kind = ThrustKind.Charged, lateral = false, prefab = V("VFX_FartCharged"), count = 4 },
                new FartVFXPool.Variant { kind = ThrustKind.Brake, lateral = false, prefab = V("VFX_FartBrake"), count = 4 },
                new FartVFXPool.Variant { kind = ThrustKind.Cooldown, lateral = false, prefab = V("VFX_FartCooldown"), count = 4 },
            };

            var hudGo = new GameObject("GameplayHUD");
            hudGo.transform.SetParent(game, false);
            var hud = hudGo.AddComponent<GameplayHUD>();
            hud.run = run;
            hud.session = session;
            hud.raft = raft;
            hud.recovery = rec;
            hud.viewCamera = cam;

            // online co-op: NetworkManager + Unity Transport (direct IP), session flow, command-line test harness
            var nmGo = new GameObject("NetworkManager");          // must be a scene root object (NGO refuses nested managers)
            var nm = nmGo.AddComponent<Unity.Netcode.NetworkManager>();
            var utp = nmGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            nm.NetworkConfig = new Unity.Netcode.NetworkConfig { NetworkTransport = utp, EnableSceneManagement = false, TickRate = 30, ConnectionApproval = true };
            var syncPrefab = NetRaftSyncPrefab();
            var prefabList = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>(NetPrefabListPath);
            if (prefabList == null)
            {
                prefabList = ScriptableObject.CreateInstance<Unity.Netcode.NetworkPrefabsList>();
                AssetDatabase.CreateAsset(prefabList, NetPrefabListPath);
            }
            if (!prefabList.Contains(syncPrefab)) prefabList.Add(new Unity.Netcode.NetworkPrefab { Prefab = syncPrefab });
            EditorUtility.SetDirty(prefabList);
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
            var nsm = systems.gameObject.AddComponent<FartOnline.Game.Net.NetworkSessionManager>();
            nsm.network = nm;
            nsm.netRaftPrefab = syncPrefab.GetComponent<FartOnline.Game.Net.NetworkRaftController>();
            nsm.raft = raft;
            nsm.session = session;
            nsm.run = run;
            nsm.hud = hud;
            var auto = systems.gameObject.AddComponent<FartOnline.Game.Net.NetAutoTest>();
            auto.sessionManager = nsm;
            auto.run = run;
            auto.raft = raft;

            GameSceneExtras?.Invoke(game, systems);   // HUD, network (added by later builders)

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var list = EditorBuildSettings.scenes.Where(s => s.path != GameScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(GameScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[FartOnline] game scene built: " + GameScenePath);
        }

        public const string NetDir = Root + "/Scripts/Multiplayer/Prefabs";
        public const string NetPrefabListPath = Root + "/Settings/NetworkPrefabs.asset";

        /// <summary>PF_NetRaftSync: the only network prefab. Spawned by the host; replicates the scene raft.</summary>
        static GameObject NetRaftSyncPrefab()
        {
            MeshKit.EnsureFolder(Root + "/Raft/Prefabs");
            string path = Root + "/Raft/Prefabs/PF_NetRaftSync.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = new GameObject("PF_NetRaftSync");
            go.AddComponent<Unity.Netcode.NetworkObject>();
            go.AddComponent<FartOnline.Game.Net.NetworkRaftController>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        public const string PlayerBuildPath = @"D:\FOBuild\FartOnline.exe";

        [MenuItem("Tools/Fart Online/Build Windows Player (development)")]
        public static void BuildPlayer()
        {
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { EndlessBuilder.EndlessScenePath },   // Fart Online = the endless river
                locationPathName = PlayerBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            PlayerSettings.productName = EndlessBuilder.ProductName;
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[FartOnline] player build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalSeconds:F0} s -> {PlayerBuildPath}");
        }

        /// <summary>Extension point so other editor builders (HUD, networking) can add to the game scene.</summary>
        public static System.Action<Transform, Transform> GameSceneExtras;

        /// <summary>GetComponent or AddComponent. (Never use "??" with Unity objects: a missing component is a fake null in the editor.)</summary>
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
