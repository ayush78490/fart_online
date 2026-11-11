# Pig character - 12: expression presets + procedural animation clips with bounded spring follow-through.
#
# Every clip is a function pose(t) sampled per frame and written as baked keys onto Pig_Rig actions.
# Secondary motion (ear tips, belly, tail) is a damped spring driven by the parent's world acceleration,
# scaled by CTRL_Body.Cartoon_Intensity and hard-clamped, so it always settles and never drifts.
import bpy, math, json, os
from mathutils import Vector, Matrix, Quaternion

ROOT = r"D:\unityProjects\fartonline\ArtSource\PigCharacter"
rig = bpy.data.objects["Pig_Rig"]
PB = rig.pose.bones
REST3 = {b.name: b.matrix_local.to_3x3() for b in rig.data.bones}
FPS = 30
bpy.context.scene.render.fps = FPS

# ------------------------------------------------------------------ expression presets (blendable)
PRESETS = {
    "Neutral": {},
    "Happy": {"Smile": 0.8, "Cheek_Raise": 0.6, "Squint_L": 0.25, "Squint_R": 0.25, "Brow_Raise_L": 0.35,
              "Brow_Raise_R": 0.35, "Ear_Tilt_L": -0.2, "Ear_Tilt_R": -0.2},
    "Excited": {"Smile": 1.0, "Mouth_Open": 0.55, "Eyes_Wide": 0.8, "Brow_Raise_L": 1.0, "Brow_Raise_R": 1.0,
                "Cheek_Raise": 0.5, "Ear_Tilt_L": -0.5, "Ear_Tilt_R": -0.5},
    "Sad": {"Frown": 0.85, "Brow_Raise_L": 0.45, "Brow_Raise_R": 0.45, "Brow_Furrow": 0.35, "Squint_L": 0.25,
            "Squint_R": 0.25, "Eye_Look_Y": -0.45, "Ear_Tilt_L": 0.8, "Ear_Tilt_R": 0.8},
    "Angry": {"Frown": 0.7, "Brow_Furrow": 1.0, "Snout_Widen": 0.7, "Squint_L": 0.45, "Squint_R": 0.45,
              "Mouth_Wide": 0.3, "Ear_Tilt_L": -0.6, "Ear_Tilt_R": -0.6},
    "Surprised": {"Mouth_O": 1.0, "Eyes_Wide": 1.0, "Brow_Raise_L": 1.0, "Brow_Raise_R": 1.0, "Snout_Compress": 0.3,
                  "Ear_Tilt_L": -0.45, "Ear_Tilt_R": -0.45},
    "Confused": {"Smile_R": 0.35, "Frown": 0.25, "Brow_Raise_L": 1.0, "Brow_Furrow": 0.25, "Squint_R": 0.45,
                 "Eye_Look_X": 0.35, "Eye_Look_Y": 0.3, "Ear_Tilt_L": -0.5, "Ear_Tilt_R": 0.45},
    "Sleepy": {"Blink_Both": 0.62, "Mouth_Open": 0.12, "Eye_Look_Y": -0.3, "Ear_Tilt_L": 0.6, "Ear_Tilt_R": 0.6,
               "Snout_Compress": 0.15},
    "Laughing": {"Smile": 1.0, "Mouth_Open": 1.0, "Cheek_Raise": 1.0, "Squint_L": 0.8, "Squint_R": 0.8,
                 "Brow_Raise_L": 0.5, "Brow_Raise_R": 0.5},
    "Worried": {"Frown": 0.45, "Brow_Raise_L": 0.7, "Brow_Raise_R": 0.7, "Brow_Furrow": 0.55, "Mouth_Wide": 0.3,
                "Eyes_Wide": 0.3, "Eye_Look_X": -0.3, "Ear_Tilt_L": 0.3, "Ear_Tilt_R": 0.3},
}
FACE_KEYS = list(PB["CTRL_Face"].keys())
BODY_KEYS = list(PB["CTRL_Body"].keys())

# ------------------------------------------------------------------ pose primitives (no depsgraph updates)
def reset():
    for pb in PB:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
    for k in FACE_KEYS:
        PB["CTRL_Face"][k] = 0.0
    for k in BODY_KEYS:
        PB["CTRL_Body"][k] = 1.0 if (k.startswith("IK_") or k == "Cartoon_Intensity") else 0.0

def R(bone, axis, deg):
    """rotate bone about an armature-space axis, expressed relative to its parent (FK-style)"""
    if abs(deg) < 1e-6:
        return
    M = REST3[bone]
    q = (M.inverted() @ Matrix.Rotation(math.radians(deg), 3, Vector(axis).normalized()) @ M).to_quaternion()
    PB[bone].rotation_quaternion = q @ PB[bone].rotation_quaternion

def L(bone, delta):
    PB[bone].location = PB[bone].location + REST3[bone].inverted() @ Vector(delta)

def P(**kw):
    for k, v in kw.items():
        (PB["CTRL_Face"] if k in FACE_KEYS else PB["CTRL_Body"])[k] = float(v)

def expr(name, w=1.0):
    for k, v in PRESETS[name].items():
        cur = PB["CTRL_Face"][k]
        PB["CTRL_Face"][k] = max(-1.0, min(1.0, cur + v * w))

def smooth(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)

def env(t, a, b, c, d):
    """trapezoid envelope: rises a->b, holds, falls c->d (t in frames)"""
    if t <= a or t >= d:
        return 0.0
    if t < b:
        return smooth((t - a) / (b - a))
    if t <= c:
        return 1.0
    return 1.0 - smooth((t - c) / (d - c))

def lerp_keys(t, keys):
    """keys: [(frame, value)], smooth interpolation"""
    if t <= keys[0][0]:
        return keys[0][1]
    for (f0, v0), (f1, v1) in zip(keys[:-1], keys[1:]):
        if f0 <= t <= f1:
            return v0 + (v1 - v0) * smooth((t - f0) / (f1 - f0))
    return keys[-1][1]

# digits: curl towards the palm (palm faces the body)
def digit(bone, curl_deg, s, thumb_up=0.0, point=0.0):
    sx = 1 if s == "L" else -1
    b = rig.data.bones[bone]
    d = (b.tail_local - b.head_local).normalized()
    palm = Vector((-sx, 0.15, 0.2)).normalized()
    R(bone, d.cross(palm), curl_deg)
    if thumb_up:
        R(bone, d.cross(Vector((0, 0, 1))), thumb_up)
    if point:
        R(bone, d.cross(Vector((0, -1, 0.15))), point)

def hand_shape(s, shape, w=1.0):
    if shape == "fist":
        for dn in ("Thumb", "Index", "Finger"):
            digit(dn + "_" + s, 85 * w, s)
    elif shape == "point":
        digit("Index_" + s, 0, s, point=55 * w)
        digit("Thumb_" + s, 70 * w, s)
        digit("Finger_" + s, 85 * w, s)
    elif shape == "thumbs_up":
        digit("Thumb_" + s, 0, s, thumb_up=80 * w)
        digit("Index_" + s, 85 * w, s)
        digit("Finger_" + s, 85 * w, s)
    elif shape == "open":
        for dn, a in (("Thumb", -20), ("Index", -12), ("Finger", -18)):
            digit(dn + "_" + s, a * w, s)
    elif shape == "grip":
        for dn in ("Thumb", "Index", "Finger"):
            digit(dn + "_" + s, 50 * w, s)

def ik_hand(s, delta):
    L("CTRL_IK_Hand_" + s, delta)

def ik_foot(s, delta):
    L("CTRL_IK_Foot_" + s, delta)

# ------------------------------------------------------------------ springs (secondary motion)
class Spring:
    def __init__(self, bone, point_fn, k, c, gain, clamp, mode):
        self.bone, self.point_fn, self.k, self.c, self.gain, self.clamp, self.mode = bone, point_fn, k, c, gain, clamp, mode
        self.reset()

    def reset(self):
        self.x = Vector()
        self.v = Vector()
        self.hist = []

    def step(self, dt, intensity):
        p = self.point_fn()
        self.hist.append(p.copy())
        if len(self.hist) > 3:
            self.hist.pop(0)
        a = Vector() if len(self.hist) < 3 else (self.hist[2] - 2 * self.hist[1] + self.hist[0]) / (dt * dt)
        # semi-implicit Euler, two substeps
        for _ in range(2):
            h = dt / 2
            acc = -self.k * self.x - self.c * self.v - a * self.gain * intensity
            self.v += acc * h
            self.x += self.v * h
        if self.x.length > self.clamp:
            self.x = self.x.normalized() * self.clamp
            self.v *= 0.5
        return self.x

    def apply(self):
        pb = PB[self.bone]
        M3 = pb.matrix.to_3x3().normalized()
        if self.mode == "rot":
            d = (pb.tail - pb.head)
            ln = d.length
            ax = d.normalized().cross(self.x)
            if ax.length < 1e-6:
                return
            ang = min(0.6, self.x.length / ln)
            q = (M3.inverted() @ Matrix.Rotation(ang, 3, ax.normalized()) @ M3).to_quaternion()
            pb.rotation_quaternion = pb.rotation_quaternion @ q
        else:
            pb.location = pb.location + M3.inverted() @ self.x

def world_head(bone):
    return lambda: rig.matrix_world @ PB[bone].head

SPRINGS = [
    Spring("Ear_Tip_L", world_head("Ear_Tip_L"), 110.0, 8.0, 0.9, 0.07, "rot"),
    Spring("Ear_Tip_R", world_head("Ear_Tip_R"), 110.0, 8.0, 0.9, 0.07, "rot"),
    Spring("Belly", world_head("Belly"), 140.0, 9.0, 0.55, 0.035, "loc"),
    Spring("Tail_2", world_head("Tail_2"), 160.0, 7.0, 0.8, 0.05, "rot"),
]

# ------------------------------------------------------------------ baking
# driver-owned channels (lids, brows, eye aim/scale, ear tilt, eye target) are produced by the CTRL props
KEY_BONES = [b.name for b in rig.data.bones if not b.name.startswith(("Lid_", "Brow_", "CTRL_Eye_Target", "Eye_"))
             and b.name not in ("CTRL_Face", "CTRL_Body", "Ear_L", "Ear_R")]

def bake(name, frames, pose_fn, loop=False, springs=True):
    act = bpy.data.actions.get(name)
    if act:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = None
    dt = 1.0 / FPS
    for s in SPRINGS:
        s.reset()
    samples = []
    passes = 2 if (loop and springs) else 1
    for ps in range(passes):
        for f in range(frames + (0 if loop else 1)):
            reset()
            pose_fn(f)
            if springs:
                bpy.context.view_layer.update()
                ci = PB["CTRL_Body"]["Cartoon_Intensity"]
                for s in SPRINGS:
                    s.step(dt, ci)
                for s in SPRINGS:
                    s.apply()
            if ps == passes - 1:
                rec = {}
                for b in KEY_BONES:
                    pb = PB[b]
                    rec[(b, "location")] = tuple(pb.location)
                    rec[(b, "rotation_quaternion")] = tuple(pb.rotation_quaternion)
                    rec[(b, "scale")] = tuple(pb.scale)
                for k in FACE_KEYS:
                    rec[("CTRL_Face", k)] = (PB["CTRL_Face"][k],)
                for k in BODY_KEYS:
                    rec[("CTRL_Body", k)] = (PB["CTRL_Body"][k],)
                samples.append(rec)
    if loop:
        samples.append(samples[0])          # close the loop exactly
    n = len(samples)
    for key in samples[0]:
        vals = [s[key] for s in samples]
        if all(v == vals[0] for v in vals):
            # constant channel: keep a single key only for props / non-rest bone values
            rest = {"location": (0, 0, 0), "rotation_quaternion": (1, 0, 0, 0), "scale": (1, 1, 1)}
            if key[1] in rest and tuple(round(x, 6) for x in vals[0]) == rest[key[1]]:
                continue
        bone, ch = key
        if bone in ("CTRL_Face", "CTRL_Body") and ch not in ("location", "rotation_quaternion", "scale"):
            path = 'pose.bones["%s"]["%s"]' % (bone, ch)
        else:
            path = 'pose.bones["%s"].%s' % (bone, ch)
        for idx in range(len(vals[0])):
            fc = act.fcurves.new(path, index=idx, action_group=bone)
            fc.keyframe_points.add(n)
            co = []
            for f, v in enumerate(vals):
                co += [float(f), float(v[idx])]
            fc.keyframe_points.foreach_set("co", co)
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
            fc.update()
    act.frame_range = (0, n - 1)
    act["loop"] = loop
    reset()
    return act

# ------------------------------------------------------------------ clips
def a_ref_pose(f):
    # the reference image: looking up, ears asymmetric, slight head tilt, pleased smile
    P(Eye_Look_Y=0.95, Eye_Look_X=0.15, Smile=0.35, Ear_Tilt_L=-0.3, Ear_Tilt_R=0.55)
    R("Head", (0, 1, 0), -6)
    R("Head", (1, 0, 0), -5)

def a_idle(f, N=72):
    ph = 2 * math.pi * f / N
    b = math.sin(ph)
    R("Chest", (1, 0, 0), -1.4 * b)
    R("Head", (1, 0, 0), 1.2 * b)
    L("Hips", (0, 0, 0.004 * b))
    P(Belly_Squash=max(0.0, -b) * 0.12, Belly_Stretch=max(0.0, b) * 0.10)
    for s, sx in (("L", 1), ("R", -1)):
        ik_hand(s, (sx * 0.006 * b, 0, 0.008 * b))
    P(Blink_Both=env(f, 50, 52, 53, 56))

def walk_cycle(f, N, stride, lift, bounce, lean, arm, squash=0.0):
    ph = 2 * math.pi * f / N
    for s, sgn in (("L", 1), ("R", -1)):
        p = ph + (0 if s == "L" else math.pi)
        ik_foot(s, (0, -stride * math.cos(p), lift * max(0.0, math.sin(p)) ** 1.5))
        R("CTRL_IK_Foot_" + s, (1, 0, 0), 14 * max(0.0, math.sin(p)) * (1 if math.cos(p) < 0 else -0.6))
        sx = 1 if s == "L" else -1
        ik_hand(s, (0, arm * math.cos(p), 0.02 * abs(math.cos(p))))
    L("Hips", (0.018 * math.sin(ph), 0, -bounce * (1 - abs(math.cos(ph)))))
    R("Hips", (0, 0, 1), 5 * math.sin(ph))
    R("Spine", (0, 0, 1), -3.5 * math.sin(ph))
    R("Spine", (1, 0, 0), lean)
    R("Chest", (0, 0, 1), -2.5 * math.sin(ph))
    R("Chest", (0, 1, 0), 2.0 * math.cos(ph))
    R("Head", (0, 0, 1), 2.5 * math.sin(ph))
    R("Head", (1, 0, 0), -lean * 0.6 + 1.5 * math.cos(2 * ph))
    if squash:
        contact = max(0.0, math.cos(2 * ph)) ** 4
        P(Squash=squash * contact, Stretch=squash * 0.6 * max(0.0, -math.cos(2 * ph)) ** 3)
    P(Smile=0.3)

def a_walk(f):
    walk_cycle(f, 32, 0.075, 0.07, 0.02, 4, 0.07)

def a_run(f):
    walk_cycle(f, 20, 0.12, 0.10, 0.04, 11, 0.12, squash=0.22)
    P(Mouth_Open=0.25, Smile=0.5)
    for s in ("L", "R"):
        hand_shape(s, "grip", 0.7)

def jump_body(f, t_air0, t_air1, peak):
    # root height: parabola between take-off and touch-down
    if t_air0 <= f <= t_air1:
        u = (f - t_air0) / (t_air1 - t_air0)
        L("Root", (0, 0, 4 * peak * u * (1 - u)))

def a_jump(f):
    crouch = lerp_keys(f, [(0, 0), (9, 1), (12, 0.0), (32, 0.0), (35, 0.9), (44, 0.15), (56, 0)])
    L("Hips", (0, 0.01, -0.09 * crouch))
    R("Spine", (1, 0, 0), 12 * crouch)
    R("Head", (1, 0, 0), -8 * crouch)
    jump_body(f, 12, 32, 0.42)
    P(Squash=lerp_keys(f, [(0, 0), (9, 0.55), (11, 0), (33, 0), (35, 0.8), (40, 0.1), (46, 0.25), (56, 0)]),
      Stretch=lerp_keys(f, [(9, 0), (12, 0.7), (18, 0.25), (26, 0), (31, 0.35), (33, 0)]),
      Belly_Squash=lerp_keys(f, [(32, 0), (35, 0.8), (40, 0), (44, 0.3), (50, 0)]))
    arms = lerp_keys(f, [(0, 0), (9, -1), (13, 1), (26, 0.6), (33, -0.4), (44, 0), (56, 0)])
    for s, sx in (("L", 1), ("R", -1)):
        ik_hand(s, (sx * 0.06 * max(0, arms), 0.10 * min(0, arms) * -1, 0.30 * max(0, arms)))
        ik_foot(s, (0, 0, 0.05 * env(f, 12, 16, 26, 31)))
    P(Mouth_O=env(f, 12, 16, 26, 32) * 0.6, Eyes_Wide=env(f, 12, 15, 28, 34) * 0.7,
      Smile=env(f, 34, 40, 56, 60) * 0.8, Squint_L=env(f, 33, 35, 37, 41) * 0.6, Squint_R=env(f, 33, 35, 37, 41) * 0.6)

def a_land(f):
    jump_body(f, -10, 6, 0.30) if f <= 6 else None
    P(Stretch=lerp_keys(f, [(0, 0.35), (5, 0.3), (6, 0)]),
      Squash=lerp_keys(f, [(5, 0), (8, 0.9), (13, 0.05), (18, 0.3), (24, 0), (34, 0)]),
      Belly_Squash=lerp_keys(f, [(5, 0), (8, 1.0), (13, 0), (17, 0.35), (22, 0)]))
    crouch = lerp_keys(f, [(5, 0), (8, 1), (16, 0.2), (22, 0.35), (34, 0)])
    L("Hips", (0, 0.01, -0.10 * crouch))
    R("Spine", (1, 0, 0), 10 * crouch)
    for s, sx in (("L", 1), ("R", -1)):
        ik_hand(s, (sx * 0.08 * crouch, -0.05 * crouch, 0.10 * lerp_keys(f, [(0, 1), (6, 1), (10, -0.3), (20, 0)])))
    P(Squint_L=env(f, 6, 8, 11, 16) * 0.7, Squint_R=env(f, 6, 8, 11, 16) * 0.7, Frown=env(f, 6, 8, 10, 15) * 0.4)

def a_look_lr(f):
    yaw = lerp_keys(f, [(0, 0), (12, 32), (26, 32), (40, -32), (54, -32), (66, 0)])
    eye = lerp_keys(f, [(0, 0), (6, 0.8), (22, 0.4), (32, -0.8), (50, -0.4), (60, 0)])
    R("Neck", (0, 0, 1), yaw * 0.35)
    R("Head", (0, 0, 1), yaw * 0.65)
    P(Eye_Look_X=eye, Blink_Both=env(f, 27, 29, 30, 33))

def a_look_up(f):
    w = lerp_keys(f, [(0, 0), (14, 1), (34, 1), (48, 0)])
    R("Head", (1, 0, 0), -14 * w)
    R("Neck", (1, 0, 0), -5 * w)
    P(Eye_Look_Y=0.95 * w, Brow_Raise_L=0.5 * w, Brow_Raise_R=0.5 * w, Ear_Tilt_L=-0.3 * w, Ear_Tilt_R=0.5 * w,
      Smile=0.35 * w)

def a_blink(f):
    P(Blink_Both=lerp_keys(f, [(0, 0), (3, 1), (5, 1), (9, 0)]))

def a_smile(f):
    w = lerp_keys(f, [(0, 0), (12, 1), (30, 1), (40, 0)])
    expr("Happy", w)

def a_laugh(f):
    w = lerp_keys(f, [(0, 0), (8, 1), (52, 1), (60, 0)])
    expr("Laughing", w)
    k = math.sin(2 * math.pi * f / 8) * w
    R("Chest", (1, 0, 0), -4 * k)
    R("Head", (1, 0, 0), -6 * w + 3 * k)
    P(Belly_Squash=max(0.0, k) * 0.5, Mouth_Open=w * (0.75 + 0.25 * k))
    for s, sx in (("L", 1), ("R", -1)):
        ik_hand(s, (-sx * 0.10 * w, -0.12 * w, 0.03 * w + 0.015 * k))

VISEMES = ["V_REST", "V_MBP", "V_AA", "V_L", "V_EE", "V_OH", "V_FV", "V_AA", "V_EE", "V_MBP", "V_OH", "V_REST"]
def a_talk(f):
    step = 5
    i = int(f // step)
    u = (f % step) / step
    for j, v in enumerate(VISEMES):
        w = 0.0
        if j == i:
            w = 1 - smooth(u) * 0.6
        elif j == i + 1:
            w = smooth(u)
        if w:
            PB["CTRL_Face"][v] = max(PB["CTRL_Face"][v], min(1.0, w))
    R("Head", (1, 0, 0), 2.5 * math.sin(2 * math.pi * f / 20))
    R("Head", (0, 0, 1), 2.0 * math.sin(2 * math.pi * f / 33))
    P(Brow_Raise_L=0.3 * max(0, math.sin(2 * math.pi * f / 30)), Brow_Raise_R=0.3 * max(0, math.sin(2 * math.pi * f / 30)))

def a_surprised(f):
    w = lerp_keys(f, [(0, 0), (5, 1.1), (9, 1), (34, 1), (46, 0)])
    expr("Surprised", min(1.0, w))
    P(Stretch=lerp_keys(f, [(0, 0), (5, 0.35), (10, 0), (46, 0)]))
    R("Head", (1, 0, 0), -6 * min(1, w))
    for s, sx in (("L", 1), ("R", -1)):
        ik_hand(s, (sx * 0.05 * w, -0.06 * w, 0.18 * w))
        hand_shape(s, "open", min(1, w))

def a_ear_follow(f):
    yaw = lerp_keys(f, [(0, 0), (4, 28), (8, -28), (12, 24), (16, -18), (19, 0)])
    R("Head", (0, 0, 1), yaw)
    nod = lerp_keys(f, [(22, 0), (25, -16), (29, 10), (32, 0)])
    R("Head", (1, 0, 0), nod)

def a_wave(f):
    up = lerp_keys(f, [(0, 0), (10, 1), (46, 1), (56, 0)])
    S = Vector((-0.33, 0.0, 0.93))
    hand_rest = Vector(rig.data.bones["Hand_R"].head_local)
    target = Vector((-0.50, -0.12, 1.24)) + Vector((0.05 * math.sin(2 * math.pi * f / 12) * up, 0, 0))
    ik_hand("R", (target - hand_rest) * up)
    R("CTRL_IK_Hand_R", (0, 1, 0), (-150 + 20 * math.sin(2 * math.pi * f / 12)) * up)
    hand_shape("R", "open", up)
    R("Head", (0, 1, 0), 5 * up)
    expr("Happy", up)

GESTURES = [("open", 0), ("fist", 24), ("point", 48), ("thumbs_up", 72), ("grip", 96), ("open", 120)]
def a_gestures(f):
    for (g0, f0), (g1, f1) in zip(GESTURES[:-1], GESTURES[1:]):
        if f0 <= f <= f1:
            u = smooth(min(1.0, (f - f0) / 8.0))
            for s in ("L", "R"):
                hand_shape(s, g0, 1 - u)
                hand_shape(s, g1, u)
    lift = lerp_keys(f, [(0, 0), (10, 1), (114, 1), (124, 0)])
    for s, sx in (("L", 1), ("R", -1)):
        ik_hand(s, (-sx * 0.04 * lift, -0.16 * lift, 0.16 * lift))
        R("CTRL_IK_Hand_" + s, (0, 1, 0), -sx * 35 * lift)

def a_squat(f):
    w = lerp_keys(f, [(0, 0), (22, 1), (38, 1), (60, 0)])
    L("Hips", (0, 0.02, -0.16 * w))
    R("Spine", (1, 0, 0), 15 * w)
    R("Head", (1, 0, 0), -10 * w)
    P(Belly_Squash=0.6 * w)
    for s, sx in (("L", 1), ("R", -1)):
        ik_hand(s, (-sx * 0.02 * w, -0.24 * w, 0.10 * w))

ORDER = ["Neutral", "Happy", "Excited", "Sad", "Angry", "Surprised", "Confused", "Sleepy", "Laughing", "Worried", "Neutral"]
def a_expressions(f):
    seg = 22
    i = min(len(ORDER) - 2, int(f // seg))
    u = smooth(min(1.0, (f - i * seg) / 10.0))
    expr(ORDER[i], 1 - u)
    expr(ORDER[i + 1], u)
    P(Blink_Both=max(PB["CTRL_Face"]["Blink_Both"], env(f % 66, 40, 42, 43, 46)))

CLIPS = [
    ("Pig_RefPose", 1, a_ref_pose, False),
    ("Pig_Idle_Breathing", 72, a_idle, True),
    ("Pig_Walk", 32, a_walk, True),
    ("Pig_Run", 20, a_run, True),
    ("Pig_Jump", 60, a_jump, False),
    ("Pig_Land", 36, a_land, False),
    ("Pig_Look_LeftRight", 70, a_look_lr, False),
    ("Pig_Look_Up", 50, a_look_up, False),
    ("Pig_Blink", 12, a_blink, False),
    ("Pig_Smile", 42, a_smile, False),
    ("Pig_Laugh", 62, a_laugh, False),
    ("Pig_Talk", 60, a_talk, False),
    ("Pig_Surprised", 48, a_surprised, False),
    ("Pig_Ear_FollowThrough", 48, a_ear_follow, False),
    ("Pig_Wave", 58, a_wave, False),
    ("Pig_Hand_Gestures", 126, a_gestures, False),
    ("Pig_Squat", 62, a_squat, False),
    ("Pig_Expression_Transitions", 22 * 10 + 12, a_expressions, False),
]

# single-frame pose clips: rest + every bone-driven facial control at its extreme. Unity samples these once
# and blends the deltas at runtime (PigFaceController), so no bone-axis conventions are hard-coded in C#.
FACE_POSE_CONTROLS = [("Blink_L", 1), ("Blink_R", 1), ("Squint_L", 1), ("Squint_R", 1), ("Eyes_Wide", 1),
                      ("Brow_Raise_L", 1), ("Brow_Raise_R", 1), ("Brow_Furrow", 1),
                      ("Ear_Tilt_L", 1), ("Ear_Tilt_L", -1), ("Ear_Tilt_R", 1), ("Ear_Tilt_R", -1),
                      ("Eye_Look_X", 1), ("Eye_Look_X", -1), ("Eye_Look_Y", 1), ("Eye_Look_Y", -1)]
def _face_clip(ctrl, val):
    def f(t):
        P(**{ctrl: val})
    return f
CLIPS.append(("Pig_FACE_Rest", 1, lambda t: None, False))
for ctrl, val in FACE_POSE_CONTROLS:
    CLIPS.append(("Pig_FACE_%s%s" % (ctrl, "" if ctrl not in ("Ear_Tilt_L", "Ear_Tilt_R", "Eye_Look_X", "Eye_Look_Y")
                                     else ("_Pos" if val > 0 else "_Neg")), 1, _face_clip(ctrl, val), False))

def bake_all(names=None):
    out = []
    for name, frames, fn, loop in CLIPS:
        if names and name not in names:
            continue
        a = bake(name, frames, fn, loop=loop, springs=not name.startswith("Pig_FACE_"))
        out.append((name, int(a.frame_range[1]), len(a.fcurves)))
    rig.animation_data.action = bpy.data.actions.get("Pig_RefPose")
    return out

with open(os.path.join(ROOT, "exports", "PigExpressions.json"), "w") as fh:
    json.dump({"presets": PRESETS, "visemes": ["V_AA", "V_EE", "V_OH", "V_FV", "V_MBP", "V_L", "V_REST"]}, fh, indent=2)

if __name__ != "__lib__":
    for r in bake_all():
        print("clip", r)
