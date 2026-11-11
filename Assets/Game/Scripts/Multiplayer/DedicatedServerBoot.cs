using System;
using System.Collections;
using UnityEngine;

namespace FartOnline.Game.Net
{
    /// <summary>
    /// Starts the game as a headless room server when launched with -fo-dedicated (by Backend/room-service):
    ///   FartOnlineServer -batchmode -nographics -fo-dedicated -fo-port 17800 -fo-listen 127.0.0.1 -fo-room ABC123
    /// The ticket secret comes from the FO_TICKET_SECRET environment variable. Runs the simulation at 60 frames a
    /// second (physics stays at its fixed step), with audio muted.
    /// </summary>
    public class DedicatedServerBoot : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has("-fo-dedicated")) return;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
            AudioListener.volume = 0f;
            var go = new GameObject("DedicatedServerBoot");
            DontDestroyOnLoad(go);
            go.AddComponent<DedicatedServerBoot>();
        }

        static bool Has(string flag) => Array.IndexOf(System.Environment.GetCommandLineArgs(), flag) >= 0;

        static string Arg(string flag, string fallback)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, flag);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
        }

        IEnumerator Start()
        {
            yield return null;                       // let the scene's components finish Start()
            var nsm = NetworkSessionManager.Instance;
            ushort port = ushort.TryParse(Arg("-fo-port", "17800"), out var p) ? p : (ushort)17800;
            string listen = Arg("-fo-listen", "0.0.0.0");
            string room = Arg("-fo-room", "LOCAL1").ToUpperInvariant();
            if (nsm == null || !nsm.StartDedicated(port, listen, room))
            {
                Console.WriteLine("[FOROOM] failed");
                Debug.LogError("[Net] dedicated server failed to start");
                Application.Quit(1);
            }
        }
    }
}
