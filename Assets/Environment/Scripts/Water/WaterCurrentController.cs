using UnityEngine;

namespace FartOnline.Water
{
    /// <summary>
    /// Scales the river current that RaftBuoyancy drags the raft towards (RiverPath flow speed × strength).
    /// The current helps carry the raft downstream but is kept modest so it never overrides player thrust.
    /// One instance per scene; without one the strength is 1.
    /// </summary>
    public class WaterCurrentController : MonoBehaviour
    {
        [Tooltip("Multiplier on every RiverPoint.flowSpeed")] [Range(0f, 2f)] public float strength = 1f;

        public static float Strength { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Strength = 1f;

        void OnEnable() => Strength = strength;
        void OnValidate() { if (isActiveAndEnabled) Strength = strength; }
        void OnDisable() => Strength = 1f;

        public void SetStrength(float s) { strength = s; Strength = s; }
    }
}
