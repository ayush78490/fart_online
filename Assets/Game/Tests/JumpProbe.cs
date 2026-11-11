using System.Collections;
using System.Text;
using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Section E (water ramp + gap) check: starts the raft at the top of the chute and tries strategies a solo player
    /// can do — drift only, sustained farting from the back, farting + a charged blast at the lip — and logs the
    /// launch speed, the airtime and whether the raft lands in the basin (wet) or wipes out.
    /// </summary>
    public class JumpProbe : MonoBehaviour
    {
        IEnumerator Start()
        {
            Application.runInBackground = true;
            var raft = FindAnyObjectByType<SharedRaftController>();
            var rec = raft.GetComponent<RaftRecoveryManager>();
            var run = FindAnyObjectByType<GameRunManager>();
            var session = FindAnyObjectByType<GameSession>();
            session.enabled = false;
            var river = raft.River;
            var input = new GameObject("JumpInput").AddComponent<ScriptedThrustInput>();
            raft.seats.Seats[0].Occupy("Jumper", RaftSeatManager.PlayerColors[0], input, false, false, 0);
            run.PrepareRun();
            run.StartCountdown();
            while (run.State != RunState.Playing) yield return null;

            float startD = river.Closest(new Vector3(4f, 0f, 262f), out _).distance;
            float gapD = river.Closest(new Vector3(4f, -7f, 294f), out _).distance;
            var sb = new StringBuilder();
            float wetFrom = -1f, lipD = -1f;
            for (float d = gapD - 12f; d < gapD + 20f; d += 0.25f)
            {
                var smp = river.SampleAtDistance(d);
                bool wet = river.TryGetWater(smp.position + Vector3.up * 0.1f, out _, out _);
                if (!wet && lipD < 0f) lipD = d;
                if (lipD > 0f && wet) { wetFrom = d; break; }
            }
            string geo = $"geometry: water ends {lipD - gapD:F1} m before the gap point, resumes {wetFrom - gapD:F1} m after it (dry span {wetFrom - lipD:F1} m)";
            sb.AppendLine("- " + geo);
            Debug.Log("[JumpProbe] " + geo);
            string[] names = { "drift only", "sustained fart from the back", "fart + charged blast at the lip" };
            for (int strategy = 0; strategy < 3; strategy++)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var s = river.SampleAtDistance(startD);
                    raft.Teleport(s.position + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.ProjectOnPlane(s.tangent, Vector3.up)));
                    raft.seats.SetReplicatedAngle(raft.seats.Seats[0], 180f);
                    raft.seats.Seats[0].Thrust.ResetSeat();
                    input.Release();
                    yield return new WaitForSeconds(0.3f);
                    string reason = null;
                    System.Action<string> onWipe = r => reason = r;
                    rec.WipedOut += onWipe;
                    if (strategy >= 1) input.HoldFart();
                    float launchSpeed = 0f, air = 0f, t0 = Time.time;
                    bool charged = false, landed = false, airborne = false;
                    while (Time.time - t0 < 14f && reason == null)
                    {
                        float d = river.Closest(raft.transform.position, out _).distance;
                        if (strategy == 2 && !charged && d > gapD - 14f) { input.BeginCharge(); charged = true; }
                        if (strategy == 2 && charged && input.state.charging && d > gapD - 4f) input.ReleaseCharge();
                        if (!raft.Buoyancy.InWater && d > gapD - 8f) { if (!airborne) launchSpeed = raft.Body.linearVelocity.magnitude; airborne = true; air += Time.deltaTime; }
                        if (airborne && raft.Buoyancy.InWater && d > gapD) { landed = true; break; }
                        yield return null;
                    }
                    input.Release();
                    rec.WipedOut -= onWipe;
                    float endD = river.Closest(raft.transform.position, out _).distance;
                    string res = landed ? "LANDED in the basin" : reason != null ? "wipeout (" + reason + ")" : "stuck";
                    string line = $"{names[strategy]} #{attempt + 1}: {res}, launch speed {launchSpeed:F1} m/s, airtime {air:F2} s, reached {endD - gapD:F1} m past the gap start";
                    sb.AppendLine("- " + line);
                    Debug.Log("[JumpProbe] " + line);
                    if (reason != null) yield return new WaitForSeconds(raft.tuning.wipeoutDelay + 0.6f);
                }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "Game/Tests/Results/JumpProbe.md"), "# Jump probe (section E)\n\n" + sb);
            Debug.Log("[JumpProbe] DONE");
        }
    }
}
