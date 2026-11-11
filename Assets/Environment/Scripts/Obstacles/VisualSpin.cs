using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// Visual-only spin (no Rigidbody, no collider on this transform): spiked rollers turning on their axle while the
    /// parent obstacle swings or slides. Runs on the shared obstacle clock, so it looks the same on every machine.
    /// The collider stays on a non-spinning parent: a capsule around a roller is the same at any spin angle.
    /// </summary>
    public class VisualSpin : MonoBehaviour
    {
        public Vector3 axis = Vector3.right;
        public float degreesPerSecond = 220f;
        public float phaseOffset;
        Quaternion baseRot;

        void Awake() => baseRot = transform.localRotation;

        void Update()
        {
            float t = (KinematicObstacle.ClockSource != null ? KinematicObstacle.ClockSource() : Time.time) + phaseOffset;
            transform.localRotation = baseRot * Quaternion.AngleAxis(t * degreesPerSecond, axis.normalized);
        }
    }
}
