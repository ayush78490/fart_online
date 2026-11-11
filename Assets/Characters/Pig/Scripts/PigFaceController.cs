using System.Collections.Generic;
using UnityEngine;

namespace FartOnline.Pig
{
    /// <summary>
    /// Runtime facial / cartoon controls for the pig. Mirrors the CTRL_Face / CTRL_Body properties of the
    /// Blender rig (same names, same ranges).
    ///  * Blend-shape controls (Smile, Mouth_Open, visemes, Body_Squash ...) drive the SkinnedMeshRenderer.
    ///  * Bone controls (blinks, eye look, brows, ears, eyes-wide) are data driven: the FBX ships one single-frame
    ///    pose clip per control at its extreme (FACE_*). They are sampled once and blended additively, so no bone
    ///    axis conventions are hard-coded here.
    /// Everything is applied on top of whatever the Animator wrote this frame, so clips, presets and gameplay
    /// code combine.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class PigFaceController : MonoBehaviour
    {
        [Header("References")]
        public SkinnedMeshRenderer skin;
        [Tooltip("FACE_Rest plus every FACE_* pose clip from PigCharacter_Game.fbx (assigned by the setup tool).")]
        public AnimationClip[] facePoseClips;

        [Header("Eyes")]
        [Range(0, 1)] public float Blink_L, Blink_R, Blink_Both;
        [Range(-1, 1)] public float Eye_Look_X, Eye_Look_Y;
        [Range(0, 1)] public float Squint_L, Squint_R, Eyes_Wide;
        [Header("Brows / ears")]
        [Range(0, 1)] public float Brow_Raise_L, Brow_Raise_R, Brow_Furrow;
        [Range(-1, 1)] public float Ear_Tilt_L, Ear_Tilt_R;
        [Header("Mouth / snout (blend shapes)")]
        [Range(0, 1)] public float Smile, Smile_L, Smile_R, Frown, Mouth_Open, Mouth_Wide, Mouth_O, Cheek_Raise;
        [Range(0, 1)] public float Snout_Compress, Snout_Widen;
        [Header("Visemes")]
        [Range(0, 1)] public float V_AA, V_EE, V_OH, V_FV, V_MBP, V_L, V_REST;
        [Header("Cartoon body")]
        [Range(0, 2)] public float Cartoon_Intensity = 1f;
        [Range(0, 1)] public float Squash, Stretch, Belly_Squash, Belly_Stretch;
        [Tooltip("Drive the shoulder corrective blend shapes from the upper-arm angle (as the Blender drivers do).")]
        public bool shoulderCorrectives = true;

        // ------------------------------------------------------------------ expression presets (same as Blender)
        public static readonly Dictionary<string, Dictionary<string, float>> Presets = new Dictionary<string, Dictionary<string, float>>
        {
            { "Neutral", new Dictionary<string, float>() },
            { "Happy", new Dictionary<string, float> { { "Smile", .8f }, { "Cheek_Raise", .6f }, { "Squint_L", .25f }, { "Squint_R", .25f }, { "Brow_Raise_L", .35f }, { "Brow_Raise_R", .35f }, { "Ear_Tilt_L", -.2f }, { "Ear_Tilt_R", -.2f } } },
            { "Excited", new Dictionary<string, float> { { "Smile", 1f }, { "Mouth_Open", .55f }, { "Eyes_Wide", .8f }, { "Brow_Raise_L", 1f }, { "Brow_Raise_R", 1f }, { "Cheek_Raise", .5f }, { "Ear_Tilt_L", -.5f }, { "Ear_Tilt_R", -.5f } } },
            { "Sad", new Dictionary<string, float> { { "Frown", .85f }, { "Brow_Raise_L", .45f }, { "Brow_Raise_R", .45f }, { "Brow_Furrow", .35f }, { "Squint_L", .25f }, { "Squint_R", .25f }, { "Eye_Look_Y", -.45f }, { "Ear_Tilt_L", .8f }, { "Ear_Tilt_R", .8f } } },
            { "Angry", new Dictionary<string, float> { { "Frown", .7f }, { "Brow_Furrow", 1f }, { "Snout_Widen", .7f }, { "Squint_L", .45f }, { "Squint_R", .45f }, { "Mouth_Wide", .3f }, { "Ear_Tilt_L", -.6f }, { "Ear_Tilt_R", -.6f } } },
            { "Surprised", new Dictionary<string, float> { { "Mouth_O", 1f }, { "Eyes_Wide", 1f }, { "Brow_Raise_L", 1f }, { "Brow_Raise_R", 1f }, { "Snout_Compress", .3f }, { "Ear_Tilt_L", -.45f }, { "Ear_Tilt_R", -.45f } } },
            { "Confused", new Dictionary<string, float> { { "Smile_R", .35f }, { "Frown", .25f }, { "Brow_Raise_L", 1f }, { "Brow_Furrow", .25f }, { "Squint_R", .45f }, { "Eye_Look_X", .35f }, { "Eye_Look_Y", .3f }, { "Ear_Tilt_L", -.5f }, { "Ear_Tilt_R", .45f } } },
            { "Sleepy", new Dictionary<string, float> { { "Blink_Both", .62f }, { "Mouth_Open", .12f }, { "Eye_Look_Y", -.3f }, { "Ear_Tilt_L", .6f }, { "Ear_Tilt_R", .6f }, { "Snout_Compress", .15f } } },
            { "Laughing", new Dictionary<string, float> { { "Smile", 1f }, { "Mouth_Open", 1f }, { "Cheek_Raise", 1f }, { "Squint_L", .8f }, { "Squint_R", .8f }, { "Brow_Raise_L", .5f }, { "Brow_Raise_R", .5f } } },
            { "Worried", new Dictionary<string, float> { { "Frown", .45f }, { "Brow_Raise_L", .7f }, { "Brow_Raise_R", .7f }, { "Brow_Furrow", .55f }, { "Mouth_Wide", .3f }, { "Eyes_Wide", .3f }, { "Eye_Look_X", -.3f }, { "Ear_Tilt_L", .3f }, { "Ear_Tilt_R", .3f } } },
        };
        public static readonly string[] Visemes = { "V_AA", "V_EE", "V_OH", "V_FV", "V_MBP", "V_L", "V_REST" };

        readonly Dictionary<string, float> expressionWeights = new Dictionary<string, float>();

        /// <summary>Blend an expression preset in (weights add; call with 0 to remove). Combines with blinks/look.</summary>
        public void SetExpression(string preset, float weight)
        {
            if (!Presets.ContainsKey(preset)) { Debug.LogWarning("Unknown pig expression " + preset); return; }
            if (weight <= 0f) expressionWeights.Remove(preset); else expressionWeights[preset] = Mathf.Clamp01(weight);
        }
        public void ClearExpressions() => expressionWeights.Clear();

        /// <summary>Set one viseme (others fade out by the caller or via weight 0).</summary>
        public void SetViseme(string viseme, float weight) => SetControl(viseme, Mathf.Clamp01(weight));

        // ------------------------------------------------------------------ control table
        static readonly string[] BlendShapeControls =
        {
            "Smile", "Smile_L", "Smile_R", "Frown", "Mouth_Open", "Mouth_Wide", "Mouth_O", "Cheek_Raise",
            "Snout_Compress", "Snout_Widen", "V_AA", "V_EE", "V_OH", "V_FV", "V_MBP", "V_L", "V_REST"
        };

        public float GetControl(string n)
        {
            var f = typeof(PigFaceController).GetField(n);
            return f != null ? (float)f.GetValue(this) : 0f;
        }
        public void SetControl(string n, float v)
        {
            var f = typeof(PigFaceController).GetField(n);
            if (f != null) f.SetValue(this, v);
        }

        float Eff(string n)
        {
            float v = GetControl(n);
            foreach (var kv in expressionWeights)
                if (Presets[kv.Key].TryGetValue(n, out var pv)) v += pv * kv.Value;
            bool signed = n == "Eye_Look_X" || n == "Eye_Look_Y" || n == "Ear_Tilt_L" || n == "Ear_Tilt_R";
            return signed ? Mathf.Clamp(v, -1f, 1f) : Mathf.Clamp01(v);
        }

        // ------------------------------------------------------------------ pose-delta data
        struct Delta { public Transform t; public Quaternion rot; public Vector3 pos; public Vector3 scl; }
        readonly Dictionary<string, List<Delta>> poseDeltas = new Dictionary<string, List<Delta>>();
        readonly Dictionary<Transform, (Quaternion r, Vector3 p, Vector3 s)> restLocal = new Dictionary<Transform, (Quaternion, Vector3, Vector3)>();
        readonly Dictionary<Transform, (Quaternion r, Vector3 p, Vector3 s)> lastWritten = new Dictionary<Transform, (Quaternion, Vector3, Vector3)>();
        readonly Dictionary<Transform, (Quaternion r, Vector3 p, Vector3 s)> lastBase = new Dictionary<Transform, (Quaternion, Vector3, Vector3)>();
        readonly Dictionary<int, float> shapeLastWritten = new Dictionary<int, float>();
        readonly Dictionary<int, float> shapeLastBase = new Dictionary<int, float>();
        readonly Dictionary<string, int> shapeIndex = new Dictionary<string, int>();
        Transform upperArmL, upperArmR, chest;
        Quaternion restArmChestL, restArmChestR;
        bool initialised;

        public bool IsInitialised => initialised;
        public int PoseControlCount => poseDeltas.Count;

        void Awake() => Initialise();

        public void Initialise()
        {
            if (initialised) return;
            if (skin == null) skin = GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin != null && skin.sharedMesh != null)
            {
                var mesh = skin.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string n = mesh.GetBlendShapeName(i);
                    int dot = n.LastIndexOf('.');                 // tolerate "Key.Smile" style prefixes
                    shapeIndex[dot >= 0 ? n.Substring(dot + 1) : n] = i;
                }
            }
            var all = GetComponentsInChildren<Transform>(true);
            AnimationClip rest = null;
            if (facePoseClips != null)
                foreach (var c in facePoseClips) if (c != null && c.name.EndsWith("FACE_Rest")) rest = c;
            if (rest != null)
            {
                rest.SampleAnimation(gameObject, 0f);
                foreach (var t in all) restLocal[t] = (t.localRotation, t.localPosition, t.localScale);
                foreach (var clip in facePoseClips)
                {
                    if (clip == null || clip == rest) continue;
                    clip.SampleAnimation(gameObject, 0f);
                    var list = new List<Delta>();
                    foreach (var t in all)
                    {
                        var r = restLocal[t];
                        Quaternion dq = Quaternion.Inverse(r.r) * t.localRotation;
                        Vector3 dp = t.localPosition - r.p;
                        Vector3 ds = new Vector3(SafeDiv(t.localScale.x, r.s.x), SafeDiv(t.localScale.y, r.s.y), SafeDiv(t.localScale.z, r.s.z));
                        if (Quaternion.Angle(dq, Quaternion.identity) > 0.05f || dp.sqrMagnitude > 1e-9f || (ds - Vector3.one).sqrMagnitude > 1e-7f)
                            list.Add(new Delta { t = t, rot = dq, pos = dp, scl = ds });
                    }
                    string key = clip.name.Substring(clip.name.IndexOf("FACE_") + 5);
                    poseDeltas[key] = list;
                }
                rest.SampleAnimation(gameObject, 0f);
            }
            upperArmL = Find(all, "UpperArm_L"); upperArmR = Find(all, "UpperArm_R"); chest = Find(all, "Chest");
            if (upperArmL && chest) restArmChestL = Quaternion.Inverse(chest.rotation) * upperArmL.rotation;
            if (upperArmR && chest) restArmChestR = Quaternion.Inverse(chest.rotation) * upperArmR.rotation;
            initialised = true;
        }

        static float SafeDiv(float a, float b) => Mathf.Abs(b) < 1e-6f ? 1f : a / b;
        static Transform Find(Transform[] all, string n) { foreach (var t in all) if (t.name == n) return t; return null; }

        // ------------------------------------------------------------------ per-frame application
        void LateUpdate() => Apply();

        public void Apply()
        {
            if (!initialised) Initialise();
            // which pose clips are active and how strongly
            var active = new List<(string, float)>();
            void Add(string key, float w) { if (w > 1e-4f && poseDeltas.ContainsKey(key)) active.Add((key, w)); }
            float sqL = Eff("Squint_L"), sqR = Eff("Squint_R"), both = Eff("Blink_Both");
            Add("Blink_L", Mathf.Min(Mathf.Max(Eff("Blink_L"), both), 1f - 0.35f * sqL));
            Add("Blink_R", Mathf.Min(Mathf.Max(Eff("Blink_R"), both), 1f - 0.35f * sqR));
            Add("Squint_L", sqL); Add("Squint_R", sqR);
            Add("Eyes_Wide", Eff("Eyes_Wide"));
            Add("Brow_Raise_L", Eff("Brow_Raise_L")); Add("Brow_Raise_R", Eff("Brow_Raise_R")); Add("Brow_Furrow", Eff("Brow_Furrow"));
            foreach (var s in new[] { "Ear_Tilt_L", "Ear_Tilt_R", "Eye_Look_X", "Eye_Look_Y" })
            {
                float v = Eff(s);
                Add(s + (v >= 0 ? "_Pos" : "_Neg"), Mathf.Abs(v));
            }

            // bones: base = what the Animator wrote this frame (or the last base if nothing wrote)
            var touched = new Dictionary<Transform, (Quaternion r, Vector3 p, Vector3 s)>();
            foreach (var (key, w) in active)
                foreach (var d in poseDeltas[key])
                {
                    if (!touched.TryGetValue(d.t, out var cur))
                        cur = BaseOf(d.t);
                    cur.r = cur.r * Quaternion.Slerp(Quaternion.identity, d.rot, w);
                    cur.p = cur.p + d.pos * w;
                    cur.s = Vector3.Scale(cur.s, Vector3.Lerp(Vector3.one, d.scl, w));
                    touched[d.t] = cur;
                }
            // bones touched last frame but not now: restore their base
            foreach (var t in new List<Transform>(lastWritten.Keys))
                if (!touched.ContainsKey(t)) touched[t] = BaseOf(t);
            lastWritten.Clear();
            foreach (var kv in touched)
            {
                kv.Key.localRotation = kv.Value.r; kv.Key.localPosition = kv.Value.p; kv.Key.localScale = kv.Value.s;
                lastWritten[kv.Key] = kv.Value;
            }

            // blend shapes
            if (skin == null) return;
            foreach (var n in BlendShapeControls) WriteShape(n, Eff(n));
            float ci = Cartoon_Intensity;
            WriteShape("Body_Squash", Mathf.Clamp01(Squash * ci));
            WriteShape("Body_Stretch", Mathf.Clamp01(Stretch * ci));
            WriteShape("Belly_Squash", Mathf.Clamp01(Belly_Squash * ci));
            WriteShape("Belly_Stretch", Mathf.Clamp01(Belly_Stretch * ci));
            if (shoulderCorrectives && chest)
            {
                // same mapping as the Blender driver: rest angle ~151 deg, full corrective by ~72 deg
                if (upperArmL) WriteShapeAbs("Corr_Shoulder_Up_L", ArmCorrective(upperArmL, restArmChestL));
                if (upperArmR) WriteShapeAbs("Corr_Shoulder_Up_R", ArmCorrective(upperArmR, restArmChestR));
            }
        }

        float ArmCorrective(Transform arm, Quaternion restRel)
        {
            Quaternion rel = Quaternion.Inverse(chest.rotation) * arm.rotation;
            float raised = Quaternion.Angle(restRel, rel) * Mathf.Deg2Rad;      // how far the arm left its rest
            float diff = 2.645f - raised;                                      // approximates the Blender ROTATION_DIFF
            return Mathf.Clamp01((2.35f - diff) / 1.1f);
        }

        (Quaternion r, Vector3 p, Vector3 s) BaseOf(Transform t)
        {
            var now = (t.localRotation, t.localPosition, t.localScale);
            if (lastWritten.TryGetValue(t, out var lw) && Same(lw, now))
                return lastBase.TryGetValue(t, out var lb) ? lb : (restLocal.TryGetValue(t, out var rl) ? rl : now);
            lastBase[t] = now;
            return now;
        }

        static bool Same((Quaternion r, Vector3 p, Vector3 s) a, (Quaternion r, Vector3 p, Vector3 s) b)
            => Quaternion.Angle(a.r, b.r) < 1e-3f && (a.p - b.p).sqrMagnitude < 1e-12f && (a.s - b.s).sqrMagnitude < 1e-12f;

        void WriteShape(string n, float control)
        {
            if (!shapeIndex.TryGetValue(n, out int i)) return;
            float cur = skin.GetBlendShapeWeight(i);
            float baseW = (shapeLastWritten.TryGetValue(i, out var lw) && Mathf.Abs(lw - cur) < 1e-4f)
                ? (shapeLastBase.TryGetValue(i, out var lb) ? lb : 0f) : cur;
            shapeLastBase[i] = baseW;
            float w = Mathf.Clamp(Mathf.Max(baseW, control * 100f), 0f, 100f);
            skin.SetBlendShapeWeight(i, w);
            shapeLastWritten[i] = w;
        }

        void WriteShapeAbs(string n, float v)
        {
            if (!shapeIndex.TryGetValue(n, out int i)) return;
            skin.SetBlendShapeWeight(i, Mathf.Clamp01(v) * 100f);
            shapeLastWritten[i] = Mathf.Clamp01(v) * 100f;
        }

        public bool HasBlendShape(string n) => shapeIndex.ContainsKey(n);
    }
}
