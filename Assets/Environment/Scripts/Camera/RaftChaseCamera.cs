using UnityEngine;
using FartOnline.Environment;

namespace FartOnline.CameraRig
{
    /// <summary>
    /// Elevated chase camera for the raft: follows behind along the river direction (not the raft's spin), looks
    /// ahead down the river so upcoming hazards stay in view, damps smoothly and pulls in when a canyon wall would
    /// block the view. Lightweight alternative to Cinemachine (not installed in this project).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RaftChaseCamera : MonoBehaviour
    {
        public Transform target;
        public RiverPath river;
        [Header("Framing")]
        [Range(30f, 90f)] public float fieldOfView = 60f;
        public float followDistance = 11f;
        public float height = 6.5f;
        [Tooltip("How far down the river the camera looks (m)")] public float lookAhead = 14f;
        [Tooltip("Vertical offset of the look target above the raft (m)")] public float lookHeight = 1.2f;
        [Range(0f, 1f), Tooltip("0 = follow river direction, 1 = follow raft heading")] public float headingInfluence = 0.25f;
        [Header("Damping")]
        public float positionDamping = 0.35f;
        public float lookDamping = 0.2f;
        [Header("Collision")]
        public float collisionRadius = 0.6f;
        public LayerMask collisionMask = ~0;
        public float minDistance = 3f;

        [Header("Tall screens (phones in portrait)")]
        [Tooltip("Vertical field of view used at the narrowest aspect (portrait phone)")] public float portraitFieldOfView = 80f;
        [Tooltip("Extra follow distance and height at the narrowest aspect")] public float portraitPullBack = 0.45f;
        /// <summary>Tests: frame for this aspect instead of the camera's own.</summary>
        public static float? AspectOverride;

        Camera cam;
        Vector3 vel, lookVel, look;

        /// <summary>0 on landscape screens, 1 on a tall portrait phone (aspect 0.45).</summary>
        float Tallness => 1f - Mathf.InverseLerp(0.45f, 1.3f, AspectOverride ?? (cam != null ? cam.aspect : 1.78f));

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (river == null) river = FindAnyObjectByType<RiverPath>();
        }

        void OnEnable() { if (target != null) SnapToTarget(); }

        public void SnapToTarget()
        {
            ComputeDesired(out Vector3 pos, out Vector3 lk);
            transform.position = pos;
            look = lk;
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            vel = lookVel = Vector3.zero;
        }

        void ComputeDesired(out Vector3 pos, out Vector3 lookTarget)
        {
            Vector3 tp = target.position;
            Vector3 dir = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            Vector3 ahead = tp + dir * lookAhead;
            if (river != null && river.points.Count > 1)
            {
                var s = river.Closest(tp, out _);
                Vector3 flow = Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized;
                dir = Vector3.Slerp(flow, dir, headingInfluence).normalized;
                var a = river.SampleAtDistance(s.distance + lookAhead);
                ahead = Vector3.Lerp(tp + dir * lookAhead, a.position, 0.6f);
            }
            lookTarget = Vector3.Lerp(tp, ahead, 0.5f) + Vector3.up * lookHeight;
            float pull = 1f + portraitPullBack * Tallness;
            pos = tp - dir * followDistance * pull + Vector3.up * height * pull;
            // keep the camera out of the canyon walls
            Vector3 from = tp + Vector3.up * 1.5f;
            Vector3 to = pos - from;
            if (Physics.SphereCast(from, collisionRadius, to.normalized, out RaycastHit hit, to.magnitude, collisionMask, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(target))
                pos = from + to.normalized * Mathf.Max(minDistance, hit.distance - collisionRadius);
        }

        void LateUpdate()
        {
            if (target == null) return;
            cam.fieldOfView = Mathf.Lerp(fieldOfView, portraitFieldOfView, Tallness);
            ComputeDesired(out Vector3 pos, out Vector3 lk);
            transform.position = Vector3.SmoothDamp(transform.position, pos, ref vel, positionDamping);
            look = Vector3.SmoothDamp(look, lk, ref lookVel, lookDamping);
            transform.rotation = Quaternion.LookRotation(look - transform.position, Vector3.up);
        }
    }
}
