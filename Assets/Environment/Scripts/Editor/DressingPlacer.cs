using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FartOnline.Environment;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Deterministic dressing of the course (rocks, vegetation, vines) and the background (outer ground that rises
    /// into distant jungle hills + background trees). Uses the exact terrain profile (RiverTerrainBuilder.SidePoints)
    /// to pick zones (beach, wall foot, ledges, wall top, plateau) and raycasts onto the terrain colliders to snap.
    /// Never places anything inside the raft clearance lane.
    /// </summary>
    public static class DressingPlacer
    {
        const string P = PropFactory.PrefabDir;
        static GameObject Prefab(string folder, string name) => AssetDatabase.LoadAssetAtPath<GameObject>(P + "/" + folder + "/" + name + ".prefab");

        static readonly string[] Rocks = { "PF_Rock_Boulder_A", "PF_Rock_Boulder_B", "PF_Rock_Boulder_C", "PF_Rock_Slab_D", "PF_Rock_Pillar_E", "PF_Rock_Pebble_F" };
        static readonly string[] Ground = { "PF_GrassTuft_A", "PF_GrassTuft_B", "PF_Fern_A", "PF_Fern_B", "PF_BroadLeaf_A", "PF_BroadLeaf_B", "PF_Shrub_A", "PF_Shrub_B" };
        static readonly string[] Trees = { "PF_Palm_A", "PF_Palm_B", "PF_Palm_C", "PF_JungleTree_A", "PF_JungleTree_B", "PF_JungleTree_C" };

        static int placed;

        static GameObject Place(Transform parent, GameObject prefab, Vector3 pos, float yaw, float scale, bool snap, bool alignToNormal, float sink, bool staticBatch)
        {
            if (prefab == null) return null;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            if (snap)
            {
                if (!Physics.Raycast(pos + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)) return null;
                pos = hit.point - Vector3.up * sink * scale;
                if (alignToNormal) rot = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, hit.normal, 0.6f)) * rot;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * scale;
            var flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;
            if (staticBatch) flags |= StaticEditorFlags.BatchingStatic;
            foreach (var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            placed++;
            return go;
        }

        static T Pick<T>(System.Random r, T[] a) => a[r.Next(a.Length)];
        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ------------------------------------------------------------------ course dressing
        public static void Scatter(RiverPath path, CourseSettings set, Transform root)
        {
            placed = 0;
            Physics.SyncTransforms();
            var rnd = new System.Random(set.seed * 7 + 3);
            var rocksT = CourseGenerator.Child(root, "Rocks", true);
            var vegT = CourseGenerator.Child(root, "Vegetation", true);
            var treeT = CourseGenerator.Child(root, "Trees", true);
            var vineT = CourseGenerator.Child(root, "Vines", true);
            float dens = set.vegetationDensity, rockD = set.rockDensity, rs = set.rockScale;
            var samples = path.Samples;
            float diag = set.MinRaftPassage;
            int last = samples.Count - 1;
            for (int i = 4; i < last - 4; i += 2)
            {
                var s = samples[i];
                for (int side = -1; side <= 1; side += 2)
                {
                    var pts = RiverTerrainBuilder.SidePoints(s, set, side, out var prof);
                    int lip = 3, shore = 4, foot = 5, wallTop = prof.Count - 3, edge = prof.Count - 2, back = prof.Count - 1;
                    Vector3 inward = -s.right * side;                                  // towards the water
                    float yawToRiver = Quaternion.LookRotation(Vector3.ProjectOnPlane(inward, Vector3.up)).eulerAngles.y;
                    bool beach = s.bankFlat > 2.5f;
                    // beach / shore: ground cover + occasional palm
                    if (beach && rnd.NextDouble() < 0.9 * dens)
                    {
                        Vector3 p = Vector3.Lerp(pts[lip], pts[foot], Range(rnd, 0.25f, 0.95f)) + s.tangent * Range(rnd, -1f, 1f);
                        Place(vegT, Prefab("Vegetation", Pick(rnd, Ground)), p, Range(rnd, 0, 360), Range(rnd, 0.9f, 1.6f), true, true, 0.05f, false);
                        if (rnd.NextDouble() < 0.18 * dens)
                            Place(treeT, Prefab("Vegetation", Pick(rnd, new[] { "PF_Palm_A", "PF_Palm_B", "PF_Palm_C" })),
                                  Vector3.Lerp(pts[shore], pts[foot], 0.7f), Range(rnd, 0, 360), Range(rnd, 0.85f, 1.15f), true, false, 0.1f, false);
                    }
                    // boulders against the wall foot
                    if (rnd.NextDouble() < 0.22 * rockD)
                    {
                        Vector3 p = pts[foot] + inward * Range(rnd, -0.5f, 0.8f);
                        Place(rocksT, Prefab("Rocks", Pick(rnd, Rocks)), p, Range(rnd, 0, 360), Range(rnd, 0.9f, 2.3f) * rs, true, true, 0.25f, true);
                    }
                    // ledges: shrubs / ferns where the profile flagged vegetation (vertex colour B on wall rows)
                    for (int k = foot + 1; k < wallTop; k++)
                        if (prof[k].c.b > 0.35f && rnd.NextDouble() < 0.12 * dens)
                            Place(vegT, Prefab("Vegetation", Pick(rnd, new[] { "PF_Shrub_A", "PF_Shrub_B", "PF_Fern_A", "PF_BroadLeaf_B" })),
                                  pts[k] + inward * 0.6f, Range(rnd, 0, 360), Range(rnd, 0.8f, 1.3f), true, true, 0.1f, false);
                    // vines hanging over the upper wall (lush sections)
                    if ((s.section == "A" || s.section == "B" || s.section == "C" || s.section == "F") && rnd.NextDouble() < 0.07 * dens)
                    {
                        Vector3 p = pts[wallTop - 1] + inward * 0.5f;
                        var v = Place(vineT, Prefab("Vegetation", Pick(rnd, new[] { "PF_Vine_A", "PF_Vine_B" })), p, yawToRiver, Range(rnd, 0.9f, 1.4f), false, false, 0f, false);
                    }
                    // plateau: trees + shrubs (frame the canyon from above)
                    if (rnd.NextDouble() < 0.35 * dens)
                        Place(treeT, Prefab("Vegetation", Pick(rnd, Trees)), Vector3.Lerp(pts[edge], pts[back], Range(rnd, 0.05f, 0.9f)) + s.tangent * Range(rnd, -1f, 1f),
                              Range(rnd, 0, 360), Range(rnd, 0.8f, 1.3f), true, false, 0.2f, false);
                    if (rnd.NextDouble() < 0.35 * dens)
                        Place(vegT, Prefab("Vegetation", Pick(rnd, new[] { "PF_Shrub_A", "PF_Shrub_B", "PF_BroadLeaf_A" })),
                              Vector3.Lerp(pts[wallTop], pts[back], Range(rnd, 0.0f, 0.7f)), Range(rnd, 0, 360), Range(rnd, 1f, 1.8f), true, true, 0.1f, false);
                }
                // rocks in the water near the banks, only where the channel stays wide enough for the raft
                if (s.hasWater && rnd.NextDouble() < 0.06 * rockD)
                {
                    float intrusion = Mathf.Clamp((s.width - (diag + 3f)) * 0.5f, 0f, 3f);
                    if (intrusion > 0.8f)
                    {
                        int side = rnd.NextDouble() < 0.5 ? -1 : 1;
                        float r = Mathf.Min(intrusion, 1.6f);
                        Vector3 p = s.position + s.right * side * (s.width * 0.5f - r * 0.5f);
                        p.y = s.position.y - set.riverDepth * 0.4f;
                        Place(rocksT, Prefab("Rocks", Pick(rnd, new[] { "PF_Rock_Boulder_A", "PF_Rock_Boulder_C", "PF_Rock_Slab_D" })), p,
                              Range(rnd, 0, 360), r * 1.3f * rs, false, false, 0f, true);
                    }
                }
            }
            Debug.Log("[TropicalCourse] dressing placed: " + placed);
        }

        // ------------------------------------------------------------------ background
        /// <summary>Outer ground around the canyon: sits 0.3 m under the plateau near the corridor (no gaps, no
        /// z-fighting), never enters the corridor, and rises into distant hills. Plus background trees.</summary>
        public static void Background(RiverPath path, CourseSettings set, Transform root, Material mat)
        {
            var bg = CourseGenerator.Child(root, "Background", true);
            var samples = path.Samples;
            Vector3 min = Vector3.one * 1e9f, max = -Vector3.one * 1e9f;
            foreach (var s in samples) { min = Vector3.Min(min, s.position); max = Vector3.Max(max, s.position); }
            float margin = 520f, cell = 12f;
            int nx = Mathf.CeilToInt((max.x - min.x + margin * 2) / cell), nz = Mathf.CeilToInt((max.z - min.z + margin * 2) / cell);
            Vector3 origin = new Vector3(min.x - margin, 0f, min.z - margin);
            // per-vertex: corridor distance + plateau height of the nearest sample
            var h = new float[nx + 1, nz + 1];
            var inside = new bool[nx + 1, nz + 1];
            var sideOf = new int[nx + 1, nz + 1];
            var pos = new Vector3[nx + 1, nz + 1];
            var col = new Color[nx + 1, nz + 1];
            for (int x = 0; x <= nx; x++)
                for (int z = 0; z <= nz; z++)
                {
                    Vector3 p = origin + new Vector3(x * cell, 0, z * cell);
                    var s = path.Closest(p, out float lat);
                    float wallH = s.wallHeight * set.canyonHeightScale;
                    float foot = s.width * 0.5f * set.riverWidthScale + Mathf.Max(1.2f, s.bankFlat);
                    float backEdge = foot + s.wallRun + 2.5f + 26f;                  // matches SideProfile's back plateau
                    float d = Mathf.Abs(lat);
                    // beyond the ends of the course 'lateral' is meaningless: use the distance to the end instead
                    if (s.distance <= 0.5f || s.distance >= path.Length - 0.5f) d = Mathf.Max(d, Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(s.position.x, 0, s.position.z)));
                    inside[x, z] = d < backEdge - 3f;
                    sideOf[x, z] = lat >= 0f ? 1 : -1;
                    float plateau = s.position.y + wallH + 2.5f - 0.3f;
                    // inside vertices slide onto the corridor boundary (tucked under the inner plateau) so boundary
                    // cells close the gap to the canyon terrain instead of leaving holes
                    pos[x, z] = inside[x, z] ? new Vector3(s.position.x, 0f, s.position.z) + s.right * sideOf[x, z] * (backEdge - 3f) : new Vector3(p.x, 0f, p.z);
                    float beyond = Mathf.Max(0f, d - backEdge);
                    float hills = Mathf.SmoothStep(0f, 1f, beyond / 160f) * (28f + 40f * (Noise.Fbm(p * 0.006f, set.seed + 77) * 0.5f + 0.5f))
                                  + Noise.Fbm(p * 0.02f, set.seed + 78) * 4f * Mathf.Clamp01(beyond / 30f);
                    h[x, z] = plateau + hills;
                    col[x, z] = new Color(0.5f, 0f, 1f, 1f);                          // grass mask everywhere (slopes show rock)
                }
            var k = new MeshKit();
            var idx = new int[nx + 1, nz + 1];
            for (int x = 0; x <= nx; x++)
                for (int z = 0; z <= nz; z++)
                    idx[x, z] = k.Add(new Vector3(pos[x, z].x, h[x, z], pos[x, z].z), new Vector2(x, z), col[x, z]);
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    int ins = (inside[x, z] ? 1 : 0) + (inside[x + 1, z] ? 1 : 0) + (inside[x, z + 1] ? 1 : 0) + (inside[x + 1, z + 1] ? 1 : 0);
                    if (ins == 4) continue;                                           // wholly inside the canyon corridor
                    int sd = sideOf[x, z];
                    if (sideOf[x + 1, z] != sd || sideOf[x, z + 1] != sd || sideOf[x + 1, z + 1] != sd) { if (ins > 0) continue; }
                    k.Quad(idx[x, z], idx[x, z + 1], idx[x + 1, z + 1], idx[x + 1, z]);
                }
            k.OrientRange(0, k.tri.Count, Vector3.up);
            var mesh = MeshKit.SaveAsset(k.Build("Background_Ground"), CourseGenerator.MeshDir + "/Background_Ground.asset");
            var go = new GameObject("Background_Ground");
            go.transform.SetParent(bg, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            // background trees on the near hills (cheap: LOD-culled prefabs, no colliders)
            var rnd = new System.Random(set.seed + 999);
            var trees = CourseGenerator.Child(bg, "BackgroundTrees", true);
            int count = Mathf.RoundToInt(260 * set.vegetationDensity);
            int tries = 0, made = 0;
            while (made < count && tries < count * 8)
            {
                tries++;
                int x = rnd.Next(1, nx), z = rnd.Next(1, nz);
                if (inside[x, z] || inside[x - 1, z] || inside[x + 1, z] || inside[x, z - 1] || inside[x, z + 1]) continue;
                Vector3 p = origin + new Vector3(x * cell + Range(rnd, -5f, 5f), h[x, z], z * cell + Range(rnd, -5f, 5f));
                var s = path.Closest(p, out float lat);
                if (Mathf.Abs(lat) > 260f) continue;                                 // keep them where they are seen
                var pf = Prefab("Vegetation", Pick(rnd, Trees));
                var t = (GameObject)PrefabUtility.InstantiatePrefab(pf, trees);
                t.transform.SetPositionAndRotation(p - Vector3.up * 0.3f, Quaternion.Euler(0, Range(rnd, 0, 360), 0));
                t.transform.localScale = Vector3.one * Range(rnd, 1.1f, 2.0f);
                made++;
            }
            Debug.Log($"[TropicalCourse] background: {nx}x{nz} cells, {made} trees");
        }
    }
}
