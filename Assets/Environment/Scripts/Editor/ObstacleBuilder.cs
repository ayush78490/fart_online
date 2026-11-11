using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FartOnline.Environment;
using FartOnline.Obstacles;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Builds the course's wooden structures, obstacles, checkpoints and finish from the RiverPath (Phase 6).
    /// All pieces are original procedural wood (rounded beams). Hazards wear red/white paint (M_Wood_Painted +
    /// vertex colours) so they read as warnings. Every passage keeps the raft's clearance (diagonal + margin).
    /// Menu: Tools > Tropical Course > Rebuild Obstacles.
    /// </summary>
    public static class ObstacleBuilder
    {
        static readonly Color Red = new Color(0.86f, 0.18f, 0.12f), White = new Color(0.95f, 0.93f, 0.88f);
        static readonly Color WoodTint = Color.white;
        static Material wood, paint, rope;

        // ------------------------------------------------------------------ geometry helpers
        struct Site
        {
            public Vector3 pos;      // on the water surface
            public Vector3 fwd, right;
            public float width;
            public RiverSample s;
        }

        static Site At(RiverPath path, float distance)
        {
            var s = path.SampleAtDistance(distance);
            var f = Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized;
            return new Site { pos = s.position, fwd = f, right = Vector3.Cross(Vector3.up, f), width = s.width, s = s };
        }

        static float SectionStart(RiverPath path, string sec)
        {
            foreach (var s in path.Samples) if (s.section == sec) return s.distance;
            return 0f;
        }

        /// <summary>Beam from a to b (rounded box), optionally painted in bands.</summary>
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
                Vector3 c = Vector3.Lerp(a, b, (t0 + t1) * 0.5f);
                PropFactory.RoundedBox(k, c, new Vector3(thick, thick, len / n + 0.02f), thick * 0.3f, i % 2 == 0 ? Red : White, rot, 2);
            }
        }

        static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material mat, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(mesh, CourseGenerator.MeshDir + "/" + name + ".asset");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        static void Box(GameObject go, Vector3 worldCenter, Vector3 size, Quaternion worldRot)
        {
            var holder = new GameObject("Collider");
            holder.transform.SetParent(go.transform, false);
            holder.transform.SetPositionAndRotation(worldCenter, worldRot);
            holder.AddComponent<BoxCollider>().size = size;
        }

        // ------------------------------------------------------------------ structures
        static GameObject Gate(Transform parent, string name, Site st, float opening, float height, bool painted, bool banner = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var kw = new MeshKit();
            var kp = new MeshKit();
            Quaternion rot = Quaternion.LookRotation(st.fwd);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 baseP = st.pos + st.right * sd * (opening * 0.5f + 0.35f) - Vector3.up * 2f;
                Beam(kw, baseP, baseP + Vector3.up * (height + 2f), 0.6f, WoodTint);
                Box(go, baseP + Vector3.up * (height * 0.5f + 1f), new Vector3(0.6f, height + 2f, 0.6f), rot);
            }
            Vector3 l = st.pos - st.right * (opening * 0.5f + 1.1f) + Vector3.up * height;
            Vector3 r = st.pos + st.right * (opening * 0.5f + 1.1f) + Vector3.up * height;
            Beam(painted ? kp : kw, l, r, 0.55f, WoodTint, painted ? 1.2f : 0f);
            Beam(kw, l - Vector3.up * 0.6f + st.right * 0.4f, r - Vector3.up * 0.6f - st.right * 0.4f, 0.3f, WoodTint);
            if (banner)
            {
                // chequered finish banner hanging under the top beam
                int n = Mathf.RoundToInt(opening / 0.8f);
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        Vector3 c = st.pos + st.right * (-opening * 0.5f + (i + 0.5f) * opening / n) + Vector3.up * (height - 1.0f - j * 0.75f);
                        PropFactory.RoundedBox(kp, c, new Vector3(opening / n, 0.72f, 0.12f), 0.03f, (i + j) % 2 == 0 ? new Color(0.08f, 0.08f, 0.08f) : White, rot, 1);
                    }
            }
            MeshObject(go.transform, name + "_Wood", kw.Build(name + "_Wood"), wood);
            if (kp.v.Count > 0) MeshObject(go.transform, name + "_Paint", kp.Build(name + "_Paint"), paint);
            return go;
        }

        static GameObject Dock(Transform parent, string name, Site st, int side, float length, float depth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var k = new MeshKit();
            Quaternion rot = Quaternion.LookRotation(st.fwd);
            Vector3 c0 = st.pos + st.right * side * (st.width * 0.5f - depth * 0.5f + 0.6f) + Vector3.up * 0.55f;
            int planks = Mathf.RoundToInt(length / 0.45f);
            for (int i = 0; i < planks; i++)
            {
                Vector3 c = c0 + st.fwd * (-length * 0.5f + (i + 0.5f) * length / planks);
                PropFactory.RoundedBox(k, c, new Vector3(depth, 0.16f, length / planks - 0.04f), 0.04f, Color.Lerp(WoodTint, WoodTint * 0.8f, (i * 37 % 10) / 10f), rot, 1);
            }
            for (int i = 0; i <= 3; i++)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 p = c0 + st.fwd * (-length * 0.5f + i * length / 3f) + st.right * sd * (depth * 0.5f - 0.2f);
                    Beam(k, p - Vector3.up * 3f, p + Vector3.up * 0.5f, 0.32f, WoodTint * 0.85f);
                }
            MeshObject(go.transform, name + "_Wood", k.Build(name), wood);
            Box(go, c0, new Vector3(depth, 0.25f, length), rot);
            return go;
        }

        /// <summary>
        /// Overhead beam spanning the whole channel (Obstacle G): bottom at 1.5 m, so an upright raft with its seated crew
        /// (crew clearance top 1.3 m) passes underneath, but a raft pitched or rolled by a bad burst catches the crew.
        /// </summary>
        static GameObject OverheadBeam(Transform parent, string name, Site st)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var kw = new MeshKit();
            var kp = new MeshKit();
            float half = st.width * 0.5f + 0.5f, h = 1.75f;
            Vector3 l = st.pos - st.right * half, r = st.pos + st.right * half;
            Beam(kw, l - Vector3.up * 2f, l + Vector3.up * 2.8f, 0.55f, WoodTint);
            Beam(kw, r - Vector3.up * 2f, r + Vector3.up * 2.8f, 0.55f, WoodTint);
            Beam(kp, l + Vector3.up * h, r + Vector3.up * h, 0.5f, WoodTint, 0.9f);
            MeshObject(go.transform, name + "_Wood", kw.Build(name + "_Wood"), wood);
            MeshObject(go.transform, name + "_Paint", kp.Build(name + "_Paint"), paint);
            Box(go, st.pos + Vector3.up * h, new Vector3(0.5f, 0.5f, half * 2f), Quaternion.LookRotation(st.right));
            Box(go, l + Vector3.up * 0.4f, new Vector3(0.55f, 4.8f, 0.55f), Quaternion.identity);
            Box(go, r + Vector3.up * 0.4f, new Vector3(0.55f, 4.8f, 0.55f), Quaternion.identity);
            return go;
        }

        static GameObject LowBeam(Transform parent, string name, Site st, int side, float reach)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var kw = new MeshKit();
            var kp = new MeshKit();
            Vector3 bank = st.pos + st.right * side * (st.width * 0.5f + 0.4f);
            Beam(kw, bank - Vector3.up * 2f, bank + Vector3.up * 2.6f, 0.55f, WoodTint);
            Vector3 a = bank + Vector3.up * 0.95f, b = bank - st.right * side * reach + Vector3.up * 0.95f;
            Beam(kp, a, b, 0.5f, WoodTint, 0.9f);
            Beam(kw, bank + Vector3.up * 2.4f, Vector3.Lerp(a, b, 0.6f) + Vector3.up * 0.2f, 0.18f, WoodTint * 0.7f);   // brace
            MeshObject(go.transform, name + "_Wood", kw.Build(name + "_Wood"), wood);
            MeshObject(go.transform, name + "_Paint", kp.Build(name + "_Paint"), paint);
            Box(go, (a + b) * 0.5f, new Vector3(0.5f, 0.5f, reach), Quaternion.LookRotation(b - a));
            Box(go, bank + Vector3.up * 0.3f, new Vector3(0.55f, 4.6f, 0.55f), Quaternion.identity);
            return go;
        }

        static GameObject PendulumRig(Transform parent, string name, Site st, float ropeLen, float logLen, float phase)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var frame = new MeshKit();
            float pivotH = 0.95f + ropeLen;
            float span = st.width * 0.5f + 1.2f;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 p = st.pos + st.right * sd * span;
                Beam(frame, p - Vector3.up * 2f, p + Vector3.up * (pivotH + 0.6f), 0.6f, WoodTint);
                Box(go, p + Vector3.up * pivotH * 0.5f, new Vector3(0.6f, pivotH + 2.6f, 0.6f), Quaternion.LookRotation(st.fwd));
            }
            Beam(frame, st.pos - st.right * (span + 0.4f) + Vector3.up * (pivotH + 0.3f), st.pos + st.right * (span + 0.4f) + Vector3.up * (pivotH + 0.3f), 0.55f, WoodTint);
            MeshObject(go.transform, name + "_Frame", frame.Build(name + "_Frame"), wood);
            // swinging part: pivot (Pendulum) -> ropes + painted log, swings along the flow (about the river's right axis)
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(go.transform, false);
            pivot.transform.SetPositionAndRotation(st.pos + Vector3.up * pivotH, Quaternion.LookRotation(-st.fwd, Vector3.up) * Quaternion.Euler(0, -90, 0));
            pivot.transform.rotation = Quaternion.LookRotation(Vector3.Cross(st.right, Vector3.up), Vector3.up);   // local X = river right
            var rb = pivot.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var pend = pivot.AddComponent<Pendulum>();
            pend.length = ropeLen; pend.amplitude = 52f; pend.period = 3.4f; pend.phaseOffset = phase;
            var swing = new MeshKit();
            var logK = new MeshKit();
            Vector3 lp = Vector3.down * ropeLen;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                var ropePath = new List<Vector3> { Vector3.right * sd * logLen * 0.35f, lp + Vector3.right * sd * logLen * 0.35f };
                PropFactory.Tube(swing, ropePath, 0.05f, 0.05f, 5, new Color(0.55f, 0.45f, 0.3f), new Color(0.55f, 0.45f, 0.3f), 0, 0);
            }
            Beam(logK, lp - Vector3.right * logLen * 0.5f, lp + Vector3.right * logLen * 0.5f, 0.9f, WoodTint, 0.8f);
            var ropes = MeshObject(pivot.transform, name + "_Ropes", swing.Build(name + "_Ropes"), wood);
            var logGo = MeshObject(pivot.transform, name + "_Log", logK.Build(name + "_Log"), paint);
            var cap = logGo.AddComponent<CapsuleCollider>();
            cap.direction = 0; cap.radius = 0.48f; cap.height = logLen; cap.center = lp;
            return go;
        }

        static GameObject Sweeper(Transform parent, string name, Site st, int side, float reach, float phase)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var post = new MeshKit();
            Vector3 p = st.pos + st.right * side * (st.width * 0.5f + 0.2f);
            Beam(post, p - Vector3.up * 2f, p + Vector3.up * 2.2f, 0.9f, WoodTint);
            MeshObject(go.transform, name + "_Post", post.Build(name + "_Post"), wood);
            Box(go, p + Vector3.up * 0.1f, new Vector3(0.9f, 4.2f, 0.9f), Quaternion.identity);
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(go.transform, false);
            // forward points across the river at the middle of the wipe
            pivot.transform.SetPositionAndRotation(p + Vector3.up * 0.75f, Quaternion.LookRotation(-st.right * side, Vector3.up));
            pivot.AddComponent<Rigidbody>().isKinematic = true;
            var sw = pivot.AddComponent<RotatingSweeper>();
            sw.degreesPerSecond = 70f; sw.wipeArc = 150f; sw.reach = reach; sw.phaseOffset = phase;
            var arm = new MeshKit();
            Beam(arm, Vector3.zero, Vector3.forward * reach, 0.55f, WoodTint, 0.8f);
            var armGo = MeshObject(pivot.transform, name + "_Arm", arm.Build(name + "_Arm"), paint);
            var bc = armGo.AddComponent<BoxCollider>();
            bc.center = Vector3.forward * reach * 0.5f; bc.size = new Vector3(0.55f, 0.55f, reach);
            return go;
        }

        static GameObject Barrier(Transform parent, string name, Site st, float length, float travel, float phase)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(st.pos - st.right * travel * 0.5f, Quaternion.LookRotation(st.fwd));
            go.AddComponent<Rigidbody>().isKinematic = true;
            var mb = go.AddComponent<MovingBarrier>();
            mb.travel = Vector3.right * travel; mb.moveTime = 2.4f; mb.pauseTime = 1.1f; mb.phaseOffset = phase;
            var k = new MeshKit();
            // floating log wall: three stacked painted logs along the flow, local space
            for (int i = 0; i < 3; i++)
                Beam(k, new Vector3(0, 0.15f + i * 0.45f, -length * 0.5f), new Vector3(0, 0.15f + i * 0.45f, length * 0.5f), 0.5f, WoodTint, 1f);
            var vis = new GameObject(name + "_Logs");
            vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(k.Build(name + "_Logs"), CourseGenerator.MeshDir + "/" + name + "_Logs.asset");
            vis.AddComponent<MeshRenderer>().sharedMaterial = paint;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, 0.6f, 0); bc.size = new Vector3(0.6f, 1.6f, length);
            return go;
        }

        static GameObject Kicker(Transform parent, string name, Site lipSite, RiverPath path, float lipDistance)
        {
            // rock ramp in the chute centre: rises from the water to +1.3 m at the lip and launches the raft
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var a = path.SampleAtDistance(lipDistance - 7f);
            Vector3 start = a.position - Vector3.up * 0.6f;
            Vector3 end = lipSite.pos + Vector3.up * 1.3f;
            Vector3 dir = (end - start);
            float len = dir.magnitude;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var k = new MeshKit();
            Vector3 size = new Vector3(5.4f, 0.9f, len);
            PropFactory.RoundedBox(k, (start + end) * 0.5f - rot * Vector3.up * 0.45f, size, 0.35f, new Color(0.6f, 0.35f, 0.4f, 1f), rot, 3);
            var rock = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_Rock_Wet.mat");
            MeshObject(go.transform, name + "_Ramp", k.Build(name + "_Ramp"), rock);
            Box(go, (start + end) * 0.5f - rot * Vector3.up * 0.45f, size, rot);
            return go;
        }

        static GameObject WarningPost(Transform parent, string name, Site st, int side)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var k = new MeshKit();
            Vector3 p = st.pos + st.right * side * (st.width * 0.5f + 0.9f);
            Beam(k, p - Vector3.up * 1f, p + Vector3.up * 3.2f, 0.4f, WoodTint, 0.5f);
            // arrow sign pointing downstream
            Vector3 s0 = p + Vector3.up * 2.6f - st.right * side * 0.3f;
            PropFactory.RoundedBox(k, s0, new Vector3(1.4f, 0.55f, 0.12f), 0.06f, Red, Quaternion.LookRotation(st.right * side));
            MeshObject(go.transform, name + "_Paint", k.Build(name), paint);
            return go;
        }

        static GameObject CheckpointAt(Transform parent, string name, Site st, int index)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(st.pos + Vector3.up * 1.5f, Quaternion.LookRotation(st.fwd));
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(st.width + 2f, 6f, 3f);
            var cp = go.AddComponent<Checkpoint>();
            cp.index = index;
            var resp = new GameObject("Respawn");
            resp.transform.SetParent(go.transform, false);
            resp.transform.SetPositionAndRotation(st.pos + st.fwd * 3f + Vector3.up * 0.15f, Quaternion.LookRotation(st.fwd));
            cp.respawnPoint = resp.transform;
            // flag pole on the bank; the flag plank rises when the checkpoint is reached
            var k = new MeshKit();
            Vector3 pole = st.pos + st.right * (st.width * 0.5f + 1.2f);
            Beam(k, pole - Vector3.up, pole + Vector3.up * 4.5f, 0.22f, WoodTint);
            MeshObject(go.transform, name + "_Pole", k.Build(name + "_Pole"), wood);
            var fk = new MeshKit();
            PropFactory.RoundedBox(fk, Vector3.zero, new Vector3(1.3f, 0.75f, 0.08f), 0.05f, new Color(1f, 0.82f, 0.15f), Quaternion.identity, 1);
            var flag = MeshObject(go.transform, name + "_Flag", fk.Build(name + "_Flag"), paint);
            flag.transform.SetPositionAndRotation(pole + Vector3.up * 2.2f + st.right * 0.7f, Quaternion.LookRotation(st.fwd));
            cp.flag = flag.transform;
            return go;
        }

        static ParticleSystem Confetti(Transform parent, Vector3 pos)
        {
            var go = new GameObject("Confetti");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 300;
            var grad = new ParticleSystem.MinMaxGradient(new Color(1f, 0.3f, 0.3f), new Color(0.3f, 0.9f, 1f)) { mode = ParticleSystemGradientMode.TwoColors };
            main.startColor = grad;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 220) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.rotation = new Vector3(-90, 0, 0);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = WaterfallBuilder.ParticleMaterial();
            return ps;
        }

        // ------------------------------------------------------------------ course
        [MenuItem("Tools/Tropical Course/Rebuild Obstacles")]
        public static void RebuildMenu()
        {
            if (!CourseGenerator.OpenScene()) return;
            Build();
            CourseGenerator.SaveScene();
        }

        public static void Build()
        {
            var set = CourseGenerator.Settings();
            var path = CourseGenerator.River();
            wood = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_Wood.mat");
            paint = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_Wood_Painted.mat");
            if (paint == null) { MaterialLibrary.Rebuild(); paint = AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/M_Wood_Painted.mat"); }
            var root = CourseGenerator.Child(CourseGenerator.CourseRoot().transform, "Gameplay", true);
            var structs = CourseGenerator.Child(root, "WoodenStructures");
            var obst = CourseGenerator.Child(root, "Obstacles");
            var cps = CourseGenerator.Child(root, "Checkpoints");
            float diag = set.MinRaftPassage;
            float A = SectionStart(path, "A"), B = SectionStart(path, "B"), C = SectionStart(path, "C");
            float D = SectionStart(path, "D"), E = SectionStart(path, "E"), F = SectionStart(path, "F");
            float L = path.Length;
            int cpi = 0;

            // A: start dock beside the spawn, start gate downstream, checkpoint 0
            Dock(structs, "StartDock", At(path, 30f), -1, 9f, 3f);
            CheckpointAt(cps, "Checkpoint_0_Start", At(path, 36f), cpi++);
            Gate(structs, "StartGate", At(path, 42f), 12f, 5.5f, true);
            // B: narrow canyon gate + slalom low beams
            var bg = At(path, B + 10f);
            Gate(obst, "CanyonGate", bg, Mathf.Max(diag + 0.6f, 6.5f), 4.2f, true);
            OverheadBeam(obst, "OverheadBeam_1", At(path, B + 26f));
            LowBeam(obst, "LowBeam_2", At(path, B + 40f), +1, At(path, B + 40f).width * 0.40f);
            CheckpointAt(cps, "Checkpoint_1_Canyon", At(path, B + 50f), cpi++);
            // C: central boulder in the basin (passages each side checked against the raft)
            var cs = At(path, C + 22f);
            float boulderR = Mathf.Min(4.5f, (cs.width - 2f * (diag + 2.5f)) * 0.5f);
            if (boulderR > 1.5f)
            {
                var rockPf = AssetDatabase.LoadAssetAtPath<GameObject>(PropFactory.PrefabDir + "/Rocks/PF_Rock_Boulder_B.prefab");
                var rock = (GameObject)PrefabUtility.InstantiatePrefab(rockPf, obst);
                rock.name = "Basin_Boulder";
                rock.transform.SetPositionAndRotation(cs.pos - Vector3.up * 1.2f, Quaternion.Euler(0, 35, 0));
                rock.transform.localScale = Vector3.one * boulderR / 1.1f;
            }
            CheckpointAt(cps, "Checkpoint_2_Basin", At(path, C + 40f), cpi++);
            // D: rotating sweeper mid S-curve (bank mounted, wipes across)
            var ds = At(path, D + 30f);
            Sweeper(obst, "Sweeper_SCurve", ds, +1, ds.width * 0.62f, 0f);
            CheckpointAt(cps, "Checkpoint_3_Rapids", At(path, D + 52f), cpi++);
            // E: warning posts, kicker at the chute lip, landing checkpoint
            float lip = 0f;
            var ss = path.Samples;
            for (int i = 1; i < ss.Count; i++) if (ss[i - 1].hasWater && !ss[i].hasWater) { lip = ss[i - 1].distance; break; }
            var es = At(path, E + 4f);
            WarningPost(structs, "ChuteWarning_L", es, -1);
            WarningPost(structs, "ChuteWarning_R", es, +1);
            Kicker(obst, "ChuteKicker", At(path, lip - 0.5f), path, lip - 0.5f);
            float landing = lip;
            for (int i = 1; i < ss.Count; i++) if (!ss[i - 1].hasWater && ss[i].hasWater) { landing = ss[i].distance; break; }
            CheckpointAt(cps, "Checkpoint_4_Landing", At(path, landing + 12f), cpi++);
            // F: two pendulum logs, a moving barrier, finish gate + finish dock
            PendulumRig(obst, "Pendulum_1", At(path, F + 10f), 6f, 6.5f, 0f);
            PendulumRig(obst, "Pendulum_2", At(path, F + 22f), 6f, 6.5f, 1.7f);
            var fb = At(path, F + 36f);
            Barrier(obst, "MovingBarrier", fb, 6f, Mathf.Max(4f, fb.width - diag - 4f), 0.5f);
            var fin = At(path, L - 32f);
            var gate = Gate(structs, "FinishGate", fin, fin.width - 1f, 7f, true, true);
            var trig = new GameObject("FinishTrigger");
            trig.transform.SetParent(gate.transform, false);
            trig.transform.SetPositionAndRotation(fin.pos + Vector3.up * 1.5f, Quaternion.LookRotation(fin.fwd));
            var tbc = trig.AddComponent<BoxCollider>(); tbc.isTrigger = true; tbc.size = new Vector3(fin.width + 2f, 6f, 2f);
            var fg = trig.AddComponent<FinishGate>();
            fg.celebration = Confetti(gate.transform, fin.pos + Vector3.up * 7.5f);
            Dock(structs, "FinishDock", At(path, L - 22f), -1, 10f, 4f);
            // safety net far below the course: respawn at the last checkpoint
            var hz = new GameObject("HazardZone_Below");
            hz.transform.SetParent(root, false);
            var mid = path.SampleAtDistance(L * 0.5f).position;
            hz.transform.position = new Vector3(mid.x, -45f, mid.z);
            var hb = hz.AddComponent<BoxCollider>(); hb.isTrigger = true; hb.size = new Vector3(600f, 10f, L + 200f);
            hz.AddComponent<HazardZone>();
            Debug.Log($"[TropicalCourse] obstacles built: {cpi} checkpoints, boulder radius {boulderR:F1} m, raft diagonal {diag:F2} m");
        }
    }
}
