using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using FartOnline.Environment;

namespace FartOnline.EnvironmentEditor
{
    /// <summary>
    /// Renders preview images of the course from fixed viewpoints derived from the RiverPath (overview + one per
    /// section + gameplay camera). Output: ArtSource/Environment/Previews/*.png (outside Assets, not imported).
    /// </summary>
    public static class EnvironmentCapture
    {
        public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Environment", "Previews"));

        public static string Render(string name, Vector3 pos, Vector3 lookAt, float fov = 55f, int w = 960, int h = 540)
        {
            Directory.CreateDirectory(OutDir);
            var go = new GameObject("_CaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            rt.Create();
            cam.targetTexture = rt;
            // Shaders compile asynchronously (forcing synchronous compilation made frames take ~50 s and coincided
            // with a D3D12 device-removed crash). Render a few frames and wait until compilation has finished.
            for (int i = 0; i < 40; i++)
            {
                cam.Render();
                if (!ShaderUtil.anythingCompiling && i >= 2) break;
                System.Threading.Thread.Sleep(100);
            }
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string file = Path.Combine(OutDir, name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            return file;
        }

        /// <summary>Viewpoint behind and above the river at a given distance, looking downstream.</summary>
        public static void RiverView(RiverPath path, float distance, float back, float up, float side, out Vector3 pos, out Vector3 look)
        {
            var s = path.SampleAtDistance(distance);
            Vector3 f = Vector3.ProjectOnPlane(s.tangent, Vector3.up).normalized;
            pos = s.position - f * back + Vector3.up * up + s.right * side;
            var a = path.SampleAtDistance(distance + back * 0.8f);
            look = a.position + Vector3.up * 1.5f;
        }

        [MenuItem("Tools/Tropical Course/Capture Previews")]
        public static void CaptureAll()
        {
            var path = Object.FindAnyObjectByType<RiverPath>();
            if (path == null) { Debug.LogError("[TropicalCourse] no RiverPath in the open scene"); return; }
            var files = new List<string>();
            float L = path.Length;
            Render("_warmup", path.SampleAtDistance(10f).position + Vector3.up * 20f, path.SampleAtDistance(40f).position);
            File.Delete(Path.Combine(OutDir, "_warmup.png"));
            // overview from high above the middle of the course, angled
            Vector3 mid = path.SampleAtDistance(L * 0.5f).position;
            files.Add(Render("00_overview", mid + new Vector3(-170f, 210f, -150f), mid + new Vector3(0, -10, 10), 50f));
            files.Add(Render("01_overview_top", mid + new Vector3(0f, 420f, -40f), mid, 60f));
            // per section: find the first sample of each section
            string last = null;
            int idx = 2;
            foreach (var s in path.Samples)
            {
                if (s.section == last) continue;
                last = s.section;
                RiverView(path, s.distance + 6f, 16f, 9f, 3f, out Vector3 p, out Vector3 l);
                files.Add(Render(idx.ToString("00") + "_section_" + s.section, p, l));
                idx++;
            }
            // gameplay camera
            var cam = Camera.main;
            if (cam != null) files.Add(Render(idx.ToString("00") + "_gameplay_camera", cam.transform.position, cam.transform.position + cam.transform.forward * 10f, cam.fieldOfView));
            Debug.Log("[TropicalCourse] previews:\n" + string.Join("\n", files));
        }
    }
}
