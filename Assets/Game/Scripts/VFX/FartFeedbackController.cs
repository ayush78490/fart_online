using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Client-side feedback for the shared raft: turns each seat's replicated thrust state and burst events into
    /// gas VFX (pooled bursts + a per-seat sustained stream), character reactions, sounds, splashes and a wake.
    /// Runs identically on every peer from replicated data, so particles are never sent over the network.
    /// The gas always leaves opposite to the push: pushing the raft forward blows the cloud backwards.
    /// </summary>
    public class FartFeedbackController : MonoBehaviour
    {
        public SharedRaftController raft;
        public RaftRecoveryManager recovery;
        [Tooltip("Looping puff emitter per seat (index = seat)")] public ParticleSystem[] seatStreams = new ParticleSystem[4];
        public ParticleSystem impactSplash;
        public ParticleSystem landingSplash;
        public ParticleSystem wake;
        [Tooltip("Sustained stream puffs per second at full intensity")] public float streamRate = 26f;
        [Tooltip("Hide gas behind the camera-facing side when many players fire (keeps hazards visible)")]
        [Range(0.2f, 1f)] public float crowdOpacity = 0.6f;

        [Tooltip("Opacity of stream puffs blowing straight toward the camera")] [Range(0.1f, 1f)] public float cameraFacingOpacity = 0.4f;

        bool wasInWater = true;
        Camera cam;
        float airTime;

        void Awake()
        {
            foreach (var s in raft.seats.Seats) s.Thrust.Burst += e => OnBurst(s, e);
            recovery.Impact += OnImpact;
            recovery.WipedOut += _ => { foreach (var s in raft.seats.Seats) if (s.IsOccupied && s.characterAnimator != null) s.characterAnimator.PlayWipeout(); GameAudio.Play(AudioCue.Wipeout, transform.position); };
            recovery.Respawned += () => GameAudio.Play(AudioCue.Respawn, transform.position);
        }

        /// <summary>Network clients call this for impacts reported by the server.</summary>
        public void OnImpact(float strength, Vector3 point, bool heavy)
        {
            if (impactSplash != null)
            {
                impactSplash.transform.position = point;
                impactSplash.Emit(Mathf.RoundToInt(Mathf.Lerp(6, 28, strength)));
            }
            GameAudio.Play(heavy ? AudioCue.RaftCollisionHeavy : AudioCue.RaftCollision, point, Mathf.Lerp(0.4f, 1f, strength));
            if (heavy) GameAudio.Play(AudioCue.WoodCreak, point, 0.8f);
            foreach (var s in raft.seats.Seats)
                if (s.IsOccupied && s.characterAnimator != null) s.characterAnimator.PlayImpact(strength);
            RaftCameraShake.Add(heavy ? 0.6f : 0.25f * strength);
        }

        Vector3 GasWorld(Vector2 pushDir) => -raft.transform.TransformDirection(new Vector3(pushDir.x, 0f, pushDir.y)).normalized;

        void OnBurst(PlayerSeatController seat, ThrustEvent e)
        {
            var pool = FartVFXPool.Instance;
            Vector3 origin = seat.Thrust.OriginWorld;
            const bool lateral = false;   // round raft: the gas always leaves outward behind the player
            Vector3 gas = GasWorld(e.direction);
            Vector3 v = raft.Body != null ? raft.Body.linearVelocity : Vector3.zero;
            switch (e.kind)
            {
                case ThrustKind.Sustained:
                    pool?.Spawn(ThrustKind.Sustained, lateral, origin, gas, e.strength01, seat.PlayerColor, v);
                    break;   // the held stream is voiced by GameAudio.FartStream (see Update)
                case ThrustKind.Tap:
                    pool?.Spawn(ThrustKind.Tap, lateral, origin, gas, e.strength01, seat.PlayerColor, v);
                    GameAudio.Play(AudioCue.Fart, origin, 0.55f, seat.seatIndex);
                    break;
                case ThrustKind.Charged:
                    pool?.Spawn(ThrustKind.Charged, false, origin, gas, e.strength01, seat.PlayerColor, v);
                    GameAudio.Play(AudioCue.FartCharged, origin, 1f, seat.seatIndex);
                    RaftCameraShake.Add(0.2f + 0.3f * e.strength01);
                    break;
                case ThrustKind.Brake:
                    pool?.Spawn(ThrustKind.Brake, false, origin, gas, e.strength01, seat.PlayerColor, v);
                    GameAudio.Play(AudioCue.Fart, origin, 0.7f, seat.seatIndex);
                    break;
                case ThrustKind.Cooldown:
                    pool?.Spawn(ThrustKind.Cooldown, false, origin, Vector3.up, 0.3f, seat.PlayerColor, v);
                    GameAudio.Play(AudioCue.FartEmpty, origin, 0.5f, seat.seatIndex);
                    break;
            }
        }

        void Update()
        {
            if (cam == null) cam = Camera.main;
            int sustaining = 0;
            foreach (var s in raft.seats.Seats) if (s.IsOccupied && s.Thrust.State.sustaining) sustaining++;
            float crowd = sustaining > 2 ? crowdOpacity : 1f;
            Vector3 v = raft.Body != null ? raft.Body.linearVelocity : Vector3.zero;
            for (int i = 0; i < raft.seats.Seats.Length && i < seatStreams.Length; i++)
            {
                var seat = raft.seats.Seats[i];
                var ps = seatStreams[i];
                if (ps == null) continue;
                var st = seat.Thrust.State;
                GameAudio.FartStream(i, seat.IsOccupied && st.sustaining, seat.Thrust.OriginWorld, 0.7f + 0.3f * Mathf.Clamp01(st.direction.magnitude));
                var em = ps.emission;
                if (seat.IsOccupied && st.sustaining)
                {
                    Vector3 gas = GasWorld(st.direction);
                    ps.transform.SetPositionAndRotation(seat.Thrust.OriginWorld, Quaternion.LookRotation(gas, Vector3.up));
                    em.rateOverTime = streamRate * Mathf.Clamp01(st.direction.magnitude);
                    var main = ps.main;
                    Color c = Color.Lerp(Color.white, seat.PlayerColor, 0.18f);
                    // gas blowing straight at the chase camera would hide the river ahead: thin it out
                    float toward = cam != null ? Vector3.Dot(gas, (cam.transform.position - ps.transform.position).normalized) : 0f;
                    c.a = crowd * Mathf.Lerp(1f, cameraFacingOpacity, Mathf.InverseLerp(0.2f, 0.9f, toward));
                    main.startColor = c;
                    var vel = ps.inheritVelocity;
                    vel.enabled = true;
                    if (!ps.isPlaying) ps.Play();
                }
                else em.rateOverTime = 0f;
            }

            // wake and landing splash
            bool inWater = raft.Buoyancy.InWater;
            if (wake != null)
            {
                var em = wake.emission;
                float speed = Vector3.ProjectOnPlane(raft.RelativeVelocity, Vector3.up).magnitude;
                em.rateOverTime = inWater ? Mathf.Clamp(speed * 6f, 0f, 40f) : 0f;
            }
            if (!inWater) airTime += Time.deltaTime;
            if (inWater && !wasInWater && airTime > 0.35f)
            {
                float k = Mathf.Clamp01(Mathf.Abs(v.y) / 8f + airTime * 0.3f);
                if (landingSplash != null) { landingSplash.transform.position = raft.transform.position; landingSplash.Emit(Mathf.RoundToInt(Mathf.Lerp(20, 70, k))); }
                GameAudio.Play(AudioCue.Splash, raft.transform.position, Mathf.Lerp(0.5f, 1f, k));
                foreach (var s in raft.seats.Seats) if (s.IsOccupied && s.characterAnimator != null) s.characterAnimator.PlayImpact(0.4f + 0.6f * k);
                RaftCameraShake.Add(0.3f + 0.4f * k);
            }
            if (inWater) airTime = 0f;
            wasInWater = inWater;
        }
    }
}
