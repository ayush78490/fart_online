using UnityEngine;
using FartOnline.Environment;

namespace FartOnline.Water
{
    /// <summary>
    /// Lightweight multi-point buoyancy against the RiverPath water surface, plus drag towards the local current.
    /// Deterministic and cheap: no fluid simulation, one closest-sample query per float point.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RaftBuoyancy : MonoBehaviour
    {
        public RiverPath river;
        [Tooltip("Local-space float points (corners + centre of the deck underside)")]
        public Vector3[] floatPoints =
        {
            new Vector3(-1.8f, -0.2f, -1.8f), new Vector3(1.8f, -0.2f, -1.8f),
            new Vector3(-1.8f, -0.2f, 1.8f), new Vector3(1.8f, -0.2f, 1.8f), new Vector3(0f, -0.2f, 0f)
        };
        [Tooltip("Upward acceleration per metre of submersion, per point (m/s^2)")] public float buoyancy = 18f;
        [Tooltip("Maximum submersion considered (m)")] public float maxDepth = 0.8f;
        [Tooltip("Vertical damping while floating")] public float verticalDamping = 2.5f;
        [Tooltip("How strongly the raft is dragged towards the current velocity (1/s)")] public float currentDrag = 0.9f;
        [Tooltip("Fast water grips harder: extra drag per m/s of flow above fastWaterFrom (chutes carry the raft to the jump)")] public float fastWaterGrip = 0.3f;
        public float fastWaterFrom = 4f;
        [Tooltip("Angular damping while floating (1/s)")] public float waterAngularDrag = 1.6f;

        public bool InWater { get; private set; }
        public float SubmergedFraction { get; private set; }
        public Vector3 CurrentVelocity { get; private set; }

        Rigidbody rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (river == null) river = FindAnyObjectByType<RiverPath>();
        }

        void FixedUpdate()
        {
            if (river == null) return;
            int wet = 0;
            Vector3 flowSum = Vector3.zero;
            float per = 1f / floatPoints.Length;
            foreach (var lp in floatPoints)
            {
                Vector3 wp = transform.TransformPoint(lp);
                if (!river.TryGetWater(wp, out float h, out Vector3 flow)) continue;
                float depth = h - wp.y;
                if (depth <= 0f) continue;
                wet++;
                flowSum += flow * WaterCurrentController.Strength;
                float d = Mathf.Min(depth, maxDepth);
                Vector3 v = rb.GetPointVelocity(wp);
                float up = buoyancy * d - verticalDamping * v.y;
                rb.AddForceAtPosition(Vector3.up * up * rb.mass * per, wp, ForceMode.Force);
            }
            SubmergedFraction = wet * per;
            InWater = wet > 0;
            if (!InWater) { CurrentVelocity = Vector3.zero; return; }
            CurrentVelocity = flowSum / wet;
            // horizontal drag towards the current: the river carries the raft, paddling acts against/along it
            Vector3 rel = CurrentVelocity - rb.linearVelocity;
            rel.y = Mathf.Min(rel.y, 0f) * 0.2f;                            // chutes may pull down, never lift
            float grip = currentDrag * (1f + Mathf.Max(0f, CurrentVelocity.magnitude - fastWaterFrom) * fastWaterGrip);
            rb.AddForce(rel * grip * SubmergedFraction, ForceMode.Acceleration);
            rb.AddTorque(-rb.angularVelocity * waterAngularDrag * SubmergedFraction, ForceMode.Acceleration);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            foreach (var lp in floatPoints) Gizmos.DrawWireSphere(transform.TransformPoint(lp), 0.15f);
        }
    }
}
