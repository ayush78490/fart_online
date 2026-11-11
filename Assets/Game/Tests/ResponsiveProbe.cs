using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Renders the UI at phone, tablet and desktop sizes in both orientations (menu Tools > Fart Online > Tests >
    /// Run Responsive Probe). The HUD is laid out for each viewport through <see cref="GameplayHUD.ViewportOverride"/>
    /// and composited over the scene exactly like <see cref="UiProbe"/>. Touch controls are forced on for the mobile
    /// sizes. Writes ArtSource/Game/Previews/Responsive/*.png.
    /// </summary>
    public class ResponsiveProbe : MonoBehaviour
    {
        static readonly (string name, int w, int h, bool touch)[] Views =
        {
            ("phone_portrait", 1080, 2340, true),
            ("phone_landscape", 2340, 1080, true),
            ("tablet_portrait", 1536, 2048, true),
            ("tablet_landscape", 2048, 1536, true),
            ("desktop", 1920, 1080, false),
        };

        string dir;
        GameplayHUD hud;
        Canvas canvas;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            float savedBest = PlayerPrefs.GetFloat("fo.endless.best", 0f);
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Game", "Previews", "Responsive"));
            Directory.CreateDirectory(dir);
            hud = FindAnyObjectByType<GameplayHUD>();
            canvas = hud.Canvas;
            var session = FindAnyObjectByType<GameSession>();
            var run = FindAnyObjectByType<GameRunManager>();
            yield return new WaitForSecondsRealtime(1.5f);

            foreach (var v in Views) yield return Shot(v, "menu");
            TestCrew.StartSolo(session, SoloAssist.Standard, 2);
            yield return new WaitForSecondsRealtime(7f);
            foreach (var v in Views) yield return Shot(v, "hud");
            run.Pause();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot(Views[0], "pause");
            yield return Shot(Views[1], "pause");
            run.Resume();
            var settings = canvas.transform.Find("Settings").gameObject;
            settings.SetActive(true);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot(Views[0], "settings");
            settings.SetActive(false);
            var controls = canvas.transform.Find("Controls").gameObject;
            controls.SetActive(true);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot(Views[0], "howtoplay");
            controls.SetActive(false);
            FindAnyObjectByType<RaftRecoveryManager>().Crash("a stamping press");
            yield return new WaitForSecondsRealtime(2.2f);
            yield return Shot(Views[0], "gameover");
            yield return Shot(Views[3], "gameover");

            GameplayHUD.ViewportOverride = null;
            FartOnline.CameraRig.RaftChaseCamera.AspectOverride = null;
            TouchInput.ForceForTests = false;
            hud.UpdateResponsive(true);
            PlayerPrefs.SetFloat("fo.endless.best", savedBest);
            PlayerPrefs.Save();
            Debug.Log("[ResponsiveProbe] DONE " + dir);
        }

        IEnumerator Shot((string name, int w, int h, bool touch) v, string what)
        {
            GameplayHUD.ViewportOverride = new Vector2(v.w, v.h);
            TouchInput.ForceForTests = v.touch;
            FartOnline.CameraRig.RaftChaseCamera.AspectOverride = v.w / (float)v.h;
            hud.UpdateResponsive(true);
            for (int i = 0; i < 20; i++) yield return null;      // let the camera settle on the new framing
            yield return Grab(what + "_" + v.name, v.w, v.h);
        }

        IEnumerator Grab(string name, int W, int H)
        {
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
            var scene = Render(cam, W, H, null);
            var onBlack = Render(uiCam, W, H, Color.black);
            var onWhite = Render(uiCam, W, H, Color.white);
            var outPx = new Color32[W * H];
            for (int k = 0; k < outPx.Length; k++)
            {
                Color b = onBlack[k], w = onWhite[k], sc = scene[k];
                float a = Mathf.Clamp01(1f - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3f);
                Color c = new Color(b.r + sc.r * (1f - a), b.g + sc.g * (1f - a), b.b + sc.b * (1f - a), 1f);
                outPx[k] = new Color(Mathf.LinearToGammaSpace(c.r), Mathf.LinearToGammaSpace(c.g), Mathf.LinearToGammaSpace(c.b), 1f);
            }
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.SetPixels32(outPx);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            scaler.enabled = true;
            SetLayer(canvas.transform, 0);
            Destroy(camGo);
        }

        static Color[] Render(Camera c, int w, int h, Color? clear)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = c.targetTexture;
            float aspect = c.aspect;
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
