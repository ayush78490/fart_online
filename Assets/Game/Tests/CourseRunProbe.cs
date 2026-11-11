using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Full-course traversal test (obstacles + balancing): an AI-only crew (1 = the solo case, 4 = full crew) plays the
    /// whole course at 3× speed through the normal pipeline (same physics, gas, recovery). Logs checkpoint times,
    /// wipeouts and where the raft stalls; writes Assets/Game/Tests/Results/CourseRun.md.
    /// </summary>
    public class CourseRunProbe : MonoBehaviour
    {
        public int[] crews = { 1, 4 };
        public static int[] OverrideCrews;
        public float timeLimit = 420f;     // game seconds per run
        public float timeScale = 3f;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            var session = FindAnyObjectByType<GameSession>();
            var run = FindAnyObjectByType<GameRunManager>();
            var raft = FindAnyObjectByType<SharedRaftController>();
            var rec = raft.GetComponent<RaftRecoveryManager>();
            if (OverrideCrews != null) { crews = OverrideCrews; OverrideCrews = null; }
            var sb = new StringBuilder("# Course run (AI crew)\n\n");
            sb.AppendLine($"Run: {System.DateTime.Now:yyyy-MM-dd HH:mm}. AI skill 0.60, time scale {timeScale}×, limit {timeLimit:F0} s per run.\n");
            foreach (int crew in crews)
            {
                TestCrew.StartSolo(session, SoloAssist.Standard, Mathf.Min(crew, 3), 0.6f);   // seat 0 = "human" + AI seats 1..3
                raft.seats.Seats[0].Vacate();                           // AI only
                if (crew == 4) { var ai = new GameObject("AICrew_extra").AddComponent<AICrewInput>(); ai.transform.SetParent(session.transform); ai.ReadyFor(raft, raft.seats.Seats[0], 0.6f); raft.seats.Seats[0].Occupy("AI Crew 0", RaftSeatManager.PlayerColors[0], ai, false, true, 0); }
                Time.timeScale = timeScale;
                int lastCp = -2; float stallTimer = 0f; float lastProgress = 0f;
                var line = new StringBuilder();
                string stalls = "";
                while (run.State != RunState.Finished && run.Stats.elapsed < timeLimit)
                {
                    if (run.Stats.checkpointIndex != lastCp)
                    {
                        lastCp = run.Stats.checkpointIndex;
                        line.Append($" CP{lastCp + 1}@{run.Stats.elapsed:F0}s");
                    }
                    float prog = raft.River.Closest(raft.transform.position, out _).distance;
                    stallTimer = Mathf.Abs(prog - lastProgress) < 0.5f ? stallTimer + Time.deltaTime : 0f;
                    if (Mathf.Abs(prog - lastProgress) >= 0.5f) lastProgress = prog;
                    if (stallTimer > 12f)
                    {
                        stalls += $" stalled@{prog:F0}m";
                        stallTimer = 0f;
                        rec.RequestRecovery();                           // what a player would do: reset to the checkpoint
                    }
                    yield return null;
                }
                Time.timeScale = 1f;
                string result = run.State == RunState.Finished ? $"FINISHED in {run.Stats.elapsed:F0} s" : $"NOT finished in {timeLimit:F0} s (reached {raft.River.Closest(raft.transform.position, out _).distance:F0} m of {raft.River.Length:F0} m)";
                string msg = $"- **{crew} AI crew:** {result}, wipeouts {run.Stats.wipeouts}, farts {run.Stats.thrustActivations}, checkpoints:{line}{(stalls.Length > 0 ? ", " + stalls : "")}";
                sb.AppendLine(msg);
                Debug.Log("[CourseRun] " + msg);
                session.EndSession();
                yield return new WaitForSeconds(1f);
            }
            string dir = Path.Combine(Application.dataPath, "Game/Tests/Results");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "CourseRun.md"), sb.ToString());
            Debug.Log("[CourseRun] DONE");
        }
    }
}
