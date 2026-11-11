# Pig character - 00: scene setup, collections, reference plane.
import bpy, os

ROOT = r"D:\unityProjects\fartonline\ArtSource\PigCharacter"
REF = os.path.join(ROOT, "reference", "GoGoPig.jpg")

# Reference calibration (pixel -> metre). Feet bottom at py=885, centre column px=365.
S = 0.00236
IMG_W, IMG_H = 719, 942

COLLECTIONS = ["CHARACTER_BODY", "CHARACTER_FACE", "CHARACTER_EYES", "CHARACTER_EARS",
               "CHARACTER_HOOVES", "CHARACTER_RIG", "CHARACTER_CONTROLS", "CHARACTER_DETAILS",
               "LIGHTING_PREVIEW", "REFERENCE", "WIP_CONSTRUCTION"]

# checkpoint of whatever was there before
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "exports", "_checkpoint_00_initial.blend"), copy=True)

# wipe default scene content
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
for c in list(bpy.data.collections):
    bpy.data.collections.remove(c)
for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights, bpy.data.images, bpy.data.metaballs):
    for d in list(blk):
        if d.users == 0:
            blk.remove(d)

sc = bpy.context.scene
for name in COLLECTIONS:
    c = bpy.data.collections.new(name)
    sc.collection.children.link(c)

sc.unit_settings.system = 'METRIC'
sc.unit_settings.scale_length = 1.0
sc.render.fps = 30

# reference image empty, standing in the XZ plane behind the character, facing -Y (front view)
img = bpy.data.images.load(REF, check_existing=True)
ref = bpy.data.objects.new("REF_GoGoPig_Front", None)
ref.empty_display_type = 'IMAGE'
ref.data = img
ref.empty_display_size = IMG_H * S            # larger image side
ref.rotation_euler = (1.5707963, 0, 0)
cx_px, cy_px = IMG_W / 2, IMG_H / 2
ref.location = ((cx_px - 365) * S, 0.9, (830 - cy_px) * S)
ref.empty_image_side = 'FRONT'
ref.show_in_front = False
ref.color[3] = 0.6
ref.use_empty_image_alpha = True
bpy.data.collections["REFERENCE"].objects.link(ref)
ref.hide_render = True

print("setup ok; ref size", ref.empty_display_size, "loc", tuple(round(v, 3) for v in ref.location))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "PigCharacter_Master.blend"))
print("saved", bpy.data.filepath)
