using UnityEngine;
using UnityEngine.InputSystem;
using FartOnline.Water;
using FartOnline.Obstacles;

namespace FartOnline.Environment
{
    /// <summary>
    /// Temporary shared raft for testing the course (stand-in for the real multiplayer raft controller).
    /// Keyboard: W/S paddle forward/back, A/D turn, Q/E strafe. Gamepad: left stick paddle/turn.
    /// Respawns at the last checkpoint when it falls below the respawn height or leaves the river for too long.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(RaftBuoyancy))]
    public class TestRaft : MonoBehaviour, ICourseRaft
    {
        [Header("Paddling")]
        public float paddleAcceleration = 4.5f;
        public float strafeAcceleration = 2.5f;
        public float turnAcceleration = 2.2f;
        [Tooltip("Paddling only works while floating")] public bool requireWater = true;

        [Header("Recovery")]
        public float respawnBelowY = -30f;
        [Tooltip("Seconds out of the water (not airborne over a jump) before respawning")] public float outOfWaterRespawn = 6f;
        public Transform respawnPoint;

        Rigidbody rb;
        RaftBuoyancy buoy;
        float dryTime;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            buoy = GetComponent<RaftBuoyancy>();
        }

        public void SetCheckpoint(Transform t) => respawnPoint = t;

        public bool IsAuthoritative => true;
        public void ReachCheckpoint(Checkpoint checkpoint, Transform respawn) => SetCheckpoint(respawn);
        public void Wipeout(string reason) => Respawn();

        public void Respawn()
        {
            Transform t = respawnPoint;
            if (t == null) return;
            rb.position = t.position + Vector3.up * 0.6f;
            rb.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up), Vector3.up);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            dryTime = 0f;
        }

        void FixedUpdate()
        {
            Vector2 move = Vector2.zero;
            float strafe = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                move.y += (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                move.x += (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                strafe += (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                move += gp.leftStick.ReadValue();
                strafe += gp.rightStick.ReadValue().x;
            }
            move = Vector2.ClampMagnitude(move, 1f);
            bool canPaddle = !requireWater || buoy.InWater;
            if (canPaddle)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                rb.AddForce((fwd * move.y * paddleAcceleration + right * strafe * strafeAcceleration) * buoy.SubmergedFraction, ForceMode.Acceleration);
                rb.AddTorque(Vector3.up * move.x * turnAcceleration * buoy.SubmergedFraction, ForceMode.Acceleration);
            }

            // recovery
            bool airborne = rb.linearVelocity.y < -0.5f || rb.linearVelocity.y > 0.5f;
            dryTime = buoy.InWater || airborne ? 0f : dryTime + Time.fixedDeltaTime;
            if (rb.position.y < respawnBelowY || dryTime > outOfWaterRespawn) Respawn();
        }
    }
}
