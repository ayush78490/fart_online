using System.Collections;
using System.IO;
using UnityEngine;

namespace FartOnline.Game.Tests
{
    /// <summary>A/B capture to find the black slivers near waterfalls: Game view with and without waterfall particles.</summary>
    public class ArtifactProbe : MonoBehaviour
    {
        IEnumerator Start()
        {
            Application.runInBackground = true;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Game", "Previews"));
            var cam = Camera.main;
            var chase = cam.GetComponent<FartOnline.CameraRig.RaftChaseCamera>();
            if (chase != null) chase.enabled = false;
            var shake = cam.GetComponent<RaftCameraShake>();
            if (shake != null) shake.enabled = false;
            var falls = FindObjectsByType<FartOnline.Water.Waterfall>();
            var basin = falls[0];
            foreach (var f in falls) if (f.height > 10f && f.height < 30f) { basin = f; break; }
            Vector3 target = basin.transform.position;
            cam.transform.position = target + new Vector3(-14f, 4f, -16f);
            cam.transform.LookAt(target + Vector3.down * 2f);
            yield return new WaitForSeconds(2.5f);
            yield return Grab(dir, "30_artifact_with_particles");
            foreach (var ps in FindObjectsByType<ParticleSystem>()) if (ps.GetComponentInParent<FartOnline.Water.Waterfall>() != null) ps.GetComponent<ParticleSystemRenderer>().enabled = false;
            yield return new WaitForSeconds(0.3f);
            yield return Grab(dir, "31_artifact_no_waterfall_particles");
            foreach (var f in falls) if (f.sheet != null) f.sheet.enabled = false;
            yield return new WaitForSeconds(0.3f);
            yield return Grab(dir, "32_artifact_no_sheets");
            Debug.Log($"[ArtifactProbe] DONE basin fall '{basin.name}' h={basin.height}");
        }

        static IEnumerator Grab(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
