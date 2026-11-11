using UnityEngine;
using FartOnline.Environment;
using FartOnline.Game.Endless;

namespace FartOnline.Game
{
    /// <summary>
    /// TEST-ONLY bot driver for the editor probes and trailer recorder (see <c>TestCrew</c>). The game itself has no AI
    /// crew: only real players sit on the raft. Produces ordinary
    /// <see cref="PlayerInputState"/> through the same pipeline as humans: it walks along the rim of the round raft to
    /// the spot from which a fart pushes the raft where it wants to go, then farts, obeying the same gas tank, cooldowns
    /// and seat spacing, with the same VFX. It only perceives what a player could (the river ahead, the raft's motion,
    /// obstacles in a forward sphere cast), reacts with a delay, aims imperfectly and eases off in narrow passages.
    /// On the endless river it steers for the open lane between the obstacles ahead (the lane a player can see).
    /// </summary>
    public class AICrewInput : MonoBehaviour, IThrustInputSource
    {
        public SharedRaftController raft;
        public PlayerSeatController seat;
        [Range(0f, 1f), Tooltip("0 = sloppy and slow, 1 = sharp")] public float skill = 0.5f;
        public float lookAhead = 14f;
        public LayerMask obstacleMask = ~0;

        PlayerInputState state;
        float targetAngle = 180f, nextThink, wantPush, pulseUntil = -1f;
        bool brakeWanted;

        public void ReadyFor(SharedRaftController r, PlayerSeatController s, float aiSkill)
        {
            raft = r;
            seat = s;
            skill = aiSkill;
        }

        public PlayerInputState ReadInput() => state;

        static readonly System.Collections.Generic.List<AICrewInput> All = new System.Collections.Generic.List<AICrewInput>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>Evenly spread spot for this seat: occupied seats keep their order around the rim, starting from a
        /// human player's seat (or the first seat), 360 / n degrees apart.</summary>
        float HomeAngle()
        {
            var seats = raft.seats.Seats;
            PlayerSeatController anchor = null;
            int n = 0;
            foreach (var s in seats) if (s != null && s.IsOccupied) { n++; if (anchor == null && !s.IsAI) anchor = s; }
            if (anchor == null) foreach (var s in seats) if (s != null && s.IsOccupied) { anchor = s; break; }
            if (anchor == null || n == 0) return seat.Angle;
            float myRel = Mathf.Repeat(seat.Angle - anchor.Angle, 360f);
            int rank = 0;
            foreach (var s in seats)
                if (s != null && s.IsOccupied && s != anchor && Mathf.Repeat(s.Angle - anchor.Angle, 360f) < myRel) rank++;
            if (seat != anchor) rank++;
            return anchor.Angle + rank * 360f / n;
        }

        void Update()
        {
            if (raft == null || raft.River == null || seat == null) return;
            if (Time.time >= nextThink)
            {
                float reaction = EndlessRiver.Instance != null ? Mathf.Lerp(0.3f, 0.12f, skill) : Mathf.Lerp(0.55f, 0.2f, skill);
                nextThink = Time.time + reaction * Random.Range(0.8f, 1.2f);   // reaction delay
                Think();
            }
            // crew coordination (players cannot pass each other on the rim): the bot nearest the spot that pushes the
            // way the raft needs walks there; the others go to evenly spread "home" spots so someone is always close to
            // any direction. Every bot whose current spot already pushes roughly the right way farts too.
            bool wants = wantPush > 0.3f;
            float myErr = Mathf.Abs(Mathf.DeltaAngle(seat.Angle, targetAngle));
            bool leader = true;
            foreach (var other in All)
                if (other != this && other.raft == raft && other.wantPush > 0.3f &&
                    Mathf.Abs(Mathf.DeltaAngle(other.seat.Angle, targetAngle)) + 12f < myErr) leader = false;
            float goal = wants && leader ? targetAngle : HomeAngle();
            float delta = ReachableOffset(goal, out _);
            state.move = Mathf.Abs(delta) > 8f ? -Mathf.Sign(delta) : 0f;
            bool fart = wants && myErr < Mathf.Lerp(48f, 38f, skill) && seat.Thrust.Resource.Gas > 0.15f
                        && (EndlessRiver.Instance == null || Time.time < pulseUntil);      // endless: measured pulses, not long streams
            if (fart && !state.fartHeld) state.tapSeq++;
            state.fartHeld = fart;
            if (brakeWanted) { state.brakeSeq++; brakeWanted = false; }
        }

        /// <summary>
        /// Signed walk (deg, + = counter-clockwise) to the closest angle to <paramref name="want"/> inside the arc this seat can walk to (between the occupied
        /// neighbour seats, minus the spacing). <paramref name="error"/> = how far that is from the wanted angle (deg).
        /// </summary>
        float ReachableOffset(float want, out float error)
        {
            float spacing = raft.tuning != null ? raft.tuning.minSeatSpacing : 42f;
            float me = seat.Angle;
            float cw = 360f, ccw = 360f;          // free arc on each side (angle decreasing = cw, increasing = ccw)
            foreach (var s in raft.seats.Seats)
            {
                if (s == null || s == seat || !s.IsOccupied) continue;
                float d = Mathf.Repeat(s.Angle - me, 360f);       // 0..360 counter-clockwise to the other seat
                ccw = Mathf.Min(ccw, d - spacing);
                cw = Mathf.Min(cw, 360f - d - spacing);
            }
            cw = Mathf.Max(0f, cw); ccw = Mathf.Max(0f, ccw);
            float off = Mathf.DeltaAngle(me, want);               // + = counter-clockwise
            float best = off >= 0f ? Mathf.Min(off, ccw) : -Mathf.Min(-off, cw);
            // the other way round may get closer when the short way is blocked
            float other = off >= 0f ? -Mathf.Min(360f - off, cw) : Mathf.Min(360f + off, ccw);
            float errBest = Mathf.Abs(Mathf.DeltaAngle(me + best, want)), errOther = Mathf.Abs(Mathf.DeltaAngle(me + other, want));
            if (errOther + 5f < errBest) { best = other; errBest = errOther; }
            error = errBest;
            return best;
        }

        /// <summary>True when a wait gate ahead will not stay open while the raft passes it: hold back upstream.</summary>
        bool MustWait(float raftDistance, float along, float raftRadius)
        {
            FartOnline.Obstacles.WaitGateObstacle gate = null;
            foreach (var g in FartOnline.Obstacles.WaitGateObstacle.Active)
                if (g != null && g.riverDistance > raftDistance - raftRadius && (gate == null || g.riverDistance < gate.riverDistance)) gate = g;
            if (gate == null) return false;
            float dist = gate.riverDistance - raftDistance - raftRadius - 1f;      // to the gate's upstream face
            if (dist < 0.5f || dist > 30f) return false;                           // committed, or still far away
            float v = Mathf.Max(0.8f, along);
            float span = (raftRadius * 2f + 2.4f) / v;
            if (gate.IsOpenAt(gate.ClockNow + dist / v, span)) return false;
            if (dist < 8f && along > 1.2f && Random.value < 0.6f) brakeWanted = true;
            return true;
        }

        void Think()
        {
            RiverPath river = raft.River;
            Transform t = raft.transform;
            var here = river.Closest(t.position, out _);
            Vector3 vel = Vector3.ProjectOnPlane(raft.RelativeVelocity, Vector3.up);
            var endless = EndlessRiver.Instance;
            float look = endless != null ? Mathf.Clamp(raft.Body.linearVelocity.magnitude * 2.2f, 8f, 16f) : lookAhead;
            var ahead = river.SampleAtDistance(here.distance + look);
            Vector3 aheadPoint = ahead.position;
            if (endless != null) aheadPoint += ahead.right * endless.SafeLaneX(ahead.distance);
            Vector3 toAhead = Vector3.ProjectOnPlane(aheadPoint - t.position, Vector3.up);

            // desired world push: toward the point ahead, minus the sideways drift
            Vector3 lateralDrift = Vector3.Project(vel, Vector3.Cross(Vector3.up, toAhead.normalized));
            Vector3 want = toAhead.normalized - lateralDrift * Mathf.Lerp(0.15f, 0.4f, skill);

            float speed = vel.magnitude;
            float narrow = Mathf.InverseLerp(14f, 8f, Mathf.Min(here.width, ahead.width));
            wantPush = Mathf.Lerp(1f, 0.25f, Mathf.Max(narrow, Mathf.InverseLerp(3.5f, 6f, speed)));

            if (endless != null)
            {
                // the river carries the raft: push mostly sideways toward the lane, and brake when drifting fast
                Vector3 flow = Vector3.ProjectOnPlane(ahead.tangent, Vector3.up).normalized;
                // predictive lane keeping: where will the raft be (sideways) in a moment, compared with the safe lane a
                // few metres ahead? Push only when that prediction is off by more than a small dead band, so the crew
                // settles instead of bouncing between hard-left and hard-right pushes. The banks are lined with spiked
                // rollers: shove hard away from an edge that is getting close.
                float latNow = Vector3.Dot(t.position - here.position, here.right);
                float latVel = Vector3.Dot(raft.Body.linearVelocity, here.right);
                float lane = endless.SafeLaneX(here.distance + Mathf.Clamp(raft.Body.linearVelocity.magnitude * 1.2f, 3f, 7f));
                float predicted = lane - (latNow + latVel * Mathf.Lerp(2.1f, 1.7f, skill));
                float edgeRoom = endless.HalfWidth - endless.edgeGuard - endless.raftRadius;
                float danger = Mathf.InverseLerp(edgeRoom - 1.4f, edgeRoom - 0.2f, Mathf.Abs(latNow + latVel * 0.6f));
                float sidePush = Mathf.Abs(predicted) < 0.35f ? 0f : Mathf.Clamp(predicted * 0.7f, -1.5f, 1.5f);
                Vector3 correction = here.right * sidePush - here.right * (Mathf.Sign(latNow) * danger * 2f);
                float along = Vector3.Dot(raft.Body.linearVelocity, flow);
                bool hold = MustWait(here.distance, along, endless.raftRadius);
                want = correction + flow * (hold ? -1.3f : along > endless.FlowAt(here.distance) + 0.6f ? -0.4f : 0.05f);
                wantPush = hold ? 1f : Mathf.Clamp01(correction.magnitude / 0.5f);
                // a pulse as long as the error needs (a strong fart moves the raft fast: long streams overshoot)
                if (hold) pulseUntil = Time.time + 0.5f;
                else if (wantPush > 0.3f && Time.time >= pulseUntil)
                    pulseUntil = Time.time + Mathf.Clamp(correction.magnitude * 0.3f, 0.12f, 0.55f);
                Vector3 l = t.InverseTransformDirection(want);
                Vector2 pv = new Vector2(l.x, l.z);
                if (pv.sqrMagnitude < 1e-4f || wantPush < 0.15f) { wantPush = 0f; return; }
                targetAngle = RimMath.AngleForPush(pv.normalized) + Mathf.Lerp(18f, 4f, skill) * Random.Range(-1f, 1f);
                return;
            }

            bool blocked = Physics.SphereCast(t.position + Vector3.up * 0.8f, 1.3f, toAhead.normalized, out RaycastHit hit, 9f, obstacleMask, QueryTriggerInteraction.Ignore)
                           && hit.rigidbody != null && hit.rigidbody.isKinematic && !hit.transform.IsChildOf(t);
            if (blocked) { wantPush = 0f; if (speed > 1.2f && Random.value < 0.4f + 0.5f * skill) brakeWanted = true; }

            Vector3 local = t.InverseTransformDirection(want);
            Vector2 push = new Vector2(local.x, local.z);
            if (push.sqrMagnitude < 1e-4f) { wantPush = 0f; return; }
            float aimError = Mathf.Lerp(25f, 6f, skill) * Random.Range(-1f, 1f);
            targetAngle = RimMath.AngleForPush(push.normalized) + aimError;
        }
    }
}
