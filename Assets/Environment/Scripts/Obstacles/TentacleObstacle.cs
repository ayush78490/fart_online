using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// River kraken tentacle: a chain of bones rising from the water by the bank. The base sways toward the river and
    /// back while a curl wave runs up the chain. Every bone angle is bounded, so the tip never reaches further than
    /// <see cref="MaxReach"/> from the base (the planner's footprint).
    /// </summary>
    public class TentacleObstacle : KinematicObstacle
    {
        public Transform[] bones;
        [Tooltip("Lean of the base toward the river (deg): min and max of the sway")] public Vector2 lean = new Vector2(10f, 50f);
        [Tooltip("Extra curl per bone (deg) at the peak of the wave")] public float curl = 7f;
        public float period = 3.4f;
        public float boneLength = 0.6f;

        /// <summary>Horizontal reach bound of the tip from the base (m, before scaling).</summary>
        public float MaxReach => bones != null ? bones.Length * boneLength : 0f;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localOffset = Vector3.zero;
            localRotation = Quaternion.identity;
        }

        protected override void Animate(float t)
        {
            if (bones == null || bones.Length == 0) return;
            float w = t * Mathf.PI * 2f / Mathf.Max(0.2f, period);
            float s = 0.5f + 0.5f * Mathf.Sin(w);
            // base leans toward the river (local +Z of the rig points into the river), side-to-side wobble along the flow
            bones[0].localRotation = Quaternion.Euler(Mathf.Lerp(lean.x, lean.y, s), 0f, Mathf.Sin(w * 0.7f) * 12f);
            for (int i = 1; i < bones.Length; i++)
            {
                float phase = Mathf.Sin(w - i * 0.55f);
                bones[i].localRotation = Quaternion.Euler(curl * (0.6f + 0.4f * phase), 0f, Mathf.Sin(w * 0.9f - i * 0.4f) * 5f);
            }
        }
    }
}
