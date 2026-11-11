using UnityEngine;

namespace FartOnline.Game.Online
{
    /// <summary>
    /// Where the online services live. One asset: <c>Assets/Game/Resources/FartOnlineBackend.asset</c>
    /// (menu <c>Tools > Fart Online > Online > Backend Settings</c>).
    ///  - Supabase: accounts, usernames, friends, invites. The anon key is a PUBLIC key (it is shipped in every build);
    ///    all writes are checked by row level security and the security-definer functions in Backend/supabase/schema.sql.
    ///  - Room service: the small Node app on the AWS game server (Backend/room-service) that starts a dedicated game
    ///    server per room and signs join tickets.
    /// Command line / URL overrides for testing: -fo-rooms &lt;url&gt; (desktop), ?fo-rooms=&lt;url&gt; (web).
    /// </summary>
    [CreateAssetMenu(menuName = "Fart Online/Backend Config", fileName = "FartOnlineBackend")]
    public class BackendConfig : ScriptableObject
    {
        [Header("Supabase (Project Settings > API)")]
        [Tooltip("https://<project-ref>.supabase.co")] public string supabaseUrl = "";
        [Tooltip("The anon / public key (safe to ship)")] [TextArea(2, 4)] public string supabaseAnonKey = "";

        [Header("Room service (AWS)")]
        [Tooltip("https://<elastic-ip-with-dashes>.sslip.io/api  (or http://127.0.0.1:8080 for local tests)")]
        public string roomServiceUrl = "http://127.0.0.1:8080";

        public bool HasSupabase => !string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseAnonKey);

        static BackendConfig instance;

        public static BackendConfig Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = Resources.Load<BackendConfig>("FartOnlineBackend");
                if (instance == null) instance = CreateInstance<BackendConfig>();
                string over = Override("fo-rooms");
                if (!string.IsNullOrEmpty(over)) { instance = Instantiate(instance); instance.roomServiceUrl = over; }
                return instance;
            }
        }

        public string RoomServiceUrl => (roomServiceUrl ?? "").TrimEnd('/');
        public string SupabaseUrl => (supabaseUrl ?? "").TrimEnd('/');

        static string Override(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-" + key) return args[i + 1];
#if UNITY_WEBGL && !UNITY_EDITOR
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q >= 0)
                foreach (var part in url.Substring(q + 1).Split('&'))
                {
                    int eq = part.IndexOf('=');
                    if (eq > 0 && part.Substring(0, eq) == key) return UnityEngine.Networking.UnityWebRequest.UnEscapeURL(part.Substring(eq + 1));
                }
#endif
            return null;
        }
    }
}
