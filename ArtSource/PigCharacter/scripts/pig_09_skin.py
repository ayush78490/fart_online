# Pig character - 09: skin weights.
#  * Pig_Body: bone-heat weights from the body/limb deform bones, then a procedural Belly jiggle region.
#  * Rigid / semi-rigid parts: exact procedural weights (eyes, lids, brows, snout, ears, hooves, claws, tail).
import bpy, math
from mathutils import Vector
from mathutils.geometry import intersect_point_line

rig = bpy.data.objects["Pig_Rig"]
arm = rig.data

def bone_seg(n):
    b = arm.bones[n]
    return rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local

def seg_dist(p, a, b):
    q, t = intersect_point_line(p, a, b)
    t = max(0.0, min(1.0, t))
    return (p - a.lerp(b, t)).length

def bind(ob):
    for m in list(ob.modifiers):
        if m.type == 'ARMATURE':
            ob.modifiers.remove(m)
    mw = ob.matrix_world.copy()
    ob.parent = rig
    ob.parent_type = 'OBJECT'
    ob.matrix_world = mw
    m = ob.modifiers.new("Armature", 'ARMATURE')
    m.object = rig
    m.use_deform_preserve_volume = False
    # armature before anything else
    while ob.modifiers.find("Armature") > 0:
        bpy.ops.object.modifier_move_up({"object": ob}, modifier="Armature") if False else None
        break
    return m

def rigid(obname, bone):
    ob = bpy.data.objects[obname]
    ob.vertex_groups.clear()
    g = ob.vertex_groups.new(name=bone)
    g.add(list(range(len(ob.data.vertices))), 1.0, 'REPLACE')
    bind(ob)

def chain(obname, bones, power=6.0):
    ob = bpy.data.objects[obname]
    ob.vertex_groups.clear()
    segs = [bone_seg(b) for b in bones]
    groups = [ob.vertex_groups.new(name=b) for b in bones]
    mw = ob.matrix_world
    for v in ob.data.vertices:
        p = mw @ v.co
        ws = [1.0 / max(1e-4, seg_dist(p, a, b)) ** power for a, b in segs]
        tot = sum(ws)
        for g, w in zip(groups, ws):
            if w / tot > 0.01:
                g.add([v.index], w / tot, 'REPLACE')
    bind(ob)

# ------------------------------------------------------------------ body: analytic weights
# Heat weighting on this very round body produced hard borders (crumpling when twisting/leaning), so the body
# uses deterministic weights: smooth z-blend along the spine, polyline-projected limbs with soft armpit/crotch
# transitions, a belly jiggle region, then a light smoothing pass.
from mathutils.geometry import intersect_point_line as ipl

def sstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)

def interp_knots(x, knots):
    """knots: [(position, bone)] sorted; returns {bone: w} with smoothstep blending between neighbours"""
    if x <= knots[0][0]:
        return {knots[0][1]: 1.0}
    if x >= knots[-1][0]:
        return {knots[-1][1]: 1.0}
    for (x0, b0), (x1, b1) in zip(knots[:-1], knots[1:]):
        if x0 <= x <= x1:
            t = sstep(x0, x1, x)
            return {b0: 1 - t, b1: t}

TORSO_KNOTS = [(0.40, "Hips"), (0.66, "Spine"), (0.92, "Chest"), (1.07, "Neck"), (1.20, "Head")]

def head_of(n):
    return rig.matrix_world @ arm.bones[n].head_local
def tail_of(n):
    return rig.matrix_world @ arm.bones[n].tail_local

body = bpy.data.objects["Pig_Body"]
for m in list(body.modifiers):
    if m.type == 'ARMATURE':
        body.modifiers.remove(m)
body.vertex_groups.clear()
lip_attr = body.data.attributes.get("lip_side")
mouth_verts = set()
for poly in body.data.polygons:
    if poly.material_index == 1:
        mouth_verts |= set(poly.vertices)

W = [dict() for _ in body.data.vertices]
mw = body.matrix_world
for v in body.data.vertices:
    p = mw @ v.co
    s = "L" if p.x >= 0 else "R"
    w = W[v.index]
    # ---- arm membership
    S, E, Wr, H = head_of("UpperArm_" + s), head_of("LowerArm_" + s), head_of("Hand_" + s), tail_of("Hand_" + s)
    pts = [S, E, Wr, H]
    best = (1e9, 0.0)
    acc = 0.0
    for a_, b_ in zip(pts[:-1], pts[1:]):
        q, t = ipl(p, a_, b_)
        tc = max(0.0, min(1.0, t))
        d = (p - a_.lerp(b_, tc)).length
        if d < best[0]:
            best = (d, acc + tc * (b_ - a_).length)
        acc += (b_ - a_).length
    d_arm, u = best
    axial = (p - S).dot((E - S).normalized())
    r_here = 0.125 - 0.04 * min(1.0, u / acc)
    m_arm = sstep(-0.07, 0.05, axial) * (1 - sstep(r_here * 1.15, r_here * 1.75, d_arm))
    # ---- leg membership
    T0, K, F = head_of("Thigh_" + s), head_of("Shin_" + s), head_of("Foot_" + s)
    q, t = ipl(p, T0, F)
    lp = T0.lerp(F, max(0.0, min(1.0, t)))
    d_leg = (Vector((p.x, p.y, 0)) - Vector((lp.x, lp.y, 0))).length
    m_leg = sstep(0.36, 0.22, p.z) * (1 - sstep(0.14, 0.22, d_leg))
    m_leg = max(m_leg, 1.0 if p.z < 0.06 else 0.0)
    m_arm *= (1 - m_leg)
    # ---- torso share
    rest = (1 - m_arm) * (1 - m_leg)
    tw = interp_knots(p.z, TORSO_KNOTS)
    # front of the face: whole muzzle/chin/lips are Head
    front = max(0.0, min(1.0, (-p.y - 0.05) / 0.15))
    if v.index in mouth_verts or (lip_attr and lip_attr.data[v.index].value != 0):
        tw = {"Head": 1.0}
    elif front > 0 and p.z > 0.98:
        hw = sstep(1.06 - 0.08 * front, 1.12 - 0.06 * front, p.z) * front
        tw = {k: x * (1 - hw) for k, x in tw.items()}
        tw["Head"] = tw.get("Head", 0.0) + hw
    # shoulder (clavicle) takes part of the upper chest near the shoulder joint
    sh = 0.55 * math.exp(-((p - S).length / 0.16) ** 2) * sstep(0.80, 0.95, p.z)
    for k in tw:
        tw[k] *= (1 - sh)
    tw["Shoulder_" + s] = sh
    for k, x in tw.items():
        w[k] = w.get(k, 0.0) + x * rest
    # ---- arm segments
    if m_arm > 0:
        L1, L2 = (E - S).length, (Wr - E).length
        L3 = (H - Wr).length
        aw = interp_knots(u, [(L1 * 0.45, "UpperArm_" + s), (L1 + L2 * 0.5, "LowerArm_" + s), (L1 + L2 + L3 * 0.55, "Hand_" + s)])
        # digits near their nubs
        dig = {}
        for dn in ("Thumb", "Index", "Finger"):
            c = (head_of(dn + "_" + s) + tail_of(dn + "_" + s)) / 2
            dd = (p - c).length
            if dd < 0.06:
                dig[dn + "_" + s] = (1 - sstep(0.025, 0.06, dd))
        dsum = sum(dig.values())
        if dsum > 0:
            hand_share = aw.get("Hand_" + s, 0.0)
            take = min(1.0, dsum) * hand_share
            aw["Hand_" + s] = hand_share - take
            for k, x in dig.items():
                aw[k] = take * x / dsum
        for k, x in aw.items():
            w[k] = w.get(k, 0.0) + x * m_arm
    # ---- leg segments
    if m_leg > 0:
        lw = interp_knots(p.z, [(0.045, "Foot_" + s), (0.125, "Shin_" + s), (0.25, "Thigh_" + s), (0.40, "Hips")])
        for k, x in lw.items():
            w[k] = w.get(k, 0.0) + x * m_leg
    # ---- belly jiggle region (front of the belly), taken proportionally from what is there
    BC, BR = Vector((0.0, -0.30, 0.48)), Vector((0.40, 0.30, 0.33))
    dd = Vector(((p.x - BC.x) / BR.x, (p.y - BC.y) / BR.y, (p.z - BC.z) / BR.z)).length
    if dd < 1.0:
        bw = 0.6 * (1 - dd * dd) ** 2 * (1 - m_arm)
        for k in list(w):
            w[k] *= (1 - bw)
        w["Belly"] = bw

groups = {}
for i, w in enumerate(W):
    tot = sum(w.values())
    for k, x in w.items():
        if x / tot > 0.002:
            g = groups.get(k) or body.vertex_groups.new(name=k)
            groups[k] = g
            g.add([i], x / tot, 'REPLACE')

for o in bpy.context.selected_objects:
    o.select_set(False)
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
bpy.ops.object.vertex_group_smooth(group_select_mode='ALL', factor=0.5, repeat=4, expand=0.0)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
bind(body)
unweighted = [v.index for v in body.data.vertices if not any(g.weight > 0 for g in v.groups)]
print("body groups:", sorted(g.name for g in body.vertex_groups), "unweighted:", len(unweighted))

# ------------------------------------------------------------------ rigid parts
for s in ("L", "R"):
    rigid("Pig_Eye_" + s, "Eye_" + s)
    rigid("Pig_Lid_Up_" + s, "Lid_Up_" + s)
    rigid("Pig_Lid_Low_" + s, "Lid_Low_" + s)
    rigid("Pig_Brow_" + s, "Brow_" + s)
    rigid("Pig_Hoof_B" + s, "Foot_" + s)
    chain("Pig_Ear_" + s, ["Ear_" + s, "Ear_Tip_" + s], power=4.0)
    chain("Pig_Hoof_F" + s, ["Thumb_" + s, "Index_" + s, "Finger_" + s], power=8.0)
rigid("Pig_Snout", "Head")
chain("Pig_Tail", ["Tail_1", "Tail_2"], power=4.0)

# ear root stays glued to the head
for s in ("L", "R"):
    ob = bpy.data.objects["Pig_Ear_" + s]
    hg = ob.vertex_groups.new(name="Head")
    a, b = bone_seg("Ear_" + s)
    for v in ob.data.vertices:
        p = ob.matrix_world @ v.co
        q, t = intersect_point_line(p, a, b)
        if t < 0.25:
            w = 1.0 - max(0.0, t) / 0.25
            hg.add([v.index], w * 0.85, 'REPLACE')
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)

print("skinned:", [o.name for o in bpy.data.objects if o.parent == rig])
