using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Swaps a SkinnedMeshRenderer to a lighter mesh on the "Mobile" quality level (phones, tablets, the web build).
    /// The crew pigs are 30.8k triangles / 19.7k vertices each, and four of them cost ~5 ms of GPU time and ~3 ms of
    /// main thread on an Adreno 619. The mobile mesh (built by <c>Tools > Fart Online > Build Mobile Pig Mesh</c>)
    /// keeps the same bones, bind poses, blend shapes (same names and order), submeshes and UVs, so the Animator,
    /// PigFaceController and the materials work unchanged.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    public class MobileMeshSwap : MonoBehaviour
    {
        public Mesh mobileMesh;

        void Awake()
        {
            if (mobileMesh == null || !PlatformBoot.IsMobileQuality) return;
            var smr = GetComponent<SkinnedMeshRenderer>();
            if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount != mobileMesh.blendShapeCount) return;   // stale asset
            smr.sharedMesh = mobileMesh;
        }
    }
}
