using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// Puffer mine: bobs and slowly turns while it inflates and deflates (scale between <see cref="minScale"/> and 1).
    /// Its footprint is its fully inflated size.
    /// </summary>
    public class PulseObstacle : KinematicObstacle
    {
        public float period = 2.6f;
        [Range(0.3f, 1f)] public float minScale = 0.7f;
        public float bob = 0.25f;
        public float turnSpeed = 25f;
        Vector3 fullScale;
        bool captured;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            float w = t * Mathf.PI * 2f / Mathf.Max(0.2f, period);
            localOffset = Vector3.up * Mathf.Sin(w * 0.5f) * bob;
            localRotation = Quaternion.Euler(0f, t * turnSpeed, 0f);
        }

        protected override void Animate(float t)
        {
            if (!captured) { fullScale = transform.localScale; captured = true; }   // after the spawner set the size
            float w = t * Mathf.PI * 2f / Mathf.Max(0.2f, period);
            // quick puff, slow deflate
            float k = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(w), 2f);
            transform.localScale = fullScale * Mathf.Lerp(minScale, 1f, k);
        }
    }
}
