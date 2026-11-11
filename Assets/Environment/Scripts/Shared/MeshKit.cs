using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>Small mesh-building helpers shared by the environment generators (editor) and the endless river
    /// (runtime). Asset saving is editor-only.</summary>
    public class MeshKit
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Vector2> uv2 = new List<Vector2>();
        public readonly List<Color> col = new List<Color>();
        public readonly List<int> tri = new List<int>();

        public int Add(Vector3 p, Vector2 t, Color c) { v.Add(p); uv.Add(t); uv2.Add(Vector2.zero); col.Add(c); return v.Count - 1; }
        public int Add(Vector3 p, Vector2 t, Vector2 t2, Color c) { v.Add(p); uv.Add(t); uv2.Add(t2); col.Add(c); return v.Count - 1; }

        public void Quad(int a, int b, int c, int d) { tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(a); tri.Add(c); tri.Add(d); }

        /// <summary>Connects two rows of equal length into quads.</summary>
        public void Strip(int[] rowA, int[] rowB)
        {
            for (int k = 0; k < rowA.Length - 1; k++) Quad(rowA[k], rowB[k], rowB[k + 1], rowA[k + 1]);
        }

        /// <summary>Flips the triangles in [triStart, triEnd) if their summed (area-weighted) normal faces away from
        /// <paramref name="desired"/>. Unity's front face normal is Cross(b - a, c - a).</summary>
        public void OrientRange(int triStart, int triEnd, Vector3 desired)
        {
            Vector3 sum = Vector3.zero;
            for (int i = triStart; i < triEnd; i += 3)
                sum += Vector3.Cross(v[tri[i + 1]] - v[tri[i]], v[tri[i + 2]] - v[tri[i]]);
            if (Vector3.Dot(sum, desired) >= 0f) return;
            for (int i = triStart; i < triEnd; i += 3) { int t = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t; }
        }

        /// <summary>Per-triangle: flip so each triangle faces away from <paramref name="center"/> (closed, roughly convex shapes).</summary>
        public void OrientOutward(int triStart, int triEnd, Vector3 center)
        {
            for (int i = triStart; i < triEnd; i += 3)
            {
                Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center) < 0f) { int t = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t; }
            }
        }

        /// <summary>Per-triangle: flip so each triangle faces away from the nearest point of a polyline axis (tubes).</summary>
        public void OrientOutwardAxis(int triStart, int triEnd, List<Vector3> axis)
        {
            for (int i = triStart; i < triEnd; i += 3)
            {
                Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                Vector3 m = (a + b + c) / 3f, best = axis[0];
                float bd = float.MaxValue;
                for (int k = 0; k < axis.Count - 1; k++)
                {
                    Vector3 ab = axis[k + 1] - axis[k];
                    float t = Mathf.Clamp01(Vector3.Dot(m - axis[k], ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    Vector3 q = axis[k] + ab * t;
                    float d = (m - q).sqrMagnitude;
                    if (d < bd) { bd = d; best = q; }
                }
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), m - best) < 0f) { int t2 = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t2; }
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetUVs(1, uv2);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        public static Vector3 AverageNormal(Mesh m)
        {
            Vector3 s = Vector3.zero;
            foreach (var n in m.normals) s += n;
            return s.normalized;
        }

        public static void FlipWinding(Mesh m)
        {
            var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            m.triangles = t;
            m.RecalculateNormals();
        }

#if UNITY_EDITOR
        /// <summary>Saves (or overwrites in place, keeping references) a mesh asset.</summary>
        public static Mesh SaveAsset(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = System.IO.Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
#endif
    }

    /// <summary>Deterministic value noise (no dependency on UnityEngine.Random state).</summary>
    public static class Noise
    {
        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                int h = seed * 374761393 + x * 668265263 + y * 2147483647 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        static float S(float t) => t * t * (3f - 2f * t);

        /// <summary>3D value noise in [-1, 1].</summary>
        public static float Value(Vector3 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = S(p.x - x), fy = S(p.y - y), fz = S(p.z - z);
            float Lx(int yy, int zz) => Mathf.Lerp(Hash(x, yy, zz, seed), Hash(x + 1, yy, zz, seed), fx);
            float a = Mathf.Lerp(Lx(y, z), Lx(y + 1, z), fy);
            float b = Mathf.Lerp(Lx(y, z + 1), Lx(y + 1, z + 1), fy);
            return Mathf.Lerp(a, b, fz) * 2f - 1f;
        }

        public static float Fbm(Vector3 p, int seed, int octaves = 3)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++) { sum += Value(p, seed + i * 31) * amp; norm += amp; p *= 2.03f; amp *= 0.5f; }
            return sum / norm;
        }
    }
}
