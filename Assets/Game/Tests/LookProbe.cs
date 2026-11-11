using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using FartOnline.Game.Endless;
using FartOnline.Obstacles;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Look check for the bright cartoon pass (menu Tools > Fart Online > Tests > Run Look Probe):
    ///  1. gameplay with an AI crew through the chase camera, with the HUD and the on-screen controller (phone
    ///     landscape) and without touch controls (desktop);
    ///  2. close-ups of each spiked hazard and of the bank decor, found in the planned rows of a test river.
    /// Writes ArtSource/Game/Previews/Look/*.png.
    /// </summary>
    public class LookProbe : MonoBehaviour
    {
        string dir;
        GameplayHUD hud;
        Canvas canvas;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            float savedBest = PlayerPrefs.GetFloat("fo.endless.best", 0f);
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Game", "Previews", "Look"));
            Directory.CreateDirectory(dir);
            hud = FindAnyObjectByType<GameplayHUD>();
            canvas = hud.Canvas;
            var session = FindAnyObjectByType<GameSession>();
            var run = FindAnyObjectByType<GameRunManager>();
            var river = FindAnyObjectByType<EndlessRiver>();
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("menu_desktop", 1920, 1080, false);

            TestCrew.StartSolo(session, SoloAssist.Standard, 3);
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSecondsRealtime(i == 0 ? 6f : 4f);
                yield return Shot("play_phone_" + i, 2340, 1080, true);
                yield return Shot("play_desktop_" + i, 1920, 1080, false);
                Debug.Log($"[LookProbe] shot {i}: state {run.State}, score {run.Stats.score:F0}");
            }
            session.EndSession();
            yield return null;

            // close-ups: a test river built ahead, hazards found in the planned rows
            var cam = Camera.main;
            var dummy = new GameObject("LookTarget").transform;
            river.target = dummy;
            KinematicObstacle.ClockSource = () => Time.time;
            var wanted = new List<ObstacleKind> { ObstacleKind.SpikeSwing, ObstacleKind.MineField, ObstacleKind.SpikeLogRoll, ObstacleKind.SpikeBridge };
            for (int seed = 11; seed < 40 && wanted.Count > 0; seed++)
            {
                river.ResetRiver(seed);
                river.BuildUpTo(500f);
                dummy.position = river.path.SampleAtDistance(300f).position;
                yield return null;
                foreach (var row in new List<ObstacleRow>(river.Planner.Rows))
                    foreach (var it in row.items)
                    {
                        if (!wanted.Contains(it.kind) || row.distance < river.TargetDistance + 15f) continue;
                        wanted.Remove(it.kind);
                        dummy.position = river.path.SampleAtDistance(row.distance).position;
                        yield return null;
                        var s = river.path.SampleAtDistance(row.distance - 13f);
                        var at = river.path.SampleAtDistance(row.distance);
                        Vector3 look = at.position + at.right * it.x + Vector3.up * 1.2f;
                        Vector3 eye = s.position + s.right * (it.x * 0.6f) + Vector3.up * 5.5f;
                        yield return Close("hazard_" + it.kind, cam, eye, look, 58f);
                    }
            }
            // bank decor: look at the left bank from mid-river
            {
                var s = river.path.SampleAtDistance(river.TargetDistance + 60f);
                Vector3 eye = s.position + Vector3.up * 4f - Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized * 6f;
                yield return Close("banks_ahead", cam, eye, s.position + Vector3.up * 3f + Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized * 30f, 62f);
                yield return Close("banks_left", cam, s.position + Vector3.up * 3f + s.right * 4f, s.position - s.right * 14f + Vector3.up * 6f + s.tangent * 6f, 62f);
            }
            Destroy(dummy.gameObject);
            PlayerPrefs.SetFloat("fo.endless.best", savedBest);
            PlayerPrefs.Save();
            GameplayHUD.ViewportOverride = null;
            FartOnline.CameraRig.RaftChaseCamera.AspectOverride = null;
            TouchInput.ForceForTests = false;
            Debug.Log("[LookProbe] DONE " + dir + " missing: " + string.Join(",", wanted));
        }

        IEnumerator Close(string name, Camera cam, Vector3 eye, Vector3 look, float fov)
        {
            var go = new GameObject("LookCam");
            var c = go.AddComponent<Camera>();
            c.CopyFrom(cam);
            c.enabled = false;
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            c.fieldOfView = fov;
            yield return new WaitForSecondsRealtime(0.4f);
            var px = Render(c, 1600, 900, null);
            Save(name, px, 1600, 900);
            Destroy(go);
        }

        IEnumerator Shot(string name, int w, int h, bool touch)
        {
            GameplayHUD.ViewportOverride = new Vector2(w, h);
            TouchInput.ForceForTests = touch;
            FartOnline.CameraRig.RaftChaseCamera.AspectOverride = w / (float)h;
            hud.UpdateResponsive(true);
            for (int i = 0; i < 10; i++) yield return null;
            var cam = Camera.main;
            var scaler = canvas.GetComponent<CanvasScaler>();
            var camGo = new GameObject("UiCaptureCam");
            var uiCam = camGo.AddComponent<Camera>();
            uiCam.cullingMask = 1 << 5;
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.enabled = false;
            SetLayer(canvas.transform, 5);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCam;
            canvas.planeDistance = 1f;
            scaler.enabled = false;
            canvas.scaleFactor = hud.ComputedScale;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var scene = Render(cam, w, h, null);
            var onBlack = Render(uiCam, w, h, Color.black);
            var onWhite = Render(uiCam, w, h, Color.white);
            var outPx = new Color[w * h];
            for (int k = 0; k < outPx.Length; k++)
            {
                Color b = onBlack[k], wh = onWhite[k], sc = scene[k];
                float a = Mathf.Clamp01(1f - ((wh.r - b.r) + (wh.g - b.g) + (wh.b - b.b)) / 3f);
                outPx[k] = new Color(b.r + sc.r * (1f - a), b.g + sc.g * (1f - a), b.b + sc.b * (1f - a), 1f);
            }
            Save(name, outPx, w, h);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            scaler.enabled = true;
            SetLayer(canvas.transform, 0);
            Destroy(camGo);
        }

        void Save(string name, Color[] linear, int w, int h)
        {
            var px = new Color32[linear.Length];
            for (int k = 0; k < px.Length; k++)
            {
                Color c = linear[k];
                px[k] = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), 1f);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        static Color[] Render(Camera c, int w, int h, Color? clear)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = c.targetTexture;
            if (clear.HasValue) c.backgroundColor = clear.Value;
            c.targetTexture = rt;
            c.aspect = w / (float)h;
            c.Render();
            c.targetTexture = prev;
            c.ResetAspect();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels();
            Destroy(t);
            return px;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
