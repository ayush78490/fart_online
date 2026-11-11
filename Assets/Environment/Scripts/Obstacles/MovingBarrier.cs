using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>Barrier that slides between its start pose and a local offset, easing in/out and pausing at each end.</summary>
    public class MovingBarrier : KinematicObstacle
    {
        [Tooltip("Travel, in the barrier's local space (m)")] public Vector3 travel = new Vector3(6f, 0f, 0f);
        [Tooltip("Seconds to travel one way")] public float moveTime = 2.2f;
        [Tooltip("Seconds paused at each end")] public float pauseTime = 1.2f;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localRotation = Quaternion.identity;
            float cycle = 2f * (moveTime + pauseTime);
            float u = Mathf.Repeat(t, cycle);
            float f;
            if (u < pauseTime) f = 0f;
            else if (u < pauseTime + moveTime) f = Mathf.SmoothStep(0f, 1f, (u - pauseTime) / moveTime);
            else if (u < 2f * pauseTime + moveTime) f = 1f;
            else f = 1f - Mathf.SmoothStep(0f, 1f, (u - 2f * pauseTime - moveTime) / moveTime);
            localOffset = travel * f;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.1f);
            var m = StartMatrix();
            Vector3 a = m.MultiplyPoint(Vector3.zero), b = m.MultiplyPoint(travel);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, 0.3f);
            Gizmos.DrawWireSphere(b, 0.3f);
        }
    }
}
