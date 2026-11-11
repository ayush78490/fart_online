using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// Bank flamethrower: a pilot flame flickers and swells as a warning, then a jet of fire roars out across the water
    /// to <see cref="reach"/> metres, holds, and dies back. Only the full jet is deadly (its trigger collider is on while
    /// the flame is longer than a third of its reach). Driven by the shared obstacle clock, so every machine sees the
    /// same blasts. The flame mesh is built one metre long along +Z and stretched to the current length.
    /// </summary>
    public class FireJetObstacle : KinematicObstacle
    {
        [Tooltip("Stretched along local Z to the flame length")] public Transform flame;
        public Collider flameCollider;
        [Tooltip("Small flame at the nozzle that grows during the warning")] public Transform pilot;
        [Tooltip("Inner flame blobs that flicker (optional)")] public Transform[] tongues;
        [Tooltip("Flame length at full blast (m)")] public float reach = 5f;
        public float period = 4.4f;
        [Tooltip("Warning before each blast (s)")] public float warnTime = 1.2f;
        public float igniteTime = 0.22f, blastTime = 1.5f;

        /// <summary>0 = off, 1 = full jet.</summary>
        public float Jet01 { get; private set; }
        public bool Deadly => Jet01 > 0.33f;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localOffset = Vector3.zero;
            localRotation = Quaternion.identity;
        }

        protected override void Animate(float t)
        {
            float u = Mathf.Repeat(t, Mathf.Max(1f, period));
            float on = warnTime, full = on + igniteTime, off = full + blastTime, gone = off + igniteTime;
            float k = u < on ? 0f : u < full ? (u - on) / igniteTime : u < off ? 1f : u < gone ? 1f - (u - off) / igniteTime : 0f;
            k = Mathf.SmoothStep(0f, 1f, k);
            Jet01 = k;
            float flick = 1f + 0.07f * Mathf.Sin(t * 31f) + 0.05f * Mathf.Sin(t * 57f + 1.3f);
            if (flame != null)
            {
                bool show = k > 0.02f;
                if (flame.gameObject.activeSelf != show) flame.gameObject.SetActive(show);
                float w = Mathf.Lerp(0.35f, 1f, k) * flick;
                flame.localScale = new Vector3(w, w, Mathf.Max(0.05f, k * reach * (1f + 0.03f * Mathf.Sin(t * 23f))));
            }
            if (flameCollider != null) flameCollider.enabled = Deadly;
            if (pilot != null)
            {
                // warning: the pilot flame swells and flickers faster just before the blast
                float warn = u < on ? Mathf.Clamp01(u / on) : k > 0f ? 1f : 0.25f;
                float f = 1f + 0.25f * Mathf.Sin(t * Mathf.Lerp(12f, 40f, warn));
                pilot.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.15f, warn) * f;
            }
            if (tongues != null)
                for (int i = 0; i < tongues.Length; i++)
                {
                    if (tongues[i] == null) continue;
                    float f = 1f + 0.18f * Mathf.Sin(t * (19f + i * 7f) + i * 1.7f);
                    tongues[i].localScale = new Vector3(f, f, 1f);        // never longer than the jet (its footprint)
                }
        }
    }
}
