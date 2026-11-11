# Rebuilds the whole pig character in order. Run inside Blender:
#   exec(open(r"...\scripts\pig_build_all.py").read())
# Each stage is its own script so a single stage can be re-run while iterating.
import os

SCRIPTS = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else \
    r"D:\unityProjects\fartonline\ArtSource\PigCharacter\scripts"

STAGES = [
    "pig_01_body_blockout.py",   # metaball volume
    "pig_03_body_mesh.py",       # sculpt source + quad retopo
    "pig_02_face_parts.py",      # snout, eyes, lids, brows, ears, hooves, claws, tail
    "pig_06_body_details.py",    # mouth slit + bag, belly button
    "pig_04_materials.py",       # material system
    "pig_07_masks.py",           # PigMask vertex colours
    "pig_08_rig.py",             # Pig_Rig: deform skeleton, controls, IK/FK, face drivers
    "pig_09_skin.py",            # skin weights
    "pig_11_shapekeys.py",       # facial / body shape keys + drivers
]

def run(stages=STAGES, upto=None):
    for s in stages:
        p = os.path.join(SCRIPTS, s)
        print("==== stage", s)
        exec(compile(open(p).read(), p, 'exec'), {"__name__": "__stage__"})
        if upto and s == upto:
            break

run()
