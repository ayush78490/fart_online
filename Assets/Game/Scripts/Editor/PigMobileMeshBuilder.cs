using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using FartOnline.Game;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Menu Tools > Fart Online > Build Mobile Pig Mesh. Makes a real low-poly copy of the pig's skinned mesh for
    /// phones: Unity's Mesh LOD generator simplifies each submesh, then the chosen LOD's triangles are compacted into a
    /// new mesh with only the vertices they use (Mesh LOD alone would keep all 19.7k vertices, and GPU skinning and
    /// the 23 blend shapes cost per vertex). Bone weights, bind poses, blend shapes, colours and UVs are carried over.
    /// Small submeshes (eyes, mouth parts) keep more detail than the body so they don't disappear.
    /// Output: Assets/Characters/Pig/Models/Pig_Mesh_Mobile.asset, wired to PF_Pig through <see cref="MobileMeshSwap"/>.
    /// </summary>
    public static class PigMobileMeshBuilder
    {
        public const string PigPrefab = "Assets/Characters/Pig/PF_Pig.prefab";
        public const string OutPath = "Assets/Characters/Pig/Models/Pig_Mesh_Mobile.asset";

        [MenuItem("Tools/Fart Online/Build Mobile Pig Mesh")]
        public static void BuildMenu() => Debug.Log("[FartOnline] " + Build(3));

        /// <param name="bodyLod">LOD level for large submeshes; small ones use fewer levels (see LodFor)</param>
        public static string Build(int bodyLod)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PigPrefab);
            var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var src = smr.sharedMesh;
            if (src.name.EndsWith("_Mobile")) return "PF_Pig already uses the mobile mesh as its main mesh: aborted";

            var lodded = Object.Instantiate(src);
            MeshLodUtility.GenerateMeshLods(lodded, 6);
            var allIdx = new List<int>();
            using (var data = Mesh.AcquireReadOnlyMeshData(lodded))
            {
                var md = data[0];
                if (md.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                    foreach (var i in md.GetIndexData<ushort>()) allIdx.Add(i);
                else
                    foreach (var i in md.GetIndexData<int>()) allIdx.Add(i);
            }

            int subCount = src.subMeshCount;
            var remap = new Dictionary<int, int>();
            var keep = new List<int>();                 // new vertex -> old vertex
            var subTris = new List<int>[subCount];
            var report = new System.Text.StringBuilder();
            int trisBefore = 0, trisAfter = 0;
            for (int s = 0; s < subCount; s++)
            {
                var desc = lodded.GetSubMesh(s);
                int lod0Count = (int)lodded.GetLod(s, 0).indexCount;
                int lod = LodFor(lod0Count / 3, bodyLod, lodded.lodCount);
                var range = lodded.GetLod(s, lod);
                while (range.indexCount < lod0Count * 0.15f && lod > 0) { lod--; range = lodded.GetLod(s, lod); }   // never lose a part
                subTris[s] = new List<int>((int)range.indexCount);
                for (int k = 0; k < range.indexCount; k++)
                {
                    int old = allIdx[desc.indexStart + (int)range.indexStart + k] + desc.baseVertex;
                    if (!remap.TryGetValue(old, out int nv)) { nv = keep.Count; remap[old] = nv; keep.Add(old); }
                    subTris[s].Add(nv);
                }
                trisBefore += lod0Count / 3;
                trisAfter += (int)range.indexCount / 3;
                report.Append($"sub{s} LOD{lod} {lod0Count / 3}->{range.indexCount / 3}; ");
            }
            Object.DestroyImmediate(lodded);

            var m = new Mesh { name = src.name + "_Mobile" };
            m.indexFormat = keep.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(Pick(src.vertices, keep));
            if (src.normals.Length > 0) m.SetNormals(Pick(src.normals, keep));
            if (src.tangents.Length > 0) m.SetTangents(Pick(src.tangents, keep));
            if (src.colors32.Length > 0) m.SetColors(Pick(src.colors32, keep));
            for (int ch = 0; ch < 8; ch++)
            {
                if (!src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch)) continue;
                var uv = new List<Vector4>();
                src.GetUVs(ch, uv);
                m.SetUVs(ch, Pick(uv.ToArray(), keep));
            }

            // bone weights (any count per vertex)
            var perVertex = src.GetBonesPerVertex();
            var weights = src.GetAllBoneWeights();
            var start = new int[src.vertexCount];
            for (int v = 0, acc = 0; v < src.vertexCount; v++) { start[v] = acc; acc += perVertex[v]; }
            var newPer = new NativeArray<byte>(keep.Count, Allocator.Temp);
            var newW = new List<BoneWeight1>();
            for (int i = 0; i < keep.Count; i++)
            {
                int old = keep[i];
                newPer[i] = perVertex[old];
                for (int k = 0; k < perVertex[old]; k++) newW.Add(weights[start[old] + k]);
            }
            var newWArr = new NativeArray<BoneWeight1>(newW.ToArray(), Allocator.Temp);
            m.SetBoneWeights(newPer, newWArr);
            newPer.Dispose();
            newWArr.Dispose();
            m.bindposes = src.bindposes;

            m.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++) m.SetTriangles(subTris[s], s, false);

            var dv = new Vector3[src.vertexCount];
            var dn = new Vector3[src.vertexCount];
            var dt = new Vector3[src.vertexCount];
            for (int b = 0; b < src.blendShapeCount; b++)
            {
                string bn = src.GetBlendShapeName(b);
                for (int f = 0; f < src.GetBlendShapeFrameCount(b); f++)
                {
                    src.GetBlendShapeFrameVertices(b, f, dv, dn, dt);
                    m.AddBlendShapeFrame(bn, src.GetBlendShapeFrameWeight(b, f), Pick(dv, keep), Pick(dn, keep), Pick(dt, keep));
                }
            }
            m.bounds = src.bounds;
            m.UploadMeshData(false);

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutPath);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); m = existing; }
            else AssetDatabase.CreateAsset(m, OutPath);
            EditorUtility.SetDirty(m);

            // wire it to the prefab
            var root = PrefabUtility.LoadPrefabContents(PigPrefab);
            var target = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var swap = target.GetComponent<MobileMeshSwap>();
            if (swap == null) swap = target.gameObject.AddComponent<MobileMeshSwap>();
            swap.mobileMesh = m;
            PrefabUtility.SaveAsPrefabAsset(root, PigPrefab);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();

            return $"mobile pig mesh: {src.vertexCount} -> {keep.Count} vertices, {trisBefore} -> {trisAfter} triangles, {src.blendShapeCount} blend shapes | {report}";
        }

        static int LodFor(int tris, int bodyLod, int lodCount)
        {
            int lod = tris > 5000 ? bodyLod : tris > 1000 ? bodyLod - 1 : tris > 300 ? bodyLod - 2 : 0;
            return Mathf.Clamp(lod, 0, lodCount - 1);
        }

        static T[] Pick<T>(T[] src, List<int> keep)
        {
            var r = new T[keep.Count];
            for (int i = 0; i < keep.Count; i++) r[i] = src[keep[i]];
            return r;
        }
    }
}
