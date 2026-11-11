using UnityEngine;

namespace FartOnline.Game
{
    /// <summary>
    /// Small trauma-based camera shake added after the chase camera has positioned itself (bursts, impacts,
    /// landings). Static <see cref="Add"/> so feedback code does not need a camera reference.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class RaftCameraShake : MonoBehaviour
    {
        [Tooltip("Max offset at full trauma (m)")] public float maxOffset = 0.35f;
        [Tooltip("Max roll at full trauma (deg)")] public float maxRoll = 2.5f;
        public float decay = 1.6f;
        [Range(0f, 1f), Tooltip("Accessibility: global shake amount")] public float intensity = 1f;

        static float trauma;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => trauma = 0f;

        public static void Add(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        void LateUpdate()
        {
            if (trauma <= 0f) return;
            float k = trauma * trauma * intensity;
            float t = Time.time * 24f;
            Vector3 off = new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t) - 0.5f, 0f) * 2f * maxOffset * k;
            transform.position += transform.rotation * off;
            transform.rotation *= Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t, 5.7f) - 0.5f) * 2f * maxRoll * k);
            trauma = Mathf.Max(0f, trauma - decay * Time.deltaTime);
        }
    }
}
