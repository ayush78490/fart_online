using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Procedural, original prop meshes + prefabs for the tropical course (rocks, vegetation, wood).
    /// Vertex colours drive Env/StylizedSurface:
    ///   rocks:   R strata, G wetness (base), B grass/moss (top)
    ///   foliage: RGB tint (multiplied with the material colour), A = wind weight (0 rooted .. 1 free)
    ///   wood:    RGB tint
    /// Deterministic: every mesh is built from a fixed seed. Menu: Tools > Tropical Course > Rebuild Prop Library.
    /// </summary>
    public static class PropFactory
    {
        public const string PrefabDir = CourseGenerator.Root + "/Prefabs";
        public const string PropMeshDir = CourseGenerator.Root + "/Meshes/Props";

        // ------------------------------------------------------------------ primitives
        /// <summary>Icosphere (subdivided icosahedron) with shared vertices.</summary>
        public static void Icosphere(int subdiv, out List<Vector3> verts, out List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            tris = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                   3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var cache = new Dictionary<long, int>();
            var vl = verts;
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                if (cache.TryGetValue(key, out int m)) return m;
                vl.Add(((vl[a] + vl[b]) * 0.5f).normalized);
                cache[key] = vl.Count - 1;
                return vl.Count - 1;
            }
            for (int s = 0; s < subdiv; s++)
            {
                var nt = new List<int>();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = nt;
            }
        }

        static Mesh Finish(MeshKit k, string name, Vector3 orient)
        {
            if (orient != Vector3.zero) k.OrientRange(0, k.tri.Count, orient);
            return k.Build(name);
        }

        // ------------------------------------------------------------------ rocks
        public enum RockShape { Boulder, Slab, Pillar, Pebble }

        /// <summary>Rounded, stylized rock: low-frequency lumps (no sharp noise), flattened base sunk into the ground.</summary>
        public static Mesh Rock(string name, RockShape shape, int seed, int subdiv = 3)
        {
            Icosphere(subdiv, out var vs, out var ts);
            Vector3 scale = shape == RockShape.Slab ? new Vector3(1.6f, 0.55f, 1.2f)
                          : shape == RockShape.Pillar ? new Vector3(0.75f, 1.9f, 0.8f)
                          : shape == RockShape.Pebble ? new Vector3(1f, 0.6f, 0.85f) : new Vector3(1.1f, 0.9f, 1f);
            var k = new MeshKit();
            foreach (var v0 in vs)
            {
                Vector3 v = v0;
                float lump = Noise.Fbm(v * 1.3f + Vector3.one * seed, seed, 3) * 0.28f;
                float facet = Mathf.Abs(Noise.Value(v * 2.2f, seed + 9)) * 0.08f;       // broad planar facets
                v *= 1f + lump - facet;
                v = Vector3.Scale(v, scale);
                if (v.y < -0.35f * scale.y) v.y = Mathf.Lerp(v.y, -0.35f * scale.y, 0.85f);  // flat-ish base
                float h01 = Mathf.InverseLerp(-scale.y, scale.y, v.y);
                float strata = 0.5f + 0.5f * Mathf.Sin(v.y * 5f + Noise.Value(v * 1.5f, seed + 3) * 3f);
                float wet = Mathf.Clamp01(1f - h01 * 2.5f);
                float moss = Mathf.Clamp01((v0.y - 0.45f) * 2.2f) * (shape == RockShape.Pillar ? 0.6f : 1f);
                k.Add(v, new Vector2(v.x, v.z), new Color(strata, wet * 0.6f, moss, 1f));
            }
            k.tri.AddRange(ts);
            k.OrientOutward(0, k.tri.Count, new Vector3(0f, -0.1f * scale.y, 0f));
            return Finish(k, name, Vector3.zero);
        }

        // ------------------------------------------------------------------ foliage helpers
        static void Leaf(MeshKit k, Vector3 root, Vector3 dir, Vector3 side, float length, float width, float arch, int segs,
                         Color tint, float windBase, float fold = 0.25f)
        {
            dir = dir.normalized; side = side.normalized;
            Vector3 up = Vector3.Cross(side, dir).normalized;
            int[] prev = null;
            for (int s = 0; s <= segs; s++)
            {
                float t = s / (float)segs;
                float w = width * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.08f, 1f, t)) * (1f - 0.35f * t);
                Vector3 c = root + dir * length * t + up * arch * Mathf.Sin(t * Mathf.PI * 0.9f) - Vector3.up * arch * 0.8f * t * t;
                float wind = Mathf.Lerp(windBase, 1f, t);
                Color col = Color.Lerp(tint * 0.8f, tint * 1.12f, t); col.a = wind;
                // three verts across: edge, folded midrib, edge (the fold reads from any angle)
                var row = new[]
                {
                    k.Add(c - side * w - up * w * fold, new Vector2(0, t), col),
                    k.Add(c, new Vector2(0.5f, t), col * 1.05f),
                    k.Add(c + side * w - up * w * fold, new Vector2(1, t), col)
                };
                for (int q = 0; q < 3; q++) { var cc = k.col[row[q]]; cc.a = wind; k.col[row[q]] = cc; }
                if (prev != null) k.Strip(prev, row);
                prev = row;
            }
        }

        public static void Tube(MeshKit k, List<Vector3> path, float r0, float r1, int sides, Color c0, Color c1, float wind0, float wind1, bool rings = false)
        {
            int[] prev = null;
            int t0 = k.tri.Count;
            for (int i = 0; i < path.Count; i++)
            {
                float t = i / (float)(path.Count - 1);
                Vector3 d = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 a = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
                Vector3 b = Vector3.Cross(d, a);
                float r = Mathf.Lerp(r0, r1, t) * (rings && i % 2 == 1 ? 1.08f : 1f);
                Color col = Color.Lerp(c0, c1, t) * (rings && i % 2 == 1 ? 0.85f : 1f);
                col.a = Mathf.Lerp(wind0, wind1, t);
                var row = new int[sides + 1];
                for (int s = 0; s <= sides; s++)
                {
                    float ang = s / (float)sides * Mathf.PI * 2f;
                    row[s] = k.Add(path[i] + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r, new Vector2(s / (float)sides, t), col);
                }
                if (prev != null) k.Strip(prev, row);
                prev = row;
            }
            k.OrientOutwardAxis(t0, k.tri.Count, path);
        }

        static void Blob(MeshKit k, Vector3 center, Vector3 radii, int seed, Color tint, float wind, int subdiv = 2)
        {
            Icosphere(subdiv, out var vs, out var ts);
            int off = k.v.Count;
            foreach (var v0 in vs)
            {
                Vector3 v = v0 * (1f + Noise.Fbm(v0 * 1.7f, seed, 2) * 0.22f);
                Color col = tint * Mathf.Lerp(0.8f, 1.12f, v0.y * 0.5f + 0.5f); col.a = wind * Mathf.Clamp01(v0.y * 0.5f + 0.7f);
                k.Add(center + Vector3.Scale(v, radii), new Vector2(v0.x, v0.z), col);
            }
            int t0 = k.tri.Count;
            foreach (var t in ts) k.tri.Add(off + t);
            k.OrientOutward(t0, k.tri.Count, center);
        }

        // ------------------------------------------------------------------ vegetation
        public static Mesh Palm(string name, int seed, float height, int fronds, Color leaf, Color trunk, out int trunkTris)
        {
            var rnd = new System.Random(seed);
            var trunkK = new MeshKit();
            var path = new List<Vector3>();
            float lean = (float)rnd.NextDouble() * 0.25f + 0.08f;
            Vector3 leanDir = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f).normalized;
            for (int i = 0; i <= 14; i++)
            {
                float t = i / 14f;
                path.Add(Vector3.up * height * t + leanDir * height * lean * t * t);
            }
            Tube(trunkK, path, 0.22f, 0.13f, 8, trunk, trunk * 1.15f, 0f, 0.35f, true);
            trunkTris = trunkK.tri.Count;
            var k = trunkK;
            Vector3 top = path[path.Count - 1];
            for (int f = 0; f < fronds; f++)
            {
                float a = f / (float)fronds * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.4f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0.45f - (float)rnd.NextDouble() * 0.25f, Mathf.Sin(a));
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                Color tint = Color.Lerp(leaf, leaf * 1.25f, (float)rnd.NextDouble()); tint.a = 1;
                Leaf(k, top, dir, side, height * 0.6f, 0.95f, 1.6f, 12, tint, 0.35f, 0.18f);
            }
            return Finish(k, name, Vector3.zero);
        }

        public static Mesh BroadLeafPlant(string name, int seed, Color leaf)
        {
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            int n = 6 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.5f;
                float len = 0.9f + (float)rnd.NextDouble() * 0.6f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0.9f + (float)rnd.NextDouble() * 0.5f, Mathf.Sin(a));
                Color tint = Color.Lerp(leaf, leaf * 1.3f, (float)rnd.NextDouble());
                Leaf(k, Vector3.up * 0.05f, dir, Vector3.Cross(Vector3.up, dir), len, 0.38f, 0.25f, 7, tint, 0.1f, 0.3f);
            }
            return Finish(k, name, Vector3.zero);
        }

        public static Mesh Fern(string name, int seed, Color leaf)
        {
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            int n = 9 + rnd.Next(4);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.3f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 1.1f + (float)rnd.NextDouble() * 0.5f, Mathf.Sin(a));
                Color tint = Color.Lerp(leaf * 0.85f, leaf * 1.2f, (float)rnd.NextDouble());
                Leaf(k, Vector3.zero, dir, Vector3.Cross(Vector3.up, dir), 1.0f + (float)rnd.NextDouble() * 0.5f, 0.24f, 0.32f, 8, tint, 0.15f, 0.12f);
            }
            return Finish(k, name, Vector3.zero);
        }

        public static Mesh GrassTuft(string name, int seed, Color grass)
        {
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            int n = 12 + rnd.Next(5);
            for (int i = 0; i < n; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = (float)rnd.NextDouble() * 0.18f;
                Vector3 root = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                Vector3 dir = new Vector3(Mathf.Cos(a) * 0.35f, 1f, Mathf.Sin(a) * 0.35f);
                Color tint = Color.Lerp(grass * 0.8f, grass * 1.25f, (float)rnd.NextDouble());
                Leaf(k, root, dir, Vector3.Cross(Vector3.up, dir + Vector3.right * 0.01f), 0.5f + (float)rnd.NextDouble() * 0.35f, 0.075f, 0.12f, 4, tint, 0f, 0.1f);
            }
            return Finish(k, name, Vector3.zero);
        }

        public static Mesh Shrub(string name, int seed, Color leaf)
        {
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            int n = 3 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                var c = new Vector3(((float)rnd.NextDouble() - 0.5f) * 1.2f, 0.45f + (float)rnd.NextDouble() * 0.35f, ((float)rnd.NextDouble() - 0.5f) * 1.2f);
                float r = 0.5f + (float)rnd.NextDouble() * 0.35f;
                Blob(k, c, new Vector3(r, r * 0.8f, r), seed + i * 7, Color.Lerp(leaf * 0.85f, leaf * 1.15f, (float)rnd.NextDouble()), 0.25f);
            }
            return Finish(k, name, Vector3.zero);
        }

        public static Mesh JungleTree(string name, int seed, float height, Color leaf, Color trunk, out int trunkTris)
        {
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            var path = new List<Vector3>();
            Vector3 bend = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f) * height * 0.15f;
            for (int i = 0; i <= 8; i++) { float t = i / 8f; path.Add(Vector3.up * height * 0.7f * t + bend * Mathf.Sin(t * Mathf.PI)); }
            Tube(k, path, 0.38f, 0.22f, 8, trunk, trunk * 1.1f, 0f, 0.1f);
            trunkTris = k.tri.Count;
            Vector3 top = path[path.Count - 1];
            int n = 4 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3(((float)rnd.NextDouble() - 0.5f) * height * 0.45f, (float)rnd.NextDouble() * height * 0.2f, ((float)rnd.NextDouble() - 0.5f) * height * 0.45f);
                float r = height * (0.18f + (float)rnd.NextDouble() * 0.1f);
                Blob(k, top + off, new Vector3(r, r * 0.75f, r), seed + i * 13, Color.Lerp(leaf * 0.8f, leaf * 1.15f, (float)rnd.NextDouble()), 0.15f);
            }
            return Finish(k, name, Vector3.zero);
        }

        public static Mesh Vine(string name, int seed, float length, Color leaf)
        {
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            int strands = 3 + rnd.Next(3);
            for (int s = 0; s < strands; s++)
            {
                Vector3 root = new Vector3(((float)rnd.NextDouble() - 0.5f) * 1.6f, 0, 0);
                float len = length * (0.6f + (float)rnd.NextDouble() * 0.4f);
                var stem = new List<Vector3>();
                for (int q = 0; q <= 12; q++) { float tq = q / 12f; stem.Add(root + Vector3.down * len * tq + Vector3.forward * 0.05f + Vector3.right * Mathf.Sin(tq * 9f + s) * 0.08f); }
                Tube(k, stem, 0.035f, 0.02f, 5, leaf * 0.6f, leaf * 0.7f, 0.2f, 1f);
                int leaves = Mathf.CeilToInt(len / 0.22f);
                for (int i = 0; i < leaves; i++)
                {
                    float t = i / (float)leaves;
                    Vector3 p = root + Vector3.down * len * t + Vector3.forward * 0.05f + Vector3.right * Mathf.Sin(t * 9f + s) * 0.08f;
                    Vector3 dir = new Vector3(((float)rnd.NextDouble() - 0.5f), -0.6f, 1f);
                    Leaf(k, p, dir, Vector3.right, 0.38f, 0.17f, 0.05f, 3, Color.Lerp(leaf * 0.8f, leaf * 1.15f, (float)rnd.NextDouble()), Mathf.Lerp(0.2f, 1f, t), 0.1f);
                }
            }
            return Finish(k, name, Vector3.zero);
        }

        // ------------------------------------------------------------------ wood
        /// <summary>Rounded beam: a box whose corners are rounded with radius r (superellipse projection).</summary>
        public static void RoundedBox(MeshKit k, Vector3 center, Vector3 size, float r, Color tint, Quaternion rot, int res = 3)
        {
            r = Mathf.Min(r, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f);
            Vector3 inner = size * 0.5f - Vector3.one * r;
            // cube-sphere: each face is a (res*2+1) grid, vertices pushed onto the rounded box
            int n = res * 2 + 2;
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var fn in normals)
            {
                Vector3 ax = Vector3.Cross(fn, fn == Vector3.up || fn == Vector3.down ? Vector3.forward : Vector3.up).normalized;
                Vector3 ay = Vector3.Cross(fn, ax);
                var grid = new int[n + 1, n + 1];
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n; j++)
                    {
                        Vector3 cp = fn + ax * (i / (float)n * 2f - 1f) + ay * (j / (float)n * 2f - 1f);   // on unit cube
                        Vector3 inPt = Vector3.Scale(new Vector3(Mathf.Clamp(cp.x, -1, 1), Mathf.Clamp(cp.y, -1, 1), Mathf.Clamp(cp.z, -1, 1)), inner);
                        Vector3 dir = (Vector3.Scale(cp, size * 0.5f) - inPt).normalized;
                        Vector3 p = inPt + dir * r;
                        grid[i, j] = k.Add(center + rot * p, new Vector2(i / (float)n, j / (float)n), tint);
                    }
                int t0 = k.tri.Count;
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++) k.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                k.OrientRange(t0, k.tri.Count, rot * fn);
            }
        }

        public static Mesh Beam(string name, Vector3 size, Color tint)
        {
            var k = new MeshKit();
            RoundedBox(k, Vector3.zero, size, Mathf.Min(size.x, size.y) * 0.22f, tint, Quaternion.identity);
            return k.Build(name);
        }

        // ------------------------------------------------------------------ prefab helpers
        public static Mesh Save(Mesh m, string name)
        {
            MeshKit.EnsureFolder(PropMeshDir);
            return MeshKit.SaveAsset(m, PropMeshDir + "/" + name + ".asset");
        }

        /// <summary>Prefab with a single mesh (optionally split into two submeshes at <paramref name="splitTri"/>).</summary>
        public static GameObject SavePrefab(string folder, string name, Mesh mesh, Material[] mats, bool collider, bool convex,
                                            float cullScreen = 0.012f, int splitTri = -1, Mesh colliderMesh = null)
        {
            if (splitTri > 0)
            {
                var all = mesh.triangles;
                mesh.subMeshCount = 2;
                var a = new int[splitTri]; System.Array.Copy(all, a, splitTri);
                var b = new int[all.Length - splitTri]; System.Array.Copy(all, splitTri, b, 0, b.Length);
                mesh.SetTriangles(a, 0);
                mesh.SetTriangles(b, 1);
            }
            var saved = Save(mesh, name);
            string dir = PrefabDir + "/" + folder;
            MeshKit.EnsureFolder(dir);
            var go = new GameObject(name);
            var model = new GameObject("LOD0");
            model.transform.SetParent(go.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = saved;
            var r = model.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = colliderMesh != null ? Save(colliderMesh, name + "_Col") : saved;
                mc.convex = convex;
            }
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullScreen, new Renderer[] { r }) });
            lod.RecalculateBounds();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, dir + "/" + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>(CourseGenerator.MatDir + "/" + n + ".mat");

        // ------------------------------------------------------------------ library
        [MenuItem("Tools/Tropical Course/Rebuild Prop Library")]
        public static void RebuildLibrary()
        {
            MaterialLibrary.Rebuild();
            var s = CourseGenerator.Settings();
            Color leafA = s.leaves, leafB = s.leavesAlt, grass = s.grass;
            Color white = Color.white;
            var trunkWood = new Color(0.72f, 0.58f, 0.42f);
            string[] rockMats = { "M_Rock_Red", "M_Rock_Orange", "M_Rock_Dark" };
            var shapes = new[] { RockShape.Boulder, RockShape.Boulder, RockShape.Boulder, RockShape.Slab, RockShape.Pillar, RockShape.Pebble };
            for (int i = 0; i < shapes.Length; i++)
            {
                string n = "PF_Rock_" + shapes[i] + "_" + (char)('A' + i);
                var vis = Rock(n, shapes[i], 100 + i * 17, 3);
                var col = Rock(n + "_Col", shapes[i], 100 + i * 17, 1);
                SavePrefab("Rocks", n, vis, new[] { M(rockMats[i % 3]) }, true, true, 0.006f, -1, col);
            }
            var leaves = M("M_Tropical_Leaves");
            var grassM = M("M_Grass");
            var wood = M("M_Wood");
            for (int i = 0; i < 3; i++)
            {
                var palm = Palm("PF_Palm_" + (char)('A' + i), 300 + i * 11, 7.5f + i * 1.8f, 10 + i, i == 1 ? leafB : leafA, trunkWood, out int tt);
                SavePrefab("Vegetation", "PF_Palm_" + (char)('A' + i), palm, new[] { wood, leaves }, false, false, 0.01f, tt);
                var tree = JungleTree("PF_JungleTree_" + (char)('A' + i), 400 + i * 13, 9f + i * 3f, i == 2 ? leafB : leafA, trunkWood * 0.8f, out int jt);
                SavePrefab("Vegetation", "PF_JungleTree_" + (char)('A' + i), tree, new[] { wood, leaves }, false, false, 0.008f, jt);
            }
            for (int i = 0; i < 2; i++)
            {
                SavePrefab("Vegetation", "PF_BroadLeaf_" + (char)('A' + i), BroadLeafPlant("PF_BroadLeaf_" + (char)('A' + i), 500 + i, i == 0 ? leafA : leafB), new[] { leaves }, false, false, 0.02f);
                SavePrefab("Vegetation", "PF_Fern_" + (char)('A' + i), Fern("PF_Fern_" + (char)('A' + i), 600 + i, leafA), new[] { leaves }, false, false, 0.02f);
                SavePrefab("Vegetation", "PF_GrassTuft_" + (char)('A' + i), GrassTuft("PF_GrassTuft_" + (char)('A' + i), 700 + i, grass), new[] { grassM }, false, false, 0.03f);
                SavePrefab("Vegetation", "PF_Shrub_" + (char)('A' + i), Shrub("PF_Shrub_" + (char)('A' + i), 800 + i, i == 0 ? leafA : leafB), new[] { leaves }, false, false, 0.015f);
                SavePrefab("Vegetation", "PF_Vine_" + (char)('A' + i), Vine("PF_Vine_" + (char)('A' + i), 900 + i, 5f + i * 2f, leafA), new[] { leaves }, false, false, 0.02f);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[TropicalCourse] prop library rebuilt in " + PrefabDir);
        }
    }
}
