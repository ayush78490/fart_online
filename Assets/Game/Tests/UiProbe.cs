using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace FartOnline.Game.Tests
{
    /// <summary>
    /// Play-mode capture of every HUD screen at the 1920x1080 reference resolution, independent of the Game view
    /// size: the gameplay camera renders the scene, then the HUD canvas (switched to Screen Space - Camera on a
    /// UI-only camera) is rendered over black and over white; the difference gives exact coverage, and the UI is
    /// composited over the scene in linear space. Writes
    /// ArtSource/Game/Previews/UI/*.png: main menu, how-to-play, settings, lobby, countdown, HUD in play, pause and
    /// results (endless: crash banner and game over board). The solo run uses two AI crew so the player cards and radar have content.
    /// </summary>
    public class UiProbe : MonoBehaviour
    {
        /// <summary>Capture the online screens instead (account, play online, friends with sample data, invite, lobby).</summary>
        public static bool OnlineScreens;
        string dir;
        GameplayHUD hud;
        Canvas canvas;

        IEnumerator Start()
        {
            Application.runInBackground = true;
            float savedBest = PlayerPrefs.GetFloat("fo.endless.best", 0f);     // tests must not change the player's record
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Game", "Previews", "UI"));
            Directory.CreateDirectory(dir);
            hud = FindAnyObjectByType<GameplayHUD>();
            canvas = hud.GetComponentInChildren<Canvas>();
            var session = FindAnyObjectByType<GameSession>();
            var run = FindAnyObjectByType<GameRunManager>();
            var raft = FindAnyObjectByType<SharedRaftController>();

            yield return Wait(1.5f);
            if (OnlineScreens) { OnlineScreens = false; yield return Online(); yield break; }
            yield return Grab("30_main_menu");
            yield return Overlay("Controls", "31_how_to_play");
            yield return Overlay("Settings", "32_settings");

            run.EnterLobby();
            yield return Wait(0.6f);
            yield return Grab("33_lobby");
            run.ReturnToMenu();

            TestCrew.StartSolo(session, SoloAssist.Standard, 2);
            yield return Wait(1.2f);
            yield return Grab("34_hud_countdown");
            yield return Wait(9f);
            yield return Grab("35_hud_playing");
            run.Pause();
            yield return Wait(0.6f);
            yield return Grab("36_pause");
            run.Resume();
            yield return Wait(0.5f);

            yield return Wait(8f);
            yield return Grab("35b_hud_obstacles");
            if (run.Endless)
            {
                FindAnyObjectByType<RaftRecoveryManager>().Crash("a boulder");
                yield return Wait(0.4f);
                yield return Grab("37a_crash");
                yield return Wait(1.6f);
                yield return Grab("37_game_over");
            }
            else
            {
                var go = typeof(GameRunManager).GetMethod("Go", BindingFlags.Instance | BindingFlags.NonPublic);
                go.Invoke(run, new object[] { RunState.Finished });
                yield return Wait(1.2f);
                yield return Grab("37_results");
            }
            PlayerPrefs.SetFloat("fo.endless.best", savedBest);
            PlayerPrefs.Save();
            Debug.Log("[UiProbe] DONE " + dir);
        }

        IEnumerator Online()
        {
            var ui = FindAnyObjectByType<OnlineUI>();
            var acc = FartOnline.Game.Online.AccountService.Instance;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            System.Action<string> call = m => typeof(OnlineUI).GetMethod(m, flags).Invoke(ui, null);
            yield return Grab("40_menu_online");
            call("ShowAccount");
            yield return Wait(0.6f);
            yield return Grab("41_account_login");
            typeof(OnlineUI).GetField("signUpMode", flags).SetValue(ui, true);
            call("RefreshAccountMode");
            yield return Wait(0.3f);
            yield return Grab("42_account_signup");
            // pretend to be signed in with a few friends in every state
            var accType = typeof(FartOnline.Game.Online.AccountService);
            accType.GetProperty("Username").SetValue(acc, "pig_tester");
            accType.GetField("session", flags).SetValue(acc, new FartOnline.Game.Online.AuthSession { access_token = "x" });
            var list = (System.Collections.Generic.List<FartOnline.Game.Online.FriendEntry>)accType.GetField("friends", flags).GetValue(acc);
            list.Clear();
            list.Add(new FartOnline.Game.Online.FriendEntry { friend_id = "a", username = "bacon_bob", status = "pending", incoming = true, request_id = 1 });
            list.Add(new FartOnline.Game.Online.FriendEntry { friend_id = "b", username = "Hamlet99", status = "accepted", online = true, request_id = 2 });
            list.Add(new FartOnline.Game.Online.FriendEntry { friend_id = "c", username = "TrufflePig", status = "accepted", online = false, request_id = 3 });
            list.Add(new FartOnline.Game.Online.FriendEntry { friend_id = "d", username = "oinker", status = "pending", incoming = false, request_id = 4 });
            call("ShowOnline");
            yield return Wait(0.6f);
            yield return Grab("43_play_online");
            call("ShowFriends");
            acc.FriendsVisible = false;                         // no polling against a fake session
            yield return Wait(0.6f);
            yield return Grab("44_friends");
            call("HideAll");
            typeof(OnlineUI).GetMethod("OnInvite", flags).Invoke(ui, new object[] { new FartOnline.Game.Online.InviteEntry { invite_id = 9, from_username = "Hamlet99", room_code = "K7QH2M" } });
            yield return Wait(0.6f);
            yield return Grab("45_invite_popup");
            accType.GetField("session", flags).SetValue(acc, null);
            accType.GetProperty("Username").SetValue(acc, "");
            list.Clear();
            Debug.Log("[UiProbe] ONLINE DONE " + dir);
        }

        static IEnumerator Wait(float s) { for (float t = 0f; t < s; t += Time.unscaledDeltaTime) yield return null; }

        IEnumerator Overlay(string layer, string shot)
        {
            var rt = canvas.transform.Find(layer).gameObject;
            rt.SetActive(true);
            rt.transform.SetAsLastSibling();
            yield return Wait(0.6f);
            yield return Grab(shot);
            rt.SetActive(false);
        }

        IEnumerator Grab(string name)
        {
            const int W = 1920, H = 1080;
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
            canvas.scaleFactor = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();

            // scene alone, then the UI alone over black and over white (difference matting gives exact alpha)
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

        /// <summary>Renders one camera into a linear render texture and returns linear pixels.</summary>
        static Color[] Render(Camera c, int w, int h, Color? clear)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = c.targetTexture;
            if (clear.HasValue) c.backgroundColor = clear.Value;
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = prev;
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
