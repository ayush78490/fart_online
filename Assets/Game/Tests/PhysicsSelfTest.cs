using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Play-mode acceptance test for the round shared-raft physics. Drives the real pipeline with scripted inputs
    /// (walk the rim with A/D, fart with F) and measures the outcome: seat position decides the push direction,
    /// rim movement and seat spacing, combined and opposing thrust, tap / charged / brake impulses, gas exhaustion,
    /// wall collision response and wipeout recovery. Writes Assets/Game/Tests/Results/PhysicsSelfTest.md.
    /// Menu: Tools > Fart Online > Tests > Run Physics Self-Test (enters Play Mode).
    /// </summary>
    public class PhysicsSelfTest : MonoBehaviour
    {
        public static bool Running { get; private set; }

        SharedRaftController raft;
        RaftRecoveryManager rec;
        GameRunManager run;
        Transform start;
        readonly List<string> lines = new List<string>();
        int pass, fail;
        readonly ScriptedThrustInput[] inputs = new ScriptedThrustInput[4];

        IEnumerator Start()
        {
            Running = true;
            Application.runInBackground = true;
            raft = FindAnyObjectByType<SharedRaftController>();
            rec = raft.GetComponent<RaftRecoveryManager>();
            run = FindAnyObjectByType<GameRunManager>();
            start = run.startPoint;
            var session = FindAnyObjectByType<GameSession>();
            if (session != null) session.enabled = false;
            for (int i = 0; i < 4; i++) inputs[i] = new GameObject("TestInput_" + i).AddComponent<ScriptedThrustInput>();
            raft.SetAssist(SoloAssist.Off);
            Seat(1);
            run.PrepareRun();
            run.StartCountdown();
            while (run.State != RunState.Playing) yield return null;
            var t = raft.tuning;
            Log($"Round raft radius {t.raftRadius} m, seats at {t.seatRadius} m, mass {t.mass} kg, thrust {t.thrust} N per player, tap {t.tapImpulse} N·s, charged {t.chargedImpulseMin}-{t.chargedImpulseMax} N·s, brake {t.brakeImpulse} N·s, rim walk {t.seatMoveSpeed}°/s");
            var s0 = raft.seats.Seats[0];

            // ---- 1 sitting at the back and farting pushes the raft forward
            yield return Reset(); Place(0, 180f);
            Vector3 v0 = RelLocal();
            inputs[0].HoldFart();
            yield return new WaitForSeconds(1.5f);
            inputs[0].ReleaseFart();
            Vector3 dv = RelLocal() - v0;
            Check("Farting from the back of the round raft pushes it forward", dv.z > 1.2f && Mathf.Abs(dv.x) < 0.4f, $"Δv forward {dv.z:F2}, sideways {dv.x:F2} m/s");
            Check("Thrust through the centre does not spin the raft", Mathf.Abs(Yaw()) < 0.25f, $"yaw rate {Yaw():F2} rad/s");

            // ---- 2 walking the rim with D (right) / A (left)
            yield return Reset(); Place(0, 180f);
            inputs[0].Move(1f);                                  // D
            yield return new WaitForSeconds(0.5f);
            inputs[0].Move(0f);
            float moved = Mathf.DeltaAngle(180f, s0.Angle);
            Check("D walks the player to their right along the rim", moved < -30f && moved > -60f, $"angle 180 -> {s0.Angle:F0} ({moved:F0}°, expected ≈ -{t.seatMoveSpeed * 0.5f:F0}°)");
            inputs[0].Move(-1f);                                 // A
            yield return new WaitForSeconds(0.5f);
            inputs[0].Move(0f);
            Check("A walks the player back to the left", Mathf.Abs(Mathf.DeltaAngle(180f, s0.Angle)) < 12f, $"angle {s0.Angle:F0}");

            // ---- 3 sitting on the right side pushes the raft left; at the front pushes it backward
            yield return Reset(); Place(0, 90f);
            v0 = RelLocal();
            inputs[0].HoldFart();
            yield return new WaitForSeconds(1.2f);
            inputs[0].ReleaseFart();
            dv = RelLocal() - v0;
            Check("Farting from the right side pushes the raft left", dv.x < -0.9f && Mathf.Abs(dv.z) < 0.5f, $"Δv right {dv.x:F2}, forward {dv.z:F2} m/s");

            yield return Reset(); Place(0, 0f);
            v0 = RelLocal();
            inputs[0].HoldFart();
            yield return new WaitForSeconds(1.2f);
            inputs[0].ReleaseFart();
            dv = RelLocal() - v0;
            Check("Farting from the front pushes the raft backward (brake / reverse)", dv.z < -0.9f, $"Δv forward {dv.z:F2} m/s");

            // ---- 4 two players: combined, opposing, spacing, diagonal
            Seat(2);
            yield return Reset(); Place(0, 150f); Place(1, 210f);
            v0 = RelLocal();
            inputs[0].HoldFart(); inputs[1].HoldFart();
            yield return new WaitForSeconds(1.0f);
            dv = RelLocal() - v0;
            float yawBoth = Yaw();
            inputs[0].ReleaseFart(); inputs[1].ReleaseFart();
            Check("Two players at the back combine into a stronger forward push", dv.z > 1.6f && Mathf.Abs(dv.x) < 0.4f, $"Δv forward {dv.z:F2}, sideways {dv.x:F2} m/s, yaw {yawBoth:F2}");

            yield return Reset(); Place(0, 0f); Place(1, 180f);
            v0 = RelLocal();
            inputs[0].HoldFart(); inputs[1].HoldFart();
            yield return new WaitForSeconds(1.0f);
            dv = RelLocal() - v0;
            inputs[0].ReleaseFart(); inputs[1].ReleaseFart();
            Check("Players on opposite sides cancel each other out", Mathf.Abs(dv.z) < 0.4f && Mathf.Abs(dv.x) < 0.4f, $"Δv {dv.z:F2} / {dv.x:F2} m/s");

            yield return Reset(); Place(0, 150f); Place(1, 210f);
            inputs[0].Move(-1f);                                 // A: angle increases toward the other player at 210
            yield return new WaitForSeconds(1.5f);
            inputs[0].Move(0f);
            float gap = Mathf.DeltaAngle(raft.seats.Seats[0].Angle, raft.seats.Seats[1].Angle);
            Check("Players cannot walk through each other (minimum seat spacing)", Mathf.Abs(gap - t.minSeatSpacing) < 2f, $"gap {gap:F1}° (min {t.minSeatSpacing}°)");

            yield return Reset(); Place(0, 135f); Place(1, 225f);
            v0 = RelLocal();
            inputs[0].HoldFart();                                // only the back-right player
            yield return new WaitForSeconds(1.0f);
            dv = RelLocal() - v0;
            inputs[0].ReleaseFart();
            Check("A diagonal seat pushes the raft diagonally (forward + left)", dv.z > 0.6f && dv.x < -0.6f, $"Δv forward {dv.z:F2}, right {dv.x:F2} m/s");

            // ---- 5 impulses (solo)
            Seat(1);
            yield return Reset(); Place(0, 180f);
            v0 = RelLocal();
            inputs[0].Tap();
            yield return new WaitForSeconds(0.12f);
            dv = RelLocal() - v0;
            Check("Tapping F gives a short puff impulse", dv.z > 0.6f, $"Δv {dv.z:F2} m/s (ideal {t.tapImpulse / t.mass:F2})");

            yield return Reset(); Place(0, 180f);
            v0 = RelLocal();
            inputs[0].BeginCharge();
            yield return new WaitForSeconds(t.chargeTime + 0.1f);
            float charge = s0.Thrust.State.charge01;
            inputs[0].ReleaseCharge();
            yield return new WaitForSeconds(0.15f);
            dv = RelLocal() - v0;
            Check("Charged burst is much stronger than a tap", dv.z > 2.5f && charge > 0.95f, $"charge {charge:F2}, Δv {dv.z:F2} m/s");

            yield return Reset(); Place(0, 180f);
            inputs[0].HoldFart();
            yield return new WaitForSeconds(1.5f);
            inputs[0].ReleaseFart();
            yield return new WaitForSeconds(0.3f);
            float before = Rel().magnitude;
            inputs[0].Brake();
            yield return new WaitForSeconds(0.15f);
            float after = Rel().magnitude;
            Check("Brake burst slows the raft from any seat", after < before - 0.6f, $"speed {before:F2} -> {after:F2} m/s");

            // ---- 6 gas exhaustion
            yield return Reset(); Place(0, 180f);
            var res = s0.Thrust.Resource;
            float tEnd = Time.time + 9f;
            bool sawExhausted = false, forceWhileExhausted = false;
            while (Time.time < tEnd && !sawExhausted)
            {
                if (Time.time % 2.4f < 1.7f) inputs[0].HoldFart(); else inputs[0].ReleaseFart();
                yield return new WaitForFixedUpdate();
                if (res.Exhausted) sawExhausted = true;
            }
            for (int i = 0; i < 20 && sawExhausted; i++)
            {
                inputs[0].HoldFart();
                yield return new WaitForFixedUpdate();
                if (raft.LastTotalForce.sqrMagnitude > 1f) forceWhileExhausted = true;
            }
            inputs[0].ReleaseFart();
            Check("Gas tank empties under sustained use and blocks thrust while exhausted", sawExhausted && !forceWhileExhausted, $"exhausted {sawExhausted}, force while exhausted {forceWhileExhausted}");
            yield return new WaitForSeconds(t.exhaustedCooldown + 1.2f);
            Check("Gas regenerates after a pause", res.Gas > t.exhaustedRecoverLevel && !res.Exhausted, $"gas {res.Gas:F2}");

            // ---- 7 collision with the canyon wall (narrow canyon, section B): impact, not a reset
            yield return Reset(CheckpointPose(1)); Place(0, 270f);              // left side: pushes the raft right
            int impacts = 0; bool heavySeen = false;
            System.Action<float, Vector3, bool> onImpact = (s, p, h) => { impacts++; heavySeen |= h; };
            rec.Impact += onImpact;
            int wipeBefore = rec.WipeoutCount;
            inputs[0].HoldFart();
            for (float t2 = 0; t2 < 6f && impacts == 0; t2 += Time.deltaTime) yield return null;
            inputs[0].ReleaseFart();
            yield return new WaitForSeconds(0.5f);
            rec.Impact -= onImpact;
            Check("Pushing into the canyon wall produces an impact and keeps the raft in play", impacts > 0 && rec.WipeoutCount == wipeBefore && !rec.InWipeout, $"impacts {impacts}, heavy {heavySeen}, wipeouts +{rec.WipeoutCount - wipeBefore}");

            // ---- 8 severe wipeout: fall below the course -> respawn upright at the last checkpoint
            yield return Reset();
            Vector3 expected = rec.respawnPoint != null ? rec.respawnPoint.position : start.position;
            string reason = null;
            System.Action<string> onWipe = r => reason = r;
            rec.WipedOut += onWipe;
            raft.Teleport(raft.transform.position + Vector3.down * 45f, raft.transform.rotation);
            for (float t2 = 0; t2 < 1f && reason == null; t2 += Time.deltaTime) yield return null;
            bool thrustBlocked = !raft.thrustEnabled;
            yield return new WaitForSeconds(t.wipeoutDelay + 0.8f);
            rec.WipedOut -= onWipe;
            float dist = Vector3.ProjectOnPlane(raft.transform.position - expected, Vector3.up).magnitude;
            Check("Falling below the course triggers a wipeout that disables thrust", reason != null && (reason == "fell" || reason.StartsWith("hazard")) && thrustBlocked, $"reason {reason}, thrust blocked {thrustBlocked}");
            Check("Wipeout respawns at the last checkpoint and resumes play", dist < 2f && run.State == RunState.Playing && Vector3.Angle(raft.transform.up, Vector3.up) < 10f,
                  $"distance to respawn {dist:F2} m, state {run.State}");

            // ---- 9 capsize detection
            yield return Reset();
            reason = null;
            rec.WipedOut += onWipe;
            raft.Teleport(raft.transform.position + Vector3.up * 1.5f, Quaternion.AngleAxis(150f, raft.transform.forward) * raft.transform.rotation);
            float keepUpright = t.uprightStrength;
            t.uprightStrength = 0f;                              // stop self-righting for this test only
            for (float t2 = 0; t2 < 6f && reason == null; t2 += Time.deltaTime) yield return null;
            t.uprightStrength = keepUpright;
            rec.WipedOut -= onWipe;
            Check("Capsizing past the safe angle triggers recovery", reason == "capsized", $"reason {reason}");
            yield return new WaitForSeconds(t.wipeoutDelay + 0.8f);

            Finish();
        }

        void Seat(int count)
        {
            raft.seats.VacateAll();
            for (int i = 0; i < count; i++)
                raft.seats.Seats[i].Occupy("Test " + (i + 1), RaftSeatManager.PlayerColors[i], inputs[i], false, false, 0);
        }

        void Place(int seat, float angle) => raft.seats.SetReplicatedAngle(raft.seats.Seats[seat], angle);

        Transform CheckpointPose(int index)
        {
            var cp = FindAnyObjectByType<CheckpointManager>().Get(index);
            return cp != null ? cp.RespawnPoint : start;
        }

        IEnumerator Reset(Transform at = null)
        {
            if (at == null) at = start;
            foreach (var i in inputs) i.Release();
            yield return new WaitForFixedUpdate();
            raft.Teleport(at.position + Vector3.up * 0.2f, Quaternion.LookRotation(Vector3.ProjectOnPlane(at.forward, Vector3.up)));
            foreach (var s in raft.seats.Seats) s.Thrust.ResetSeat();
            yield return new WaitForSeconds(1.2f);   // settle on the water
        }

        Vector3 Rel() => Vector3.ProjectOnPlane(raft.RelativeVelocity, Vector3.up);
        Vector3 RelLocal() => raft.transform.InverseTransformDirection(Rel());
        float Yaw() => Vector3.Dot(raft.Body.angularVelocity, Vector3.up);

        void Check(string name, bool ok, string detail)
        {
            if (ok) pass++; else fail++;
            Log($"{(ok ? "PASS" : "FAIL")}  {name}  [{detail}]");
        }

        void Log(string s)
        {
            lines.Add(s);
            Debug.Log("[PhysicsSelfTest] " + s);
        }

        void Finish()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Physics self-test (round raft)");
            sb.AppendLine();
            sb.AppendLine($"Run: {System.DateTime.Now:yyyy-MM-dd HH:mm}, Unity {Application.unityVersion}, fixed step {Time.fixedDeltaTime:F4} s.");
            sb.AppendLine($"Result: **{pass} passed, {fail} failed**.");
            sb.AppendLine();
            foreach (var l in lines) sb.AppendLine("- " + l);
            string dir = Path.Combine(Application.dataPath, "Game/Tests/Results");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "PhysicsSelfTest.md"), sb.ToString());
            Debug.Log($"[PhysicsSelfTest] DONE {pass} passed, {fail} failed");
            Running = false;
        }
    }
}
