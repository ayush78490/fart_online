using System.IO;
using FartOnline.Game.Online;
using UnityEditor;
using UnityEngine;

namespace FartOnline.GameEditor
{
    /// <summary>Menu Tools > Fart Online > Online > Backend Settings: creates / selects the BackendConfig asset
    /// (Supabase URL + anon key, room service URL) that every build reads from Resources.</summary>
    public static class BackendSettingsMenu
    {
        public const string AssetPath = "Assets/Game/Resources/FartOnlineBackend.asset";

        [MenuItem("Tools/Fart Online/Online/Backend Settings")]
        public static void Open()
        {
            var cfg = GetOrCreate();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
        }

        public static BackendConfig GetOrCreate()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<BackendConfig>(AssetPath);
            if (cfg != null) return cfg;
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            cfg = ScriptableObject.CreateInstance<BackendConfig>();
            AssetDatabase.CreateAsset(cfg, AssetPath);
            AssetDatabase.SaveAssets();
            return cfg;
        }

        /// <summary>Scripted setup (empty arguments leave a value unchanged).</summary>
        public static string Set(string supabaseUrl, string anonKey, string roomServiceUrl)
        {
            var cfg = GetOrCreate();
            if (!string.IsNullOrEmpty(supabaseUrl)) cfg.supabaseUrl = supabaseUrl.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(anonKey)) cfg.supabaseAnonKey = anonKey.Trim();
            if (!string.IsNullOrEmpty(roomServiceUrl)) cfg.roomServiceUrl = roomServiceUrl.Trim().TrimEnd('/');
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            return $"backend: supabase={cfg.supabaseUrl} anonKey={(string.IsNullOrEmpty(cfg.supabaseAnonKey) ? "-" : cfg.supabaseAnonKey.Substring(0, Mathf.Min(12, cfg.supabaseAnonKey.Length)) + "...")} rooms={cfg.roomServiceUrl}";
        }
    }
}
