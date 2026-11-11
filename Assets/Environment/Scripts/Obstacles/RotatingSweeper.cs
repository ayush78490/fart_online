using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>Arm rotating about its local Y axis (a sweeper across the water). Optional reversal for a wiper.</summary>
    public class RotatingSweeper : KinematicObstacle
    {
        [Tooltip("Degrees per second (sign = direction)")] public float degreesPerSecond = 45f;
        [Tooltip("0 = full rotation; > 0 = wipe back and forth over this arc (degrees)")] [Range(0f, 300f)] public float wipeArc = 0f;
        [Tooltip("Arm reach from the pivot (gizmo only)")] public float reach = 5f;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localOffset = Vector3.zero;
            float angle;
            if (wipeArc <= 0f) angle = t * degreesPerSecond;
            else
            {
                float periodS = 2f * wipeArc / Mathf.Max(1f, Mathf.Abs(degreesPerSecond));
                angle = wipeArc * 0.5f * Mathf.Sin(t * Mathf.PI * 2f / periodS);
            }
            localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.1f);
            var m = StartMatrix();
            int n = 48;
            float arc = wipeArc <= 0f ? 360f : wipeArc;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= n; i++)
            {
                float a = -arc * 0.5f + arc * i / n;
                Vector3 p = m.MultiplyPoint(Quaternion.Euler(0, a, 0) * (Vector3.forward * reach));
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
