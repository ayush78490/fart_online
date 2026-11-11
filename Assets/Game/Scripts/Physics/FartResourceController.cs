using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// One player's gas tank (0..1). Sustained thrust drains it continuously, bursts cost a lump; it regenerates after a
    /// short delay. Running empty locks the player out until the tank recovers to <see cref="RaftTuning.exhaustedRecoverLevel"/>
    /// and the exhausted cooldown has passed. Simulated only by the authority; clients read the replicated value.
    /// </summary>
    public class FartResourceController : MonoBehaviour
    {
        public RaftTuning tuning;
        [Tooltip("Regeneration multiplier (solo assist raises it)")] public float regenMultiplier = 1f;

        public float Gas { get; private set; } = 1f;
        public bool Exhausted { get; private set; }
        public float CooldownRemaining => Mathf.Max(0f, exhaustedUntil - Time.time);

        float lastUse = -10f;
        float exhaustedUntil;

        public void ResetTank()
        {
            Gas = 1f;
            Exhausted = false;
            exhaustedUntil = 0f;
        }

        /// <summary>Set by clients from the replicated seat state (no simulation).</summary>
        public void SetReplicated(float gas, bool exhausted)
        {
            Gas = gas;
            Exhausted = exhausted;
        }

        public bool CanFart => !Exhausted && Gas > 0.001f;

        /// <summary>Drains gas for a sustained stream; returns the fraction of the requested time that was paid for.</summary>
        public float DrainSustained(float dt, float intensity)
        {
            if (!CanFart) return 0f;
            float cost = tuning.sustainCostPerSecond * intensity * dt;
            lastUse = Time.time;
            if (Gas >= cost) { Gas -= cost; return 1f; }
            float paid = cost > 0f ? Gas / cost : 0f;
            Gas = 0f;
            Exhaust();
            return paid;
        }

        /// <summary>Pays for a burst; a burst that empties the tank still fires, scaled by what was left.</summary>
        public float PayBurst(float cost)
        {
            if (!CanFart) return 0f;
            lastUse = Time.time;
            if (Gas >= cost) { Gas -= cost; return 1f; }
            float paid = Gas / cost;
            Gas = 0f;
            Exhaust();
            return paid;
        }

        void Exhaust()
        {
            Exhausted = true;
            exhaustedUntil = Time.time + tuning.exhaustedCooldown;
        }

        /// <summary>Authority tick (FixedUpdate rate).</summary>
        public void Simulate(float dt)
        {
            if (Time.time - lastUse > tuning.regenDelay)
                Gas = Mathf.Min(1f, Gas + tuning.regenPerSecond * regenMultiplier * dt);
            if (Exhausted && Time.time >= exhaustedUntil && Gas >= tuning.exhaustedRecoverLevel)
                Exhausted = false;
        }
    }
}
