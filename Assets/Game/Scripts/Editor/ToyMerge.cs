using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FartOnline.EnvironmentEditor;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Draw-call saver for the procedural hazards. Every transform's plain "part" children (a MeshFilter + MeshRenderer
    /// with an opaque URP Lit toy material, nothing else, no children) are merged into ONE mesh whose vertex colours
    /// carry each part's colour, drawn with the single vertex-colour material M_Toy_VertexColor (Env/StylizedSurface,
    /// Foliage mode with no wind). A spiked roller goes from four draws (body, bands, caps, spikes) to one.
    /// Unlit glow parts, transparent parts and anything with extra components or children are left alone.
    /// </summary>
    public static class ToyMerge
    {
        const string MatPath = EndlessBuilder.Dir + "/Materials/M_Toy_VertexColor.mat";

        public static Material VertexColorMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null)
            {
                m = new Material(Shader.Find("Env/StylizedSurface"));
                AssetDatabase.CreateAsset(m, MatPath);
            }
            m.SetFloat("_Mode", 1f);
            m.DisableKeyword("_MODE_ROCK"); m.DisableKeyword("_MODE_WOOD"); m.EnableKeyword("_MODE_FOLIAGE");
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Translucency", 0f);
            m.SetFloat("_WindStrength", 0f);
            m.SetFloat("_Smoothness", 0.42f);
            m.SetFloat("_Cull", 2f);
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        static bool Mergeable(Transform t, out MeshFilter mf, out MeshRenderer mr)
        {
            mf = t.GetComponent<MeshFilter>();
            mr = t.GetComponent<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null || t.childCount > 0) return false;
            if (t.GetComponents<Component>().Length != 3) return false;           // Transform + MeshFilter + MeshRenderer only
            var m = mr.sharedMaterial;
            if (m == null || m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") return false;
            if (m.GetFloat("_Surface") > 0.5f) return false;                       // transparent
            return true;
        }

        /// <summary>Merges the parts of every transform under <paramref name="root"/>. Returns the number of draws saved.</summary>
        public static int Merge(GameObject root)
        {
            int saved = 0;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t == null) continue;                                               // a part merged a moment ago
                var parts = new List<Transform>();
                for (int i = 0; i < t.childCount; i++)
                    if (Mergeable(t.GetChild(i), out _, out _)) parts.Add(t.GetChild(i));
                if (parts.Count < 2) continue;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var tri = new List<int>();
                bool shadows = false;
                foreach (var p in parts)
                {
                    Mergeable(p, out var mf, out var mr);
                    var mesh = mf.sharedMesh;
                    Color col = mr.sharedMaterial.GetColor("_BaseColor").linear;
                    col.a = 0f;                                                        // no wind sway
                    var m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale);
                    int b = v.Count;
                    var pv = mesh.vertices; var pn = mesh.normals;
                    for (int i = 0; i < pv.Length; i++)
                    {
                        v.Add(m.MultiplyPoint3x4(pv[i]));
                        n.Add(pn.Length == pv.Length ? m.MultiplyVector(pn[i]).normalized : Vector3.up);
                        c.Add(col);
                    }
                    foreach (int i in mesh.triangles) tri.Add(b + i);
                    shadows |= mr.shadowCastingMode != ShadowCastingMode.Off;
                }
                var merged = new Mesh { name = "Merged_" + root.name + "_" + t.name };
                if (v.Count > 65000) merged.indexFormat = IndexFormat.UInt32;
                merged.SetVertices(v); merged.SetNormals(n); merged.SetColors(c); merged.SetTriangles(tri, 0);
                merged.RecalculateBounds();
                var go = new GameObject("Merged");
                go.transform.SetParent(t, false);
                go.AddComponent<MeshFilter>().sharedMesh = MeshKit.SaveAsset(merged, EndlessBuilder.MeshDir + "/" + merged.name + ".asset");
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = VertexColorMaterial();
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                foreach (var p in parts) Object.DestroyImmediate(p.gameObject);
                saved += parts.Count - 1;
            }
            return saved;
        }
    }
}
