using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>Spins about a local axis at a constant rate (spiked bank rollers, spiked stars, windmill blades).</summary>
    public class SpinObstacle : KinematicObstacle
    {
        [Tooltip("Local spin axis")] public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 90f;

        protected override void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation)
        {
            localOffset = Vector3.zero;
            localRotation = Quaternion.AngleAxis(t * degreesPerSecond, axis.normalized);
        }
    }
}
