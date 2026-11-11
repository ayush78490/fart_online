using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// All gameplay physics numbers in one asset (Assets/Game/Settings/RaftTuning.asset) so they can be tuned in
    /// play mode and documented in one place. Forces are Newtons / impulses Newton-seconds on a raft of <see cref="mass"/>.
    /// See Assets/Game/Documentation/Physics_Tuning_Guide.md for how each value was chosen.
    /// </summary>
    [CreateAssetMenu(menuName = "Fart Online/Raft Tuning", fileName = "RaftTuning")]
    public class RaftTuning : ScriptableObject
    {
        [Header("Raft body")]
        public float mass = 450f;
        [Tooltip("Centre of mass in raft space; kept low for stability")] public Vector3 centerOfMass = new Vector3(0f, -0.25f, 0f);
        public float linearDrag = 0.05f;
        public float angularDrag = 0.4f;
        [Tooltip("Max horizontal speed relative to the water (m/s); a soft counter-force applies above it")] public float maxSpeed = 11.5f;
        [Tooltip("Hard cap on angular velocity (rad/s)")] public float maxAngularVelocity = 3.2f;

        [Header("Round raft and seats")]
        [Tooltip("Deck radius (m)")] public float raftRadius = 2.4f;
        [Tooltip("Radius of the circle the players sit on (m)")] public float seatRadius = 1.65f;
        [Tooltip("How fast a player walks along the rim with A/D (degrees per second)")] public float seatMoveSpeed = 105f;
        [Tooltip("Players cannot pass each other: minimum angle between two seats (deg)")] public float minSeatSpacing = 42f;
        [Tooltip("Players can move while farting, slower (fraction of the move speed)")] [Range(0f, 1f)] public float moveWhileFarting = 0.5f;

        [Header("Per-player thrust (sustained stream, Newtons)")]
        [Tooltip("Force of one player's sustained fart, applied at the seat toward the raft centre")] public float thrust = 2400f;
        [Tooltip("Sum of all players' sustained force is clamped to this (N)")] public float maxCombinedForce = 6600f;
        [Tooltip("Thrust multiplier while no float point touches water (jumps): farts still work in the air, weaker")]
        [Range(0f, 1f)] public float airThrustFactor = 0.45f;
        [Tooltip("Rim movement below this is ignored (stick drift)")] [Range(0f, 0.6f)] public float inputDeadzone = 0.25f;
        [Tooltip("Height of the thrust origin above the seat (m); slightly above the centre of mass so bursts pitch the raft a little")]
        public float thrustOriginHeight = 0.15f;
        [Tooltip("F must be held this long after the initial puff before the sustained stream starts (s)")] public float sustainDelay = 0.18f;

        [Header("Bursts (impulses, N·s)")]
        public float tapImpulse = 1000f;
        public float chargedImpulseMin = 1550f;
        public float chargedImpulseMax = 4000f;
        [Tooltip("Seconds of holding ChargeFart for a full charge")] public float chargeTime = 1.1f;
        public float brakeImpulse = 2000f;
        [Tooltip("Seconds between two bursts of the same player")] public float burstCooldown = 0.28f;

        [Header("Smooth gas (feel)")]
        [Tooltip("Time constant for the combined fart force to build up (s): the stream swells instead of switching on")] public float thrustRiseTime = 0.16f;
        [Tooltip("Time constant for the force to fade after release (s): the raft keeps gliding a moment")] public float thrustFallTime = 0.3f;
        [Tooltip("Puffs, charged blasts and brakes are released over this time constant (s) instead of one instant kick")] public float burstReleaseTime = 0.14f;

        [Header("Hold-to-fart")]
        [Tooltip("Holding F continues into a sustained stream; off = F only puffs")] public bool holdToFart = true;
        [Tooltip("Longest continuous sustained burst (s); then the player must release")] public float maxBurstDuration = 1.8f;
        [Tooltip("Forced pause after hitting the max burst duration (s)")] public float burstRecovery = 0.45f;

        [Header("Gas resource (0..1 per player)")]
        public float sustainCostPerSecond = 0.30f;
        public float tapCost = 0.07f;
        public float chargedCostMax = 0.38f;
        public float brakeCost = 0.12f;
        public float regenPerSecond = 0.24f;
        [Tooltip("Seconds without farting before gas regenerates")] public float regenDelay = 0.45f;
        [Tooltip("Lockout after the tank runs empty (s)")] public float exhaustedCooldown = 1.3f;
        [Tooltip("Gas needed before an exhausted player may fart again")] [Range(0f, 1f)] public float exhaustedRecoverLevel = 0.25f;

        [Header("Stabilisation")]
        [Tooltip("Self-righting torque per radian of roll/pitch (acceleration, rad/s² per rad)")] public float uprightStrength = 6f;
        public float uprightDamping = 2.2f;
        [Tooltip("Roll/pitch beyond this (deg) is held back harder")] public float softTiltLimit = 22f;

        [Header("Water")]
        [Tooltip("Global multiplier on the river current (WaterCurrentController overrides at runtime)")] public float currentStrength = 1f;
        [Tooltip("Buoyancy acceleration per metre of submersion per float point")] public float buoyancy = 22f;

        [Header("Recovery")]
        [Tooltip("Tilt beyond this (deg) for flipSeconds counts as capsized")] public float capsizeAngle = 75f;
        public float flipSeconds = 1.5f;
        [Tooltip("Seconds out of water (not airborne) before a wipeout")] public float strandedSeconds = 5f;
        [Tooltip("Seconds with almost no progress while players thrust before the recovery prompt appears")] public float stuckSeconds = 6f;
        public float respawnBelowY = -30f;
        [Tooltip("Pause between the wipeout and the respawn (s)")] public float wipeoutDelay = 1.6f;
        [Tooltip("Collision impulse (N·s) above which a hit counts as a heavy impact (reactions, camera shake)")] public float heavyImpactImpulse = 900f;

        [Header("Solo assist")]
        public float standardRegenMultiplier = 1.3f;
        public float highRegenMultiplier = 1.6f;
        [Tooltip("Yaw damping added when no one is thrusting (Standard / High), 1/s")] public Vector2 assistYawDamping = new Vector2(0.8f, 1.6f);
        [Tooltip("Extra self-righting multiplier (Standard / High)")] public Vector2 assistUpright = new Vector2(1.25f, 1.6f);
        [Tooltip("Fraction of a wall-scrape impulse removed (Standard / High)")] public Vector2 assistScrapeReduction = new Vector2(0.35f, 0.55f);
        [Tooltip("High assist: seats walk faster (multiplier)")] public float highAssistMoveSpeed = 1.3f;
    }
}
