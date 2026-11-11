# Pig character - 01: metaball body blockout (single fused volume).
# Character faces -Y, Z up, feet on Z=0. Units: metres.
import bpy
from mathutils import Vector

K = 0.575   # iso-surface sits at ~0.575*radius for stiffness 2 / threshold 0.6

def get_mb():
    for ob in list(bpy.data.objects):
        if ob.name.startswith("MB_Body"):
            bpy.data.objects.remove(ob, do_unlink=True)
    mb = bpy.data.metaballs.new("MB_Body")
    mb.resolution = 0.02
    mb.render_resolution = 0.012
    mb.threshold = 0.6
    ob = bpy.data.objects.new("MB_Body", mb)
    bpy.data.collections["WIP_CONSTRUCTION"].objects.link(ob)
    return ob, mb

def ellipsoid(mb, c, semi, stiff=2.0, neg=False):
    e = mb.elements.new(type='ELLIPSOID')
    e.co = c
    e.radius = 1.0
    e.size_x, e.size_y, e.size_z = (s / K for s in semi)
    e.stiffness = stiff
    e.use_negative = neg
    return e

def ball(mb, c, r, stiff=2.0, neg=False):
    e = mb.elements.new(type='BALL')
    e.co = c
    e.radius = r / K
    e.stiffness = stiff
    e.use_negative = neg
    return e

def limb(mb, pts, radii, step=0.035, stiff=2.0):
    """chain of balls between successive points, radius lerped"""
    for i in range(len(pts) - 1):
        a, b = Vector(pts[i]), Vector(pts[i + 1])
        n = max(2, int((b - a).length / step))
        for k in range(n + (1 if i == len(pts) - 2 else 0)):
            t = k / n
            ball(mb, a.lerp(b, t), radii[i] + (radii[i + 1] - radii[i]) * t, stiff)

ob, mb = get_mb()

# torso / belly / head  (calibrated from the reference silhouette, s = 0.00236 m/px, feet at row 830)
ellipsoid(mb, (0, -0.05, 0.50), (0.47, 0.42, 0.38))      # belly
ellipsoid(mb, (0, 0.03, 0.33), (0.43, 0.37, 0.19))       # seat / lower body
ellipsoid(mb, (0, 0.02, 0.90), (0.35, 0.30, 0.25))       # chest
ellipsoid(mb, (0, 0.02, 1.235), (0.335, 0.28, 0.245))    # head
ellipsoid(mb, (0, -0.15, 1.165), (0.29, 0.16, 0.12))     # muzzle / upper-lip mass under the snout
ellipsoid(mb, (0, -0.11, 1.04), (0.24, 0.15, 0.08), stiff=1.5)  # chin
ellipsoid(mb, (0, -0.15, 1.375), (0.22, 0.17, 0.10), stiff=1.6)  # forehead mass that seats the eyes

for sx in (-1, 1):
    # arms: shoulder -> elbow -> wrist -> hand, angled down/out like the reference
    limb(mb, [(sx * 0.33, 0.0, 0.93), (sx * 0.47, -0.04, 0.80), (sx * 0.56, -0.07, 0.69), (sx * 0.60, -0.08, 0.645)],
         [0.12, 0.10, 0.088, 0.08])
    # digit nubs: thumb (forward), index, outer finger
    ball(mb, (sx * 0.575, -0.145, 0.625), 0.036)
    ball(mb, (sx * 0.625, -0.09, 0.595), 0.036)
    ball(mb, (sx * 0.645, -0.035, 0.615), 0.034)
    # legs
    limb(mb, [(sx * 0.24, 0.02, 0.27), (sx * 0.255, -0.01, 0.17), (sx * 0.26, -0.02, 0.12)], [0.15, 0.125, 0.105])

print("elements", len(mb.elements))
