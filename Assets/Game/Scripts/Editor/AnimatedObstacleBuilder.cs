using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FartOnline.EnvironmentEditor;
using FartOnline.Game.Endless;
using FartOnline.Obstacles;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Procedural animated hazards for the endless river (original geometry, toy colours): spiked bank roller,
    /// spinning spike star, inflating puffer mine, kraken tentacle and bank windmill, plus a mallet head for the
    /// sweeper arm. Built in river space (+Z downstream, +X river right, y = 0 at the water surface) into
    /// Assets/Game/Endless/Prefabs; materials are URP Lit (Assets/Game/Endless/Materials) so they render everywhere,
    /// WebGL included. Every moving part is driven by a KinematicObstacle subclass on the shared obstacle clock.
    /// Menu: Tools > Fart Online > Endless > Rebuild Animated Obstacles.
    /// </summary>
    public static class AnimatedObstacleBuilder
    {
        const string MatDir = EndlessBuilder.Dir + "/Materials";
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        internal static Material Toy(string name, Color c, float smooth = 0.4f)
        {
            if (mats.TryGetValue(name, out var m) && m != null) return m;
            MeshKit.EnsureFolder(MatDir);
            string path = MatDir + "/M_Toy_" + name + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        internal static Material Purple => Toy("Purple", UiHex(0x8E3FB0));
        internal static Material Pink => Toy("Pink", UiHex(0xE65C9E));
        internal static Material White => Toy("White", UiHex(0xF6F1E7), 0.55f);
        internal static Material Red => Toy("Red", UiHex(0xDC4339));
        internal static Material Teal => Toy("Teal", UiHex(0x34B3AA));
        internal static Material Dark => Toy("Dark", UiHex(0x3B2F3F), 0.25f);
        internal static Material Yellow => Toy("Yellow", UiHex(0xF4C443));
        internal static Material Wood => Toy("Wood", UiHex(0xA9744A), 0.2f);

        /// <summary>Unlit HDR colour (fire, warning rings): glows with bloom and needs no lights.</summary>
        internal static Material Glow(string name, Color c)
        {
            if (mats.TryGetValue("Glow" + name, out var m) && m != null) return m;
            MeshKit.EnsureFolder(MatDir);
            string path = MatDir + "/M_Glow_" + name + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            mats["Glow" + name] = m;
            return m;
        }

        /// <summary>Flat ring (annulus) facing up at height y; r0 = 0 gives a disc.</summary>
        internal static void Annulus(MeshKit k, float y, float r0, float r1, int sides = 32)
        {
            int start = k.tri.Count;
            var a = new int[sides + 1];
            var b = new int[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                a[i] = k.Add(d * r0 + Vector3.up * y, Vector2.zero, Color.white);
                b[i] = k.Add(d * r1 + Vector3.up * y, Vector2.zero, Color.white);
            }
            k.Strip(a, b);
            k.OrientRange(start, k.tri.Count, Vector3.up);
        }

        internal static Color UiHex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        // ------------------------------------------------------------------ geometry
        /// <summary>Tapered cylinder from a to b (capped), outward facing.</summary>
        internal static void Cylinder(MeshKit k, Vector3 a, Vector3 b, float r0, float r1, int sides = 14)
        {
            int start = k.tri.Count;
            Vector3 axis = (b - a).normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            var ra = new int[sides + 1];
            var rb = new int[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                Vector3 d = u * Mathf.Cos(t) + v * Mathf.Sin(t);
                ra[i] = k.Add(a + d * r0, new Vector2(i / (float)sides, 0f), Color.white);
                rb[i] = k.Add(b + d * r1, new Vector2(i / (float)sides, 1f), Color.white);
            }
            k.Strip(ra, rb);
            k.OrientOutwardAxis(start, k.tri.Count, new List<Vector3> { a, b });
            Cap(k, a, axis * -1f, u, v, r0, sides);
            Cap(k, b, axis, u, v, r1, sides);
        }

        static void Cap(MeshKit k, Vector3 c, Vector3 n, Vector3 u, Vector3 v, float r, int sides)
        {
            if (r <= 0.001f) return;
            int start = k.tri.Count;
            int ci = k.Add(c, new Vector2(0.5f, 0.5f), Color.white);
            int first = k.v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                k.Add(c + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * r, Vector2.zero, Color.white);
            }
            for (int i = 0; i < sides; i++) { k.tri.Add(ci); k.tri.Add(first + i); k.tri.Add(first + i + 1); }
            k.OrientRange(start, k.tri.Count, n);
        }

        /// <summary>Cone (spike) from a base centre to a tip.</summary>
        internal static void Cone(MeshKit k, Vector3 baseC, Vector3 tip, float r, int sides = 8)
        {
            int start = k.tri.Count;
            Vector3 axis = (tip - baseC).normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            int t0 = k.Add(tip, Vector2.zero, Color.white);
            int first = k.v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                k.Add(baseC + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r, Vector2.zero, Color.white);
            }
            for (int i = 0; i < sides; i++) { k.tri.Add(t0); k.tri.Add(first + i); k.tri.Add(first + i + 1); }
            k.OrientOutward(start, k.tri.Count, baseC + axis * 0.2f * Vector3.Distance(baseC, tip));
        }

        internal static void Sphere(MeshKit k, Vector3 c, Vector3 radii, int subdiv = 2)
        {
            PropFactory.Icosphere(subdiv, out var verts, out var tris);
            int start = k.tri.Count, b = k.v.Count;
            foreach (var p in verts) k.Add(c + Vector3.Scale(p.normalized, radii), Vector2.zero, Color.white);
            foreach (var t in tris) k.tri.Add(b + t);
            k.OrientOutward(start, k.tri.Count, c);
        }

        internal static GameObject Part(Transform parent, string name, MeshKit k, Material m, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = k.Build(name);
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(mesh, EndlessBuilder.MeshDir + "/" + name + ".asset");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        internal static T Kinematic<T>(GameObject go) where T : KinematicObstacle
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return go.AddComponent<T>();
        }

        internal static GameObject Root(string name, string hazard)
        {
            var go = new GameObject(name);
            if (hazard != null) go.AddComponent<EndlessHazard>().label = hazard;
            return go;
        }

        internal static GameObject Save(GameObject root)
        {
            ToyMerge.Merge(root);                 // one vertex-coloured draw per moving part instead of one per colour
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, EndlessBuilder.PrefabDir + "/" + root.name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ prefabs
        [MenuItem("Tools/Fart Online/Endless/Rebuild Animated Obstacles")]
        public static void Build()
        {
            MeshKit.EnsureFolder(EndlessBuilder.PrefabDir);
            MeshKit.EnsureFolder(EndlessBuilder.MeshDir);
            mats.Clear();
            SpikeRoller();
            StarSpinner();
            Puffer();
            Tentacle();
            Windmill();
            FireJet();
            Whirlpool();
            Mortar();
            SpikeHazardBuilder.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("[FartOnline] animated obstacles built");
        }

        /// <summary>Spiked roller along the bank (long axis along the flow), spinning, on two static brackets.</summary>
        static void SpikeRoller()
        {
            var root = Root("PF_Obs_SpikeRoller", "a spiked roller");
            var roller = new GameObject("Roller");
            roller.transform.SetParent(root.transform, false);
            var spin = Kinematic<SpinObstacle>(roller);
            spin.axis = Vector3.forward;
            spin.degreesPerSecond = 150f;
            const float half = 3.4f, r = 0.68f;
            var body = new MeshKit(); var bands = new MeshKit(); var spikes = new MeshKit();
            Cylinder(body, new Vector3(0, 0, -half), new Vector3(0, 0, half), r, r, 18);
            foreach (float z in new[] { -half + 0.12f, 0f, half - 0.12f }) Cylinder(bands, new Vector3(0, 0, z - 0.14f), new Vector3(0, 0, z + 0.14f), r + 0.08f, r + 0.08f, 18);
            for (int row = 0; row < 8; row++)
            {
                float z = -half + 0.45f + row * (2f * half - 0.9f) / 7f;
                for (int i = 0; i < 7; i++)
                {
                    float a = (i + (row % 2) * 0.5f) / 7f * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    Cone(spikes, new Vector3(0, 0, z) + d * (r - 0.02f), new Vector3(0, 0, z) + d * (r + 0.42f), 0.15f);
                }
            }
            Part(roller.transform, "Obs_RollerBody", body, Purple);
            Part(roller.transform, "Obs_RollerBands", bands, Pink);
            Part(roller.transform, "Obs_RollerSpikes", spikes, White);
            var cap = roller.AddComponent<CapsuleCollider>();
            cap.direction = 2; cap.radius = r + 0.42f; cap.height = 2f * half + 0.2f;
            // brackets (static)
            var br = new MeshKit();
            foreach (float z in new[] { -half - 0.25f, half + 0.25f })
            {
                Cylinder(br, new Vector3(0, -1.6f, z), new Vector3(0, 0.25f, z), 0.22f, 0.18f, 10);
                Sphere(br, new Vector3(0, 0f, z), Vector3.one * 0.3f, 1);
            }
            Part(root.transform, "Obs_RollerBrackets", br, Dark);
            Save(root);
        }

        /// <summary>Four spiked arms on a hub, spinning in the stream (arm length 2.5 m before scaling).</summary>
        static void StarSpinner()
        {
            var root = Root("PF_Obs_StarSpinner", "a spinning spike star");
            var spin = Kinematic<SpinObstacle>(root);
            spin.axis = Vector3.up;
            spin.degreesPerSecond = 70f;
            var hub = new MeshKit(); var arms = new MeshKit(); var balls = new MeshKit(); var spikes = new MeshKit();
            Cylinder(hub, new Vector3(0, -1.2f, 0), new Vector3(0, 1.15f, 0), 0.5f, 0.42f, 16);
            Sphere(balls, new Vector3(0, 1.2f, 0), new Vector3(0.48f, 0.3f, 0.48f), 2);
            const float arm = 2.5f, y = 0.7f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 d = Quaternion.Euler(0, i * 90f, 0) * Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, d);
                Cylinder(arms, new Vector3(0, y, 0) + d * 0.35f, new Vector3(0, y, 0) + d * arm, 0.26f, 0.22f, 12);
                Sphere(balls, new Vector3(0, y, 0) + d * arm, Vector3.one * 0.32f, 2);
                for (float t = 0.8f; t < arm - 0.1f; t += 0.45f)
                {
                    Vector3 c = new Vector3(0, y, 0) + d * t;
                    Cone(spikes, c + side * 0.2f, c + side * 0.58f, 0.11f);
                    Cone(spikes, c - side * 0.2f, c - side * 0.58f, 0.11f);
                    Cone(spikes, c + Vector3.up * 0.2f, c + Vector3.up * 0.55f, 0.11f);
                }
                var holder = new GameObject("Col_Arm" + i);
                holder.transform.SetParent(root.transform, false);
                holder.transform.localRotation = Quaternion.LookRotation(d);
                var bc = holder.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, y, (arm + 0.35f) * 0.5f + 0.1f);
                bc.size = new Vector3(1.15f, 0.95f, arm + 0.2f);
            }
            Part(root.transform, "Obs_StarHub", hub, Dark);
            Part(root.transform, "Obs_StarArms", arms, Purple);
            Part(root.transform, "Obs_StarBalls", balls, Yellow);
            Part(root.transform, "Obs_StarSpikes", spikes, White);
            var hc = root.AddComponent<CapsuleCollider>();
            hc.radius = 0.5f; hc.height = 2.4f; hc.center = new Vector3(0, 0f, 0);
            Save(root);
        }

        /// <summary>Spiky red puffer mine with eyes; inflates and deflates (max radius incl. spikes 1.45 m).</summary>
        static void Puffer()
        {
            var root = Root("PF_Obs_Puffer", "a puffer mine");
            var pulse = Kinematic<PulseObstacle>(root);
            pulse.period = 2.6f; pulse.minScale = 0.72f; pulse.bob = 0.2f; pulse.turnSpeed = 30f;
            var body = new MeshKit(); var belly = new MeshKit(); var spikes = new MeshKit(); var eyes = new MeshKit(); var pupils = new MeshKit();
            Vector3 c = new Vector3(0, 0.15f, 0);
            Sphere(body, c, new Vector3(1f, 0.88f, 1f), 3);
            Sphere(belly, c + new Vector3(0, -0.12f, 0.08f), new Vector3(0.92f, 0.72f, 0.95f), 2);
            int n = 26;
            for (int i = 0; i < n; i++)
            {
                // golden-spiral directions on the upper ~2/3 of the sphere
                float yv = 1f - (i + 0.5f) / n * 1.35f;
                float rad = Mathf.Sqrt(Mathf.Max(0f, 1f - yv * yv));
                float a = i * 2.39996f;
                Vector3 d = new Vector3(Mathf.Cos(a) * rad, yv, Mathf.Sin(a) * rad).normalized;
                Vector3 s = c + Vector3.Scale(d, new Vector3(1f, 0.88f, 1f));
                Cone(spikes, s - d * 0.05f, s + d * 0.42f, 0.13f);
            }
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 e = c + new Vector3(sd * 0.36f, 0.32f, 0.82f);
                Sphere(eyes, e, Vector3.one * 0.2f, 2);
                Sphere(pupils, e + new Vector3(0, 0.02f, 0.13f), Vector3.one * 0.1f, 1);
            }
            Part(root.transform, "Obs_PufferBody", body, Red);
            Part(root.transform, "Obs_PufferBelly", belly, White);
            Part(root.transform, "Obs_PufferSpikes", spikes, White);
            Part(root.transform, "Obs_PufferEyes", eyes, White);
            Part(root.transform, "Obs_PufferPupils", pupils, Dark);
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 1.3f; sc.center = c;
            Save(root);
        }

        /// <summary>Kraken tentacle: 7 bones of 0.6 m, tapered, pink suckers on the river side (+Z).</summary>
        static void Tentacle()
        {
            var root = Root("PF_Obs_Tentacle", "a kraken tentacle");
            var ten = Kinematic<TentacleObstacle>(root);
            ten.boneLength = 0.6f; ten.period = 3.4f; ten.lean = new Vector2(8f, 48f); ten.curl = 7f;
            const int count = 7;
            var bones = new Transform[count];
            Transform parent = root.transform;
            for (int i = 0; i < count; i++)
            {
                var bone = new GameObject("Bone" + i).transform;
                bone.SetParent(parent, false);
                bone.localPosition = i == 0 ? Vector3.zero : new Vector3(0f, ten.boneLength, 0f);
                bones[i] = bone;
                float r0 = Mathf.Lerp(0.48f, 0.12f, i / (float)count), r1 = Mathf.Lerp(0.48f, 0.12f, (i + 1f) / count);
                var body = new MeshKit(); var cups = new MeshKit();
                Cylinder(body, Vector3.zero, new Vector3(0, ten.boneLength + 0.06f, 0), r0, r1, 14);
                if (i == count - 1) Cone(body, new Vector3(0, ten.boneLength, 0), new Vector3(0, ten.boneLength + 0.45f, 0.18f), r1, 10);
                for (int sIdx = 0; sIdx < 2; sIdx++)
                {
                    float y = 0.15f + sIdx * 0.3f;
                    float rr = Mathf.Lerp(r0, r1, y / ten.boneLength);
                    Sphere(cups, new Vector3(0, y, rr * 0.92f), new Vector3(rr * 0.38f, rr * 0.38f, rr * 0.2f), 1);
                }
                Part(bone, "Obs_TentacleBone" + i, body, Purple);
                Part(bone, "Obs_TentacleCups" + i, cups, Pink, false);
                var cap = bone.gameObject.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = r0 * 0.95f; cap.height = ten.boneLength + 2f * r0 * 0.95f; cap.center = new Vector3(0, ten.boneLength * 0.5f, 0);
                parent = bone;
            }
            ten.bones = bones;
            // ripple ring where it breaks the surface
            var ring = new MeshKit();
            Cylinder(ring, new Vector3(0, 0.35f, 0), new Vector3(0, 0.45f, 0), 0.75f, 0.7f, 18);
            Part(root.transform, "Obs_TentacleRing", ring, Toy("Foam", new Color(0.9f, 0.97f, 1f, 1f), 0.7f), false);
            Save(root);
        }

        /// <summary>Bank windmill: tower on the shore, four teal-and-white blades turning in the cross-river plane.</summary>
        static void Windmill()
        {
            var root = Root("PF_Obs_Windmill", "windmill blades");
            var tower = new MeshKit(); var roof = new MeshKit();
            Cylinder(tower, new Vector3(0, -1.5f, 0.4f), new Vector3(0, 4.6f, 0.4f), 0.75f, 0.5f, 12);
            Cone(roof, new Vector3(0, 4.55f, 0.4f), new Vector3(0, 5.8f, 0.4f), 0.85f, 12);
            Part(root.transform, "Obs_WindmillTower", tower, Wood);
            Part(root.transform, "Obs_WindmillRoof", roof, Red);
            var tc = root.AddComponent<CapsuleCollider>();
            tc.radius = 0.7f; tc.height = 6.2f; tc.center = new Vector3(0, 1.6f, 0.4f);
            var hub = new GameObject("Hub");
            hub.transform.SetParent(root.transform, false);
            hub.transform.localPosition = new Vector3(0f, 4.2f, -0.45f);
            var spin = Kinematic<SpinObstacle>(hub);
            spin.axis = Vector3.forward;
            spin.degreesPerSecond = 42f;
            var blades = new MeshKit(); var stripes = new MeshKit(); var cap = new MeshKit();
            Sphere(cap, new Vector3(0, 0, -0.15f), new Vector3(0.45f, 0.45f, 0.35f), 2);
            const float len = 4.6f;
            for (int i = 0; i < 4; i++)
            {
                Quaternion q = Quaternion.Euler(0, 0, i * 90f + 45f);
                Vector3 d = q * Vector3.up, w = q * Vector3.right;
                PropFactory.RoundedBox(blades, d * (len * 0.5f + 0.2f), new Vector3(0.9f, len - 0.4f, 0.16f), 0.06f, Color.white, q, 2);
                for (int s = 1; s <= 3; s++)
                    PropFactory.RoundedBox(stripes, d * (0.4f + s * 1.05f) + Vector3.forward * -0.01f, new Vector3(0.94f, 0.32f, 0.18f), 0.04f, Color.white, q, 1);
                var holder = new GameObject("Col_Blade" + i);
                holder.transform.SetParent(hub.transform, false);
                holder.transform.localRotation = q;
                var bc = holder.AddComponent<BoxCollider>();
                bc.center = new Vector3(0, len * 0.5f + 0.2f, 0);
                bc.size = new Vector3(0.95f, len - 0.35f, 0.3f);
            }
            Part(hub.transform, "Obs_WindmillBlades", blades, Teal);
            Part(hub.transform, "Obs_WindmillStripes", stripes, White);
            Part(hub.transform, "Obs_WindmillCap", cap, Yellow);
            Save(root);
        }

        /// <summary>
        /// Bank flamethrower: a red fuel tank on a post with a black nozzle pointing into the river (+Z), a pilot flame,
        /// and the jet (built 1 m long along +Z, stretched by <see cref="FireJetObstacle"/>; trigger capsule).
        /// The nozzle mouth is at z = 0.75, y = 0.55 (the planner's flame start).
        /// </summary>
        static void FireJet()
        {
            var root = Root("PF_Obs_FireJet", "a fire jet");
            var jet = Kinematic<FireJetObstacle>(root);
            var post = new MeshKit(); var tank = new MeshKit(); var bands = new MeshKit(); var nozzle = new MeshKit();
            Cylinder(post, new Vector3(0, -1.6f, -0.55f), new Vector3(0, 0.25f, -0.55f), 0.24f, 0.2f, 10);
            Cylinder(tank, new Vector3(0, 0.6f, -1.35f), new Vector3(0, 0.6f, 0.05f), 0.5f, 0.5f, 18);
            Sphere(tank, new Vector3(0, 0.6f, -1.35f), new Vector3(0.5f, 0.5f, 0.25f), 2);
            foreach (float z in new[] { -1.05f, -0.55f, -0.05f })
                Cylinder(bands, new Vector3(0, 0.6f, z - 0.09f), new Vector3(0, 0.6f, z + 0.09f), 0.53f, 0.53f, 18);
            Cylinder(nozzle, new Vector3(0, 0.58f, 0.0f), new Vector3(0, 0.55f, 0.75f), 0.26f, 0.17f, 14);
            Cylinder(nozzle, new Vector3(0, 0.55f, 0.62f), new Vector3(0, 0.55f, 0.78f), 0.24f, 0.24f, 14);
            Part(root.transform, "Obs_FireJetPost", post, Dark);
            Part(root.transform, "Obs_FireJetTank", tank, Red);
            Part(root.transform, "Obs_FireJetBands", bands, Yellow);
            Part(root.transform, "Obs_FireJetNozzle", nozzle, Dark);
            var body = root.AddComponent<CapsuleCollider>();
            body.direction = 2; body.radius = 0.5f; body.height = 2.3f; body.center = new Vector3(0, 0.6f, -0.55f);

            var pilot = new GameObject("Pilot").transform;
            pilot.SetParent(root.transform, false);
            pilot.localPosition = new Vector3(0f, 0.55f, 0.92f);
            var pk = new MeshKit();
            Sphere(pk, Vector3.zero, new Vector3(0.17f, 0.17f, 0.24f), 1);
            Part(pilot, "Obs_FireJetPilot", pk, Glow("FireCore", new Color(2.2f, 1.5f, 0.4f, 1f)), false);

            var flame = new GameObject("Flame").transform;
            flame.SetParent(root.transform, false);
            flame.localPosition = new Vector3(0f, 0.55f, 0.75f);
            var outer = new GameObject("Outer").transform; outer.SetParent(flame, false);
            var core = new GameObject("Core").transform; core.SetParent(flame, false);
            var ok = new MeshKit(); var ck = new MeshKit();
            for (int i = 0; i < 6; i++)
            {
                float z = 0.08f + i * 0.17f;
                float r = Mathf.Lerp(0.3f, 0.62f, i / 5f);
                Sphere(ok, new Vector3(0f, 0.04f * i, z), new Vector3(r, r * 0.9f, 0.13f), 1);
                if (i < 5) Sphere(ck, new Vector3(0f, 0.03f * i + 0.02f, z + 0.085f), new Vector3(r * 0.85f, r * 0.8f, 0.1f), 1);   // yellow bands between the orange puffs
            }
            Part(outer, "Obs_FireJetOuter", ok, Glow("Fire", new Color(1.7f, 0.42f, 0.08f, 1f)), false);
            Part(core, "Obs_FireJetCore", ck, Glow("FireCore", new Color(2.2f, 1.5f, 0.4f, 1f)), false);
            var cap = flame.gameObject.AddComponent<CapsuleCollider>();
            cap.isTrigger = true; cap.direction = 2; cap.radius = 0.55f; cap.height = 1f; cap.center = new Vector3(0f, 0.08f, 0.5f);
            jet.flame = flame;
            jet.flameCollider = cap;
            jet.pilot = pilot;
            jet.tongues = new[] { outer, core };
            Save(root);
        }

        /// <summary>
        /// Whirlpool: concentric dark-water rings with white foam spiral arms (radius 3.5 m) around a spiked grinder
        /// (deadly, radius 1.05 m). The whole thing spins; <see cref="WhirlpoolPull"/> drags the raft toward the grinder.
        /// </summary>
        static void Whirlpool()
        {
            var root = Root("PF_Obs_Whirlpool", "a whirlpool grinder");
            var spin = Kinematic<SpinObstacle>(root);
            spin.axis = Vector3.up;
            spin.degreesPerSecond = 120f;
            var pull = root.AddComponent<WhirlpoolPull>();
            pull.radius = 3.5f; pull.core = 1.05f;
            var outer = new MeshKit(); var mid = new MeshKit(); var inner = new MeshKit(); var foam = new MeshKit();
            Annulus(outer, 0.03f, 2.3f, 3.5f, 40);
            Annulus(mid, 0.036f, 1.2f, 2.3f, 40);
            Annulus(inner, 0.042f, 0f, 1.2f, 32);
            for (int arm = 0; arm < 4; arm++)
            {
                int start = foam.tri.Count;
                const int steps = 22;
                var a = new int[steps + 1]; var b = new int[steps + 1];
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float r = Mathf.Lerp(3.4f, 1.0f, t);
                    float ang = arm * Mathf.PI * 0.5f + t * 3.6f;
                    float w = Mathf.Lerp(0.08f, 0.24f, Mathf.Sin(t * Mathf.PI));
                    Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    a[i] = foam.Add(d * (r - w) + Vector3.up * 0.05f, Vector2.zero, Color.white);
                    b[i] = foam.Add(d * (r + w) + Vector3.up * 0.05f, Vector2.zero, Color.white);
                }
                foam.Strip(a, b);
                foam.OrientRange(start, foam.tri.Count, Vector3.up);
            }
            Part(root.transform, "Obs_WhirlOuter", outer, Toy("SwirlOuter", UiHex(0x2F86AE), 0.85f), false);
            Part(root.transform, "Obs_WhirlMid", mid, Toy("SwirlMid", UiHex(0x215E86), 0.85f), false);
            Part(root.transform, "Obs_WhirlInner", inner, Toy("SwirlInner", UiHex(0x143A57), 0.85f), false);
            Part(root.transform, "Obs_WhirlFoam", foam, Toy("Foam", new Color(0.9f, 0.97f, 1f, 1f), 0.7f), false);

            var hub = new MeshKit(); var spikes = new MeshKit(); var top = new MeshKit();
            Cylinder(hub, new Vector3(0, -0.6f, 0), new Vector3(0, 0.55f, 0), 0.62f, 0.5f, 16);
            Sphere(top, new Vector3(0, 0.58f, 0), new Vector3(0.5f, 0.28f, 0.5f), 2);
            foreach (float y in new[] { 0.05f, 0.4f })
                for (int i = 0; i < 8; i++)
                {
                    float a = (i + (y > 0.2f ? 0.5f : 0f)) / 8f * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Cone(spikes, new Vector3(0, y, 0) + d * 0.5f, new Vector3(0, y + 0.08f, 0) + d * 1.02f, 0.13f);
                }
            Cone(spikes, new Vector3(0, 0.8f, 0), new Vector3(0, 1.25f, 0), 0.16f);
            Part(root.transform, "Obs_WhirlGrinder", hub, Dark);
            Part(root.transform, "Obs_WhirlSpikes", spikes, White);
            Part(root.transform, "Obs_WhirlCap", top, Red);
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = 1.0f; col.height = 2.4f; col.center = new Vector3(0, 0.2f, 0);
            Save(root);
        }

        /// <summary>
        /// Bank cannon on a wooden pier (+Z = into the river) with its cannonball, red target ring and splash column.
        /// Base footprint radius about 1 m; the target ring is 1.6 m in radius before <see cref="MortarObstacle.zoneScale"/>.
        /// </summary>
        static void Mortar()
        {
            var root = Root("PF_Obs_Mortar", "a cannonball");
            var mortar = Kinematic<MortarObstacle>(root);
            var pier = new MeshKit(); var baseK = new MeshKit(); var wheels = new MeshKit();
            Cylinder(pier, new Vector3(0, -1.6f, 0), new Vector3(0, 0.7f, 0), 0.95f, 0.9f, 14);
            Cylinder(baseK, new Vector3(0, 0.7f, 0), new Vector3(0, 1.15f, 0), 0.75f, 0.65f, 14);
            foreach (int sd in new[] { -1, 1 })
                Cylinder(wheels, new Vector3(sd * 0.62f, 1.05f, 0.05f), new Vector3(sd * 0.8f, 1.05f, 0.05f), 0.42f, 0.42f, 16);
            Part(root.transform, "Obs_MortarPier", pier, Wood);
            Part(root.transform, "Obs_MortarBase", baseK, Dark);
            Part(root.transform, "Obs_MortarWheels", wheels, Wood);
            var pc = root.AddComponent<CapsuleCollider>();
            pc.radius = 0.95f; pc.height = 3f; pc.center = new Vector3(0, -0.2f, 0);

            var barrel = new GameObject("Barrel").transform;
            barrel.SetParent(root.transform, false);
            barrel.localPosition = new Vector3(0f, 1.35f, 0.1f);
            barrel.localRotation = Quaternion.Euler(-40f, 0f, 0f);
            var bk = new MeshKit(); var bb = new MeshKit();
            Cylinder(bk, new Vector3(0, 0, -0.55f), new Vector3(0, 0, 1.0f), 0.42f, 0.34f, 18);
            Sphere(bk, new Vector3(0, 0, -0.55f), Vector3.one * 0.42f, 2);
            Cylinder(bb, new Vector3(0, 0, 0.82f), new Vector3(0, 0, 1.02f), 0.4f, 0.4f, 18);
            Cylinder(bb, new Vector3(0, 0, 0.1f), new Vector3(0, 0, 0.28f), 0.44f, 0.44f, 18);
            Part(barrel, "Obs_MortarBarrel", bk, Dark);
            Part(barrel, "Obs_MortarBands", bb, Red);
            mortar.barrel = barrel;
            mortar.muzzle = new Vector3(0f, 1.35f + Mathf.Sin(40f * Mathf.Deg2Rad) * 1.05f, 0.1f + Mathf.Cos(40f * Mathf.Deg2Rad) * 1.05f);

            var ball = new GameObject("Ball").transform;
            ball.SetParent(root.transform, false);
            var ballK = new MeshKit();
            Sphere(ballK, Vector3.zero, Vector3.one * 0.45f, 2);
            Part(ball, "Obs_MortarBall", ballK, Dark);
            var bc = ball.gameObject.AddComponent<SphereCollider>();
            bc.isTrigger = true; bc.radius = 0.5f;
            mortar.ball = ball;
            mortar.ballCollider = bc;

            var target = new GameObject("Target").transform;
            target.SetParent(root.transform, false);
            var tk = new MeshKit(); var tx = new MeshKit();
            Annulus(tk, 0f, 1.3f, 1.6f, 40);
            Annulus(tk, 0f, 0.55f, 0.75f, 28);
            for (int i = 0; i < 4; i++)
            {
                Quaternion q = Quaternion.Euler(0, i * 90f + 45f, 0);
                PropFactory.RoundedBox(tx, q * new Vector3(0, 0.005f, 1.0f), new Vector3(0.16f, 0.02f, 0.55f), 0.005f, Color.white, q, 1);
            }
            Part(target, "Obs_MortarTarget", tk, Glow("Warn", new Color(2.2f, 0.25f, 0.2f, 1f)), false);
            Part(target, "Obs_MortarTicks", tx, Glow("Warn", new Color(2.2f, 0.25f, 0.2f, 1f)), false);
            mortar.target = target;

            var splash = new GameObject("Splash").transform;
            splash.SetParent(root.transform, false);
            var sk = new MeshKit(); var sp = new MeshKit();
            Cylinder(sk, new Vector3(0, -0.1f, 0), new Vector3(0, 2.2f, 0), 1.0f, 0.45f, 16);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Cone(sp, d * 0.7f + Vector3.up * 0.2f, d * 1.35f + Vector3.up * 1.4f, 0.22f, 6);
            }
            Part(splash, "Obs_MortarSplash", sk, Toy("Foam", new Color(0.9f, 0.97f, 1f, 1f), 0.7f), false);
            Part(splash, "Obs_MortarSpray", sp, White, false);
            var sc = splash.gameObject.AddComponent<CapsuleCollider>();
            sc.isTrigger = true; sc.radius = 1.0f; sc.height = 2.4f; sc.center = new Vector3(0, 1.0f, 0);
            mortar.splash = splash;
            mortar.splashCollider = sc;
            Save(root);
        }

        /// <summary>Adds a mallet head to the sweeper prefab's arm (teal head, white bands).</summary>
        public static void MalletHead(Transform arm, float reach)
        {
            var head = new MeshKit(); var bands = new MeshKit();
            Vector3 c = new Vector3(0f, 0f, reach - 0.35f);
            Cylinder(head, c + Vector3.down * 0.6f, c + Vector3.up * 0.6f, 0.48f, 0.48f, 16);
            Cylinder(bands, c + Vector3.down * 0.66f, c + Vector3.down * 0.45f, 0.5f, 0.5f, 16);
            Cylinder(bands, c + Vector3.up * 0.45f, c + Vector3.up * 0.66f, 0.5f, 0.5f, 16);
            Part(arm, "Obs_MalletHead", head, Teal);
            Part(arm, "Obs_MalletBands", bands, White);
            var col = arm.gameObject.AddComponent<CapsuleCollider>();
            col.direction = 1; col.radius = 0.5f; col.height = 1.35f; col.center = c;
        }
    }
}
