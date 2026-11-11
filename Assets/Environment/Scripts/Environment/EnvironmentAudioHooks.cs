using UnityEngine;
using FartOnline.Water;

namespace FartOnline.Environment
{
    /// <summary>
    /// Optional environmental audio hooks, kept separate from the visuals. Every slot is an AudioSource the sound
    /// designer can fill in; nothing references clip files, so the scene runs silently until clips are assigned.
    ///  * river / rapids ambience follow the raft along the river (rapids volume from the local flow speed)
    ///  * splash fires when the raft lands on the water hard enough
    ///  * waterfall, obstacle, checkpoint and finish sources live on their own components (Waterfall, KinematicObstacle,
    ///    Checkpoint, FinishGate)
    /// </summary>
    public class EnvironmentAudioHooks : MonoBehaviour
    {
        public RiverPath river;
        public RaftBuoyancy raft;
        [Header("Optional sources (assign clips in the inspector)")]
        public AudioSource riverAmbience;
        public AudioSource rapidsAmbience;
        public AudioSource splash;
        [Tooltip("Flow speed (m/s) at which the rapids ambience reaches full volume")] public float rapidsFullAt = 6f;
        [Tooltip("Downward speed (m/s) on water contact that triggers a splash")] public float splashSpeed = 3f;

        public event System.Action<float> Splashed;     // impact speed

        bool wasInWater = true;
        float lastVy;

        void Update()
        {
            if (raft == null || river == null) return;
            Vector3 p = raft.transform.position;
            if (riverAmbience != null) riverAmbience.transform.position = p;
            if (rapidsAmbience != null)
            {
                rapidsAmbience.transform.position = p;
                river.TryGetWater(p, out _, out Vector3 flow);
                rapidsAmbience.volume = Mathf.Clamp01(flow.magnitude / rapidsFullAt);
            }
            var rb = raft.GetComponent<Rigidbody>();
            if (raft.InWater && !wasInWater && -lastVy > splashSpeed)
            {
                if (splash != null) { splash.transform.position = p; splash.Play(); }
                Splashed?.Invoke(-lastVy);
                foreach (var m in raft.GetComponentsInChildren<FartOnline.Pig.PigSecondaryMotion>())
                    m.AddImpact(Mathf.Clamp01(-lastVy / 8f));                   // passengers squash on landing
            }
            wasInWater = raft.InWater;
            lastVy = rb.linearVelocity.y;
        }
    }
}
