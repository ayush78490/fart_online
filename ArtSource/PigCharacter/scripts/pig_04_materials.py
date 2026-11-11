# Pig character - 04: material system.
# M_Pig_Skin uses node group NG_Pig_Skin:
#   Final Base Color = Skin_Color x Detail
#   Detail = (1 - PatchMask * Patch_Darkness) x (1 + (PigMask.A - 0.5) * Variation) x mix(1, Cavity, Cavity_Strength)
# Roughness / specular / coat / subsurface are independent group inputs and never touch Skin_Color.
import bpy

def srgb_to_lin(c):
    return tuple((v / 12.92) if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)

SKIN_DEFAULT = srgb_to_lin((0.945, 0.615, 0.695)) + (1.0,)

def sock(ng, name, io, stype, default=None, mn=None, mx=None):
    s = ng.interface.new_socket(name=name, in_out=io, socket_type=stype)
    if default is not None:
        s.default_value = default
    if mn is not None:
        s.min_value = mn
    if mx is not None:
        s.max_value = mx
    return s

def build_skin_group():
    ng = bpy.data.node_groups.get("NG_Pig_Skin")
    if ng:
        bpy.data.node_groups.remove(ng)
    ng = bpy.data.node_groups.new("NG_Pig_Skin", "ShaderNodeTree")
    sock(ng, "BSDF", "OUTPUT", "NodeSocketShader")
    sock(ng, "Skin_Color", "INPUT", "NodeSocketColor", SKIN_DEFAULT)
    sock(ng, "Patch_Mask", "INPUT", "NodeSocketFloat", 0.0, 0.0, 1.0)
    sock(ng, "Patch_Darkness", "INPUT", "NodeSocketFloat", 0.16, 0.0, 1.0)
    sock(ng, "Cavity", "INPUT", "NodeSocketFloat", 1.0, 0.0, 1.0)
    sock(ng, "Cavity_Strength", "INPUT", "NodeSocketFloat", 0.5, 0.0, 1.0)
    sock(ng, "Variation", "INPUT", "NodeSocketFloat", 0.06, 0.0, 0.5)
    sock(ng, "Variation_Noise", "INPUT", "NodeSocketFloat", 0.5, 0.0, 1.0)
    sock(ng, "Interior_Mask", "INPUT", "NodeSocketFloat", 0.0, 0.0, 1.0)
    sock(ng, "Interior_Color", "INPUT", "NodeSocketColor", srgb_to_lin((0.30, 0.12, 0.10)) + (1.0,))
    sock(ng, "Roughness", "INPUT", "NodeSocketFloat", 0.36, 0.0, 1.0)
    sock(ng, "Micro_Roughness", "INPUT", "NodeSocketFloat", 0.06, 0.0, 0.5)
    sock(ng, "Specular_Level", "INPUT", "NodeSocketFloat", 0.5, 0.0, 1.0)
    sock(ng, "IOR", "INPUT", "NodeSocketFloat", 1.45, 1.0, 2.5)
    sock(ng, "Coat_Weight", "INPUT", "NodeSocketFloat", 0.18, 0.0, 1.0)
    sock(ng, "Coat_Roughness", "INPUT", "NodeSocketFloat", 0.22, 0.0, 1.0)
    sock(ng, "Subsurface_Weight", "INPUT", "NodeSocketFloat", 0.12, 0.0, 1.0)
    sock(ng, "Subsurface_Scale", "INPUT", "NodeSocketFloat", 0.025, 0.0, 0.2)
    sock(ng, "Bump_Strength", "INPUT", "NodeSocketFloat", 0.035, 0.0, 0.5)

    N, L = ng.nodes, ng.links
    gi = N.new("NodeGroupInput"); gi.location = (-1100, 0)
    go = N.new("NodeGroupOutput"); go.location = (700, 0)
    bsdf = N.new("ShaderNodeBsdfPrincipled"); bsdf.location = (350, 0)

    # low-frequency colour variation (object space so it sticks to the skin when deformed)
    tc = N.new("ShaderNodeTexCoord"); tc.location = (-1100, -400)
    noise = N.new("ShaderNodeTexNoise"); noise.location = (-900, -400)
    noise.inputs["Scale"].default_value = 3.5
    noise.inputs["Detail"].default_value = 3.0
    L.new(tc.outputs["Object"], noise.inputs["Vector"])
    fine = N.new("ShaderNodeTexNoise"); fine.location = (-900, -700)
    fine.inputs["Scale"].default_value = 140.0
    fine.inputs["Detail"].default_value = 2.0
    L.new(tc.outputs["Object"], fine.inputs["Vector"])

    def math(op, a, b, loc, name=None):
        m = N.new("ShaderNodeMath"); m.operation = op; m.location = loc
        for i, v in enumerate((a, b)):
            if isinstance(v, (int, float)):
                m.inputs[i].default_value = v
            else:
                L.new(v, m.inputs[i])
        if name:
            m.label = name
        return m.outputs[0]

    # patch term: 1 - mask * darkness
    pd = math('MULTIPLY', gi.outputs["Patch_Mask"], gi.outputs["Patch_Darkness"], (-700, 200))
    patch = math('SUBTRACT', 1.0, pd, (-500, 200), "Patch")
    # variation term: 1 + (n - 0.5) * variation
    nc = math('SUBTRACT', gi.outputs["Variation_Noise"], 0.5, (-700, -300))
    nv = math('MULTIPLY', nc, gi.outputs["Variation"], (-550, -300))
    var = math('ADD', nv, 1.0, (-400, -300), "Variation")
    # cavity term: mix(1, cavity, strength)
    cav = N.new("ShaderNodeMix"); cav.data_type = 'FLOAT'; cav.location = (-500, 0)
    L.new(gi.outputs["Cavity_Strength"], cav.inputs["Factor"])
    cav.inputs["A"].default_value = 1.0
    L.new(gi.outputs["Cavity"], cav.inputs["B"])
    d1 = math('MULTIPLY', patch, var, (-250, 100))
    detail = math('MULTIPLY', d1, cav.outputs["Result"], (-100, 100), "Detail")
    # Final = Skin_Color x Detail
    mul = N.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.location = (100, 200)
    mul.label = "Skin_Color x Detail"
    mul.inputs["Factor"].default_value = 1.0
    L.new(gi.outputs["Skin_Color"], mul.inputs["A"])
    L.new(detail, mul.inputs["B"])
    # nostril / crease interiors blend to a fixed dark tint (independent of Skin_Color)
    inter = N.new("ShaderNodeMix"); inter.data_type = 'RGBA'; inter.location = (250, 250)
    inter.label = "Interior blend"
    L.new(gi.outputs["Interior_Mask"], inter.inputs["Factor"])
    L.new(mul.outputs["Result"], inter.inputs["A"])
    L.new(gi.outputs["Interior_Color"], inter.inputs["B"])
    L.new(inter.outputs["Result"], bsdf.inputs["Base Color"])

    # roughness = Roughness + (fine - 0.5) * Micro_Roughness   (independent of colour)
    fc = math('SUBTRACT', fine.outputs["Fac"], 0.5, (-600, -700))
    fr = math('MULTIPLY', fc, gi.outputs["Micro_Roughness"], (-450, -700))
    rough = math('ADD', fr, gi.outputs["Roughness"], (-300, -700))
    L.new(rough, bsdf.inputs["Roughness"])
    bump = N.new("ShaderNodeBump"); bump.location = (100, -700)
    L.new(gi.outputs["Bump_Strength"], bump.inputs["Strength"])
    bump.inputs["Distance"].default_value = 0.002
    L.new(fine.outputs["Fac"], bump.inputs["Height"])
    L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    L.new(gi.outputs["Specular_Level"], bsdf.inputs["Specular IOR Level"])
    L.new(gi.outputs["IOR"], bsdf.inputs["IOR"])
    L.new(gi.outputs["Coat_Weight"], bsdf.inputs["Coat Weight"])
    L.new(gi.outputs["Coat_Roughness"], bsdf.inputs["Coat Roughness"])
    L.new(gi.outputs["Subsurface_Weight"], bsdf.inputs["Subsurface Weight"])
    L.new(gi.outputs["Subsurface_Scale"], bsdf.inputs["Subsurface Scale"])
    bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.22)
    try:
        bsdf.subsurface_method = 'RANDOM_WALK'
    except Exception:
        pass
    L.new(bsdf.outputs["BSDF"], go.inputs["BSDF"])
    return ng

def get_mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (400, 0)
    return m, nt, out

def build_skin_material(ng):
    m, nt, out = get_mat("M_Pig_Skin")
    g = nt.nodes.new("ShaderNodeGroup"); g.node_tree = ng; g.location = (100, 0); g.label = "Pig Skin"
    g.width = 260
    attr = nt.nodes.new("ShaderNodeVertexColor"); attr.layer_name = "PigMask"; attr.location = (-400, 0)
    sep = nt.nodes.new("ShaderNodeSeparateColor"); sep.location = (-200, 0)
    nt.links.new(attr.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], g.inputs["Patch_Mask"])
    nt.links.new(sep.outputs["Green"], g.inputs["Cavity"])
    nt.links.new(sep.outputs["Blue"], g.inputs["Interior_Mask"])
    nt.links.new(attr.outputs["Alpha"], g.inputs["Variation_Noise"])
    nt.links.new(g.outputs["BSDF"], out.inputs["Surface"])
    m.diffuse_color = SKIN_DEFAULT
    return m

def simple(name, srgb, rough, spec=0.5, coat=0.0, emission=None, sss=0.0):
    m, nt, out = get_mat(name)
    b = nt.nodes.new("ShaderNodeBsdfPrincipled")
    col = srgb_to_lin(srgb) + (1.0,)
    b.inputs["Base Color"].default_value = col
    b.inputs["Roughness"].default_value = rough
    b.inputs["Specular IOR Level"].default_value = spec
    b.inputs["Coat Weight"].default_value = coat
    b.inputs["Coat Roughness"].default_value = 0.08
    b.inputs["Subsurface Weight"].default_value = sss
    nt.links.new(b.outputs["BSDF"], out.inputs["Surface"])
    m.diffuse_color = col
    return m

ng = build_skin_group()
MATS = {
    "skin": build_skin_material(ng),
    "mouth": simple("M_Pig_Mouth", (0.42, 0.13, 0.15), 0.45, 0.4),
    "eye": simple("M_Pig_Eye_White", (0.97, 0.965, 0.96), 0.12, 0.6, coat=0.6, sss=0.05),
    "pupil": simple("M_Pig_Pupil", (0.035, 0.03, 0.03), 0.08, 0.7, coat=0.8),
    "hoof": simple("M_Pig_Hoof", (0.26, 0.21, 0.19), 0.32, 0.5, coat=0.25),
}

def assign(obname, slots):
    ob = bpy.data.objects.get(obname)
    if not ob:
        return
    mats = ob.data.materials
    for i, s in enumerate(slots):          # replace in place: keeps per-face material_index intact
        if i < len(mats):
            mats[i] = MATS[s]
        else:
            mats.append(MATS[s])
    while len(mats) > len(slots):
        mats.pop()

for s in ("L", "R"):
    assign("Pig_Eye_" + s, ["eye", "pupil"])
    for lid in ("Up", "Low"):
        assign("Pig_Lid_%s_%s" % (lid, s), ["skin"])
    assign("Pig_Brow_" + s, ["hoof"])
    assign("Pig_Ear_" + s, ["skin"])
    assign("Pig_Hoof_F" + s, ["hoof"])
    assign("Pig_Hoof_B" + s, ["hoof"])
assign("Pig_Snout", ["skin"])
assign("Pig_Tail", ["skin"])
assign("Pig_Body", ["skin", "mouth"])
for m in list(bpy.data.materials):
    if m.name == "M_Pig_Nostril":
        bpy.data.materials.remove(m)
print("materials:", [m.name for m in bpy.data.materials if m.name.startswith("M_Pig")])
