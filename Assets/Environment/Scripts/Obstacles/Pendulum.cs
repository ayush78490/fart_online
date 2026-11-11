using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>Swinging hazard (e.g. a log on ropes). Pivot = this transform; swings about its local X axis.</summary>
    public class Pendulum : KinematicObstacle
    {
        [Tooltip("Swing amplitude either side of vertical (degrees)")] [Range(5f, 85f)] public float amplitude = 55f;
        [Tooltip("Seconds for a full back-and-forth swing")] public float period = 3.2f;
        [Tooltip("Rope length from the pivot to the hazard (gizmo only)")] public float length = 6f;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localOffset = Vector3.zero;
            // sin() gives the natural slow-at-the-ends / fast-in-the-middle swing
            localRotation = Quaternion.Euler(amplitude * Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.2f, period)), 0f, 0f);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.1f);
            var m = StartMatrix();
            Vector3 pivot = m.MultiplyPoint(Vector3.zero);
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= 24; i++)
            {
                float a = Mathf.Lerp(-amplitude, amplitude, i / 24f);
                Vector3 p = m.MultiplyPoint(Quaternion.Euler(a, 0, 0) * (Vector3.down * length));
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.DrawLine(pivot, m.MultiplyPoint(Vector3.down * length));
        }
    }
}
