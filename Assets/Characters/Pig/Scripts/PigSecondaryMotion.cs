using UnityEngine;

namespace FartOnline.Pig
{
    /// <summary>
    /// Bounded, deterministic cartoon follow-through for gameplay-driven motion (the baked clips already contain
    /// their own follow-through). Damped springs react to the parent's world acceleration, are scaled by
    /// PigFaceController.Cartoon_Intensity, are hard-clamped and always settle - proportions can never drift.
    /// Also provides an impact squash (AddImpact) for landings and bumps.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class PigSecondaryMotion : MonoBehaviour
    {
        [System.Serializable]
        public class Spring
        {
            public string bone;
            public bool rotational = true;
            [Tooltip("Spring stiffness (1/s^2)")] public float stiffness = 110f;
            [Tooltip("Damping (1/s)")] public float damping = 8f;
            [Tooltip("How strongly parent acceleration pushes the spring")] public float gain = 0.9f;
            [Tooltip("Hard limit of the offset (metres at the bone tip / for translation)")] public float clamp = 0.07f;
            [HideInInspector] public Transform t;
            [HideInInspector] public Vector3 x, v, p0, p1;
            [HideInInspector] public int frames;
            [HideInInspector] public float length;
        }

        public PigFaceController face;
        [Range(0, 2)] public float globalGain = 0.6f;
        public Spring[] springs =
        {
            new Spring { bone = "Ear_Tip_L", stiffness = 110f, damping = 8f, gain = 0.9f, clamp = 0.07f },
            new Spring { bone = "Ear_Tip_R", stiffness = 110f, damping = 8f, gain = 0.9f, clamp = 0.07f },
            new Spring { bone = "Belly", rotational = false, stiffness = 140f, damping = 9f, gain = 0.55f, clamp = 0.035f },
            new Spring { bone = "Tail_2", stiffness = 160f, damping = 7f, gain = 0.8f, clamp = 0.05f },
        };

        [Header("Impact squash")]
        public float impactStiffness = 260f;
        public float impactDamping = 14f;
        float impact, impactVel;

        void Awake()
        {
            if (face == null) face = GetComponent<PigFaceController>();
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var s in springs)
            {
                foreach (var t in all) if (t.name == s.bone) { s.t = t; break; }
                if (s.t != null && s.t.childCount > 0) s.length = Vector3.Distance(s.t.position, s.t.GetChild(0).position);
                if (s.length < 1e-3f) s.length = 0.12f;
            }
        }

        /// <summary>Kick the body squash spring, e.g. AddImpact(Mathf.Clamp01(landingSpeed / 6f)).</summary>
        public void AddImpact(float strength) => impactVel += Mathf.Clamp(strength, 0f, 1.5f) * 9f;

        void LateUpdate()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 1f / 20f);
            float ci = (face != null ? face.Cartoon_Intensity : 1f) * globalGain;
            foreach (var s in springs)
            {
                if (s.t == null) continue;
                Vector3 p = s.t.position;
                Vector3 a = Vector3.zero;
                if (s.frames >= 2) a = (p - 2f * s.p1 + s.p0) / (dt * dt);
                s.p0 = s.p1; s.p1 = p; s.frames++;
                float h = dt * 0.5f;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 acc = -s.stiffness * s.x - s.damping * s.v - a * s.gain * ci;
                    s.v += acc * h;
                    s.x += s.v * h;
                }
                if (s.x.magnitude > s.clamp) { s.x = s.x.normalized * s.clamp; s.v *= 0.5f; }
                if (s.rotational)
                {
                    Vector3 dir = s.t.childCount > 0 ? (s.t.GetChild(0).position - s.t.position).normalized : s.t.up;
                    Vector3 axis = Vector3.Cross(dir, s.x);
                    if (axis.sqrMagnitude > 1e-10f)
                    {
                        float ang = Mathf.Min(0.6f, s.x.magnitude / s.length) * Mathf.Rad2Deg;
                        s.t.rotation = Quaternion.AngleAxis(ang, axis.normalized) * s.t.rotation;
                    }
                }
                else
                {
                    s.t.position += s.x;
                }
            }
            // impact squash spring -> Body_Squash / Body_Stretch on top of the face controller values
            float accI = -impactStiffness * impact - impactDamping * impactVel;
            impactVel += accI * dt;
            impact = Mathf.Clamp(impact + impactVel * dt, -0.6f, 1f);
            if (face != null && Mathf.Abs(impact) > 1e-3f)
            {
                face.Squash = Mathf.Clamp01(Mathf.Max(0f, impact));
                face.Stretch = Mathf.Clamp01(Mathf.Max(0f, -impact));
                face.Belly_Squash = Mathf.Clamp01(Mathf.Max(0f, impact) * 1.2f);
            }
        }
    }
}
