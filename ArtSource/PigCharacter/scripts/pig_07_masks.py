# Pig character - 07: PigMask vertex colours for every skin mesh.
#   R = darker skin patches   G = baked ambient occlusion / cavity (1 = open)   B = interior mask (nostrils, smile crease)
#   A = low-frequency colour variation (0.1..0.9, 0.5 = neutral); A == 0 marks geometry buried inside the body
# The skin shader (Blender + Unity) reads these; none of them carries colour, so any Skin_Color works.
import bpy, bmesh, math, random
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree

SKIN_OBJS = ["Pig_Body", "Pig_Snout", "Pig_Ear_L", "Pig_Ear_R", "Pig_Lid_Up_L", "Pig_Lid_Up_R",
             "Pig_Lid_Low_L", "Pig_Lid_Low_R", "Pig_Tail"]
# parked eyelids are excluded: they sit inside the head at rest and would black out the forehead
OCCLUDERS = ["Pig_Body", "Pig_Snout", "Pig_Ear_L", "Pig_Ear_R", "Pig_Tail", "Pig_Eye_L", "Pig_Eye_R",
             "Pig_Hoof_BL", "Pig_Hoof_BR", "Pig_Hoof_FL", "Pig_Hoof_FR"]

# patch blobs (world centre, radius, strength) - first four measured from the reference, rest are design
PATCHES = [
    (Vector((-0.50, -0.05, 0.80)), 0.085, 1.0),     # right upper arm (viewer's left)
    (Vector((0.56, -0.07, 0.82)), 0.075, 0.9),      # left upper arm
    (Vector((-0.37, -0.20, 0.10)), 0.075, 0.9),     # right hip / above hoof
    (Vector((-0.30, -0.25, 0.17)), 0.035, 0.7),     # small satellite spot
    (Vector((0.20, 0.38, 0.62)), 0.12, 0.8),        # back
    (Vector((-0.16, 0.30, 1.02)), 0.08, 0.7),       # shoulder blade
    (Vector((0.30, 0.18, 0.26)), 0.07, 0.8),        # left buttock
]

def collect_tris():
    """occluders in REST pose (original mesh data), never the evaluated/posed meshes: the samples are rest
    positions, so a posed head (e.g. an active RefPose action) would put samples inside the occluders"""
    verts, polys = [], []
    for n in OCCLUDERS:
        ob = bpy.data.objects.get(n)
        if not ob:
            continue
        me = ob.data
        off = len(verts)
        mw = ob.matrix_world
        verts += [mw @ v.co for v in me.vertices]
        polys += [[off + i for i in p.vertices] for p in me.polygons]
    return BVHTree.FromPolygons(verts, polys)

def hemisphere_dirs(n=20, seed=7):
    rnd = random.Random(seed)
    out = []
    for i in range(n):
        u, v = rnd.random(), rnd.random()
        r = math.sqrt(u)
        th = 2 * math.pi * v
        out.append(Vector((r * math.cos(th), r * math.sin(th), math.sqrt(max(0, 1 - u)))))   # cosine weighted
    return out

def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)

def patch_value(p):
    val = 0.0
    for c, r, s in PATCHES:
        d = (p - c).length
        rr = r * (1.0 + 0.35 * noise.noise(p * 14.0) + 0.15 * noise.noise(p * 40.0))
        val = max(val, s * (1.0 - smoothstep(rr * 0.6, rr, d)))
    return val

def bake(obname, bvh, dirs, max_dist=0.16):
    ob = bpy.data.objects[obname]
    me = ob.data
    mw = ob.matrix_world
    nmat = mw.to_3x3().inverted().transposed()
    attr = me.color_attributes.get("PigMask") or me.color_attributes.new("PigMask", 'FLOAT_COLOR', 'POINT')
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = me.color_attributes.find("PigMask")
    nw = me.attributes.get("nostril_w")
    lip = set()
    if obname == "Pig_Body":
        bm = bmesh.new(); bm.from_mesh(me)
        lip = set(v.index for v in bm.verts if v.is_boundary or any(e.is_boundary for e in v.link_edges))
        # crease ring: neighbours of the lip line
        ring = set()
        for v in bm.verts:
            if v.index in lip:
                ring |= set(e.other_vert(v).index for e in v.link_edges)
        bm.free()
    mouth_faces_verts = set()
    if obname == "Pig_Body":
        for p in me.polygons:
            if p.material_index == 1:
                mouth_faces_verts |= set(p.vertices)
    for v in me.vertices:
        p = mw @ v.co
        n = (nmat @ v.normal).normalized()
        t = n.orthogonal().normalized()
        b = n.cross(t)
        occ = 0.0
        origin = p + n * 0.003
        for d in dirs:
            w = (t * d.x + b * d.y + n * d.z).normalized()
            o, travelled = origin, 0.0
            while travelled < max_dist:
                hit = bvh.ray_cast(o, w, max_dist - travelled)
                if hit[0] is None:
                    break
                travelled += hit[3]
                if hit[1].dot(w) > 0:            # back face: the ray started inside that part, pass through
                    o = hit[0] + w * 1e-4
                    continue
                occ += 1.0 - (travelled / max_dist) ** 0.5
                break
        ao = 1.0 - min(1.0, occ / len(dirs) * 1.6)
        r = patch_value(p) if obname in ("Pig_Body", "Pig_Ear_L", "Pig_Ear_R", "Pig_Tail") else 0.0
        bl = 0.0
        if nw is not None:
            bl = nw.data[v.index].value
        if v.index in lip:
            bl = 0.55
        if v.index in mouth_faces_verts and v.index not in lip:
            bl = 1.0
        var = 0.5 + 0.4 * max(-1.0, min(1.0, noise.noise(p * 3.5) * 1.4))      # 0.1..0.9, 0.5 neutral
        # A == 0 flags geometry buried inside the body (snout back wall, ear/tail roots, parked eyelids):
        # the Unity skin shader skips it in the shadow pass to avoid self-shadowing blotches on the skin.
        if obname != "Pig_Body" and inside(BODY_BVH, p):
            var = 0.0
        attr.data[v.index].color = (r, ao, bl, var)
    return attr

def body_bvh():
    dg = bpy.context.evaluated_depsgraph_get()
    return BVHTree.FromObject(bpy.data.objects["Pig_Body"], dg, deform=False)   # rest geometry, not the posed mesh

def inside(bvh_body, p):
    """point-in-closed-mesh by ray parity, majority over 3 directions"""
    votes = 0
    for d in (Vector((1, 0.013, 0.007)), Vector((-0.011, 1, 0.017)), Vector((0.009, -0.015, 1))):
        d = d.normalized()
        o, n = p.copy(), 0
        while True:
            hit = bvh_body.ray_cast(o, d)
            if hit[0] is None:
                break
            n += 1
            o = hit[0] + d * 1e-4
        votes += n % 2
    return votes >= 2

def smooth_ao(obname, iterations=3):
    """neighbour-average the AO channel to remove per-vertex sampling noise"""
    ob = bpy.data.objects[obname]
    me = ob.data
    attr = me.color_attributes["PigMask"]
    nb = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        nb[a].append(b); nb[b].append(a)
    g = [d.color[1] for d in attr.data]
    for _ in range(iterations):
        g = [0.5 * g[i] + 0.5 * (sum(g[j] for j in nb[i]) / len(nb[i]) if nb[i] else g[i]) for i in range(len(g))]
    for i, d in enumerate(attr.data):
        c = list(d.color)
        c[1] = g[i]
        d.color = c

bvh = collect_tris()
BODY_BVH = body_bvh()
dirs = hemisphere_dirs(64)
for n in SKIN_OBJS:
    if bpy.data.objects.get(n):
        bake(n, bvh, dirs)
        smooth_ao(n)
        print("baked PigMask on", n)
