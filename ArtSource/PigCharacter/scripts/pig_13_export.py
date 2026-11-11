# Pig character - 13: build the optimised game mesh, bake the skin-detail texture, export FBX for Unity,
# re-import the FBX to verify it, and save the animation-test blend.
#
# The editable master keeps every part as its own object. Export works on a temporary joined copy:
#   parts -> join (shape keys/vertex groups merge by name) -> UV atlas -> detail-texture bake -> FBX.
# Clips are exported from NLA strips (one strip per action) so the driver-driven blend shapes are sampled
# into every take; the "all actions" mode would bake each action for the armature only.
import bpy, os, math
import numpy as np

ROOT = r"D:\unityProjects\fartonline\ArtSource\PigCharacter"
UNITY = r"D:\unityProjects\fartonline\Assets\Characters\Pig"
FBX_PATH = os.path.join(UNITY, "Models", "PigCharacter_Game.fbx")
TEX_DIR = os.path.join(UNITY, "Textures")
PARTS = ["Pig_Body", "Pig_Snout", "Pig_Eye_L", "Pig_Eye_R", "Pig_Lid_Up_L", "Pig_Lid_Up_R", "Pig_Lid_Low_L",
         "Pig_Lid_Low_R", "Pig_Brow_L", "Pig_Brow_R", "Pig_Ear_L", "Pig_Ear_R", "Pig_Hoof_BL", "Pig_Hoof_BR",
         "Pig_Hoof_FL", "Pig_Hoof_FR", "Pig_Tail"]
LOOPING = {"Pig_Idle_Breathing", "Pig_Walk", "Pig_Run"}

rig = bpy.data.objects["Pig_Rig"]
sc = bpy.context.scene

def deselect():
    for o in bpy.context.selected_objects:
        o.select_set(False)

def build_game_mesh():
    old = bpy.data.objects.get("Pig_Mesh")
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    copies = []
    for n in PARTS:
        src = bpy.data.objects[n]
        c = src.copy()
        c.data = src.data.copy()
        if src.data.shape_keys:
            # data.copy() also copies the shape-key datablock (and its drivers)
            pass
        bpy.data.collections["CHARACTER_BODY"].objects.link(c)
        copies.append(c)
    body = copies[0]
    # every part needs a PigMask layer so the merged attribute is well defined (0 = non-skin)
    for c in copies:
        if "PigMask" not in c.data.color_attributes:
            a = c.data.color_attributes.new("PigMask", 'FLOAT_COLOR', 'POINT')
            for d in a.data:
                d.color = (0, 1, 0, 1)
    deselect()
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    gm = bpy.context.active_object
    gm.name = "Pig_Mesh"
    gm.data.name = "Pig_Mesh"
    # remove helper attributes not needed in the game
    for an in ("nostril_w", "lip_side"):
        a = gm.data.attributes.get(an)
        if a:
            gm.data.attributes.remove(a)
    gm.data.color_attributes.active_color = gm.data.color_attributes["PigMask"]
    gm.data.color_attributes.render_color_index = gm.data.color_attributes.find("PigMask")
    # UV atlas over all parts
    while gm.data.uv_layers:
        gm.data.uv_layers.remove(gm.data.uv_layers[0])
    gm.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004, area_weight=0.0, scale_to_bounds=True)
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    return gm

def bake_detail(gm, size=1024):
    """Bake Detail = (1 - patch*0.16) * mix(1, AO, 0.5) * (1 - interior*0.72) * (1 + (var-0.5)*0.06) to a greyscale texture
    (URP/Lit fallback: _BaseColor (= Skin_Color) x _BaseMap) and the raw PigMask to an RGB mask texture."""
    os.makedirs(TEX_DIR, exist_ok=True)
    try:
        sc.render.engine = 'CYCLES'
    except TypeError:
        pass
    sc.cycles.samples = 1
    sc.cycles.device = 'CPU'
    saved = list(gm.data.materials)
    results = {}
    for tex_name, expr in (("T_Pig_SkinDetail", "detail"), ("T_Pig_SkinMask", "mask")):
        img = bpy.data.images.get(tex_name) or bpy.data.images.new(tex_name, size, size, alpha=False, float_buffer=False)
        img.scale(size, size)
        img.colorspace_settings.name = 'sRGB' if expr == "detail" else 'Non-Color'
        m = bpy.data.materials.get("_M_Bake") or bpy.data.materials.new("_M_Bake")
        m.use_nodes = True
        nt = m.node_tree
        nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission")
        vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "PigMask"
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(vc.outputs["Color"], sep.inputs["Color"])
        if expr == "detail":
            def mth(op, a, b):
                n = nt.nodes.new("ShaderNodeMath"); n.operation = op
                for i, v in enumerate((a, b)):
                    if isinstance(v, float):
                        n.inputs[i].default_value = v
                    else:
                        nt.links.new(v, n.inputs[i])
                return n.outputs[0]
            patch = mth('SUBTRACT', 1.0, mth('MULTIPLY', sep.outputs["Red"], 0.16))
            ao = mth('ADD', 0.5, mth('MULTIPLY', sep.outputs["Green"], 0.5))
            inter = mth('SUBTRACT', 1.0, mth('MULTIPLY', sep.outputs["Blue"], 0.72))
            var = mth('ADD', 1.0, mth('MULTIPLY', mth('SUBTRACT', vc.outputs["Alpha"], 0.5), 0.06))
            d = mth('MULTIPLY', mth('MULTIPLY', mth('MULTIPLY', patch, ao), inter), var)
            # store the linear multiplier in an sRGB texture: encode so that Unity's sRGB decode returns d
            cc = nt.nodes.new("ShaderNodeCombineColor")
            for ch in ("Red", "Green", "Blue"):
                nt.links.new(d, cc.inputs[ch])
            nt.links.new(cc.outputs["Color"], em.inputs["Color"])
        else:
            nt.links.new(vc.outputs["Color"], em.inputs["Color"])
        nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
        it = nt.nodes.new("ShaderNodeTexImage"); it.image = img
        nt.nodes.active = it
        # swap every slot in place: clearing slots would clamp all face material indices to 0
        for i in range(len(gm.data.materials)):
            gm.data.materials[i] = m
        deselect()
        gm.select_set(True)
        bpy.context.view_layer.objects.active = gm
        bpy.ops.object.bake(type='EMIT', margin=8, use_clear=True)
        path = os.path.join(TEX_DIR, tex_name + ".png")
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        results[tex_name] = path
    for i, mt in enumerate(saved):
        gm.data.materials[i] = mt
    bpy.data.materials.remove(bpy.data.materials["_M_Bake"])
    try:
        sc.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        pass
    return results

def lay_out_nla():
    """one NLA track+strip per Pig_ action, back to back with 10-frame gaps"""
    ad = rig.animation_data or rig.animation_data_create()
    ad.action = None
    for t in list(ad.nla_tracks):
        ad.nla_tracks.remove(t)
    start = 0
    clips = []
    for act in sorted([a for a in bpy.data.actions if a.name.startswith("Pig_")], key=lambda a: a.name):
        tr = ad.nla_tracks.new()
        tr.name = act.name
        st = tr.strips.new(act.name, start, act)
        st.name = act.name.replace("Pig_", "")
        st.extrapolation = 'NOTHING'
        clips.append((st.name, int(st.frame_start), int(st.frame_end)))
        start = int(st.frame_end) + 10
    sc.frame_start, sc.frame_end = 0, start
    return clips

def export_fbx(gm):
    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    deselect()
    rig.select_set(True)
    gm.select_set(True)
    bpy.context.view_layer.objects.active = rig
    # Unity normalises the FBX axis declaration, so a -Y-facing Blender character imports facing -Z.
    # Yaw the armature object 180 deg for the export only (verified in Unity: pig then faces +Z, Eye_L at -X).
    old_rot = rig.rotation_euler.copy()
    rig.rotation_euler = (old_rot.x, old_rot.y, old_rot.z + math.pi)
    bpy.context.view_layer.update()
    try:
        _do_export()
    finally:
        rig.rotation_euler = old_rot
        bpy.context.view_layer.update()
    return FBX_PATH

def _do_export():
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type='FACE', use_tspace=False, colors_type='LINEAR',
        prioritize_active_color=True, use_custom_props=False,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=True, armature_nodetype='NULL',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode='STRIP', embed_textures=False)

def verify_fbx(path):
    before = set(bpy.data.objects)
    acts_before = set(bpy.data.actions)
    coll = bpy.data.collections.new("_FBX_VERIFY")
    sc.collection.children.link(coll)
    lc = bpy.context.view_layer.layer_collection.children["_FBX_VERIFY"]
    bpy.context.view_layer.active_layer_collection = lc
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, ignore_leaf_bones=False, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    rep = {}
    for o in new:
        if o.type == 'ARMATURE':
            rep["armature"] = o.name
            rep["bones"] = len(o.data.bones)
            rep["root_children"] = [b.name for b in o.data.bones if b.parent is None]
        if o.type == 'MESH':
            me = o.data
            rep["mesh"] = o.name
            rep["verts"] = len(me.vertices)
            rep["tris"] = sum(len(p.vertices) - 2 for p in me.polygons)
            rep["materials"] = [m.name for m in me.materials]
            rep["shape_keys"] = len(me.shape_keys.key_blocks) - 1 if me.shape_keys else 0
            rep["vgroups"] = len(o.vertex_groups)
            rep["color_attrs"] = [a.name for a in me.color_attributes]
            rep["uv"] = [u.name for u in me.uv_layers]
            vs = [o.matrix_world @ v.co for v in me.vertices]
            rep["bbox_min"] = [round(min(v[i] for v in vs), 3) for i in range(3)]
            rep["bbox_max"] = [round(max(v[i] for v in vs), 3) for i in range(3)]
            if me.shape_keys and me.shape_keys.animation_data:
                rep["shape_anim_action"] = me.shape_keys.animation_data.action.name if me.shape_keys.animation_data.action else None
    new_acts = [a for a in bpy.data.actions if a not in acts_before]
    rep["actions"] = len(new_acts)
    shape_acts = [a for a in new_acts if any('key_blocks' in fc.data_path for fc in a.fcurves)]
    rep["actions_with_shape_curves"] = len(shape_acts)
    rep["action_names"] = sorted(a.name for a in new_acts)[:40]
    # clean up the verification import
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    for a in new_acts:
        bpy.data.actions.remove(a)
    bpy.data.collections.remove(coll)
    for blk in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.shape_keys):
        pass
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    for a in list(bpy.data.armatures):
        if a.users == 0:
            bpy.data.armatures.remove(a)
    for m in list(bpy.data.materials):
        if m.users == 0 and not m.name.startswith(("M_Pig", "M_PRV")):
            bpy.data.materials.remove(m)
    return rep

def run_export():
    """full export: game mesh -> drivers -> bake -> NLA -> FBX -> verify -> AnimationTests.blend -> restore master"""
    S = {"__name__": "__lib__"}
    p = os.path.join(ROOT, "scripts", "pig_11_shapekeys.py")
    exec(compile(open(p).read(), p, 'exec'), S)
    vl = bpy.context.view_layer.layer_collection.children
    vl["CHARACTER_RIG"].hide_viewport = False
    rig.hide_set(False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "PigCharacter_Master.blend"))
    gm = build_game_mesh()
    S["add_drivers"](gm)
    tris = sum(len(p.vertices) - 2 for p in gm.data.polygons)
    tex = bake_detail(gm)
    clips = lay_out_nla()
    for tr in rig.animation_data.nla_tracks:
        tr.mute = False
    export_fbx(gm)
    rep = verify_fbx(FBX_PATH)
    rep["game_tris"] = tris
    rep["clips"] = clips
    rep["textures"] = tex
    # animation test file: game mesh + rig + clips on the NLA timeline with markers
    c = bpy.data.collections.get("GAME_MESH") or bpy.data.collections.new("GAME_MESH")
    if c.name not in sc.collection.children:
        sc.collection.children.link(c)
    for uc in list(gm.users_collection):
        uc.objects.unlink(gm)
    c.objects.link(gm)
    parts = ["CHARACTER_BODY", "CHARACTER_FACE", "CHARACTER_EYES", "CHARACTER_EARS", "CHARACTER_HOOVES", "CHARACTER_DETAILS"]
    for n in parts:
        vl[n].exclude = True
    sc.timeline_markers.clear()
    for tr in rig.animation_data.nla_tracks:
        for st in tr.strips:
            sc.timeline_markers.new(st.name, frame=int(st.frame_start))
    sc.camera = bpy.data.objects.get("CAM_Preview")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "PigCharacter_AnimationTests.blend"), copy=True)
    # restore the master
    bpy.data.objects.remove(gm, do_unlink=True)
    bpy.data.collections.remove(c)
    for n in parts:
        vl[n].exclude = False
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    for im in ("T_Pig_SkinDetail", "T_Pig_SkinMask"):
        if bpy.data.images.get(im):
            bpy.data.images.remove(bpy.data.images[im])
    for tr in rig.animation_data.nla_tracks:
        tr.mute = True
    rig.animation_data.action = bpy.data.actions.get("Pig_RefPose")
    sc.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "PigCharacter_Master.blend"))
    return rep

if __name__ not in ("__lib__",):
    import json
    print(json.dumps(run_export(), indent=1, default=str))
