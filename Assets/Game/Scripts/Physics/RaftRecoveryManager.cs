using System;
using System.Collections;
using UnityEngine;
using FartOnline.Obstacles;
using FartOnline.Game.Endless;

namespace FartOnline.Game
{
    /// <summary>
    /// Keeps the shared raft from ever being permanently stuck. Light collisions only raise <see cref="Impact"/>
    /// (splash, reactions) and leave the raft under player control. Severe states start a wipeout:
    /// capsized beyond <see cref="RaftTuning.capsizeAngle"/>, stranded out of the water, fallen below the course,
    /// or entering a HazardZone. A wipeout disables thrust, waits <see cref="RaftTuning.wipeoutDelay"/>, then
    /// respawns at the last checkpoint with zero velocity and an upright raft. If the raft is merely stuck
    /// (players thrusting, no progress), a recovery prompt is offered instead of an automatic reset.
    /// Endless game (<see cref="endlessMode"/>): there are no checkpoints. Touching an <see cref="EndlessHazard"/> or
    /// any severe wipeout raises <see cref="Crashed"/> instead, which ends the run (game over).
    /// Simulated on the authority only.
    /// </summary>
    [RequireComponent(typeof(SharedRaftController))]
    public class RaftRecoveryManager : MonoBehaviour, ICourseRaft
    {
        public Transform respawnPoint;
        public int checkpointIndex = -1;
        [Tooltip("Endless game: obstacles and wipeouts end the run instead of respawning")] public bool endlessMode;

        public bool InWipeout { get; private set; }
        public bool StuckPrompt { get; private set; }
        public int WipeoutCount { get; private set; }
        public string LastWipeoutReason { get; private set; } = "";
        public bool IsCrashed { get; private set; }

        /// <summary>Authority: a severe wipeout started (reason).</summary>
        public event Action<string> WipedOut;
        /// <summary>Authority: raft placed back at a checkpoint.</summary>
        public event Action Respawned;
        /// <summary>Authority: collision (strength 0..1, world point, heavy?).</summary>
        public event Action<float, Vector3, bool> Impact;
        public event Action<Checkpoint> CheckpointReached;
        /// <summary>Authority, endless game: the run is over (what the raft hit).</summary>
        public event Action<string> Crashed;

        SharedRaftController raft;
        float tiltTime, dryTime, stuckTime, lastProgressCheck, lastProgressDistance;
        float lastImpactTime;

        public bool IsAuthoritative => raft == null || raft.IsAuthority;

        void Awake() => raft = GetComponent<SharedRaftController>();

        public void ReachCheckpoint(Checkpoint checkpoint, Transform respawn)
        {
            if (InWipeout) return;
            if (checkpoint.index >= checkpointIndex)
            {
                checkpointIndex = checkpoint.index;
                respawnPoint = respawn;
            }
            CheckpointReached?.Invoke(checkpoint);
        }

        public void Wipeout(string reason)
        {
            if (endlessMode) { Crash(reason == "capsized" ? "a capsize" : reason == "fell" ? "a fall" : reason == "stranded" ? "the riverbank" : reason); return; }
            if (InWipeout || !IsAuthoritative) return;
            InWipeout = true;
            WipeoutCount++;
            LastWipeoutReason = reason;
            StuckPrompt = false;
            WipedOut?.Invoke(reason);
            StartCoroutine(RespawnAfterDelay());
        }

        /// <summary>Endless game: ends the run once (authority).</summary>
        public void Crash(string what)
        {
            if (IsCrashed || !IsAuthoritative) return;
            IsCrashed = true;
            StuckPrompt = false;
            LastWipeoutReason = what;
            Crashed?.Invoke(what);
        }

        /// <summary>Player-requested reset to the last checkpoint (stuck prompt / pause menu). Counts as a wipeout.</summary>
        public void RequestRecovery() => Wipeout("player reset");

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(raft.tuning.wipeoutDelay);
            RespawnNow();
        }

        public void RespawnNow()
        {
            StopAllCoroutines();
            if (respawnPoint != null)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(respawnPoint.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                raft.Teleport(respawnPoint.position + Vector3.up * 0.6f, Quaternion.LookRotation(fwd, Vector3.up));
            }
            foreach (var s in raft.seats.Seats) s.Thrust.ResetSeat();
            InWipeout = false;
            IsCrashed = false;
            tiltTime = dryTime = stuckTime = 0f;
            StuckPrompt = false;
            lastProgressCheck = Time.time;
            lastProgressDistance = ProgressDistance();
            Respawned?.Invoke();
        }

        /// <summary>Full restart: back to the first respawn point with counters cleared (GameRunManager).</summary>
        public void ResetForNewRun(Transform start)
        {
            respawnPoint = start;
            checkpointIndex = -1;
            WipeoutCount = 0;
            RespawnNow();
        }

        float ProgressDistance()
        {
            var river = raft.River;
            if (river == null) return 0f;
            return river.Closest(transform.position, out _).distance;
        }

        void FixedUpdate()
        {
            if (!IsAuthoritative || InWipeout || IsCrashed) return;
            var t = raft.tuning;
            float dt = Time.fixedDeltaTime;
            Vector3 v = raft.Body.linearVelocity;

            // capsized
            tiltTime = Vector3.Angle(transform.up, Vector3.up) > t.capsizeAngle ? tiltTime + dt : 0f;
            if (tiltTime > t.flipSeconds) { Wipeout("capsized"); return; }

            // stranded on a bank (airborne over the jump does not count)
            bool airborne = Mathf.Abs(v.y) > 0.5f;
            dryTime = raft.Buoyancy.InWater || airborne ? 0f : dryTime + dt;
            if (dryTime > t.strandedSeconds) { Wipeout("stranded"); return; }

            if (transform.position.y < t.respawnBelowY) { Wipeout("fell"); return; }

            // stuck: thrusting but no progress along the river -> offer a reset (never forced)
            if (!endlessMode && Time.time - lastProgressCheck > 1f)
            {
                float d = ProgressDistance();
                bool progressing = Mathf.Abs(d - lastProgressDistance) > 0.6f;
                bool trying = raft.ActiveThrusters > 0 || raft.LastTotalImpulse != Vector3.zero;
                stuckTime = progressing ? 0f : (trying || v.magnitude < 0.4f ? stuckTime + (Time.time - lastProgressCheck) : stuckTime);
                lastProgressDistance = d;
                lastProgressCheck = Time.time;
                StuckPrompt = stuckTime > t.stuckSeconds;
            }
        }

        /// <summary>Some pack traps use trigger colliders (the press head): touching one ends an endless run too.</summary>
        void OnTriggerEnter(Collider other)
        {
            if (!IsAuthoritative || !endlessMode || !raft.thrustEnabled || IsCrashed) return;
            var hazard = other.GetComponentInParent<EndlessHazard>();
            if (hazard == null) return;
            Impact?.Invoke(1f, other.ClosestPoint(transform.position), true);
            Crash(hazard.label);
        }

        void OnCollisionEnter(Collision c)
        {
            if (!IsAuthoritative) return;
            if (endlessMode && raft.thrustEnabled && !IsCrashed)
            {
                var hazard = c.collider.GetComponentInParent<EndlessHazard>();
                if (hazard != null)
                {
                    Vector3 hp = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
                    Impact?.Invoke(1f, hp, true);
                    lastImpactTime = Time.time;
                    Crash(hazard.label);
                    return;
                }
            }
            if (Time.time - lastImpactTime < 0.15f) return;
            float j = c.impulse.magnitude;
            if (j < 60f) return;
            lastImpactTime = Time.time;
            bool heavy = j > raft.tuning.heavyImpactImpulse;
            Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            Impact?.Invoke(Mathf.Clamp01(j / (raft.tuning.heavyImpactImpulse * 1.5f)), p, heavy);
        }
    }
}
