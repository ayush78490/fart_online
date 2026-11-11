using UnityEngine;

namespace FartOnline.Water
{
    /// <summary>
    /// Runtime controls for a generated waterfall (sheet + foam disc + mist/splash particles). Geometry is built by
    /// the Tropical Course Generator; this component exposes the look/feel parameters and applies them without
    /// regenerating meshes (material property blocks, particle rates), so many waterfalls share one material.
    /// </summary>
    [ExecuteAlways]
    public class Waterfall : MonoBehaviour
    {
        [Header("Shape (used by the generator when rebuilding)")]
        public float height = 10f;
        public float width = 5f;
        [Header("Look")]
        public float flowSpeed = 1.6f;
        public Color color = new Color(0.55f, 0.92f, 0.95f, 1f);
        [Range(0f, 2f)] public float particleIntensity = 1f;
        [Range(0.3f, 2f)] public float foamSize = 1f;
        [Tooltip("Impact foam radius at foamSize 1 (m); drawn by the river shader as a foam spot")] public float foamRadius = 4f;
        [Header("Parts")]
        public Renderer sheet;
        public Transform foam;
        public ParticleSystem mist;
        public ParticleSystem splash;

        [Header("Audio hook (optional)")]
        [Tooltip("Assign a looping waterfall clip; left empty the scene works silently")] public AudioSource ambience;

        MaterialPropertyBlock mpb;
        float mistBase = -1f, splashBase = -1f;

        /// <summary>xyz = impact centre, w = radius (consumed by the river shader).</summary>
        public Vector4 FoamSpot
        {
            get { var p = foam.position; return new Vector4(p.x, p.y, p.z, foamRadius * foamSize); }
        }

        void OnEnable() { Apply(); WaterFoamSpots.Register(this); }
        void OnDisable() => WaterFoamSpots.Unregister(this);
        void OnValidate() => Apply();

        public void Apply()
        {
            if (sheet != null)
            {
                mpb ??= new MaterialPropertyBlock();
                sheet.GetPropertyBlock(mpb);
                mpb.SetFloat("_FlowSpeed", flowSpeed);
                mpb.SetColor("_Color", color);
                sheet.SetPropertyBlock(mpb);
            }
            SetRate(mist, ref mistBase);
            if (isActiveAndEnabled) WaterFoamSpots.Upload();
            SetRate(splash, ref splashBase);
        }

        void SetRate(ParticleSystem ps, ref float baseRate)
        {
            if (ps == null) return;
            var em = ps.emission;
            if (baseRate < 0f) baseRate = em.rateOverTime.constant;
            em.rateOverTime = baseRate * particleIntensity;
        }
    }
}
