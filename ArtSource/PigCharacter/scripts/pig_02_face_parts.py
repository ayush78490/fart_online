# Pig character - 02: snout (with real nostril cavities), eyes + eyelid shells, brows, ears,
# hooves, claws and tail. Everything is symmetric at rest; the reference's asymmetry is a pose.
import bpy, bmesh, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

SCULPT = bpy.data.objects["Pig_Body_Sculpt"]
dg = bpy.context.evaluated_depsgraph_get()
BVH = BVHTree.FromObject(SCULPT, dg)

def surf_y(x, z):
    """y of the body surface seen from the front at (x, z)"""
    hit = BVH.ray_cast(Vector((x, -3.0, z)), Vector((0, 1, 0)))
    return hit[0].y if hit[0] else None

def new_obj(name, bm, coll, smooth=True):
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.get(name)
    if me:
        bpy.data.meshes.remove(me)
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    bpy.data.collections[coll].objects.link(ob)
    return ob

def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)

# ---------------------------------------------------------------- snout
SN_C = Vector((0.0, 0.0, 1.228))     # oval centre (x, -, z)
SN_A, SN_B = 0.255, 0.128            # semi axes (x, z)
SN_YF = -0.445                       # front plane
SN_BULGE = 0.028
SN_RR = 0.042                        # rim fillet radius
NOS = [(-0.112, 1.240), (0.112, 1.240)]
NOS_A, NOS_B, NOS_D = 0.056, 0.066, 0.08
U = 72

def build_snout():
    bm = bmesh.new()
    rings = []
    for i in range(U):
        u = 2 * math.pi * i / U
        cu, su = math.cos(u), math.sin(u)
        R = math.hypot(SN_A * cu, SN_B * su)
        rrn = SN_RR / R
        ys = surf_y(SN_A * cu * 0.93, SN_C.z + SN_B * su * 0.93) or -0.30
        yb = ys + 0.045                      # buried inside the face
        prof = []
        # side wall (slight taper at the back gives a soft crease where it plugs into the face)
        for t in (0.0, 0.25, 0.5, 0.75, 1.0):
            rn = 0.90 + 0.10 * smoothstep(0.0, 0.6, t)
            prof.append((rn, yb + (SN_YF + SN_RR - yb) * t))
        # rounded rim
        for k in range(1, 8):
            a = (math.pi / 2) * k / 7
            prof.append((1.0 - rrn + rrn * math.cos(a), SN_YF + SN_RR - SN_RR * math.sin(a)))
        # front face
        r0 = 1.0 - rrn
        NF = 17
        for k in range(1, NF):
            rn = r0 * (1 - k / NF)
            q = rn / r0
            prof.append((rn, SN_YF - SN_BULGE * (1 - q * q)))
        ring = []
        for rn, y in prof:
            ring.append(bm.verts.new((SN_A * cu * rn, y, SN_C.z + SN_B * su * rn)))
        rings.append(ring)
    centre = bm.verts.new((0, SN_YF - SN_BULGE, SN_C.z))
    n = len(rings[0])
    for i in range(U):
        a, b = rings[i], rings[(i + 1) % U]
        for k in range(n - 1):
            bm.faces.new((a[k], b[k], b[k + 1], a[k + 1]))
        bm.faces.new((a[n - 1], b[n - 1], centre))
    # back cap (hidden inside head) to keep the mesh closed
    back = [r[0] for r in rings]
    bm.faces.new(list(reversed(back)))
    # nostril cavities: displace front-face verts into the snout with a rounded rim
    nw = bm.verts.layers.float.new("nostril_w")
    for v in bm.verts:
        if v.co.y > SN_YF + 0.01:
            continue
        for nx, nz in NOS:
            rho = math.hypot((v.co.x - nx) / NOS_A, (v.co.z - nz) / NOS_B)
            if rho < 1.35:
                depth = NOS_D * (1 - smoothstep(0.50, 1.05, rho))
                lip = 0.004 * math.exp(-((rho - 1.12) / 0.12) ** 2)     # soft raised rim
                v.co.y += depth - lip
                v[nw] = max(v[nw], smoothstep(0.004, 0.035, depth))
                # slightly widen the cavity towards the bottom so it reads as a hole, not a dent
                if depth > 0.02:
                    s = 1.0 + 0.25 * (depth / NOS_D)
                    v.co.x = nx + (v.co.x - nx) * s
                    v.co.z = nz + (v.co.z - nz) * s
    # relax the cavity walls (displacement leaves uneven, streaky quads inside the nostrils)
    cav = [v for v in bm.verts if v[nw] > 0.04]
    for it in range(10):
        newco = {}
        for v in cav:
            nb = [e.other_vert(v).co for e in v.link_edges]
            avg = sum(nb, Vector()) / len(nb)
            newco[v] = v.co.lerp(avg, 0.45 * min(1.0, v[nw] * 1.5))
        for v, c in newco.items():
            v.co = c
    bm.normal_update()
    ob = new_obj("Pig_Snout", bm, "CHARACTER_FACE")
    return ob

# ---------------------------------------------------------------- eyes, lids
EYE_R = 0.075
EYE_X, EYE_Z = 0.086, 1.374

def eye_centre(side):
    x = side * EYE_X
    ys = surf_y(x, EYE_Z) or -0.25
    return Vector((x, ys + 0.030, EYE_Z))

def build_eye(side):
    c = eye_centre(side)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=18, radius=EYE_R)
    # pole to the front (-Y)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    bmesh.ops.translate(bm, verts=bm.verts, vec=c)
    name = "Pig_Eye_" + ("L" if side > 0 else "R")
    ob = new_obj(name, bm, "CHARACTER_EYES")
    ob.data.materials.append(None); ob.data.materials.append(None)
    fwd = Vector((0, -1, 0))
    for p in ob.data.polygons:
        d = (p.center - c).normalized()
        ang = math.acos(max(-1, min(1, d.dot(fwd))))
        p.material_index = 1 if ang < math.radians(24) else 0      # 1 = pupil
    return ob, c

def build_lid(side, c, upper=True):
    """sphere-shell cap around the eye. Open pose hides it behind/above the eyeball inside the head;
    the lid bone rotates it about the eye's X axis to close."""
    r_in, r_out = EYE_R * 1.045, EYE_R * 1.11
    bm = bmesh.new()
    axis = Vector((0, 1, 0.18 if upper else -0.75)).normalized()   # cap centre direction (rest = open)
    half = math.radians(100 if upper else 62)
    NU, NV = 20, 7
    up = Vector((1, 0, 0))
    b1 = axis.cross(up).normalized()
    b2 = axis.cross(b1).normalized()
    def ring(r, k):
        th = half * k / NV
        out = []
        for i in range(NU):
            ph = 2 * math.pi * i / NU
            d = axis * math.cos(th) + (b1 * math.cos(ph) + b2 * math.sin(ph)) * math.sin(th)
            out.append(d * r + c)
        return out
    outer = [[bm.verts.new(p) for p in ring(r_out, k)] for k in range(1, NV + 1)]
    inner = [[bm.verts.new(p) for p in ring(r_in, k)] for k in range(1, NV + 1)]
    co = bm.verts.new(axis * r_out + c)
    ci = bm.verts.new(axis * r_in + c)
    for i in range(NU):
        j = (i + 1) % NU
        bm.faces.new((co, outer[0][i], outer[0][j]))
        bm.faces.new((ci, inner[0][j], inner[0][i]))
        for k in range(NV - 1):
            bm.faces.new((outer[k][i], outer[k + 1][i], outer[k + 1][j], outer[k][j]))
            bm.faces.new((inner[k][j], inner[k + 1][j], inner[k + 1][i], inner[k][i]))
        # rolled lid edge
        bm.faces.new((outer[-1][i], inner[-1][i], inner[-1][j], outer[-1][j]))
    name = "Pig_Lid_%s_%s" % ("Up" if upper else "Low", "L" if side > 0 else "R")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj(name, bm, "CHARACTER_EYES")

# ---------------------------------------------------------------- brows
def build_brow(side, c):
    bm = bmesh.new()
    N, M = 18, 10
    span, arch = 0.034, 0.012
    centre = Vector((side * 0.095, c.y - 0.03, 1.538))
    rings = []
    for i in range(N + 1):
        t = i / N * 2 - 1
        p = centre + Vector((t * span, 0, arch * (1 - t * t)))
        tang = Vector((span, 0, -2 * arch * t)).normalized()
        rad = 0.0105 * (1 - 0.65 * t * t)
        n1 = Vector((0, 1, 0)).cross(tang).normalized()
        n2 = Vector((0, -1, 0))
        rings.append([bm.verts.new(p + (n1 * math.cos(2 * math.pi * k / M) + n2 * math.sin(2 * math.pi * k / M) * 0.6) * rad)
                      for k in range(M)])
    for i in range(N):
        for k in range(M):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % M], rings[i + 1][(k + 1) % M], rings[i + 1][k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj("Pig_Brow_" + ("L" if side > 0 else "R"), bm, "CHARACTER_FACE")

# ---------------------------------------------------------------- ears
EAR_BASE = Vector((0.285, 0.03, 1.37))

def build_ear(side):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=1.0)
    L, W, T = 0.285, 0.235, 0.075
    for v in bm.verts:
        x, y, z = v.co
        h = (z + 1) / 2                                    # 0 base .. 1 tip
        width = W / 2 * (0.62 + 0.75 * math.sin(math.pi * min(1, h * 0.85 + 0.1))) / 1.37
        width *= (1 - 0.55 * h ** 3)                       # rounded, slightly pointed tip
        thick = T / 2 * (1 - 0.5 * h)
        nx, ny = x * width, y * thick
        # cup the front (-Y) face to make the inner ear
        if y < 0:
            cup = 0.020 * (1 - x * x) * math.sin(math.pi * min(1.0, h * 1.1))
            ny = y * thick * 0.35 + cup
        v.co = Vector((nx, ny, h * L))
    # floppy forward bend of the upper half
    for v in bm.verts:
        h = v.co.z / L
        ang = math.radians(95) * smoothstep(0.30, 1.0, h)
        piv = Vector((0, 0, 0.42 * L))
        if h > 0.30:
            v.co = Matrix.Rotation(ang, 3, 'X') @ (v.co - piv) + piv   # tip folds forward (-Y)
    # orient: yaw inner face slightly outward, roll outward from vertical, lean forward
    rot = Matrix.Rotation(side * math.radians(22), 3, 'Z') @ Matrix.Rotation(side * math.radians(52), 3, 'Y') \
        @ Matrix.Rotation(math.radians(10), 3, 'X')
    base = Vector((side * EAR_BASE.x, EAR_BASE.y, EAR_BASE.z))
    for v in bm.verts:
        v.co = rot @ v.co + base
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # recalc picks the wrong orientation on this bent, cupped shell: enforce outward normals by signed volume
    vol = sum(f.verts[0].co.dot(f.verts[i].co.cross(f.verts[i + 1].co))
              for f in bm.faces for i in range(1, len(f.verts) - 1))
    if vol < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return new_obj("Pig_Ear_" + ("L" if side > 0 else "R"), bm, "CHARACTER_EARS")

# ---------------------------------------------------------------- hooves, claws
def build_hoof(side, front=True):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=14, radius=1.0)
    a, b, h = 0.118, 0.128, 0.062
    for v in bm.verts:
        x, y, z = v.co
        r = math.hypot(x, y)
        # superellipse in z -> flatter top/bottom, straighter walls
        zz = math.copysign(abs(z) ** 0.55, z)
        k = (1 - min(1, abs(zz)) ** 4) ** 0.25 / max(1e-4, r) if r > 1e-4 else 0
        flare = 1.0 + 0.06 * (1 - (zz + 1) / 2)            # wider at the bottom
        nx, ny = x * k * a * flare, y * k * b * flare
        # cleft at the front
        if ny < -0.06:
            ny += 0.022 * math.exp(-(nx / 0.012) ** 2) * min(1, (-ny - 0.06) / 0.05)
        v.co = Vector((nx, ny, h * (zz + 1)))
    c = Vector((side * 0.262, -0.035, 0.0))
    bmesh.ops.translate(bm, verts=bm.verts, vec=c)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj("Pig_Hoof_%s%s" % ("F" if front else "B", "L" if side > 0 else "R"), bm, "CHARACTER_HOOVES")

DIGITS = [("Thumb", (0.575, -0.145, 0.625)), ("Index", (0.625, -0.09, 0.595)), ("Finger", (0.645, -0.035, 0.615))]
WRIST = (0.56, -0.07, 0.69)

def build_claws(side):
    bm = bmesh.new()
    for name, p in DIGITS:
        nub = Vector((side * p[0], p[1], p[2]))
        w = Vector((side * WRIST[0], WRIST[1], WRIST[2]))
        d = (nub - w).normalized()
        tmp = bmesh.new()
        bmesh.ops.create_uvsphere(tmp, u_segments=12, v_segments=8, radius=1.0)
        for v in tmp.verts:
            t = (v.co.z + 1) / 2
            s = 0.024 * (1 - 0.6 * t ** 1.4)
            v.co = Vector((v.co.x * s, v.co.y * s, (t - 0.3) * 0.06))
        q = Vector((0, 0, 1)).rotation_difference(d)
        for v in tmp.verts:
            v.co = q @ v.co + nub + d * 0.022
        me = bpy.data.meshes.new("_tmp")
        tmp.to_mesh(me)
        tmp.free()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj("Pig_Hoof_F" + ("L" if side > 0 else "R"), bm, "CHARACTER_HOOVES")   # hand hoof-tips (claws)

# ---------------------------------------------------------------- tail
def build_tail():
    bm = bmesh.new()
    N, M = 60, 10
    base = Vector((0, 0.355, 0.40))
    # corkscrew whose axis leans back and up, so the curl reads from the back and the side
    ax = Vector((0, 0.55, 0.85)).normalized()
    e1 = Vector((1, 0, 0))
    e2 = ax.cross(e1).normalized()
    rings = []
    for i in range(N + 1):
        t = i / N
        ang = t * 2 * math.pi * 1.75
        rad = 0.042 * (1 - 0.3 * t) * min(1.0, t * 6)          # grows out of the body, then coils
        p = base + ax * (0.035 + t * 0.11) + (e1 * math.sin(ang) + e2 * (1 - math.cos(ang))) * rad
        rings.append(p)
    pts = rings
    vr = []
    for i, p in enumerate(pts):
        tang = (pts[min(i + 1, N)] - pts[max(i - 1, 0)]).normalized()
        n1 = tang.cross(Vector((0, 0, 1)) if abs(tang.z) < 0.9 else Vector((1, 0, 0))).normalized()
        n2 = tang.cross(n1).normalized()
        r = 0.020 * (1 - 0.6 * (i / N)) + 0.004
        vr.append([bm.verts.new(p + (n1 * math.cos(2 * math.pi * k / M) + n2 * math.sin(2 * math.pi * k / M)) * r) for k in range(M)])
    for i in range(N):
        for k in range(M):
            bm.faces.new((vr[i][k], vr[i][(k + 1) % M], vr[i + 1][(k + 1) % M], vr[i + 1][k]))
    bm.faces.new(list(reversed(vr[0])))          # cap the (buried) base so the tail is closed
    tip = bm.verts.new(pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.006)
    for k in range(M):
        bm.faces.new((vr[-1][k], vr[-1][(k + 1) % M], tip))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_obj("Pig_Tail", bm, "CHARACTER_DETAILS")

# ---------------------------------------------------------------- run
built = [build_snout()]
for side in (1, -1):
    eye, c = build_eye(side)
    built += [eye, build_lid(side, c, True), build_lid(side, c, False), build_brow(side, c), build_ear(side),
              build_hoof(side, front=False), build_claws(side)]
built.append(build_tail())
for ob in built:
    print(ob.name, len(ob.data.polygons), [round(v, 3) for v in ob.dimensions])
