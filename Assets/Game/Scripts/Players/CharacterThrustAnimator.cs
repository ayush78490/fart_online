using UnityEngine;
using FartOnline.Pig;

namespace FartOnline.Game
{
    /// <summary>
    /// Readable character feedback for a seated raft player, layered on the pig's existing clips and face rig:
    /// a procedural seated pose (thighs forward, knees bent) over Idle_Breathing, a spring-damped lean toward the
    /// direction of effort, anticipation crouch before a burst, recoil after it, straining while charging,
    /// tired face when exhausted, impact / wipeout / victory / defeat reactions and an occasional look-around.
    /// Animation is purely visual: physics never reads it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CharacterThrustAnimator : MonoBehaviour
    {
        public FartThrustController thrust;
        public Animator animator;
        public PigFaceController face;
        public PigSecondaryMotion secondary;
        [Tooltip("Renderers tinted with the player colour (seat cushion, bandana)")] public Renderer[] colorRenderers;

        [Header("Seated pose (degrees, applied after the animator)")]
        public float thighPitch = -75f;
        [Tooltip("Thighs spread outward so the feet show beside the belly")] public float thighSplay = 40f;
        public float shinPitch = 20f;
        [Header("Lean")]
        public float leanPitch = 20f;
        public float leanRoll = 16f;
        public float leanStiffness = 90f;
        public float leanDamping = 11f;

        Transform hips, spine, chest, head, thighL, thighR, shinL, shinR;
        Vector2 lean, leanVel;                  // spring state in raft space (x right, y forward)
        float anticipation, recoil, strain, react, nextLook, celebrateUntil;
        Vector2 recoilDir;
        string mood = "";
        MaterialPropertyBlock mpb;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (face == null) face = GetComponentInChildren<PigFaceController>();
            if (secondary == null) secondary = GetComponentInChildren<PigSecondaryMotion>();
            var root = animator != null ? animator.transform : transform;
            hips = Find(root, "Hips"); spine = Find(root, "Spine"); chest = Find(root, "Chest"); head = Find(root, "Head");
            thighL = Find(root, "Thigh_L"); thighR = Find(root, "Thigh_R"); shinL = Find(root, "Shin_L"); shinR = Find(root, "Shin_R");
            nextLook = Time.time + Random.Range(4f, 9f);
            if (thighL == null || shinL == null || spine == null)
                Debug.LogWarning($"[CharacterThrustAnimator] {name}: bones missing (thigh {thighL != null}, shin {shinL != null}, spine {spine != null}) under {root.name}");
        }

        void OnEnable() { if (thrust != null) thrust.Burst += OnBurst; }
        void OnDisable() { if (thrust != null) thrust.Burst -= OnBurst; }

        static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = Find(c, n); if (r != null) return r; }
            return null;
        }

        public void SetPlayerColor(Color c)
        {
            if (colorRenderers == null) return;
            mpb ??= new MaterialPropertyBlock();
            foreach (var r in colorRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                r.SetPropertyBlock(mpb);
            }
        }

        void OnBurst(ThrustEvent e)
        {
            switch (e.kind)
            {
                case ThrustKind.Sustained:
                case ThrustKind.Tap:
                    anticipation = 0.6f;
                    recoil = 0.5f + e.strength01 * 0.5f;
                    recoilDir = Vector2.down;
                    Mood("Surprised", 0.35f);
                    break;
                case ThrustKind.Charged:
                    anticipation = 1f;
                    recoil = 1.4f;
                    recoilDir = Vector2.down;
                    Mood("Excited", 0.9f);
                    if (secondary != null) secondary.AddImpact(0.6f + e.strength01 * 0.5f);
                    break;
                case ThrustKind.Brake:
                    recoil = 0.9f;
                    recoilDir = Vector2.down;
                    Mood("Worried", 0.5f);
                    break;
                case ThrustKind.Cooldown:
                    Mood("Sleepy", 0.8f);
                    break;
            }
        }

        public void PlayImpact(float strength)
        {
            Mood("Surprised", 1f);
            react = Mathf.Max(react, strength);
            if (secondary != null) secondary.AddImpact(strength);
        }

        public void PlayWipeout()
        {
            Cross("Surprised");
            Mood("Worried", 1f);
            celebrateUntil = Time.time + 1.6f;
        }

        public void PlayVictory(int variant)
        {
            Cross(variant % 2 == 0 ? "Laugh" : "Wave");
            Mood("Laughing", 1f);
            celebrateUntil = Time.time + 4f;
        }

        public void PlayDefeat()
        {
            Mood("Sad", 1f);
            celebrateUntil = Time.time + 3f;
        }

        public void ResetPose()
        {
            Cross("Idle_Breathing");
            lean = leanVel = Vector2.zero;
            anticipation = recoil = strain = react = 0f;
            Mood("", 0f);
        }

        void Cross(string state)
        {
            if (animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(state, 0.2f);
        }

        void Mood(string preset, float w)
        {
            if (face == null) return;
            if (mood != "" && mood != preset) face.SetExpression(mood, 0f);
            mood = preset;
            if (preset != "") face.SetExpression(preset, w);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var st = thrust != null ? thrust.State : default;

            // target lean: toward the push while sustaining, forward crouch while charging
            // the character faces the raft centre and pushes that way: lean forward (character space) while farting
            Vector2 target = st.sustaining ? new Vector2(0f, st.direction.magnitude) : Vector2.zero;
            target.x += st.moving * 0.45f;                                     // shuffle sideways along the rim
            if (st.charge01 > 0f) target = new Vector2(0f, 0.5f * st.charge01);
            target += recoilDir * recoil * 0.6f;
            Vector2 acc = (target - lean) * leanStiffness - leanVel * leanDamping;
            leanVel += acc * dt;
            lean += leanVel * dt;

            anticipation = Mathf.MoveTowards(anticipation, 0f, dt * 4f);
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 3f);
            react = Mathf.MoveTowards(react, 0f, dt * 1.5f);
            strain = Mathf.MoveTowards(strain, st.sustaining || st.charge01 > 0f ? 1f : 0f, dt * 5f);

            if (face != null)
            {
                face.SetControl("Squint_L", 0.55f * strain);
                face.SetControl("Squint_R", 0.55f * strain);
                face.SetControl("Snout_Compress", 0.5f * strain + 0.4f * st.charge01);
                face.SetControl("Cheek_Raise", 0.6f * strain);
                face.SetControl("Brow_Furrow", 0.7f * st.charge01);
                face.SetControl("Squash", 0.5f * anticipation + 0.3f * st.charge01);
                face.SetControl("Stretch", 0.6f * Mathf.Clamp01(recoil - 0.3f));
            }
            if (mood != "" && Time.time > celebrateUntil && anticipation <= 0f && recoil <= 0f && react <= 0f) Mood("", 0f);

            // idle look-around when nothing is happening
            if (Time.time > nextLook && !st.sustaining && Mathf.Abs(st.moving) < 0.1f && Time.time > celebrateUntil)
            {
                Cross(Random.value < 0.6f ? "Look_LeftRight" : "Look_Up");
                nextLook = Time.time + Random.Range(7f, 14f);
                Invoke(nameof(BackToIdle), 2.2f);
            }
        }

        void BackToIdle() { if (Time.time > celebrateUntil) Cross("Idle_Breathing"); }

        void LateUpdate()
        {
            Transform frame = transform;   // character root: +Z faces the raft's bow
            Vector3 right = frame.right, fwd = frame.forward;
            // seated legs (world-axis rotations through each joint, independent of the bone roll)
            if (thighL != null) thighL.rotation = Quaternion.AngleAxis(-thighSplay, fwd) * Quaternion.AngleAxis(thighPitch, right) * thighL.rotation;
            if (thighR != null) thighR.rotation = Quaternion.AngleAxis(thighSplay, fwd) * Quaternion.AngleAxis(thighPitch, right) * thighR.rotation;
            if (shinL != null) shinL.rotation = Quaternion.AngleAxis(shinPitch, right) * shinL.rotation;
            if (shinR != null) shinR.rotation = Quaternion.AngleAxis(shinPitch, right) * shinR.rotation;

            float shake = strain > 0.5f ? (Mathf.PerlinNoise(Time.time * 18f, 0.3f) - 0.5f) * 3f * strain : 0f;
            float pitch = lean.y * leanPitch + anticipation * 10f + react * 14f;
            float hop = thrust != null && Mathf.Abs(thrust.State.moving) > 0.1f ? Mathf.Sin(Time.time * 18f) * 6f : 0f;   // waddle while walking the rim
            float roll = -lean.x * leanRoll + shake + hop;
            Quaternion q = Quaternion.AngleAxis(pitch * 0.5f, right) * Quaternion.AngleAxis(roll * 0.5f, fwd);
            if (spine != null) spine.rotation = q * spine.rotation;
            if (chest != null) chest.rotation = q * chest.rotation;
            if (head != null && react > 0f) head.rotation = Quaternion.AngleAxis(-react * 10f, right) * head.rotation;
        }
    }
}
