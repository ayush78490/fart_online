using System.Collections;
using System.IO;
using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Play-mode visual check for Phase 2: seats four scripted players, fires each kind of gas burst and saves
    /// screenshots (ArtSource/Game/Previews/*.png) from a close camera and from the gameplay camera, then logs the
    /// live particle count and frame time while all four players fire together.
    /// </summary>
    public class VisualProbe : MonoBehaviour
    {
        public static bool Running { get; private set; }
        SharedRaftController raft;
        readonly ScriptedThrustInput[] inputs = new ScriptedThrustInput[4];
        Camera probe;
        string outDir;

        IEnumerator Start()
        {
            Running = true;
            Application.runInBackground = true;
            outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Game", "Previews"));
            Directory.CreateDirectory(outDir);
            raft = FindAnyObjectByType<SharedRaftController>();
            var run = FindAnyObjectByType<GameRunManager>();
            var session = FindAnyObjectByType<GameSession>();
            if (session != null) session.enabled = false;
            for (int i = 0; i < 4; i++)
            {
                inputs[i] = new GameObject("ProbeInput_" + i).AddComponent<ScriptedThrustInput>();
                raft.seats.Seats[i].Occupy("P" + (i + 1), RaftSeatManager.PlayerColors[i], inputs[i], false, false, 0);
            }
            run.PrepareRun();
            run.StartCountdown();
            while (run.State != RunState.Playing) yield return null;
            var cam = new GameObject("ProbeCam");
            probe = cam.AddComponent<Camera>();
            probe.fieldOfView = 45f;
            probe.enabled = false;
            yield return new WaitForSeconds(1f);

            yield return Shot("00_round_raft_top", new Vector3(0.01f, 9f, 0f), Vector3.zero, 45f);
            yield return Shot("01_seated_crew_front", new Vector3(0.5f, 2.2f, 6.5f), new Vector3(0f, 0.6f, 0f));
            yield return Shot("02_seated_crew_side", new Vector3(6.5f, 1.8f, 0.5f), new Vector3(0f, 0.6f, 0f));
            var s1 = raft.seats.Seats[1].transform.localPosition;
            yield return Shot("02b_seated_closeup_side", s1 + new Vector3(2.4f, 0.6f, 0.4f), s1 + new Vector3(0f, 0.45f, 0f), 40f);

            inputs[0].HoldFart();                                   // sustained forward: gas streams backward
            yield return new WaitForSeconds(0.45f);
            yield return Shot("03_sustain_forward_side", new Vector3(6f, 2.2f, -1.5f), new Vector3(0f, 0.6f, -1.5f));
            inputs[0].Release();
            yield return new WaitForSeconds(1.2f);

            inputs[1].Tap();                                              // tap forward
            yield return new WaitForSeconds(0.18f);
            yield return Shot("04_tap", new Vector3(5.5f, 2.5f, -2f), new Vector3(0.5f, 0.5f, -1.5f));
            yield return new WaitForSeconds(1.2f);

            inputs[2].Move(1f);                                           // walk right along the rim (D)
            yield return new WaitForSeconds(0.6f);
            inputs[2].Move(0f);
            inputs[2].HoldFart();
            yield return new WaitForSeconds(0.45f);
            yield return Shot("05_side_push_left", new Vector3(0.5f, 3f, -7f), new Vector3(0.5f, 0.5f, 0f));
            inputs[2].Release();
            yield return new WaitForSeconds(1.2f);

            inputs[3].BeginCharge();
            yield return new WaitForSeconds(0.9f);
            yield return Shot("06_charging_pose", new Vector3(3.5f, 1.8f, -4f), new Vector3(0.9f, 0.7f, -0.9f));
            inputs[3].ReleaseCharge();
            yield return new WaitForSeconds(0.2f);
            yield return Shot("07_charged_burst", new Vector3(7f, 3f, -3f), new Vector3(0f, 0.5f, -2.5f));
            yield return new WaitForSeconds(1.5f);

            inputs[0].HoldFart();
            yield return new WaitForSeconds(0.6f);
            inputs[0].Brake();
            yield return new WaitForSeconds(0.15f);
            inputs[0].Release();
            yield return Shot("08_brake", new Vector3(6f, 2.5f, 2.5f), new Vector3(0f, 0.5f, 1.5f));
            yield return new WaitForSeconds(1.5f);

            // all four together: crowd readability + cost
            foreach (var i in inputs) i.HoldFart();
            yield return new WaitForSeconds(0.8f);
            float worst = 0f, sum = 0f; int frames = 0;
            for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime) { worst = Mathf.Max(worst, Time.unscaledDeltaTime); sum += Time.unscaledDeltaTime; frames++; yield return null; }
            int particles = 0;
            foreach (var ps in FindObjectsByType<ParticleSystem>()) particles += ps.particleCount;
            var gameplay = Camera.main;
            yield return Shot("09_all_four_gameplay_camera", gameplay.transform.position, gameplay.transform.position + gameplay.transform.forward * 10f, gameplay.fieldOfView, true);
            foreach (var i in inputs) i.Release();
            Debug.Log($"[VisualProbe] four players sustained: {particles} live particles, avg frame {sum / Mathf.Max(1, frames) * 1000f:F1} ms, worst {worst * 1000f:F1} ms");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("10_gameplay_camera_idle", gameplay.transform.position, gameplay.transform.position + gameplay.transform.forward * 10f, gameplay.fieldOfView, true);
            Debug.Log("[VisualProbe] DONE " + outDir);
            Running = false;
        }

        IEnumerator Shot(string name, Vector3 localPos, Vector3 localLook, float fov = 45f, bool world = false)
        {
            yield return new WaitForEndOfFrame();
            Vector3 p = world ? localPos : raft.transform.TransformPoint(localPos);
            Vector3 l = world ? localLook : raft.transform.TransformPoint(localLook);
            probe.transform.SetPositionAndRotation(p, Quaternion.LookRotation(l - p, Vector3.up));
            probe.fieldOfView = fov;
            var rt = RenderTexture.GetTemporary(960, 540, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 2;
            probe.targetTexture = rt;
            probe.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            probe.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
