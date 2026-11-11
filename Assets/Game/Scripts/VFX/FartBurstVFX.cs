using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// One pooled gas-burst effect (PF_FartBurstVFX and its variants). Children are particle systems simulated in
    /// world space: GasCore (compact initial puff), GasCloudParticles (the burst cluster), ExpandingCloud (one
    /// big swelling puff), TrailingWisps (small puffs left behind), ShockRing (flat ring), SmallDebrisOrSparkles
    /// (player-coloured sparkles). The effect is oriented so +Z points where the GAS goes (opposite to the thrust)
    /// and inherits a little of the raft's velocity so clouds trail behind a moving raft.
    /// </summary>
    public class FartBurstVFX : MonoBehaviour
    {
        public ThrustKind kind;
        public ParticleSystem gasCore, gasCloud, expandingCloud, trailingWisps, shockRing, sparkles;
        [Tooltip("Seconds until this instance can be reused")] public float lifetime = 2.5f;
        [Tooltip("Fraction of the raft velocity given to the puffs")] [Range(0f, 1f)] public float inheritVelocity = 0.6f;

        public float FreeAt { get; private set; }
        ParticleSystem[] all;

        void Awake()
        {
            all = new[] { gasCore, gasCloud, expandingCloud, trailingWisps, shockRing, sparkles };
        }

        /// <summary>Plays the burst. gasDirection = world direction the gas travels; strength 0..1 scales size and speed.</summary>
        public void Play(Vector3 position, Vector3 gasDirection, float strength, Color accent, Vector3 raftVelocity)
        {
            if (all == null) Awake();
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(gasDirection.sqrMagnitude > 1e-4f ? gasDirection : Vector3.back, Vector3.up));
            float s = Mathf.Lerp(0.7f, 1.3f, Mathf.Clamp01(strength));
            transform.localScale = Vector3.one * s;
            foreach (var ps in all)
            {
                if (ps == null) continue;
                ps.Clear(true);
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                Vector3 iv = raftVelocity * inheritVelocity;
                vel.x = new ParticleSystem.MinMaxCurve(iv.x);
                vel.y = new ParticleSystem.MinMaxCurve(iv.y);
                vel.z = new ParticleSystem.MinMaxCurve(iv.z);
            }
            if (sparkles != null)
            {
                var m = sparkles.main;
                m.startColor = new ParticleSystem.MinMaxGradient(accent, Color.Lerp(accent, Color.white, 0.6f));
            }
            if (shockRing != null)
            {
                var m = shockRing.main;
                m.startColor = Color.Lerp(new Color(0.85f, 1f, 0.6f, 0.8f), accent, 0.35f);
            }
            foreach (var ps in all) if (ps != null) ps.Play(false);
            var src = GetComponent<AudioSource>();
            if (src != null && src.clip != null) src.Play();
            FreeAt = Time.time + lifetime;
            gameObject.SetActive(true);
        }
    }
}
