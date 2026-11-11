using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using FartOnline.Environment;
using FartOnline.Game.Endless;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Films trailer footage (menu Tools > Fart Online > Trailer > Record Footage / Record Test).
    /// A full AI crew plays the endless game while this drives the gameplay camera through a cycle of cinematic rigs
    /// and writes every frame (fixed 60 fps capture clock, so the footage is smooth however slow the capture is) to
    /// ArtSource/Trailer/raw/NNNNNN.jpg, with ArtSource/Trailer/raw/log.csv saying which rig and game state each frame has.
    /// The HUD is not filmed (it is a screen overlay and the camera renders to a texture).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class TrailerDirector : MonoBehaviour
    {
        public static bool TestOnly;
        public static bool CrashMode;
        float playT; int crashRuns; bool aiOff;
        public static string RigFilter = "";

        static int W = 2560, H = 1440;
        const int Fps = 60;
        public static bool Vert;
        string label;
        Canvas canvas;
        FartOnline.CameraRig.RaftChaseCamera chaseCam;
        static readonly string[] RigNames = { "chase", "lowfront", "orbit", "top", "side", "flyby", "hazard", "pigface", "pigrear" };

        Camera cam;
        RenderTexture rt;
        Texture2D tex;
        string dir;
        StringBuilder log = new StringBuilder();
        int frame, pending;

        GameSession session;
        GameRunManager run;
        EndlessRiver river;
        Transform raft;

        int rig = -1, shotId, shotCount;
        float shotStart, shotLength;
        Vector3 anchor, anchorLook;
        float side = 1f, orbit0;
        int pigSeat;
        ObstacleRow hazardRow;
        string pendingEvent = "";
        bool crashed;
        int crashFrames;
        List<int> order = new List<int>();

        IEnumerator Start()
        {
            Application.runInBackground = true;
            AudioListener.volume = 0f;
            float savedBest = PlayerPrefs.GetFloat("fo.endless.best", 0f);
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Trailer", Vert ? "rawvert" : CrashMode ? "rawcrash" : TestOnly ? "rawtest" : "raw"));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            if (Vert) { W = 1080; H = 1920; } else { W = 2560; H = 1440; }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            session = FindAnyObjectByType<GameSession>();
            run = session.run;
            river = FindAnyObjectByType<EndlessRiver>();
            raft = session.raft.transform;
            cam = session.gameplayCamera != null ? session.gameplayCamera : Camera.main;
            var chase = cam.GetComponent<FartOnline.CameraRig.RaftChaseCamera>();
            chaseCam = chase;
            if (chase != null) chase.enabled = false;
            foreach (var mb in cam.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name == "RaftCameraShake") mb.enabled = false;

            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = Vert ? 8 : 4 };
            rt.Create();
            tex = new Texture2D(W, H, TextureFormat.RGB24, false, false);
            cam.targetTexture = rt;
            cam.aspect = W / (float)H;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 600f);

            for (int i = 0; i < RigNames.Length; i++)
                if (string.IsNullOrEmpty(RigFilter) || RigFilter.Contains(RigNames[i])) order.Add(i);

            yield return new WaitForSecondsRealtime(1f);
            session.recovery.Crashed += what => { pendingEvent = "crash:" + what; crashed = true; crashFrames = 0; };
            if (Vert) { Time.captureFramerate = Fps; yield return StartCoroutine(Vertical()); shotCount = 1000000; }
            else { StartRun(); Time.captureFramerate = Fps; }

            float shotSeconds = TestOnly ? 1.2f : 3.6f;
            int cycles = TestOnly ? 1 : 3;
            int wantedShots = order.Count * cycles;
            var eof = new WaitForEndOfFrame();
            int guard = 0;
            while (shotCount <= wantedShots && guard++ < 40000)
            {
                yield return eof;
                if (run.State == RunState.Playing || run.State == RunState.Checkpoint || crashed)
                {
                    if (CrashMode)
                    {
                        if (rig < 0) { NextShot(); rig = new[] { 0, 1, 2, 3, 4, 0 }[crashRuns % 6]; Time.timeScale = 1f; shotLength = 9f; playT = Time.time; aiOff = false; }
                        if (!aiOff && Time.time - playT > 3.5f)
                        {
                            aiOff = true;
                            foreach (var ai in FindObjectsByType<AICrewInput>(FindObjectsSortMode.None)) ai.enabled = false;
                        }
                        if (!crashed && Time.time - playT > 40f) { crashed = true; crashFrames = 999; pendingEvent = "timeout"; }
                    }
                    else if (rig < 0 || (!crashed && ShotOver(shotSeconds))) NextShot();
                    Capture();
                    if (crashed)
                    {
                        Time.timeScale = 0.35f;
                        if (++crashFrames > 170)
                        {
                            crashed = false;
                            Time.timeScale = 1f;
                            rig = -1;
                            crashRuns++;
                            if (CrashMode && crashRuns >= 6) break;
                            if (CrashMode) { session.EndSession(); StartRun(); continue; }
                            run.RestartRun();
                            MakeFullCrew();
                        }
                    }
                }
            }
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            while (Volatile.Read(ref pending) > 0) yield return null;
            File.WriteAllText(Path.Combine(dir, "log.csv"), "frame,shot,rig,state,score,timescale,event\n" + log);
            cam.targetTexture = null;
            cam.ResetAspect();
            PlayerPrefs.SetFloat("fo.endless.best", savedBest);
            PlayerPrefs.Save();
            AudioListener.volume = 1f;
            Debug.Log("[TrailerDirector] DONE " + frame + " frames in " + dir);
        }

        void Ui(bool on)
        {
            canvas.enabled = on;
            if (chaseCam != null) { chaseCam.enabled = on; if (on) chaseCam.SnapToTarget(); }
            if (on) { rig = -1; Time.timeScale = 1f; }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>The 9:16 take: menu, countdown and HUD play through the game camera (the canvas is drawn by the filming
        /// camera), then cinematic rigs and a drift into a hazard in slow motion; the last run keeps the HUD up to the game over board.</summary>
        IEnumerator Vertical()
        {
            var eof = new WaitForEndOfFrame();
            var hud = FindAnyObjectByType<GameplayHUD>();
            canvas = hud.Canvas;
            GameplayHUD.ViewportOverride = new Vector2(W, H);
            TouchInput.ForceForTests = true;
            FartOnline.CameraRig.RaftChaseCamera.AspectOverride = W / (float)H;
            hud.UpdateResponsive(true);
            for (int i = 0; i < 5; i++) yield return null;
            SetLayer(canvas.transform, 5);
            cam.cullingMask |= 1 << 5;
            cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.15f);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.3f;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            canvas.scaleFactor = hud.ComputedScale;
            order.Clear();
            order.AddRange(new[] { 6, 0, 3, 5, 1, 7, 6, 2, 4, 6, 8 });
            int[] crashRigs = { 4, 0, 3, 1, 2 };
            int runs = TestOnly ? 2 : 6;

            Ui(true);
            label = "menu";
            for (int i = 0; i < (TestOnly ? 60 : 200); i++) { yield return eof; Capture(); }

            for (int r = 0; r < runs; r++)
            {
                bool last = r == runs - 1, withHud = r == 0 || last;
                if (r > 0) session.EndSession();
                crashed = false;
                StartRun();
                Ui(withHud);
                canvas.scaleFactor = hud.ComputedScale;
                label = "count";
                int guard = 0;
                while (run.State != RunState.Playing && guard++ < 1200) { yield return eof; if (withHud) Capture(); }
                float t0 = Time.time;
                label = "hudplay";
                float hudSecs = r == 0 ? 4f : last ? 6f : 0f;
                while (Time.time - t0 < hudSecs && !crashed) { yield return eof; Capture(); }
                if (!last)
                {
                    Ui(false);
                    label = null;
                    rig = -1;
                    t0 = Time.time;
                    float cine = TestOnly ? 4f : 11f;
                    while (Time.time - t0 < cine && !crashed)
                    {
                        if (rig < 0 || ShotOver(3f)) NextShot();
                        yield return eof;
                        Capture();
                    }
                    foreach (var ai in FindObjectsByType<AICrewInput>(FindObjectsSortMode.None)) ai.enabled = false;
                    if (!crashed)
                    {
                        NextShot();
                        rig = crashRigs[r % crashRigs.Length];
                        Time.timeScale = 1f;
                        shotLength = 12f;
                    }
                    t0 = Time.time;
                    while (!crashed && Time.time - t0 < 40f) { yield return eof; Capture(); }
                    Time.timeScale = 0.35f;
                    for (int i = 0; i < 170; i++) { yield return eof; Capture(); }
                    Time.timeScale = 1f;
                }
                else
                {
                    foreach (var ai in FindObjectsByType<AICrewInput>(FindObjectsSortMode.None)) ai.enabled = false;
                    t0 = Time.time;
                    while (!crashed && Time.time - t0 < 40f) { yield return eof; Capture(); }
                    label = "hudover";
                    for (int i = 0; i < 330; i++) { yield return eof; Capture(); }
                }
            }
            label = null;
            GameplayHUD.ViewportOverride = null;
            TouchInput.ForceForTests = false;
            FartOnline.CameraRig.RaftChaseCamera.AspectOverride = null;
        }

        void StartRun()
        {
            TestCrew.StartSolo(session, SoloAssist.Standard, 3, 1f);
            MakeFullCrew();
        }

        /// <summary>Seat 1 (the idle human) gets an AI too, so all four pigs work.</summary>
        void MakeFullCrew()
        {
            var seat = session.raft.seats.Seats[0];
            if (seat.InputSource is AICrewInput) return;
            var go = new GameObject("AICrew_Trailer");
            go.transform.SetParent(session.transform, false);
            var ai = go.AddComponent<AICrewInput>();
            ai.ReadyFor(session.raft, seat, 1f);
            seat.Occupy("Player 1", RaftSeatManager.PlayerColors[0], ai, Vert, !Vert, 0);
        }

        bool ShotOver(float seconds)
        {
            float t = Time.time - shotStart;
            if (t >= shotLength) return true;
            float d = river.path.Closest(raft.position, out _).distance;
            if (rig == 5 && d > river.path.Closest(anchor, out _).distance + 9f) return true;
            if (rig == 6 && hazardRow != null && d > hazardRow.distance + 3.5f) return true;
            return false;
        }

        void NextShot()
        {
            rig = order[shotCount % order.Count];
            shotCount++;
            shotId++;
            shotStart = Time.time;
            shotLength = TestOnly ? 1.2f : Vert ? 3.0f : 3.6f;
            side = (shotId % 2 == 0) ? 1f : -1f;
            orbit0 = Random.Range(0f, 360f);
            Time.timeScale = (rig == 7 || rig == 8) ? 0.5f : 1f;
            if (rig == 7 || rig == 8) shotLength *= 0.6f;
            var s = river.path.Closest(raft.position, out _);
            if (rig == 5)
            {
                var a = river.path.SampleAtDistance(s.distance + 17f);
                anchor = a.position + a.right * side * (river.HalfWidth * 0.55f) + Vector3.up * 0.9f;
                shotLength = 6f;
            }
            if (rig == 6)
            {
                hazardRow = null;
                foreach (var row in river.Planner.Rows)
                    if (row.distance > s.distance + 14f && (hazardRow == null || row.distance < hazardRow.distance)) hazardRow = row;
                if (hazardRow != null)
                {
                    var a = river.path.SampleAtDistance(hazardRow.distance + 7f);
                    anchor = a.position + a.right * Mathf.Clamp(hazardRow.safeX, -river.HalfWidth + 1.5f, river.HalfWidth - 1.5f) + Vector3.up * 1.5f;
                    shotLength = 8f;
                }
                else rig = 0;
            }
            if (rig == 7 || rig == 8) pigSeat = (pigSeat + 1) % 4;
        }

        void LateUpdate()
        {
            if (rig < 0 || raft == null || cam == null) return;
            Vector3 P = raft.position;
            var s = river.path.Closest(P, out _);
            Vector3 F = Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized;
            Vector3 R = s.right;
            float hw = river.HalfWidth;
            float u = Mathf.Clamp01((Time.time - shotStart) / Mathf.Max(0.01f, shotLength));
            Vector3 pos, look, up = Vector3.up;
            float fov;
            bool clamp = true;
            switch (rig)
            {
                default: // chase: lower and closer than the game camera, drifting across
                    pos = P - F * 7.5f + Vector3.up * 3.1f + R * Mathf.Lerp(-1.6f, 1.6f, u) * side;
                    look = P + F * 5f + Vector3.up * 0.8f;
                    fov = 52f;
                    break;
                case 1: // low on the water ahead of the raft, riding backwards
                    pos = P + F * Mathf.Lerp(9.5f, 6.5f, u) + R * 1.6f * side + Vector3.up * 0.75f;
                    look = P + Vector3.up * 0.8f;
                    fov = 38f;
                    break;
                case 2: // orbit
                {
                    float a = (orbit0 + 110f * u * side) * Mathf.Deg2Rad;
                    pos = P + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5.4f + Vector3.up * Mathf.Lerp(1.5f, 2.4f, u);
                    look = P + Vector3.up * 0.7f;
                    fov = 42f;
                    break;
                }
                case 3: // drone, straight down, slowly turning
                    pos = P + Vector3.up * 15f;
                    look = P;
                    up = Quaternion.AngleAxis(Mathf.Lerp(-14f, 14f, u) * side, Vector3.up) * F;
                    fov = 44f;
                    clamp = false;
                    break;
                case 4: // long lens from the bank side
                    pos = P + R * side * (hw - 1f) - F * Mathf.Lerp(3f, 0.5f, u) + Vector3.up * 1.15f;
                    look = P + Vector3.up * 0.65f;
                    fov = 30f;
                    break;
                case 5: // waiting ahead, the raft flies by
                    pos = anchor;
                    look = P + Vector3.up * 0.7f;
                    fov = 40f;
                    clamp = false;
                    break;
                case 6: // behind the next hazard, the raft comes through the gap
                    pos = anchor;
                    look = P + Vector3.up * 0.9f;
                    fov = 46f;
                    clamp = false;
                    break;
                case 7: // a pig's face, from across the raft
                case 8: // behind a pig: the gas comes at the lens
                {
                    var seat = session.raft.seats.Seats[pigSeat].transform;
                    Vector3 S = seat.position;
                    Vector3 O = Vector3.ProjectOnPlane(S - P, Vector3.up).normalized;
                    Vector3 T = Vector3.Cross(Vector3.up, O);
                    if (rig == 7)
                    {
                        pos = S - O * Mathf.Lerp(2.3f, 1.9f, u) + T * Mathf.Lerp(-0.5f, 0.5f, u) + Vector3.up * 0.95f;
                        look = S + Vector3.up * 0.6f;
                        fov = 44f;
                    }
                    else
                    {
                        pos = S + O * Mathf.Lerp(3.4f, 4.2f, u) + T * 1.3f * side + Vector3.up * 0.9f;
                        look = S + Vector3.up * 0.5f;
                        fov = 40f;
                    }
                    clamp = false;
                    break;
                }
            }
            if (clamp)
            {
                // stay over the water, off the spiked banks
                var cs = river.path.Closest(pos, out float lateral);
                float max = hw - 0.9f;
                if (Mathf.Abs(lateral) > max) pos -= cs.right * (lateral - Mathf.Sign(lateral) * max);
            }
            if (Vert)
            {
                // a tall frame sees a narrow slice: pull back and widen
                float[] vf = { 62f, 55f, 58f, 58f, 66f, 60f, 64f, 50f, 58f };
                fov = vf[rig];
                if (rig == 0) { pos = P - F * 9f + Vector3.up * 4.6f + R * Mathf.Lerp(-1.2f, 1.2f, u) * side; look = P + F * 7f + Vector3.up * 0.8f; }
                if (rig == 1) pos += Vector3.up * 0.35f;
                if (rig == 2) pos = P + (pos - P) * 1.2f;
                if (rig == 3) pos = P + Vector3.up * 17f;
                if (rig == 6) pos += Vector3.up * 0.7f;
                if (rig == 4) { pos = P + R * side * 3.2f - F * Mathf.Lerp(6.5f, 5.5f, u) + Vector3.up * 4.2f; look = P + F * 2.5f + Vector3.up * 0.6f; fov = 60f; }
            }
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, up));
            cam.fieldOfView = fov;
        }

        void Capture()
        {
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            RenderTexture.active = active;
            byte[] raw = tex.GetRawTextureData();
            string path = Path.Combine(dir, frame.ToString("D6") + ".jpg");
            while (Volatile.Read(ref pending) > 10) Thread.Sleep(2);
            Interlocked.Increment(ref pending);
            Task.Run(() =>
            {
                try { File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(raw, GraphicsFormat.R8G8B8_SRGB, (uint)W, (uint)H, 0, 95)); }
                finally { Interlocked.Decrement(ref pending); }
            });
            log.Append(frame).Append(',').Append(shotId).Append(',').Append(label ?? RigNames[Mathf.Max(0, rig)]).Append(',')
               .Append(run.State).Append(',').Append(run.Stats.score.ToString("F0")).Append(',')
               .Append(Time.timeScale.ToString("F2")).Append(',').Append(pendingEvent).Append('\n');
            pendingEvent = "";
            frame++;
        }
    }
}
