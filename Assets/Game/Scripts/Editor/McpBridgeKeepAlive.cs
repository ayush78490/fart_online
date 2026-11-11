using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FartOnline.GameEditor
{
    /// <summary>
    /// Development helper: after every domain reload, makes sure the MCP for Unity HTTP bridge is connected
    /// (it does not auto-start after an editor relaunch unless "AutoStartOnLoad" is on). Reflection keeps the
    /// project compiling if the MCP package is ever removed. Disable with the menu item.
    /// </summary>
    [InitializeOnLoad]
    static class McpBridgeKeepAlive
    {
        const string DisabledKey = "FartOnline.McpKeepAlive.Disabled";
        static double nextCheck;
        static int attempts;

        static McpBridgeKeepAlive()
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(DisabledKey, false)) return;
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
            nextCheck = EditorApplication.timeSinceStartup + 3.0;
            EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Fart Online/Dev/Toggle MCP Bridge Keep-Alive")]
        static void Toggle()
        {
            bool off = !EditorPrefs.GetBool(DisabledKey, false);
            EditorPrefs.SetBool(DisabledKey, off);
            Debug.Log("[McpKeepAlive] " + (off ? "disabled" : "enabled (takes effect after the next reload)"));
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 10.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                var locator = Type.GetType("MCPForUnity.Editor.Services.MCPServiceLocator, MCPForUnity.Editor");
                if (locator == null) { EditorApplication.update -= Tick; return; }
                var bridge = locator.GetProperty("Bridge").GetValue(null);
                bool running = (bool)bridge.GetType().GetProperty("IsRunning").GetValue(bridge);
                if (running) { attempts = 0; return; }
                if (++attempts > 30) { EditorApplication.update -= Tick; return; }
                var task = bridge.GetType().GetMethod("StartAsync", Type.EmptyTypes).Invoke(bridge, null) as Task<bool>;
                Debug.Log("[McpKeepAlive] MCP bridge not running: starting it (attempt " + attempts + ")");
                task?.ContinueWith(t => Debug.Log("[McpKeepAlive] bridge start result: " + (t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result.ToString())));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[McpKeepAlive] " + e.GetBaseException().Message);
                EditorApplication.update -= Tick;
            }
        }
    }
}
