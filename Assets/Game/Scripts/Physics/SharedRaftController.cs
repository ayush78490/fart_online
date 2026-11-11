using System;
using UnityEngine;
using FartOnline.Environment;
using FartOnline.Water;

namespace FartOnline.Game
{
    /// <summary>
    /// The single authoritative physics owner of the round shared raft. Every physics step it moves each occupied seat
    /// along the rim from its A/D input, then asks the seat's
    /// <see cref="FartThrustController"/> for its force, sums them and applies the combined result once:
    ///   F_total = Σ F_i,   τ_total = Σ (r_i × F_i)   with r_i = thrust origin − centre of mass.
    /// Then it adds bounded stabilisation, the speed limit and the optional solo assist. On network clients
    /// <see cref="authority"/> returns false and this component does nothing (the raft is driven by replication).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(RaftBuoyancy))]
    [DefaultExecutionOrder(-50)]
    public class SharedRaftController : MonoBehaviour
    {
        public RaftTuning tuning;
        public RaftSeatManager seats;
        public SoloAssist assist = SoloAssist.Standard;
        [Tooltip("Thrust is accepted only while the run is in a playable state (set by GameRunManager)")]
        public bool thrustEnabled = true;
        [Tooltip("Draw force/torque vectors in the scene view")] public bool debugGizmos = true;

        /// <summary>Returns true where this instance simulates the raft (offline: always; online: server only).</summary>
        public Func<bool> authority;
        public bool IsAuthority => authority == null || authority();

        public Rigidbody Body { get; private set; }
        public RaftBuoyancy Buoyancy { get; private set; }
        public RiverPath River => Buoyancy != null ? Buoyancy.river : null;
        public Vector3 LastTotalForce { get; private set; }
        public Vector3 LastTotalTorque { get; private set; }
        public Vector3 LastTotalImpulse { get; private set; }
        public int ActiveThrusters { get; private set; }
        /// <summary>Velocity relative to the water (what the speed limit acts on).</summary>
        public Vector3 RelativeVelocity => Body.linearVelocity - (Buoyancy.InWater ? Buoyancy.CurrentVelocity : Vector3.zero);

        /// <summary>Raised on the authority for every burst of any seat (relayed to clients by the network layer).</summary>
        public event Action<ThrustEvent> AnyBurst;

        Vector3 prevAngular, prevLinear;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Buoyancy = GetComponent<RaftBuoyancy>();
            if (seats == null) seats = GetComponent<RaftSeatManager>();
            ApplyTuning();
            foreach (var s in seats.Seats) s.Thrust.Burst += e => { if (IsAuthority) AnyBurst?.Invoke(e); };
        }

        public void ApplyTuning()
        {
            if (tuning == null) return;
            Body.mass = tuning.mass;
            Body.centerOfMass = tuning.centerOfMass;
            Body.linearDamping = tuning.linearDrag;
            Body.angularDamping = tuning.angularDrag;
            Body.maxAngularVelocity = tuning.maxAngularVelocity;
            Buoyancy.buoyancy = tuning.buoyancy;
            seats.tuning = tuning;
            foreach (var s in seats.Seats)
            {
                s.Thrust.tuning = tuning;
                s.Thrust.Resource.tuning = tuning;
                s.Thrust.Resource.regenMultiplier = RegenMultiplier;
            }
        }

        public float RegenMultiplier =>
            !IsSolo || assist == SoloAssist.Off ? 1f : assist == SoloAssist.Standard ? tuning.standardRegenMultiplier : tuning.highRegenMultiplier;

        bool IsSolo => seats.OccupiedCount <= 1;
        int AssistIndex => IsSolo && assist != SoloAssist.Off ? (int)assist : 0;

        public void SetAssist(SoloAssist a)
        {
            assist = a;
            foreach (var s in seats.Seats) s.Thrust.Resource.regenMultiplier = RegenMultiplier;
        }

        Vector3 smoothForce, smoothTorque, pendingImpulse, pendingTorque;

        void FixedUpdate()
        {
            if (!IsAuthority || tuning == null) return;
            float dt = Time.fixedDeltaTime;
            float env = Buoyancy.InWater ? 1f : tuning.airThrustFactor;
            Vector3 com = Body.worldCenterOfMass;
            Vector3 v = Body.linearVelocity;
            Vector3 F = Vector3.zero, T = Vector3.zero, J = Vector3.zero, TJ = Vector3.zero;
            int active = 0;

            foreach (var seat in seats.Seats)
            {
                if (!seat.IsOccupied) continue;
                var input = seat.ReadInput();
                // walk along the rim (A = character's left = clockwise angle increase, D = right)
                float move = Mathf.Abs(input.move) >= tuning.inputDeadzone ? Mathf.Clamp(input.move, -1f, 1f) : 0f;
                if (!thrustEnabled) move = 0f;
                float speed = tuning.seatMoveSpeed * (seat.Thrust.State.sustaining ? tuning.moveWhileFarting : 1f)
                              * (AssistIndex == 2 ? tuning.highAssistMoveSpeed : 1f);
                seats.MoveSeat(seat, -move * speed * dt);
                seat.Moving = move;
                seat.Thrust.Simulate(input, seat.Angle, move, transform, v, dt, thrustEnabled, env, out Vector3 f, out Vector3 j);
                Vector3 r = seat.Thrust.OriginWorld - com;
                if (f != Vector3.zero) { F += f; T += Vector3.Cross(r, f); active++; }
                if (j != Vector3.zero) { J += j; TJ += Vector3.Cross(r, j); active++; }
            }
            // combined sustained force is bounded; scaling force and torque together keeps the r × F relationship
            float fm = F.magnitude;
            if (fm > tuning.maxCombinedForce) { float k = tuning.maxCombinedForce / fm; F *= k; T *= k; }
            // smooth gas: the stream swells in and fades out; bursts are released from a pressure reservoir over a
            // fraction of a second (same total impulse, no single-frame kick), so the raft accelerates and glides softly
            bool rising = F.sqrMagnitude >= smoothForce.sqrMagnitude;
            float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.005f, rising ? tuning.thrustRiseTime : tuning.thrustFallTime));
            smoothForce += (F - smoothForce) * a;
            smoothTorque += (T - smoothTorque) * a;
            if (smoothForce.sqrMagnitude < 1f && F == Vector3.zero) { smoothForce = Vector3.zero; smoothTorque = Vector3.zero; }
            pendingImpulse += J;
            pendingTorque += TJ;
            float b = 1f - Mathf.Exp(-dt / Mathf.Max(0.005f, tuning.burstReleaseTime));
            Vector3 release = pendingImpulse * b, releaseT = pendingTorque * b;
            pendingImpulse -= release;
            pendingTorque -= releaseT;
            if (pendingImpulse.sqrMagnitude < 0.01f) { release += pendingImpulse; releaseT += pendingTorque; pendingImpulse = pendingTorque = Vector3.zero; }
            if (!thrustEnabled) { smoothForce = smoothTorque = pendingImpulse = pendingTorque = Vector3.zero; release = releaseT = Vector3.zero; }
            if (smoothForce != Vector3.zero) { Body.AddForce(smoothForce, ForceMode.Force); Body.AddTorque(smoothTorque, ForceMode.Force); }
            if (release != Vector3.zero) { Body.AddForce(release, ForceMode.Impulse); Body.AddTorque(releaseT, ForceMode.Impulse); }
            LastTotalForce = F;
            LastTotalTorque = T;
            LastTotalImpulse = J;
            ActiveThrusters = active;

            Stabilise(active);
            LimitSpeed();
            prevAngular = Body.angularVelocity;
            prevLinear = Body.linearVelocity;
        }

        void Stabilise(int active)
        {
            int a = AssistIndex;
            float upright = tuning.uprightStrength * (a == 0 ? 1f : a == 1 ? tuning.assistUpright.x : tuning.assistUpright.y);
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            if (tilt > tuning.softTiltLimit) upright *= 1f + (tilt - tuning.softTiltLimit) / 20f;
            float medium = Buoyancy.InWater ? 1f : 0.3f;
            Vector3 axis = Vector3.Cross(transform.up, Vector3.up);          // |axis| = sin(tilt)
            Vector3 tiltRate = Vector3.ProjectOnPlane(Body.angularVelocity, Vector3.up);
            Body.AddTorque((axis * upright - tiltRate * tuning.uprightDamping) * medium, ForceMode.Acceleration);

            if (a == 0 || !Buoyancy.InWater) return;
            float yawRate = Vector3.Dot(Body.angularVelocity, Vector3.up);
            if (active == 0)
            {
                float d = a == 1 ? tuning.assistYawDamping.x : tuning.assistYawDamping.y;
                Body.AddTorque(-Vector3.up * yawRate * d, ForceMode.Acceleration);
            }
        }

        void LimitSpeed()
        {
            Vector3 rel = Vector3.ProjectOnPlane(RelativeVelocity, Vector3.up);
            float over = rel.magnitude - tuning.maxSpeed;
            if (over > 0f) Body.AddForce(-rel.normalized * over * 4f, ForceMode.Acceleration);
        }

        /// <summary>Solo assist: soften minor wall scrapes by giving back part of the lost speed and the added spin.</summary>
        void OnCollisionEnter(Collision c)
        {
            if (!IsAuthority || AssistIndex == 0) return;
            if (c.rigidbody != null && !c.rigidbody.isKinematic) return;            // only static walls / rocks
            if (c.impulse.magnitude > tuning.heavyImpactImpulse) return;            // real crashes stay real
            float k = AssistIndex == 1 ? tuning.assistScrapeReduction.x : tuning.assistScrapeReduction.y;
            Body.angularVelocity = Vector3.Lerp(Body.angularVelocity, prevAngular, k);
            Vector3 lv = Body.linearVelocity;
            float lost = prevLinear.magnitude - lv.magnitude;
            if (lost > 0f && lv.sqrMagnitude > 0.01f) Body.linearVelocity = lv.normalized * (lv.magnitude + lost * k);
        }

        /// <summary>Teleport with zero velocity (respawn / restart). Authority only.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            prevAngular = prevLinear = Vector3.zero;
            smoothForce = smoothTorque = pendingImpulse = pendingTorque = Vector3.zero;
            Physics.SyncTransforms();
        }

        void OnDrawGizmos()
        {
            if (!debugGizmos || !Application.isPlaying || Body == null) return;
            Vector3 c = Body.worldCenterOfMass;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(c, c + LastTotalForce / 500f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(c, c + LastTotalTorque / 500f);
        }
    }
}
