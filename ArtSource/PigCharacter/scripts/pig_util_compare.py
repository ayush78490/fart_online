# Utility: render the character from an ortho camera matched pixel-for-pixel to the reference
# and write a side-by-side + overlay comparison image.
import bpy, os
import numpy as np

ROOT = r"D:\unityProjects\fartonline\ArtSource\PigCharacter"
REF = os.path.join(ROOT, "reference", "GoGoPig.jpg")
S = 0.00236
IMG_W, IMG_H = 719, 942

def ensure_ref_camera():
    cam = bpy.data.objects.get("CAM_RefFront")
    if cam is None:
        cd = bpy.data.cameras.new("CAM_RefFront")
        cam = bpy.data.objects.new("CAM_RefFront", cd)
        bpy.data.collections["REFERENCE"].objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = IMG_H * S
    cam.data.clip_end = 100
    cam.location = ((IMG_W / 2 - 365) * S, -20.0, (830 - IMG_H / 2) * S)
    cam.rotation_euler = (1.5707963, 0, 0)
    return cam

def render_compare(out_name="compare.png", engine_mode="MATERIAL", hide=("REFERENCE", "LIGHTING_PREVIEW", "WIP_CONSTRUCTION"), show_extra=()):
    sc = bpy.context.scene
    cam = ensure_ref_camera()
    old = (sc.camera, sc.render.engine, sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage,
           sc.render.film_transparent, sc.render.filepath)
    hidden = []
    for c in bpy.data.collections:
        if c.name in hide:
            for ob in c.objects:
                if ob.name in show_extra:
                    continue
                if not ob.hide_render:
                    ob.hide_render = True
                    hidden.append(ob)
    sc.camera = cam
    try:
        sc.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        pass
    sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = IMG_W, IMG_H, 100
    sc.render.film_transparent = True
    tmp = os.path.join(ROOT, "renders", "_tmp_front.png")
    sc.render.filepath = tmp
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    bpy.ops.render.render(write_still=True)
    for ob in hidden:
        ob.hide_render = False
    (sc.camera, sc.render.engine, sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage,
     sc.render.film_transparent, sc.render.filepath) = old

    r = bpy.data.images.load(tmp, check_existing=False)
    ref = bpy.data.images.load(REF, check_existing=True)
    ref.reload()
    A = np.array(r.pixels[:], dtype=np.float32).reshape(IMG_H, IMG_W, 4)
    B = np.array(ref.pixels[:], dtype=np.float32).reshape(IMG_H, IMG_W, 4)
    alpha = A[..., 3:4]
    # overlay: render composited at 55% over the reference
    over = B.copy()
    over[..., :3] = B[..., :3] * (1 - 0.55 * alpha) + A[..., :3] * 0.55 * alpha
    # silhouette outline of render in green on the reference
    edge = np.zeros((IMG_H, IMG_W), bool)
    m = alpha[..., 0] > 0.5
    edge[1:-1, 1:-1] = m[1:-1, 1:-1] & ~(m[:-2, 1:-1] & m[2:, 1:-1] & m[1:-1, :-2] & m[1:-1, 2:])
    outl = B.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            e = np.roll(np.roll(edge, dy, 0), dx, 1)
            outl[e] = (0.0, 0.9, 0.1, 1.0)
    solo = B.copy()
    solo[..., :3] = B[..., :3] * (1 - alpha) + A[..., :3] * alpha
    W3 = np.concatenate([outl, solo, over], axis=1)
    W3[..., 3] = 1.0
    out = bpy.data.images.new("_cmp", W3.shape[1], W3.shape[0], alpha=True)
    out.pixels[:] = W3.ravel()
    path = os.path.join(ROOT, "renders", out_name)
    out.filepath_raw = path
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    bpy.data.images.remove(r)
    return path
