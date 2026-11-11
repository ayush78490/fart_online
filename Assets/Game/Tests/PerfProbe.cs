using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using FartOnline.Game.Endless;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Performance check (menu Tools > Fart Online > Tests > Run Perf Probe): switches to the "Mobile" quality level
    /// (what phones and the web build run), plays 30 s of the endless river with an AI crew and records frame times,
    /// rendering stats (batches, SetPass calls, triangles: editor only), active renderers and the time spent building
    /// river chunks. Appends a row to Assets/Game/Tests/Results/PerfProbe.md so before / after runs can be compared.
    /// Editor numbers are not phone numbers, but batches, SetPass calls, triangles and chunk-build spikes carry over.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        public static string Label = "run";

        IEnumerator Start()
        {
            Application.runInBackground = true;
            float savedBest = PlayerPrefs.GetFloat("fo.endless.best", 0f);
            int prevQuality = QualitySettings.GetQualityLevel();
            int mobile = System.Array.IndexOf(QualitySettings.names, "Mobile");
            if (mobile >= 0) QualitySettings.SetQualityLevel(mobile, true);
            PlatformBoot.ApplyQualityTweaks();
            yield return new WaitForSecondsRealtime(1f);
            var session = FindAnyObjectByType<GameSession>();
            var run = FindAnyObjectByType<GameRunManager>();
            TestCrew.StartSolo(session, SoloAssist.Standard, 3);
            yield return new WaitForSecondsRealtime(2f);
            EndlessRiver.MaxBuildMs = 0f;

            var dts = new List<float>();
            var batches = new List<int>(); var setpass = new List<int>(); var tris = new List<int>(); var renderers = new List<int>();
            float t0 = Time.realtimeSinceStartup, nextStat = 0f;
            int crashes = 0;
            while (Time.realtimeSinceStartup - t0 < 30f)
            {
                yield return null;
                dts.Add(Time.unscaledDeltaTime * 1000f);
                if (run.State == RunState.GameOver) { crashes++; TestCrew.StartSolo(session, SoloAssist.Standard, 3); yield return new WaitForSecondsRealtime(1f); }
                if (Time.realtimeSinceStartup - t0 > nextStat)
                {
                    nextStat += 0.5f;
#if UNITY_EDITOR
                    batches.Add(UnityEditor.UnityStats.drawCalls);
                    setpass.Add(UnityEditor.UnityStats.setPassCalls);
                    tris.Add(UnityEditor.UnityStats.triangles);
#endif
                    int n = 0;
                    foreach (var r in FindObjectsByType<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) n++;
                    renderers.Add(n);
                }
            }
            dts.Sort();
            float avg = 0f; foreach (var d in dts) avg += d; avg /= Mathf.Max(1, dts.Count);
            float p95 = dts[Mathf.Clamp((int)(dts.Count * 0.95f), 0, dts.Count - 1)], max = dts[dts.Count - 1];
            string line = $"| {System.DateTime.Now:MM-dd HH:mm} | {Label} | {avg:F1} | {p95:F1} | {max:F1} | {Avg(batches)} | {Avg(setpass)} | {Avg(tris) / 1000}k | {Avg(renderers)} | {EndlessRiver.MaxBuildMs:F1} | {crashes} |";
            string dir = Path.Combine(Application.dataPath, "Game", "Tests", "Results");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "PerfProbe.md");
            if (!File.Exists(file))
                File.WriteAllText(file, "# Perf probe (Mobile quality level, editor, AI crew of 3, 30 s)\n\n" +
                    "| When | Label | Avg frame (ms) | p95 (ms) | Max (ms) | Draw calls | SetPass | Triangles | Renderers | Max chunk build (ms) | Crashes |\n|---|---|---|---|---|---|---|---|---|---|---|\n");
            File.AppendAllText(file, line + "\n");
            Debug.Log("[PerfProbe] " + line);
            session.EndSession();
            QualitySettings.SetQualityLevel(prevQuality, true);
            PlayerPrefs.SetFloat("fo.endless.best", savedBest);
            PlayerPrefs.Save();
            Debug.Log("[PerfProbe] DONE");
        }

        static int Avg(List<int> v)
        {
            if (v.Count == 0) return 0;
            long s = 0; foreach (var x in v) s += x;
            return (int)(s / v.Count);
        }
    }
}
