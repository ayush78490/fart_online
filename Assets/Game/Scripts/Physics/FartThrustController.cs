using System;
using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Turns one player's <see cref="PlayerInputState"/> into physical thrust at that player's seat on the round raft.
    /// The player sits on the rim facing the centre, so the gas leaves outward behind them and the raft is pushed
    /// from the seat toward the centre: F_i = D_i × T_i × S_i with D_i = (centre − seat) / |centre − seat| in raft
    /// space, T_i = <see cref="RaftTuning.thrust"/> and S_i the intensity. Moving along the rim (A/D) changes D_i,
    /// which is how players steer. The force is applied by <see cref="SharedRaftController"/> at the thrust origin,
    /// so torque r × F is computed physically as well (small on a round raft: the push line passes near the centre).
    /// F press = initial puff (one impulse); F held = sustained stream (ForceMode.Force) after a short delay;
    /// ChargeFart release = charged impulse; Brake = impulse against the raft's velocity.
    /// Only the authority calls <see cref="Simulate"/>; everyone receives <see cref="State"/> and <see cref="Burst"/>.
    /// </summary>
    [RequireComponent(typeof(FartResourceController))]
    public class FartThrustController : MonoBehaviour
    {
        public int seatIndex;
        [Tooltip("Where the gas leaves the character; force is applied here")] public Transform thrustOrigin;
        public RaftTuning tuning;

        /// <summary>Raised on the authority for every discrete burst and for the start of a sustained stream.</summary>
        public event Action<ThrustEvent> Burst;

        public FartResourceController Resource => resource != null ? resource : (resource = GetComponent<FartResourceController>());
        FartResourceController resource;
        public SeatThrustState State { get; private set; }
        public int Activations { get; private set; }
        public float ThrustSeconds { get; private set; }

        PlayerInputState prev;
        bool hasPrev;
        float sustainTime, recoveryUntil, lastBurst = -10f, charge, lastCooldownCue = -10f, pressTime = -10f;
        bool sustainingLast;

        public void ResetSeat()
        {
            hasPrev = false;
            sustainTime = 0f;
            recoveryUntil = 0f;
            charge = 0f;
            sustainingLast = false;
            Resource.ResetTank();
            var s = State;
            State = new SeatThrustState { angle = s.angle, gas01 = 1f };
        }

        /// <summary>Clients: apply the replicated state (no physics).</summary>
        public void ApplyReplicatedState(SeatThrustState s)
        {
            State = s;
            Resource.SetReplicated(s.gas01, s.exhausted);
        }

        /// <summary>Clients: replay a burst received from the authority.</summary>
        public void RaiseReplicatedBurst(ThrustEvent e) => Burst?.Invoke(e);

        public Vector3 OriginWorld => thrustOrigin != null ? thrustOrigin.position : transform.position;

        /// <summary>
        /// Authority step for a seat at rim angle <paramref name="angle"/>. Returns the sustained force (world, N) and any
        /// burst impulse (world, N·s), both acting at <see cref="OriginWorld"/>. Never both from the same action.
        /// </summary>
        public void Simulate(PlayerInputState input, float angle, float moving, Transform raft, Vector3 raftVelocity, float dt,
                             bool enabledByRun, float environmentFactor, out Vector3 force, out Vector3 impulse)
        {
            force = Vector3.zero;
            impulse = Vector3.zero;
            if (!hasPrev) { prev = input; hasPrev = true; }   // never fire presses that happened before this seat existed

            Resource.Simulate(dt);
            bool fPressed = input.tapSeq != prev.tapSeq;
            bool chargeReleased = input.chargeSeq != prev.chargeSeq;
            bool brakePressed = input.brakeSeq != prev.brakeSeq;
            prev = input;

            Vector2 push = RimMath.Push(angle);                                   // raft space, toward the centre
            Vector3 pushWorld = raft.TransformDirection(new Vector3(push.x, 0f, push.y));

            if (!enabledByRun)
            {
                charge = 0f;
                sustainTime = 0f;
                sustainingLast = false;
                Publish(angle, moving, false, Vector2.zero);
                return;
            }

            if (input.charging && Resource.CanFart) charge = Mathf.Min(1f, charge + dt / Mathf.Max(0.05f, tuning.chargeTime));

            bool burstReady = Time.time - lastBurst >= tuning.burstCooldown;
            if (fPressed) pressTime = Time.time;
            if (fPressed || chargeReleased || brakePressed)
            {
                if (!Resource.CanFart) CooldownCue(push);
                else if (burstReady)
                {
                    if (chargeReleased && charge > 0.05f)
                    {
                        float c = Mathf.Max(0.15f, charge);
                        float paid = Resource.PayBurst(tuning.chargedCostMax * c);
                        impulse = pushWorld * Mathf.Lerp(tuning.chargedImpulseMin, tuning.chargedImpulseMax, c) * paid * environmentFactor;
                        FireBurst(ThrustKind.Charged, push, c * paid);
                    }
                    else if (brakePressed)
                    {
                        Vector3 local = raft.InverseTransformDirection(Vector3.ProjectOnPlane(raftVelocity, raft.up));
                        Vector2 against = new Vector2(-local.x, -local.z);
                        float speed = against.magnitude;
                        Vector2 bdir = speed > 0.3f ? against / speed : push;
                        float k = Mathf.Clamp(speed / 3f, 0.4f, 1f);
                        float paid = Resource.PayBurst(tuning.brakeCost);
                        impulse = raft.TransformDirection(new Vector3(bdir.x, 0f, bdir.y)) * tuning.brakeImpulse * k * paid * environmentFactor;
                        FireBurst(ThrustKind.Brake, bdir, k * paid);
                    }
                    else if (fPressed && !input.charging)
                    {
                        float paid = Resource.PayBurst(tuning.tapCost);
                        impulse = pushWorld * tuning.tapImpulse * paid * environmentFactor;
                        FireBurst(ThrustKind.Tap, push, 0.35f * paid);
                    }
                }
                if (chargeReleased) charge = 0f;
            }
            if (!input.charging && !chargeReleased) charge = 0f;

            // sustained stream while F stays held (after the initial puff)
            bool sustaining = false;
            Vector2 sustainDir = Vector2.zero;
            bool wantSustain = tuning.holdToFart && input.fartHeld && !input.charging && Time.time - pressTime >= tuning.sustainDelay;
            if (wantSustain && Time.time >= recoveryUntil && Resource.CanFart)
            {
                float paid = Resource.DrainSustained(dt, 1f);
                if (paid > 0f)
                {
                    sustaining = true;
                    sustainDir = push;
                    force = pushWorld * tuning.thrust * paid * environmentFactor;
                    sustainTime += dt;
                    ThrustSeconds += dt;
                    if (sustainTime >= tuning.maxBurstDuration) { recoveryUntil = Time.time + tuning.burstRecovery; sustainTime = 0f; }
                }
            }
            if (!input.fartHeld) sustainTime = 0f;
            if (wantSustain && !Resource.CanFart && !sustainingLast) CooldownCue(push);

            if (sustaining && !sustainingLast) FireBurst(ThrustKind.Sustained, push, 1f);
            sustainingLast = sustaining;
            Publish(angle, moving, sustaining, sustainDir);
        }

        void Publish(float angle, float moving, bool sustaining, Vector2 dir)
        {
            State = new SeatThrustState
            {
                angle = angle,
                moving = moving,
                sustaining = sustaining,
                direction = dir,
                gas01 = Resource.Gas,
                charge01 = charge,
                exhausted = Resource.Exhausted,
            };
        }

        void FireBurst(ThrustKind kind, Vector2 dir, float strength)
        {
            if (kind != ThrustKind.Sustained) lastBurst = Time.time;
            Activations++;
            Burst?.Invoke(new ThrustEvent { seat = seatIndex, kind = kind, direction = dir.normalized, strength01 = Mathf.Clamp01(strength) });
        }

        void CooldownCue(Vector2 dir)
        {
            if (Time.time - lastCooldownCue < 0.6f) return;
            lastCooldownCue = Time.time;
            Burst?.Invoke(new ThrustEvent { seat = seatIndex, kind = ThrustKind.Cooldown, direction = dir, strength01 = 0f });
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 1f, 0.2f);
            Gizmos.DrawWireSphere(OriginWorld, 0.12f);
        }
    }
}
