using UnityEngine;

namespace FartOnline.Game.Endless
{
    /// <summary>
    /// Whirlpool: once the raft's rim crosses the swirl (radius <see cref="radius"/>, scaled with the object) the water
    /// drags it toward the spiked grinder in the middle and swings it round. The pull grows toward the core; at full
    /// strength it is weaker than one player's sideways fart (about 3.9 m/s²), so a crew that reacts can escape and a drifting
    /// raft is eaten. Applied by the physics authority only (host / offline), like every other force on the raft.
    /// The planner's footprint is the whole swirl, so the guaranteed safe path never feels the pull.
    /// </summary>
    public class WhirlpoolPull : MonoBehaviour
    {
        [Tooltip("Swirl radius before scaling (m)")] public float radius = 3.5f;
        [Tooltip("Radius of the deadly grinder before scaling (m)")] public float core = 1.0f;
        [Tooltip("Pull toward the core at the grinder's edge (m/s²)")] public float strength = 2.7f;
        [Tooltip("Sideways swirl at the grinder's edge (m/s²)")] public float swirl = 1.4f;
        [Tooltip("+1 = counter-clockwise seen from above")] public float direction = 1f;

        static SharedRaftController raft;

        public bool Pulling { get; private set; }

        void FixedUpdate()
        {
            if (raft == null) { raft = FindAnyObjectByType<SharedRaftController>(); if (raft == null) return; }
            Pulling = false;
            if (!raft.IsAuthority || !raft.thrustEnabled || raft.Body.isKinematic) return;
            float scale = transform.lossyScale.x;
            float rimR = raft.tuning != null ? raft.tuning.raftRadius : 2.4f;
            Vector3 d = transform.position - raft.Body.worldCenterOfMass;
            d.y = 0f;
            float dist = d.magnitude;
            float edge = dist - rimR;                       // gap between the raft's rim and the whirlpool centre
            float r = radius * scale, c = core * scale;
            if (edge >= r || dist < 0.01f) return;
            float k = Mathf.Clamp01(1f - (edge - c) / Mathf.Max(0.1f, r - c));
            Vector3 inward = d / dist;
            Vector3 tangent = Vector3.Cross(Vector3.up, inward) * direction;
            raft.Body.AddForce((inward * strength + tangent * swirl) * k, ForceMode.Acceleration);
            raft.Body.AddTorque(Vector3.up * direction * 0.6f * k, ForceMode.Acceleration);
            Pulling = true;
        }
    }
}
