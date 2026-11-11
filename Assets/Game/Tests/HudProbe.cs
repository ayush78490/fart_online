using System.Collections;
using System.IO;
using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Play-mode check of the HUD and menus (Game view screenshots incl. the overlay canvas) and a short solo run with
    /// AI crew: captures the main menu, starts solo with two AI crew members, lets the AI drive for a while and logs
    /// how far down the course the raft got, then captures the HUD and the pause menu.
    /// </summary>
    public class HudProbe : MonoBehaviour
    {
        string dir;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Game", "Previews"));
            Directory.CreateDirectory(dir);
            var session = FindAnyObjectByType<GameSession>();
            var run = FindAnyObjectByType<GameRunManager>();
            var raft = FindAnyObjectByType<SharedRaftController>();
            yield return new WaitForSeconds(1.5f);
            yield return Grab("20_main_menu");

            TestCrew.StartSolo(session, SoloAssist.Standard, 2);
            raft.seats.Seats[0].Vacate();                      // the "human" sits out: only the AI crew drives this test
            for (float t = 0; t < 4.5f; t += Time.deltaTime) yield return null;
            yield return Grab("21_hud_playing");
            float d0 = Progress(raft);
            for (float t = 0; t < 25f; t += Time.deltaTime) yield return null;
            float d1 = Progress(raft);
            yield return Grab("22_hud_ai_crew");
            Debug.Log($"[HudProbe] AI crew (2) drove from {d0:F0} m to {d1:F0} m along the river in 25 s, checkpoint {run.Stats.checkpointIndex}, wipeouts {run.Stats.wipeouts}, state {run.State}");
            run.Pause();
            yield return null;
            yield return Grab("23_pause_menu");
            run.Resume();
            Debug.Log("[HudProbe] DONE");
        }

        static float Progress(SharedRaftController raft) => raft.River != null ? raft.River.Closest(raft.transform.position, out _).distance : 0f;

        IEnumerator Grab(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
