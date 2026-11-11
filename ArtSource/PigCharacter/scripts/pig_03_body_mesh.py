# Pig character - 03: metaball blockout -> high-res sculpt source -> quad retopo (deforming game body).
import bpy, time

def build_body_mesh(target_faces=8000):
    for n in ("Pig_Body_Sculpt", "Pig_Body"):
        ob = bpy.data.objects.get(n)
        if ob:
            bpy.data.objects.remove(ob, do_unlink=True)
        me = bpy.data.meshes.get(n)
        if me:
            bpy.data.meshes.remove(me)
    mbo = bpy.data.objects["MB_Body"]
    mbo.data.resolution = 0.012
    lc = bpy.context.view_layer.layer_collection.children["WIP_CONSTRUCTION"]
    lc.hide_viewport = False
    for o in bpy.context.selected_objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = mbo
    mbo.select_set(True)
    bpy.ops.object.convert(target='MESH', keep_original=True)
    hi = bpy.context.active_object
    hi.name = hi.data.name = "Pig_Body_Sculpt"
    hi.data.remesh_voxel_size = 0.008
    bpy.ops.object.voxel_remesh()
    m = hi.modifiers.new("Smooth", "CORRECTIVE_SMOOTH")
    m.iterations, m.factor, m.smooth_type, m.use_only_smooth, m.rest_source = 8, 0.5, 'SIMPLE', True, 'ORCO'
    bpy.ops.object.modifier_apply(modifier="Smooth")

    lo = hi.copy()
    lo.data = hi.data.copy()
    lo.name = lo.data.name = "Pig_Body"
    bpy.data.collections["CHARACTER_BODY"].objects.link(lo)
    for c in list(lo.users_collection):
        if c.name != "CHARACTER_BODY":
            c.objects.unlink(lo)
    hi.select_set(False)
    bpy.context.view_layer.objects.active = lo
    lo.select_set(True)
    t = time.time()
    bpy.ops.object.quadriflow_remesh(use_mesh_symmetry=True, use_preserve_sharp=False, use_preserve_boundary=False,
                                     smooth_normals=False, mode='FACES', target_faces=target_faces, seed=3)
    # pull the retopo back onto the sculpt surface to recover volume lost by quadriflow
    sw = lo.modifiers.new("Fit", "SHRINKWRAP")
    sw.target, sw.wrap_method = hi, 'NEAREST_SURFACEPOINT'
    bpy.ops.object.modifier_apply(modifier="Fit")
    for p in lo.data.polygons:
        p.use_smooth = True
    lc.hide_viewport = True
    print("body:", len(lo.data.vertices), "verts", len(lo.data.polygons), "faces, quadriflow %.1fs" % (time.time() - t))
    return lo, hi

if __name__ == "__main__" or True:
    build_body_mesh()
