using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using FartOnline.Game.Endless;
using FartOnline.Obstacles;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Play-mode check of the endless river (menu Tools > Fart Online > Tests > Run Endless Probe).
    /// 1. Layout guarantee, physically: for several seeds the river is generated from the start to full difficulty
    ///    (3.2 km). For every metre of the guaranteed safe path and 52 moments of the moving obstacles' cycles, a
    ///    raft-sized capsule (radius = raft radius + 0.1 m) is swept along it and every touching hazard collider counts as a
    ///    violation. Expected: 0. Also logs the obstacle mix and the gap widths.
    /// 2. Play test: AI crews (3 AI, then 1 AI) ride real physics runs until they crash or reach the distance cap.
    /// Report: Assets/Game/Tests/Results/EndlessProbe.md.
    /// </summary>
    public class EndlessProbe : MonoBehaviour
    {
        public static int[] Seeds = { 11, 2024, 77777, 31337, 9001 };
        public static float LayoutLength = 4200f;   // past 3 km: the overdrive (tighter rows, faster hazards)
        public static (int ai, float cap, float maxSeconds)[] Runs = { (3, 900f, 300f), (1, 600f, 240f) };
        /// <summary>Set by the "crash check only" menu entry: skips parts 1 and 2.</summary>
        public static bool CrashCheckOnly;

        readonly StringBuilder report = new StringBuilder();
        float probeClock;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            float savedBest = PlayerPrefs.GetFloat("fo.endless.best", 0f);     // tests must not change the player's record
            var river = FindAnyObjectByType<EndlessRiver>();
            var run = FindAnyObjectByType<GameRunManager>();
            var session = FindAnyObjectByType<GameSession>();
            var raft = FindAnyObjectByType<SharedRaftController>();
            report.AppendLine("# Endless probe");
            report.AppendLine();
            report.AppendLine($"Generated {System.DateTime.Now:yyyy-MM-dd HH:mm}. Water width {river.waterWidth} m, raft radius {river.raftRadius} m.");
            report.AppendLine();
            report.AppendLine("## 1. Layout guarantee (raft-sized sweep along the safe path, 52 obstacle-clock samples; fire jets and cannonball triggers included; whirlpool pull must never reach the raft's rim)");
            report.AppendLine();
            report.AppendLine("| Seed | Rows | Obstacles | Min gap (m) | Path metres checked | Violations |");
            report.AppendLine("|---|---|---|---|---|---|");
            int totalViolations = 0;
            var kinds = new Dictionary<ObstacleKind, int>();
            yield return null;
            foreach (int seed in CrashCheckOnly ? new int[0] : Seeds)
            {
                var result = new int[4];
                var minGap = new float[] { 99f };
                yield return Layout(river, seed, result, minGap, kinds);
                totalViolations += result[3];
                report.AppendLine($"| {seed} | {result[0]} | {result[1]} | {minGap[0]:F2} | {result[2]} | {result[3]} |");
                Debug.Log($"[EndlessProbe] seed {seed}: rows {result[0]}, obstacles {result[1]}, checked {result[2]} m, violations {result[3]}");
            }
            report.AppendLine();
            var mix = new StringBuilder();
            foreach (var kv in kinds) mix.Append(kv.Key).Append(' ').Append(kv.Value).Append(", ");
            report.AppendLine("Obstacle mix: " + mix.ToString().TrimEnd(',', ' ') + ".");
            report.AppendLine();
            report.AppendLine(totalViolations == 0 ? "**PASS: the safe path is clear for every seed at every sampled time.**" : $"**FAIL: {totalViolations} violations.**");
            report.AppendLine();
            report.AppendLine(gateCount == 0 ? "Wait gates: none planned." :
                $"Wait gates: {gateCount}, open {gateWorstMargin:F2} s longer than the raft needs to pass at the slowest current there (worst case). " +
                (gateWorstMargin > 0.3f ? "**PASS**" : "**FAIL: open window too short**"));
            Debug.Log($"[EndlessProbe] wait gates {gateCount}, worst open-window margin {gateWorstMargin:F2} s");
            report.AppendLine();
            KinematicObstacle.ClockSource = () => run.ObstacleClock;
            river.target = raft.transform;

            // 2. AI play tests on real physics
            report.AppendLine("## 2. AI play tests (real physics, Standard assist)");
            report.AppendLine();
            report.AppendLine("| AI crew | Seed | Result | Distance (m) | Time (s) | Crash |");
            report.AppendLine("|---|---|---|---|---|---|");
            Time.timeScale = 2f;
            foreach (var (ai, cap, maxSeconds) in CrashCheckOnly ? new (int, float, float)[0] : Runs)
            {
                TestCrew.StartSolo(session, SoloAssist.Standard, ai);
                raft.seats.Seats[0].Vacate();                  // only the AI crew drives
                float t0 = Time.time;
                while (run.State != RunState.GameOver && run.Stats.score < cap && Time.time - t0 < maxSeconds + 4f) yield return null;
                string result = run.State == RunState.GameOver ? "crashed" : run.Stats.score >= cap ? "reached cap" : "time cap";
                report.AppendLine($"| {ai} | {run.RunSeed} | {result} | {run.Stats.score:F0} | {run.Stats.elapsed:F0} | {run.Stats.crashReason} |");
                Debug.Log($"[EndlessProbe] AI {ai}: {result} at {run.Stats.score:F0} m after {run.Stats.elapsed:F0} s ({run.Stats.crashReason})");
                session.EndSession();
                yield return null;
            }
            Time.timeScale = 1f;

            // 3. crash check: drop the raft just upstream of real obstacles and let the current carry it in
            report.AppendLine();
            report.AppendLine("## 3. Crash check (raft dropped upstream of an obstacle, no thrust)");
            report.AppendLine();
            report.AppendLine("| Obstacle | Result | Reason |");
            report.AppendLine("|---|---|---|");
            int crashFails = 0;
            foreach (var want in new[] { ObstacleKind.Boulder, ObstacleKind.Crates, ObstacleKind.Barrels, ObstacleKind.BuoyLine, ObstacleKind.Log, ObstacleKind.Spinner, ObstacleKind.Press, ObstacleKind.SpikeTrap, ObstacleKind.Buzzsaw,
                                       ObstacleKind.StarSpinner, ObstacleKind.Puffer, ObstacleKind.BankRoller, ObstacleKind.Tentacle,
                                       ObstacleKind.FireJet, ObstacleKind.Mortar, ObstacleKind.Whirlpool,
                                       ObstacleKind.SpikeSwing, ObstacleKind.MineField, ObstacleKind.SpikeLogRoll, ObstacleKind.SpikeBridge, ObstacleKind.WaitGate })
            {
              // up to 3 attempts on different rivers: with sharp bends a raft dropped without thrust can drift past a small
              // target (a miss is a probe miss, not a hazard that fails to kill); a kind that never ends the run still fails
              for (int attempt = 0; attempt < 3; attempt++)
              {
                session.StartSolo(SoloAssist.Off);
                while (run.State != RunState.Playing) yield return null;
                // find the first planned obstacle of this kind and steer the raft's start onto it
                ObstaclePlacement target = default;
                bool found = false;
                for (int guard = 0; guard < 40 && !found; guard++)
                {
                    foreach (var row in river.Planner.Rows)
                        foreach (var it in row.items)
                            if (!found && it.kind == want && row.distance > river.TargetDistance + 20f) { target = it; found = true; }
                    if (!found) { river.BuildUpTo(river.BuiltTo); yield return null; }
                }
                if (!found) { report.AppendLine($"| {want} | not planned in range | |"); break; }
                // timed killers: hold the raft where the fire / cannonball lands until it fires (up to 8 s)
                if (want == ObstacleKind.FireJet || want == ObstacleKind.Mortar || want == ObstacleKind.SpikeSwing || want == ObstacleKind.WaitGate)
                {
                    float hx = want == ObstacleKind.FireJet ? target.x - target.side * (ObstaclePlanner.FireStart + target.size * 0.55f) : want == ObstacleKind.SpikeSwing || want == ObstacleKind.WaitGate ? target.x : target.extra;
                    float hMax = river.HalfWidth - river.edgeGuard - river.raftRadius - 0.15f;      // inside the bank rollers
                    hx = Mathf.Clamp(hx, -hMax, hMax);
                    var sh = river.path.SampleAtDistance(target.distance);
                    Vector3 hold = sh.position + sh.right * hx + Vector3.up * 0.1f;
                    Quaternion hq = Quaternion.LookRotation(Vector3.ProjectOnPlane(sh.tangent, Vector3.up), Vector3.up);
                    float h0 = Time.time;
                    while (run.State != RunState.GameOver && Time.time - h0 < 8f)
                    {
                        raft.Teleport(hold, hq);
                        yield return new WaitForFixedUpdate();
                    }
                    bool hit = run.State == RunState.GameOver;
                    if (!hit) crashFails++;
                    report.AppendLine($"| {want} | {(hit ? "game over" : "NO CRASH")} | {run.Stats.crashReason} (raft held in the danger zone) |");
                    Debug.Log($"[EndlessProbe] crash check {want}: {(hit ? "game over (" + run.Stats.crashReason + ")" : "NO CRASH")}");
                    session.EndSession();
                    yield return null;
                    break;
                }
                var s0 = river.path.SampleAtDistance(target.distance - 7f);
                // fences: aim just inside the tip (their middle can be against the bank, where the raft runs aground)
                float x = want == ObstacleKind.BuoyLine || want == ObstacleKind.SpikeBridge ? target.extra + target.side * 1.2f
                         : want == ObstacleKind.SpikeLogRoll ? target.x + target.extra * 0.5f
                         : want == ObstacleKind.BankRoller ? target.x - target.side * 1.3f
                         : want == ObstacleKind.Tentacle ? target.x - target.side * 1.5f
                         : want == ObstacleKind.Whirlpool ? target.x + (target.x < 0f ? 1f : -1f) * (target.size * 0.3f + river.raftRadius + 0.6f)   // rim in the swirl, clear of the grinder: the pull must drag it in
                         : target.x;
                float xMax = river.HalfWidth - river.edgeGuard - river.raftRadius - 0.15f;      // stay inside the bank rollers
                x = Mathf.Clamp(x, -xMax, xMax);
                raft.Teleport(s0.position + s0.right * x + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.ProjectOnPlane(s0.tangent, Vector3.up), Vector3.up));
                float t0 = Time.time;
                while (run.State != RunState.GameOver && Time.time - t0 < 12f) yield return null;
                bool ok = run.State == RunState.GameOver;
                Debug.Log($"[EndlessProbe] crash check {want} attempt {attempt + 1}: {(ok ? "game over (" + run.Stats.crashReason + ")" : "NO CRASH")}");
                if (!ok && attempt < 2) { session.EndSession(); yield return null; continue; }
                if (!ok) crashFails++;
                report.AppendLine($"| {want} | {(ok ? "game over" : "NO CRASH")} | {run.Stats.crashReason}{(attempt > 0 ? $" (attempt {attempt + 1})" : "")} |");
                session.EndSession();
                yield return null;
                break;
              }
            }
            // the spiked rollers lining the banks: drift the raft into the river's edge
            {
                session.StartSolo(SoloAssist.Off);
                while (run.State != RunState.Playing) yield return null;
                EndlessHazard edge = null;
                for (int guard = 0; guard < 40 && edge == null; guard++)
                {
                    foreach (var h in FindObjectsByType<EndlessHazard>())
                        if (h.label == "a spiked bank roller" && h.gameObject.activeInHierarchy &&
                            river.path.Closest(h.transform.position, out _).distance > river.TargetDistance + 25f) { edge = h; break; }
                    if (edge == null) yield return null;
                }
                bool hit = false;
                string why = "no bank roller found";
                if (edge != null)
                {
                    var se = river.path.Closest(edge.transform.position, out _);
                    float side = Mathf.Sign(Vector3.Dot(edge.transform.position - se.position, se.right));
                    var s0 = river.path.SampleAtDistance(se.distance - 9f);
                    Vector3 start = s0.position + s0.right * side * (river.HalfWidth - 2.9f) + Vector3.up * 0.3f;
                    raft.Teleport(start, Quaternion.LookRotation(Vector3.ProjectOnPlane(s0.tangent, Vector3.up), Vector3.up));
                    float t0 = Time.time;
                    var body = raft.GetComponent<Rigidbody>();
                    while (run.State != RunState.GameOver && Time.time - t0 < 10f)
                    {
                        body.AddForce(s0.right * side * 2.5f, ForceMode.Acceleration);     // a raft drifting into the bank
                        yield return new WaitForFixedUpdate();
                    }
                    hit = run.State == RunState.GameOver;
                    why = run.Stats.crashReason;
                }
                if (!hit) crashFails++;
                report.AppendLine($"| Bank edge | {(hit ? "game over" : "NO CRASH")} | {why} |");
                Debug.Log($"[EndlessProbe] crash check bank edge: {(hit ? "game over (" + why + ")" : "NO CRASH")}");
                session.EndSession();
                yield return null;
            }
            report.AppendLine();
            report.AppendLine(crashFails == 0 ? "**PASS: every obstacle hit ends the run.**" : $"**FAIL: {crashFails} hits did not end the run.**");

            string dir = Path.Combine(Application.dataPath, "Game", "Tests", "Results");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, CrashCheckOnly ? "EndlessCrashCheck.md" : "EndlessProbe.md"), report.ToString());
            PlayerPrefs.SetFloat("fo.endless.best", savedBest);
            PlayerPrefs.Save();
            CrashCheckOnly = false;
            Debug.Log("[EndlessProbe] DONE violations=" + totalViolations);
        }

        readonly HashSet<WaitGateObstacle> gatesSeen = new HashSet<WaitGateObstacle>();
        int gateCount;
        float gateWorstMargin = float.MaxValue;

        IEnumerator Layout(EndlessRiver river, int seed, int[] result, float[] minGap, Dictionary<ObstacleKind, int> kinds)
        {
            var probeTarget = new GameObject("ProbeTarget").transform;
            river.target = probeTarget;
            KinematicObstacle.ClockSource = () => probeClock;
            river.ResetRiver(seed);
            var counted = new HashSet<int>();
            float scanned = river.startDistance;
            float radius = river.raftRadius + 0.1f;
            var hits = new Collider[32];
            float d = river.startDistance;
            while (scanned < LayoutLength)
            {
                river.BuildUpTo(d);
                foreach (var row in river.Planner.Rows)
                    if (counted.Add(row.index))
                    {
                        result[0]++;
                        result[1] += row.items.Count;
                        minGap[0] = Mathf.Min(minGap[0], row.gap);
                        foreach (var it in row.items) kinds[it.kind] = (kinds.TryGetValue(it.kind, out int n) ? n : 0) + 1;
                    }
                float to = Mathf.Min(river.BuiltTo - 12f, LayoutLength);
                if (to > scanned)
                {
                    var movers = FindObjectsByType<KinematicObstacle>();
                    foreach (var g in FindObjectsByType<WaitGateObstacle>())
                        if (gatesSeen.Add(g))
                        {
                            // the raft (diameter + the log's 1.7 m depth) must pass while it is fully open, at the slowest current
                            float needed = (2f * river.raftRadius + 1.7f) / river.FlowAt(g.riverDistance);
                            gateWorstMargin = Mathf.Min(gateWorstMargin, g.openTime - needed);
                            gateCount++;
                        }
                    var pulls = FindObjectsByType<WhirlpoolPull>();
                    for (int k = 0; k < 52; k++)
                    {
                        probeClock = k * 0.25f;
                        foreach (var m in movers) m.ApplyPoseNow();
                        Physics.SyncTransforms();
                        for (float x = scanned; x < to; x += 1f)
                        {
                            var s = river.path.SampleAtDistance(x);
                            Vector3 c = s.position + s.right * river.SafeLaneX(x);
                            int n = Physics.OverlapCapsuleNonAlloc(c, c + Vector3.up * 0.9f, radius, hits, ~0, QueryTriggerInteraction.Collide);
                            if (k == 0)
                                foreach (var wp in pulls)
                                {
                                    if (wp == null) continue;
                                    Vector3 dv = wp.transform.position - c; dv.y = 0f;
                                    if (dv.magnitude - river.raftRadius < wp.radius * wp.transform.lossyScale.x)
                                    {
                                        result[3]++;
                                        if (result[3] <= 5) Debug.LogWarning($"[EndlessProbe] seed {seed}: whirlpool pull reaches the safe path at {x:F0} m");
                                    }
                                }
                            for (int i = 0; i < n; i++)
                            {
                                var hz = hits[i].GetComponentInParent<EndlessHazard>();
                                if (hz == null) continue;
                                if (hits[i].GetComponentInParent<WaitGateObstacle>() != null) continue;   // timed: checked by its open window
                                result[3]++;
                                if (result[3] <= 5) Debug.LogWarning($"[EndlessProbe] seed {seed}: {hz.label} ({hits[i].name}) on the safe path at {x:F0} m, clock {probeClock:F2} s");
                            }
                        }
                    }
                    result[2] += Mathf.RoundToInt(to - scanned);
                    scanned = to;
                }
                d += 60f;
                probeTarget.position = river.path.SampleAtDistance(d).position;
                yield return null;
            }
            Destroy(probeTarget.gameObject);
        }

    }
}
