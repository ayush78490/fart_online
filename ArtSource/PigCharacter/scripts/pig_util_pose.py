# Utility: pose helpers + multi-pose contact-sheet renders for deformation validation.
import bpy, os, math
import numpy as np
from mathutils import Matrix, Vector

ROOT = r"D:\unityProjects\fartonline\ArtSource\PigCharacter"
rig = bpy.data.objects["Pig_Rig"]
PB = rig.pose.bones

FACE_DEFAULTS = {}
def reset_pose():
    for pb in PB:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    for k in list(PB["CTRL_Face"].keys()):
        PB["CTRL_Face"][k] = 0.0
    for k, v in (("Cartoon_Intensity", 1.0), ("Squash", 0.0), ("Stretch", 0.0), ("Belly_Squash", 0.0),
                 ("Belly_Stretch", 0.0), ("IK_Arm_L", 1.0), ("IK_Arm_R", 1.0), ("IK_Leg_L", 1.0), ("IK_Leg_R", 1.0)):
        if k in PB["CTRL_Body"].keys():
            PB["CTRL_Body"][k] = v
    bpy.context.view_layer.update()

def rot_world(bone, axis, deg, pivot=None):
    """rotate a pose bone about a world axis through its head (or pivot)"""
    pb = PB[bone]
    bpy.context.view_layer.update()
    M = pb.matrix.copy()
    h = pivot if pivot is not None else M.translation.copy()
    R = Matrix.Translation(h) @ Matrix.Rotation(math.radians(deg), 4, Vector(axis).normalized()) @ Matrix.Translation(-h)
    pb.matrix = R @ M
    bpy.context.view_layer.update()

def move_world(bone, delta):
    pb = PB[bone]
    bpy.context.view_layer.update()
    M = pb.matrix.copy()
    M.translation += Vector(delta)
    pb.matrix = M
    bpy.context.view_layer.update()

def set_props(**kw):
    for k, v in kw.items():
        tgt = PB["CTRL_Face"] if k in PB["CTRL_Face"].keys() else PB["CTRL_Body"]
        tgt[k] = float(v)
    rig.update_tag()
    bpy.context.view_layer.update()

def render_sheet(poses, out_name, cam_loc=(1.6, -4.4, 1.35), target=(0, 0, 0.78), lens=70, size=360, cols=4,
                 frame=None):
    """poses: list of (label, fn). Renders each with EEVEE and tiles them."""
    sc = bpy.context.scene
    cam = bpy.data.objects.get("CAM_Sheet") or bpy.data.objects.new("CAM_Sheet", bpy.data.cameras.new("CAM_Sheet"))
    if cam.name not in bpy.data.collections["LIGHTING_PREVIEW"].objects:
        bpy.data.collections["LIGHTING_PREVIEW"].objects.link(cam)
    cam.data.lens = lens
    cam.location = cam_loc
    cam.rotation_euler = (Vector(target) - Vector(cam_loc)).to_track_quat('-Z', 'Y').to_euler()
    old = (sc.camera, sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage, sc.render.filepath,
           sc.render.film_transparent)
    sc.camera = cam
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    hidden = []
    for c in ("REFERENCE", "WIP_CONSTRUCTION"):
        for ob in bpy.data.collections[c].objects:
            if not ob.hide_render:
                ob.hide_render = True
                hidden.append(ob)
    tiles = []
    tmp = os.path.join(ROOT, "renders", "_tile.png")
    for label, fn in poses:
        reset_pose()
        if frame is not None:
            sc.frame_set(frame)
        fn()
        bpy.context.view_layer.update()
        sc.render.filepath = tmp
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(tmp, check_existing=False)
        a = np.array(im.pixels[:], dtype=np.float32).reshape(size, size, 4)
        bpy.data.images.remove(im)
        tiles.append(a)
    reset_pose()
    for ob in hidden:
        ob.hide_render = False
    (sc.camera, sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage, sc.render.filepath,
     sc.render.film_transparent) = old
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.ones((rows * size, cols * size, 4), np.float32)
    for i, t in enumerate(tiles):
        r, c = i // cols, i % cols
        sheet[(rows - 1 - r) * size:(rows - r) * size, c * size:(c + 1) * size] = t
    out = bpy.data.images.new("_sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    out.pixels[:] = sheet.ravel()
    path = os.path.join(ROOT, "renders", out_name)
    out.filepath_raw = path
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    return path
