# Pig character - 05: neutral studio preview lighting (LIGHTING_PREVIEW collection, never exported).
import bpy, math
from mathutils import Vector

coll = bpy.data.collections["LIGHTING_PREVIEW"]
for ob in list(coll.objects):
    bpy.data.objects.remove(ob, do_unlink=True)

def area(name, loc, energy, size, color=(1, 1, 1), target=(0, 0, 0.85)):
    ld = bpy.data.lights.get(name) or bpy.data.lights.new(name, 'AREA')
    ld.energy, ld.size, ld.color = energy, size, color
    ob = bpy.data.objects.new(name, ld)
    ob.location = loc
    d = Vector(target) - Vector(loc)
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    coll.objects.link(ob)
    return ob

area("LGT_Key", (-2.2, -3.0, 3.0), 260, 2.5, (1.0, 0.97, 0.94))
area("LGT_Fill", (2.8, -2.4, 1.4), 90, 3.5, (0.94, 0.97, 1.0))
area("LGT_Rim", (1.2, 3.0, 2.6), 260, 1.5)
area("LGT_Top", (0.0, -0.5, 4.0), 60, 3.0)

# soft ground for contact shadow
me = bpy.data.meshes.get("PRV_Ground") or bpy.data.meshes.new("PRV_Ground")
if not me.vertices:
    me.from_pydata([(-6, -6, 0), (6, -6, 0), (6, 6, 0), (-6, 6, 0)], [], [(0, 1, 2, 3)])
g = bpy.data.objects.new("PRV_Ground", me)
coll.objects.link(g)
gm = bpy.data.materials.get("M_PRV_Ground") or bpy.data.materials.new("M_PRV_Ground")
gm.use_nodes = True
b = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
b.inputs["Base Color"].default_value = (0.78, 0.79, 0.8, 1)
b.inputs["Roughness"].default_value = 0.8
me.materials.clear(); me.materials.append(gm)

w = bpy.context.scene.world or bpy.data.worlds.new("World")
bpy.context.scene.world = w
w.use_nodes = True
bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
bg.inputs["Color"].default_value = (0.62, 0.64, 0.68, 1)
bg.inputs["Strength"].default_value = 0.42

sc = bpy.context.scene
try:
    sc.view_settings.view_transform = 'Standard'
except TypeError:
    pass
ee = sc.eevee
for attr, val in (("use_raytracing", True), ("use_shadows", True), ("taa_render_samples", 64), ("use_gtao", True)):
    if hasattr(ee, attr):
        setattr(ee, attr, val)
# preview camera (3/4 front)
cam = bpy.data.objects.get("CAM_Preview")
if not cam:
    cam = bpy.data.objects.new("CAM_Preview", bpy.data.cameras.new("CAM_Preview"))
    coll.objects.link(cam)
cam.data.lens = 70
cam.location = (1.6, -4.4, 1.35)
d = Vector((0, 0, 0.78)) - cam.location
cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
print("preview lighting ok")
