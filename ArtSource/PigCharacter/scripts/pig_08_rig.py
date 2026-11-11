# Pig character - 08: Pig_Rig armature (deform skeleton + control bones), IK/FK, eye targets,
# face/body control properties, skin weights for every part.
import bpy, math
from mathutils import Vector, Matrix

RIG_NAME = "Pig_Rig"
PARTS_RIGID = {}   # filled below: object -> weighting rule

def obj(n):
    return bpy.data.objects[n]

def centre(n):
    ob = obj(n)
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return sum(vs, Vector()) / len(vs)

def bbox(n):
    ob = obj(n)
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return (Vector([min(v[i] for v in vs) for i in range(3)]), Vector([max(v[i] for v in vs) for i in range(3)]))

# ------------------------------------------------------------------ joint positions
EYE_L = centre("Pig_Eye_L")
BROW_L = centre("Pig_Brow_L")
EAR_BASE_L = Vector((0.285, 0.03, 1.37))
def ear_tip(n, base):
    ob = obj(n)
    return max((ob.matrix_world @ v.co for v in ob.data.vertices), key=lambda p: (p - base).length)
EAR_TIP_L = ear_tip("Pig_Ear_L", EAR_BASE_L)
# ear mid: the vertex-average of the upper half
EAR_MID_L = EAR_BASE_L.lerp(EAR_TIP_L, 0.5) + Vector((0.02, -0.03, 0.04))

J = {
    "Root": ((0, 0, 0), (0, 0.3, 0)),
    "Hips": ((0, 0.02, 0.40), (0, 0.02, 0.62)),
    "Spine": ((0, 0.02, 0.62), (0, 0.02, 0.84)),
    "Chest": ((0, 0.02, 0.84), (0, 0.02, 1.04)),
    "Neck": ((0, 0.02, 1.04), (0, 0.02, 1.15)),
    "Head": ((0, 0.02, 1.15), (0, 0.02, 1.50)),
    "Belly": ((0, -0.22, 0.50), (0, -0.42, 0.50)),
    "Tail_1": ((0, 0.36, 0.41), (0, 0.42, 0.47)),
    "Tail_2": ((0, 0.42, 0.47), (0, 0.46, 0.56)),
}
PARENT = {"Hips": "Root", "Spine": "Hips", "Chest": "Spine", "Neck": "Chest", "Head": "Neck", "Belly": "Spine",
          "Tail_1": "Hips", "Tail_2": "Tail_1"}

DIG = {"Thumb": (0.575, -0.145, 0.625), "Index": (0.625, -0.09, 0.595), "Finger": (0.645, -0.035, 0.615)}
HAND_END = Vector((0.60, -0.08, 0.645))
for s, sx in (("L", 1), ("R", -1)):
    m = lambda v: (sx * v[0], v[1], v[2])
    J["Shoulder_" + s] = (m((0.10, 0.02, 1.00)), m((0.33, 0.0, 0.93)))
    J["UpperArm_" + s] = (m((0.33, 0.0, 0.93)), m((0.47, -0.04, 0.80)))
    J["LowerArm_" + s] = (m((0.47, -0.04, 0.80)), m((0.56, -0.07, 0.69)))
    J["Hand_" + s] = (m((0.56, -0.07, 0.69)), m(HAND_END))
    for d, p in DIG.items():
        nub = Vector(p)
        base = Vector((0.56, -0.07, 0.69)).lerp(nub, 0.55)
        tip = nub + (nub - Vector((0.56, -0.07, 0.69))).normalized() * 0.055
        J[d + "_" + s] = (m(base), m(tip))
        PARENT[d + "_" + s] = "Hand_" + s
    J["Thigh_" + s] = (m((0.235, 0.02, 0.36)), m((0.255, -0.03, 0.18)))
    J["Shin_" + s] = (m((0.255, -0.03, 0.18)), m((0.262, -0.02, 0.075)))
    J["Foot_" + s] = (m((0.262, -0.02, 0.075)), m((0.262, -0.16, 0.035)))
    e = EYE_L if sx > 0 else Vector((-EYE_L.x, EYE_L.y, EYE_L.z))
    J["Eye_" + s] = (tuple(e), tuple(e + Vector((0, -0.10, 0))))
    J["Lid_Up_" + s] = (tuple(e), tuple(e + Vector((0, -0.06, 0))))
    J["Lid_Low_" + s] = (tuple(e), tuple(e + Vector((0, -0.05, 0))))
    b = BROW_L if sx > 0 else Vector((-BROW_L.x, BROW_L.y, BROW_L.z))
    J["Brow_" + s] = (tuple(b), tuple(b + Vector((0, 0, 0.045))))
    eb = EAR_BASE_L if sx > 0 else Vector((-EAR_BASE_L.x, EAR_BASE_L.y, EAR_BASE_L.z))
    em = EAR_MID_L if sx > 0 else Vector((-EAR_MID_L.x, EAR_MID_L.y, EAR_MID_L.z))
    et = EAR_TIP_L if sx > 0 else Vector((-EAR_TIP_L.x, EAR_TIP_L.y, EAR_TIP_L.z))
    J["Ear_" + s] = (tuple(eb), tuple(em))
    J["Ear_Tip_" + s] = (tuple(em), tuple(et))
    PARENT.update({"Shoulder_" + s: "Chest", "UpperArm_" + s: "Shoulder_" + s, "LowerArm_" + s: "UpperArm_" + s,
                   "Hand_" + s: "LowerArm_" + s, "Thigh_" + s: "Hips", "Shin_" + s: "Thigh_" + s,
                   "Foot_" + s: "Shin_" + s, "Eye_" + s: "Head", "Lid_Up_" + s: "Head", "Lid_Low_" + s: "Head",
                   "Brow_" + s: "Head", "Ear_" + s: "Head", "Ear_Tip_" + s: "Ear_" + s})

CONNECT = {"Spine", "Chest", "Neck", "Head", "LowerArm_L", "LowerArm_R", "Hand_L", "Hand_R", "Shin_L", "Shin_R",
           "Foot_L", "Foot_R", "Ear_Tip_L", "Ear_Tip_R", "Tail_2", "UpperArm_L", "UpperArm_R"}

# control (non-deform) bones
CTRL = {
    "CTRL_Body": ((0, 0.75, 0.9), (0, 0.75, 1.05), "Root"),
    "CTRL_Face": ((0, -0.75, 1.62), (0, -0.75, 1.72), "Head"),
    "CTRL_Eye_Target": ((0, -1.2, EYE_L.z), (0, -1.2, EYE_L.z + 0.08), "Head"),
}
for s, sx in (("L", 1), ("R", -1)):
    CTRL["CTRL_Eye_Target_" + s] = ((sx * EYE_L.x, -1.2, EYE_L.z), (sx * EYE_L.x, -1.2, EYE_L.z + 0.05), "CTRL_Eye_Target")
    CTRL["CTRL_IK_Hand_" + s] = (J["Hand_" + s][0], tuple(Vector(J["Hand_" + s][0]) + (Vector(J["Hand_" + s][1]) - Vector(J["Hand_" + s][0])) * 1.6), "Root")
    CTRL["Pole_Arm_" + s] = ((sx * 0.50, 0.45, 0.80), (sx * 0.50, 0.50, 0.80), "Root")
    CTRL["CTRL_IK_Foot_" + s] = (J["Foot_" + s][0], J["Foot_" + s][1], "Root")      # same orientation as Foot
    CTRL["Pole_Leg_" + s] = ((sx * 0.26, -0.55, 0.18), (sx * 0.26, -0.60, 0.18), "Root")

NON_DEFORM_FOR_BODY_HEAT = {"Root", "Eye_L", "Eye_R", "Lid_Up_L", "Lid_Up_R", "Lid_Low_L", "Lid_Low_R", "Brow_L",
                            "Brow_R", "Ear_L", "Ear_R", "Ear_Tip_L", "Ear_Tip_R", "Tail_1", "Tail_2", "Belly"}

# ------------------------------------------------------------------ build armature
def build_armature():
    old = bpy.data.objects.get(RIG_NAME)
    if old:
        for ob in bpy.data.objects:
            if ob.parent == old:
                mw = ob.matrix_world.copy()
                ob.parent = None
                ob.matrix_world = mw
            for m in list(ob.modifiers) if ob.type == 'MESH' else []:
                if m.type == 'ARMATURE':
                    ob.modifiers.remove(m)
        bpy.data.objects.remove(old, do_unlink=True)
    arm = bpy.data.armatures.get(RIG_NAME)
    if arm:
        bpy.data.armatures.remove(arm)
    arm = bpy.data.armatures.new(RIG_NAME)
    rig = bpy.data.objects.new(RIG_NAME, arm)
    bpy.data.collections["CHARACTER_RIG"].objects.link(rig)
    arm.display_type = 'OCTAHEDRAL'
    rig.show_in_front = True
    for o in bpy.context.selected_objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.edit_bones
    for n, (h, t) in J.items():
        b = eb.new(n)
        b.head, b.tail = Vector(h), Vector(t)
    for n, (h, t, p) in CTRL.items():
        b = eb.new(n)
        b.head, b.tail = Vector(h), Vector(t)
        b.use_deform = False
    for n, p in PARENT.items():
        eb[n].parent = eb[p]
        if n in CONNECT and (eb[n].head - eb[p].tail).length < 1e-4:
            eb[n].use_connect = True
    for n, (h, t, p) in CTRL.items():
        eb[n].parent = eb[p]
    eb["Root"].use_deform = False
    # rolls: face/eye/lid bones get local X = world +X so lid/eye rotations are about the eye's horizontal axis
    for b in eb:
        b.align_roll(Vector((0, 0, 1)) if abs(b.vector.normalized().z) < 0.9 else Vector((0, -1, 0)))
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig

rig = build_armature()
arm = rig.data
pb = rig.pose.bones

# bone collections for readability
def bcoll(name, bones, color=None):
    c = arm.collections.get(name) or arm.collections.new(name)
    for n in bones:
        c.assign(arm.bones[n])
    return c
bcoll("Deform_Body", [n for n in J if not any(n.startswith(k) for k in ("Eye", "Lid", "Brow", "Ear", "Thumb", "Index", "Finger"))])
bcoll("Deform_Face", [n for n in J if any(n.startswith(k) for k in ("Eye", "Lid", "Brow", "Ear"))])
bcoll("Deform_Fingers", [n for n in J if any(n.startswith(k) for k in ("Thumb", "Index", "Finger"))])
bcoll("Controls", [n for n in CTRL])

# ------------------------------------------------------------------ custom properties
FACE_PROPS = ["Blink_L", "Blink_R", "Blink_Both", "Eye_Look_X", "Eye_Look_Y", "Squint_L", "Squint_R", "Eyes_Wide",
              "Brow_Raise_L", "Brow_Raise_R", "Brow_Furrow", "Smile", "Smile_L", "Smile_R", "Frown", "Mouth_Open",
              "Mouth_Wide", "Mouth_O", "Cheek_Raise", "Snout_Compress", "Snout_Widen", "Ear_Tilt_L", "Ear_Tilt_R",
              "V_AA", "V_EE", "V_OH", "V_FV", "V_MBP", "V_L", "V_REST"]
SIGNED = {"Eye_Look_X", "Eye_Look_Y", "Ear_Tilt_L", "Ear_Tilt_R"}
BODY_PROPS = {"Cartoon_Intensity": (1.0, 0.0, 2.0), "Squash": (0.0, 0.0, 1.0), "Stretch": (0.0, 0.0, 1.0),
              "Belly_Squash": (0.0, 0.0, 1.0), "Belly_Stretch": (0.0, 0.0, 1.0),
              "IK_Arm_L": (1.0, 0.0, 1.0), "IK_Arm_R": (1.0, 0.0, 1.0), "IK_Leg_L": (1.0, 0.0, 1.0), "IK_Leg_R": (1.0, 0.0, 1.0)}

def add_prop(bone, name, default, mn, mx):
    bone[name] = float(default)
    ui = bone.id_properties_ui(name)
    ui.update(min=mn, max=mx, soft_min=mn, soft_max=mx, default=float(default))
    bone.property_overridable_library_set('["%s"]' % name, True)

for n in FACE_PROPS:
    add_prop(pb["CTRL_Face"], n, 0.0, -1.0 if n in SIGNED else 0.0, 1.0)
for n, (d, mn, mx) in BODY_PROPS.items():
    add_prop(pb["CTRL_Body"], n, d, mn, mx)

# ------------------------------------------------------------------ constraints: IK / FK, eyes
def pole_angle_search(lower, target, pole, chain=2):
    """pick the pole angle that keeps the rest pose unchanged"""
    c = pb[lower].constraints.new('IK')
    c.target, c.subtarget, c.pole_target, c.pole_subtarget, c.chain_count = rig, target, rig, pole, chain
    rest = {n: rig.data.bones[n].matrix_local.copy() for n in (lower, pb[lower].parent.name)}
    best = (1e9, 0)
    for deg in range(-180, 180, 2):
        c.pole_angle = math.radians(deg)
        bpy.context.view_layer.update()
        err = sum((pb[n].matrix.to_3x3() - rest[n].to_3x3()).median_scale if False else
                  sum(abs(a - b) for ra, rb in zip(pb[n].matrix.to_3x3(), rest[n].to_3x3()) for a, b in zip(ra, rb))
                  for n in rest)
        if err < best[0]:
            best = (err, deg)
    centre_deg = best[1]
    for k in range(-8, 9):
        c.pole_angle = math.radians(centre_deg + k * 0.25)
        bpy.context.view_layer.update()
        err = sum(sum(abs(a - b) for ra, rb in zip(pb[n].matrix, rest[n]) for a, b in zip(ra, rb)) for n in rest)
        if err < best[0]:
            best = (err, centre_deg + k * 0.25)
    c.pole_angle = math.radians(best[1])
    return c, best

for s in ("L", "R"):
    c, best = pole_angle_search("LowerArm_" + s, "CTRL_IK_Hand_" + s, "Pole_Arm_" + s)
    c.name = "IK_Arm"
    print("arm", s, "pole angle", best)
    cr = pb["Hand_" + s].constraints.new('COPY_ROTATION')
    cr.target, cr.subtarget, cr.name = rig, "CTRL_IK_Hand_" + s, "IK_HandRot"
    c, best = pole_angle_search("Shin_" + s, "CTRL_IK_Foot_" + s, "Pole_Leg_" + s)
    c.name = "IK_Leg"
    print("leg", s, "pole angle", best)
    cr = pb["Foot_" + s].constraints.new('COPY_ROTATION')
    cr.target, cr.subtarget, cr.name = rig, "CTRL_IK_Foot_" + s, "IK_FootRot"
    dt = pb["Eye_" + s].constraints.new('DAMPED_TRACK')
    dt.target, dt.subtarget, dt.track_axis = rig, "CTRL_Eye_Target_" + s, 'TRACK_Y'
    dt.name = "EyeAim"

# IK/FK switch drivers
def drive(owner, path, expr, vars_, index=-1):
    fc = owner.driver_add(path, index) if index >= 0 else owner.driver_add(path)
    d = fc.driver
    d.type = 'SCRIPTED'
    d.expression = expr
    for vn, (bone, prop) in vars_.items():
        v = d.variables.new()
        v.name = vn
        v.type = 'SINGLE_PROP'
        v.targets[0].id_type = 'OBJECT'
        v.targets[0].id = rig
        v.targets[0].data_path = 'pose.bones["%s"]["%s"]' % (bone, prop)
    return fc

for s in ("L", "R"):
    drive(pb["LowerArm_" + s].constraints["IK_Arm"], "influence", "v", {"v": ("CTRL_Body", "IK_Arm_" + s)})
    drive(pb["Hand_" + s].constraints["IK_HandRot"], "influence", "v", {"v": ("CTRL_Body", "IK_Arm_" + s)})
    drive(pb["Shin_" + s].constraints["IK_Leg"], "influence", "v", {"v": ("CTRL_Body", "IK_Leg_" + s)})
    drive(pb["Foot_" + s].constraints["IK_FootRot"], "influence", "v", {"v": ("CTRL_Body", "IK_Leg_" + s)})

# eye look: move the shared target (bounded: +-1 => ~28 deg yaw, ~22 deg pitch)
drive(pb["CTRL_Eye_Target"], "location", "x * 0.52", {"x": ("CTRL_Face", "Eye_Look_X")}, 0)
# bone-local Y of a vertical bone is world Z -> look up/down
drive(pb["CTRL_Eye_Target"], "location", "y * 0.40", {"y": ("CTRL_Face", "Eye_Look_Y")}, 1)

# lids: rotation about the bone's local X (= world -X for these forward-pointing bones)
for s in ("L", "R"):
    for b in ("Lid_Up_" + s, "Lid_Low_" + s, "Brow_" + s, "Ear_" + s):
        pb[b].rotation_mode = 'XYZ'
    drive(pb["Lid_Up_" + s], "rotation_euler",
          "-radians(158) * min(1.0, max(b, bb) + sq * 0.35) + radians(14) * w",
          {"b": ("CTRL_Face", "Blink_" + s), "bb": ("CTRL_Face", "Blink_Both"), "sq": ("CTRL_Face", "Squint_" + s),
           "w": ("CTRL_Face", "Eyes_Wide")}, 0)
    drive(pb["Lid_Low_" + s], "rotation_euler",
          "radians(48) * min(1.0, max(b, bb) * 0.55 + sq)",
          {"b": ("CTRL_Face", "Blink_" + s), "bb": ("CTRL_Face", "Blink_Both"), "sq": ("CTRL_Face", "Squint_" + s)}, 0)
    sx = 1 if s == "L" else -1
    # brows: raise (bone-local Y = world up), furrow pulls inward/down and tilts the inner end down
    drive(pb["Brow_" + s], "location", "r * 0.035 - f * 0.012 + w * 0.012",
          {"r": ("CTRL_Face", "Brow_Raise_" + s), "f": ("CTRL_Face", "Brow_Furrow"), "w": ("CTRL_Face", "Eyes_Wide")}, 1)
    drive(pb["Brow_" + s], "location", "f * %f" % (-0.018 * sx), {"f": ("CTRL_Face", "Brow_Furrow")}, 0)
    drive(pb["Brow_" + s], "rotation_euler", "f * %f" % (math.radians(-22) * sx), {"f": ("CTRL_Face", "Brow_Furrow")}, 1)
    # eyes wide: eyeball pops a little bigger (bone scale, all axes)
    for ax in range(3):
        drive(pb["Eye_" + s], "scale", "1.0 + 0.13 * w - 0.05 * sq",
              {"w": ("CTRL_Face", "Eyes_Wide"), "sq": ("CTRL_Face", "Squint_" + s)}, ax)
    # ear tilt: rotate about the bone's local X (forward / back flop), +-1 => +-35 deg
    drive(pb["Ear_" + s], "rotation_euler", "t * radians(35)", {"t": ("CTRL_Face", "Ear_Tilt_" + s)}, 0)

bpy.context.view_layer.update()
print("rig bones:", len(arm.bones), "deform:", sum(1 for b in arm.bones if b.use_deform))
