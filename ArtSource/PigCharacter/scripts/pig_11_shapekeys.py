# Pig character - 11: facial + body shape keys (identical names on every part, merged at export), drivers.
#
# Shape keys are authored procedurally in rest space (object space == world space here). Every key is
# bounded: its full value (1.0) is the safe extreme that was validated visually.
import bpy, math
from mathutils import Vector, Matrix

rig = bpy.data.objects["Pig_Rig"]
MOUTH_HALF = 0.300

def curve_z(x):
    return 1.083 + 0.088 * (abs(x) / MOUTH_HALF) ** 2.2

def sstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)

def gauss(d, r):
    return math.exp(-(d / r) ** 2)

JAW_PIVOT = Vector((0.0, 0.02, 1.13))

def rot_x(p, deg, pivot=JAW_PIVOT):
    return Matrix.Rotation(math.radians(deg), 3, 'X') @ (p - pivot) + pivot

# ------------------------------------------------------------------ per-vertex region data for the body
body = bpy.data.objects["Pig_Body"]
lip_attr = body.data.attributes["lip_side"]
LIP = [lip_attr.data[i].value for i in range(len(body.data.vertices))]
MOUTH_VERTS = set()
for poly in body.data.polygons:
    if poly.material_index == 1:
        MOUTH_VERTS |= set(poly.vertices)

def regions(i, p):
    """returns dict of region weights for body vertex i at position p"""
    u = p.x / MOUTH_HALF
    dz = p.z - curve_z(p.x)
    front = sstep(-0.06, -0.20, p.y)
    lat = 1 - sstep(0.80, 1.30, abs(u))
    side = LIP[i]
    inside = i in MOUTH_VERTS
    r = {}
    # lower jaw (chin + lower lip + lower half of the mouth bag)
    if side < 0:
        r["jaw"] = 1 - sstep(0.85, 1.12, abs(u))
    elif side > 0 or (inside and side == 0):
        r["jaw"] = 0.0
    else:
        r["jaw"] = (1 - sstep(0.0, 0.17, -dz)) * front * lat if dz < 0 else 0.0
    # upper lip band
    if side > 0:
        r["upper"] = 1 - sstep(0.85, 1.12, abs(u))
    else:
        r["upper"] = (1 - sstep(0.0, 0.045, dz)) * front * lat if dz >= 0 and not inside else 0.0
    # lip line band (both sides) for curvature-type shapes
    r["line"] = gauss(dz, 0.055) * front * (1 - sstep(1.05, 1.45, abs(u)))
    if inside:
        r["line"] = 1 - sstep(0.85, 1.15, abs(u))
    r["lower_lip"] = (1 if side < 0 else ((1 - sstep(0.0, 0.05, -dz)) * front * lat if dz < 0 and not inside else 0.0))
    for sx in (1, -1):
        c = Vector((sx * 0.255, -0.24, 1.17))
        r["cheek_%d" % sx] = gauss((p - c).length, 0.085) * front
    r["u"] = u
    return r

BODY_R = [regions(v.index, body.matrix_world @ v.co) for v in body.data.vertices]

# ------------------------------------------------------------------ shape definitions (body)
def sh_mouth_open(i, p, r, deg=20.0):
    if r["jaw"] > 0:
        q = rot_x(p, deg * r["jaw"])
        p = q
    if r["upper"] > 0:
        p = p + Vector((0, 0, 0.010 * r["upper"]))
    return p

def sh_smile(i, p, r, sx=0):
    m = 1.0 if sx == 0 else sstep(-0.06, 0.06, p.x * sx)
    u = r["u"]
    w = r["line"] * m
    d = Vector((0.014 * u, 0.018 * u * u, 0.052 * min(1.0, u * u) - 0.006)) * w
    ck = (r["cheek_1"] + r["cheek_-1"]) * m
    d += Vector((0, -0.008, 0.018)) * ck
    return p + d

def sh_frown(i, p, r):
    u = r["u"]
    w = r["line"]
    d = Vector((-0.006 * u, 0.008 * u * u, -0.055 * min(1.0, u * u) + 0.010)) * w
    d += Vector((0, -0.006, 0.006)) * r["jaw"] * (1 - min(1.0, abs(u)))     # slight pout of the chin
    return p + d

def sh_wide(i, p, r):
    u = r["u"]
    return p + Vector((0.045 * u, 0.008 * min(1.0, u * u), 0.004 * min(1.0, u * u))) * r["line"]

def sh_o(i, p, r):
    u = r["u"]
    q = sh_mouth_open(i, p, r, deg=13.0)
    w = r["line"]
    pull = -0.13 * u * (1 - sstep(0.95, 1.35, abs(u)))
    # corners pucker towards the centre; centre of the lips pushes forward and the corners roll in
    dz = (0.012 if LIP[i] > 0 else (-0.010 if LIP[i] < 0 else 0.0)) * (1 - min(1.0, abs(u)))
    return q + Vector((pull, -0.030 * (1 - min(1.0, u * u)) + 0.02 * min(1.0, u * u), dz)) * w

def sh_cheek(i, p, r):
    ck = r["cheek_1"] + r["cheek_-1"]
    return p + Vector((0, -0.012, 0.026)) * ck

def sh_mbp(i, p, r):
    d = Vector()
    if LIP[i] > 0:
        d += Vector((0, -0.004, -0.0032))
    elif LIP[i] < 0:
        d += Vector((0, -0.004, 0.0032))
    d += Vector((0, -0.006, 0)) * r["line"] * (1 - min(1.0, abs(r["u"])))
    return p + d

def sh_fv(i, p, r):
    q = sh_mouth_open(i, p, r, deg=3.0)
    d = Vector((0, 0.012, 0.007)) * r["lower_lip"] * (1 - sstep(0.7, 1.1, abs(r["u"])))
    d += Vector((0, 0, 0.004)) * r["upper"]
    return q + d

BELLY_C, BELLY_R = Vector((0.0, -0.10, 0.50)), Vector((0.58, 0.55, 0.46))
def belly_w(p):
    d = Vector(((p.x - BELLY_C.x) / BELLY_R.x, (p.y - BELLY_C.y) / BELLY_R.y, (p.z - BELLY_C.z) / BELLY_R.z)).length
    return (1 - sstep(0.55, 1.0, d))

def sh_belly_squash(i, p, r):
    w = belly_w(p)
    return p + Vector((p.x * 0.10, (p.y + 0.02) * 0.11, -(p.z - 0.30) * 0.13)) * w

def sh_belly_stretch(i, p, r):
    w = belly_w(p)
    return p + Vector((-p.x * 0.06, -(p.y + 0.02) * 0.07, (p.z - 0.30) * 0.10)) * w

# whole-body squash / stretch: applied to every part so eyes, hooves etc. stay attached
def sh_squash(i, p, r):
    return Vector((p.x * 1.11, p.y * 1.11, p.z * 0.80))

def sh_stretch(i, p, r):
    return Vector((p.x * 0.92, p.y * 0.92, p.z * 1.16))

def sh_corr_shoulder(sx):
    S = Vector((sx * 0.33, 0.0, 0.93))
    def f(i, p, r):
        d = (p - S)
        w = gauss(d.length, 0.13) * sstep(0.85, 1.0, p.z) * (1.0 if p.x * sx > 0.15 else 0.0)
        n = body.data.vertices[i].normal
        return p + n * 0.025 * w + Vector((sx * 0.01, 0, -0.012)) * w
    return f

def combo(*parts):
    """viseme = weighted sum of deltas of base shapes"""
    def f(i, p, r):
        d = Vector()
        for fn, wgt in parts:
            d += (fn(i, p, r) - p) * wgt
        return p + d
    return f

BODY_SHAPES = {
    "Smile": sh_smile, "Smile_L": lambda i, p, r: sh_smile(i, p, r, 1), "Smile_R": lambda i, p, r: sh_smile(i, p, r, -1),
    "Frown": sh_frown, "Mouth_Open": sh_mouth_open, "Mouth_Wide": sh_wide, "Mouth_O": sh_o, "Cheek_Raise": sh_cheek,
    "V_AA": combo((sh_mouth_open, 0.75), (sh_wide, 0.15)),
    "V_EE": combo((sh_wide, 0.8), (sh_mouth_open, 0.22), (sh_smile, 0.2)),
    "V_OH": combo((sh_o, 0.9), (sh_mouth_open, 0.3)),
    "V_FV": sh_fv, "V_MBP": sh_mbp,
    "V_L": combo((sh_mouth_open, 0.45), (sh_wide, 0.1)),
    "V_REST": combo((sh_mouth_open, 0.05),),
    "Belly_Squash": sh_belly_squash, "Belly_Stretch": sh_belly_stretch,
    "Body_Squash": sh_squash, "Body_Stretch": sh_stretch,
    "Corr_Shoulder_Up_L": sh_corr_shoulder(1), "Corr_Shoulder_Up_R": sh_corr_shoulder(-1),
}

# ------------------------------------------------------------------ snout shapes
SNOUT_C = Vector((0.0, -0.37, 1.228))
SNOUT_BACK_Y = -0.30
def sn_compress(i, p, r):
    q = p.copy()
    if q.y < SNOUT_BACK_Y:
        q.y = SNOUT_BACK_Y + (q.y - SNOUT_BACK_Y) * 0.76
    q.x *= 1.05
    q.z = SNOUT_C.z + (q.z - SNOUT_C.z) * 1.05
    return q
def sn_widen(i, p, r):
    return Vector((p.x * 1.14, p.y, SNOUT_C.z + (p.z - SNOUT_C.z) * 0.95))
def sn_smile(i, p, r):
    return Vector((p.x * 1.02, p.y + 0.004, p.z + 0.008))
def sn_cheek(i, p, r):
    return p + Vector((0, 0, 0.010))
def sn_o(i, p, r):
    return Vector((p.x * 0.97, p.y - 0.008, p.z - 0.004))
def sn_frown(i, p, r):
    return p + Vector((0, 0, -0.005))

SNOUT_SHAPES = {"Snout_Compress": sn_compress, "Snout_Widen": sn_widen, "Smile": sn_smile,
                "Smile_L": combo((sn_smile, 0.5),), "Smile_R": combo((sn_smile, 0.5),),
                "Cheek_Raise": sn_cheek, "Mouth_O": sn_o, "Frown": sn_frown,
                "V_OH": combo((sn_o, 0.9),), "V_EE": combo((sn_smile, 0.3),),
                "Body_Squash": sh_squash, "Body_Stretch": sh_stretch}
PART_SHAPES = {"Body_Squash": sh_squash, "Body_Stretch": sh_stretch}

def build_keys(ob, shapes, region_data=None):
    me = ob.data
    if me.shape_keys:
        ob.shape_key_clear()
    ob.shape_key_add(name="Basis", from_mix=False)
    base = [v.co.copy() for v in me.vertices]
    for name, fn in shapes.items():
        kb = ob.shape_key_add(name=name, from_mix=False)
        for i, co in enumerate(base):
            r = region_data[i] if region_data else None
            kb.data[i].co = fn(i, co, r)
        kb.slider_min, kb.slider_max = 0.0, 1.0
    return me.shape_keys

# ------------------------------------------------------------------ drivers
PROP_OF = {  # shape key -> (bone, expression using v = prop, c = Cartoon_Intensity)
    "Body_Squash": ("CTRL_Body", "Squash", "min(1.0, v * c)"),
    "Body_Stretch": ("CTRL_Body", "Stretch", "min(1.0, v * c)"),
    "Belly_Squash": ("CTRL_Body", "Belly_Squash", "min(1.0, v * c)"),
    "Belly_Stretch": ("CTRL_Body", "Belly_Stretch", "min(1.0, v * c)"),
}

def add_drivers(ob):
    keys = ob.data.shape_keys
    if keys.animation_data:
        for fc in list(keys.animation_data.drivers):
            keys.animation_data.drivers.remove(fc)
    for kb in keys.key_blocks[1:]:
        fc = kb.driver_add("value")
        d = fc.driver
        d.type = 'SCRIPTED'
        v = d.variables.new()
        v.name = "v"
        v.targets[0].id = rig
        if kb.name.startswith("Corr_Shoulder_Up_"):
            s = kb.name[-1]
            v.type = 'ROTATION_DIFF'
            v.targets[0].bone_target = "UpperArm_" + s
            v.targets[1].id = rig
            v.targets[1].bone_target = "Chest"
            # rest difference ~ 140deg (arm hangs down-out); raising the arm reduces it
            d.expression = "max(0.0, min(1.0, (2.35 - v) / 1.1))"
            continue
        if kb.name in PROP_OF:
            bone, prop, expr = PROP_OF[kb.name]
            v.targets[0].data_path = 'pose.bones["%s"]["%s"]' % (bone, prop)
            c = d.variables.new()
            c.name = "c"
            c.targets[0].id = rig
            c.targets[0].data_path = 'pose.bones["CTRL_Body"]["Cartoon_Intensity"]'
            d.expression = expr
        else:
            v.targets[0].data_path = 'pose.bones["CTRL_Face"]["%s"]' % kb.name
            d.expression = "max(0.0, min(1.0, v))"

PARTS = ["Pig_Eye_L", "Pig_Eye_R", "Pig_Lid_Up_L", "Pig_Lid_Up_R", "Pig_Lid_Low_L", "Pig_Lid_Low_R", "Pig_Brow_L",
         "Pig_Brow_R", "Pig_Ear_L", "Pig_Ear_R", "Pig_Hoof_BL", "Pig_Hoof_BR", "Pig_Hoof_FL", "Pig_Hoof_FR", "Pig_Tail"]

if __name__ != "__lib__":
    build_keys(body, BODY_SHAPES, BODY_R)
    add_drivers(body)
    sn = bpy.data.objects["Pig_Snout"]
    build_keys(sn, SNOUT_SHAPES)
    add_drivers(sn)
    for n in PARTS:
        ob = bpy.data.objects[n]
        build_keys(ob, PART_SHAPES)
        add_drivers(ob)
    print("body keys:", len(body.data.shape_keys.key_blocks) - 1, "snout keys:", len(sn.data.shape_keys.key_blocks) - 1)
