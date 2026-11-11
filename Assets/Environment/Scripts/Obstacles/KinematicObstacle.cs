using UnityEngine;

namespace FartOnline.Obstacles
{
    /// <summary>
    /// Base for deterministic moving obstacles: a kinematic Rigidbody driven purely by time (repeatable, never
    /// knocked off its path), so it pushes the raft physically but always moves the same way.
    /// Subclasses implement Evaluate(t) returning the local pose relative to the start pose.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public abstract class KinematicObstacle : MonoBehaviour
    {
        [Tooltip("Seconds added to the clock: offsets this obstacle against its neighbours")] public float phaseOffset;
        [Tooltip("Global speed multiplier")] [Range(0.1f, 3f)] public float speed = 1f;

        [Header("Audio hook (optional)")]
        [Tooltip("Optional looping/one-shot source for movement sounds; the scene works silently without it")]
        public AudioSource movementAudio;

        protected Rigidbody rb;
        protected Vector3 startPos;
        protected Quaternion startRot;
        bool hasStart;

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            CaptureStart();
        }

        void CaptureStart()
        {
            if (hasStart) return;
            startPos = transform.localPosition;
            startRot = transform.localRotation;
            hasStart = true;
        }

        /// <summary>Optional shared clock (seconds). The endless game sets it to the run time so every moving
        /// obstacle is in the same phase on the host and on network clients; null = Time.time.</summary>
        public static System.Func<float> ClockSource;

        protected float Clock => (Application.isPlaying ? (ClockSource != null ? ClockSource() : Time.time) : 0f) * speed + phaseOffset;

        /// <summary>Local pose (relative to the start pose) at time t.</summary>
        protected abstract void Evaluate(float t, out Vector3 localOffset, out Quaternion localRotation);

        void FixedUpdate()
        {
            Evaluate(Clock, out Vector3 off, out Quaternion r);
            Transform parent = transform.parent;
            Vector3 lp = startPos + startRot * off;
            Quaternion lr = startRot * r;
            Vector3 wp = parent != null ? parent.TransformPoint(lp) : lp;
            Quaternion wr = parent != null ? parent.rotation * lr : lr;
            rb.MovePosition(wp);
            rb.MoveRotation(wr);
            Animate(Clock);
        }

        /// <summary>Extra animation besides the pose (scale pulses, bones); called every physics step and by ApplyPoseNow.</summary>
        protected virtual void Animate(float t) { }

        /// <summary>Tests: puts the obstacle at its pose for the current clock immediately (no physics step).</summary>
        public void ApplyPoseNow()
        {
            Evaluate(Clock, out Vector3 off, out Quaternion r);
            Transform parent = transform.parent;
            Vector3 lp = startPos + startRot * off;
            Quaternion lr = startRot * r;
            Vector3 wp = parent != null ? parent.TransformPoint(lp) : lp;
            Quaternion wr = parent != null ? parent.rotation * lr : lr;
            transform.SetPositionAndRotation(wp, wr);
            if (rb != null) { rb.position = wp; rb.rotation = wr; }
            Animate(Clock);
        }

        protected Matrix4x4 StartMatrix()
        {
            if (!Application.isPlaying) { startPos = transform.localPosition; startRot = transform.localRotation; }
            var parent = transform.parent;
            var local = Matrix4x4.TRS(startPos, startRot, Vector3.one);
            return parent != null ? parent.localToWorldMatrix * local : local;
        }
    }
}
