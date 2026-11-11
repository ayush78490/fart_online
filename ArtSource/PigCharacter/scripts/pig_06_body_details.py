# Pig character - 06: mouth slit + mouth bag, belly button, PigMask vertex colours.
import bpy, bmesh, math, heapq
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree

body = bpy.data.objects["Pig_Body"]
sculpt = bpy.data.objects["Pig_Body_Sculpt"]
dg = bpy.context.evaluated_depsgraph_get()
BVH = BVHTree.FromObject(sculpt, dg)

# ---------------------------------------------------------------- smile curve (front view x,z)
MOUTH_HALF = 0.300
def curve_z(x):
    return 1.083 + 0.088 * (abs(x) / MOUTH_HALF) ** 2.2

CURVE = [(MOUTH_HALF * (i / 60 * 2 - 1), 0.0) for i in range(61)]
CURVE = [(x, curve_z(x)) for x, _ in CURVE]

def curve_closest(x, z):
    best = (1e9, None)
    for i in range(len(CURVE) - 1):
        ax, az = CURVE[i]
        bx, bz = CURVE[i + 1]
        dx, dz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((x - ax) * dx + (z - az) * dz) / (dx * dx + dz * dz)))
        px, pz = ax + dx * t, az + dz * t
        d = math.hypot(x - px, z - pz)
        if d < best[0]:
            best = (d, (px, pz))
    return best

def front_surface(x, z):
    hit = BVH.ray_cast(Vector((x, -3, z)), Vector((0, 1, 0)))
    return hit[0]

def project(co):
    loc, nrm, idx, d = BVH.find_nearest(co)
    return loc if loc else co

bm = bmesh.new()
bm.from_mesh(body.data)
LS = bm.verts.layers.int.get("lip_side") or bm.verts.layers.int.new("lip_side")   # create before taking vert refs
bm.verts.ensure_lookup_table()
bm.edges.ensure_lookup_table()

def is_front(v):
    return v.co.y < -0.12 and v.normal.y < -0.15 and 0.95 < v.co.z < 1.30 and abs(v.co.x) < MOUTH_HALF + 0.08

# start/end verts nearest to curve ends
def nearest_front(x, z):
    cands = [v for v in bm.verts if is_front(v)]
    return min(cands, key=lambda v: math.hypot(v.co.x - x, v.co.z - z))

vs = nearest_front(-MOUTH_HALF, curve_z(MOUTH_HALF))
ve = nearest_front(MOUTH_HALF, curve_z(MOUTH_HALF))

# Dijkstra along edges, penalising distance from the curve
dist = {vs.index: 0.0}
prev = {}
pq = [(0.0, vs.index)]
while pq:
    d, i = heapq.heappop(pq)
    if i == ve.index:
        break
    if d > dist.get(i, 1e9):
        continue
    v = bm.verts[i]
    for e in v.link_edges:
        w = e.other_vert(v)
        if not is_front(w):
            continue
        mid = (v.co + w.co) / 2
        dc, _ = curve_closest(mid.x, mid.z)
        c = e.calc_length() + 6.0 * dc + 0.002
        nd = d + c
        if nd < dist.get(w.index, 1e9):
            dist[w.index] = nd
            prev[w.index] = i
            heapq.heappush(pq, (nd, w.index))
path = [ve.index]
while path[-1] != vs.index:
    path.append(prev[path[-1]])
path.reverse()
pverts = [bm.verts[i] for i in path]
print("mouth path verts:", len(pverts))

# snap path onto the curve, re-project onto the sculpt surface
for v in pverts:
    _, (px, pz) = curve_closest(v.co.x, v.co.z)
    hit = front_surface(px, pz)
    if hit:
        v.co = hit
# relax the 2-ring around the path so faces even out, keeping them on the surface
pset = set(pverts)
ring = set()
for v in pverts:
    for e in v.link_edges:
        ring.add(e.other_vert(v))
ring2 = set(ring)
for v in ring:
    for e in v.link_edges:
        ring2.add(e.other_vert(v))
ring2 -= pset
for it in range(6):
    newpos = {}
    for v in ring2:
        nb = [e.other_vert(v).co for e in v.link_edges]
        avg = sum(nb, Vector()) / len(nb)
        newpos[v] = v.co.lerp(avg, 0.5)
    for v, p in newpos.items():
        v.co = project(p)

# split the path edges -> lips
pedges = []
for a, b in zip(pverts[:-1], pverts[1:]):
    e = bm.edges.get((a, b))
    pedges.append(e)
bmesh.ops.split_edges(bm, edges=pedges)
bm.verts.ensure_lookup_table()
bnd_edges = [e for e in bm.edges if e.is_boundary]
bverts = set(v for e in bnd_edges for v in e.verts)
print("lip boundary verts:", len(bverts))

def side_of(v):
    """+1 upper lip, -1 lower lip, 0 corner"""
    if len(v.link_faces) == 0:
        return 0
    cz = sum((f.calc_center_median().z - curve_z(f.calc_center_median().x)) for f in v.link_faces) / len(v.link_faces)
    ups = [f for f in v.link_faces if f.calc_center_median().z > curve_z(f.calc_center_median().x)]
    dns = [f for f in v.link_faces if f.calc_center_median().z <= curve_z(f.calc_center_median().x)]
    if ups and dns:
        return 0
    return 1 if cz > 0 else -1

side = {v: side_of(v) for v in bverts}
for v, s_ in side.items():
    v[LS] = s_
corners = [v for v in bverts if side[v] == 0]
print("corners:", len(corners), "upper:", sum(1 for v in bverts if side[v] > 0), "lower:", sum(1 for v in bverts if side[v] < 0))

# lip crease (basis): open a thin smile line, tuck both lips inwards
for v in bverts:
    s = side[v]
    v.co.z += 0.0022 * s
    v.co.y += 0.006 if s >= 0 else 0.009
# soften the first ring around the lips
for v in ring2 | ring:
    if v in bverts:
        continue
    dc, _ = curve_closest(v.co.x, v.co.z)
    if dc < 0.03:
        v.co.y += 0.004 * (1 - dc / 0.03)

# mouth bag: three extruded rings into the head, then capped. Each ring is placed relative to the
# lip vertex it grew from: x scaled towards the centre, pushed back (+y), opened vertically.
MAT_MOUTH = 1
bag_faces = set()
cur_edges = bnd_edges
cur_side = dict(side)
lip_pos = {v: v.co.copy() for v in bverts}
for depth, open_z, xs, drop in ((0.014, 0.003, 1.0, 0.0), (0.050, 0.030, 0.90, 0.008), (0.100, 0.020, 0.72, 0.014)):
    old_verts = set(v for e in cur_edges for v in e.verts)
    res = bmesh.ops.extrude_edge_only(bm, edges=cur_edges)
    newv = [g for g in res["geom"] if isinstance(g, bmesh.types.BMVert)]
    nside, npos = {}, {}
    for nv in newv:
        src = next(e.other_vert(nv) for e in nv.link_edges if e.other_vert(nv) in old_verts)
        s_ = cur_side[src]
        L = lip_pos[src]
        nside[nv], npos[nv] = s_, L
        nv[LS] = s_
        nv.co = Vector((L.x * xs, L.y + depth, L.z + open_z * s_ - drop))
    bag_faces |= set(f for v in newv for f in v.link_faces)
    cur_edges = [g for g in res["geom"] if isinstance(g, bmesh.types.BMEdge) and all(v in nside for v in g.verts)]
    cur_side, lip_pos = nside, npos
cap = bmesh.ops.holes_fill(bm, edges=cur_edges, sides=0)
tri = bmesh.ops.triangulate(bm, faces=cap["faces"])
bag_faces |= set(tri["faces"])
for f in bag_faces:
    f.material_index = MAT_MOUTH

# ---------------------------------------------------------------- belly button (inset, all quads)
BB = Vector((0.0, 0.0, 0.335))
hit = front_surface(0.0, BB.z)
centre = min((v for v in bm.verts if v.co.y < 0), key=lambda v: (v.co - hit).length)
faces = list(centre.link_faces)
r1 = bmesh.ops.inset_region(bm, faces=faces, thickness=0.006, depth=0.0, use_even_offset=True)
r2 = bmesh.ops.inset_region(bm, faces=faces, thickness=0.007, depth=0.0, use_even_offset=True)
inner = set(v for f in faces for v in f.verts)
c = sum((v.co for v in inner), Vector()) / len(inner)
for v in inner:
    d = v.co - c
    d.z *= 0.45                              # slit-like, wider than tall
    v.co = c + d * 0.75 + Vector((0, 0.013, 0))
# soft rim
rim = set(v for f in r2["faces"] for v in f.verts) - inner
for v in rim:
    v.co.y += 0.002

bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(body.data)
bm.free()
body.data.update()
for p in body.data.polygons:
    p.use_smooth = True
print("body faces after details:", len(body.data.polygons))
