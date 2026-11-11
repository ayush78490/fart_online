# Pig character - 10: body deformation stress poses (validation only, nothing is keyed).
import bpy, math
from mathutils import Vector
exec(compile(open(r"D:\unityProjects\fartonline\ArtSource\PigCharacter\scripts\pig_util_pose.py").read(),
             "pig_util_pose.py", 'exec'))

def arm_raise(s, deg):
    set_props(**{"IK_Arm_" + s: 0.0})
    rot_world("UpperArm_" + s, (0, -1 if s == "L" else 1, 0), deg)

def elbow_bend(s, deg):
    pb = PB["LowerArm_" + s]
    d = (pb.tail - pb.head).normalized()
    rot_world("LowerArm_" + s, d.cross(Vector((0, -1, 0))), deg)

def pose_arms_side():
    arm_raise("L", 45); arm_raise("R", 45)

def pose_arms_up():
    arm_raise("L", 125); arm_raise("R", 125)

def pose_elbows():
    set_props(IK_Arm_L=0, IK_Arm_R=0)
    rot_world("UpperArm_L", (1, 0, 0), 35); rot_world("UpperArm_R", (1, 0, 0), 35)
    elbow_bend("L", 105); elbow_bend("R", 105)

def pose_hands_face():
    for s, sx in (("L", 1), ("R", -1)):
        h = PB["CTRL_IK_Hand_" + s]
        target = Vector((sx * 0.30, -0.42, 1.05))
        move_world("CTRL_IK_Hand_" + s, target - h.head)

def pose_legs_spread():
    for s, sx in (("L", 1), ("R", -1)):
        move_world("CTRL_IK_Foot_" + s, (sx * 0.14, 0, 0))
    move_world("Hips", (0, 0, -0.035))

def pose_crouch():
    move_world("Hips", (0, 0.0, -0.11))
    rot_world("Spine", (1, 0, 0), 10)
    pose_hands_mid()

def pose_hands_mid():
    for s, sx in (("L", 1), ("R", -1)):
        move_world("CTRL_IK_Hand_" + s, (sx * 0.05, -0.12, -0.05))

def pose_lean():
    rot_world("Spine", (1, 0, 0), 18); rot_world("Chest", (1, 0, 0), 14); rot_world("Head", (1, 0, 0), -12)

def pose_twist():
    rot_world("Spine", (0, 0, 1), 22); rot_world("Chest", (0, 0, 1), 18); rot_world("Head", (0, 0, 1), 15)

def pose_sidebend():
    rot_world("Spine", (0, 1, 0), 14); rot_world("Chest", (0, 1, 0), 12)

def pose_reach():
    move_world("CTRL_IK_Hand_L", (0.10, -0.40, 0.30))
    rot_world("Chest", (0, 0, 1), 12)

def pose_squat_deep():
    move_world("Hips", (0, 0.02, -0.16))
    rot_world("Spine", (1, 0, 0), 14)

def pose_ears_tail():
    set_props(Ear_Tilt_L=1.0, Ear_Tilt_R=-1.0)
    rot_world("Tail_1", (1, 0, 0), 30)

POSES = [("arms_side", pose_arms_side), ("arms_up", pose_arms_up), ("elbows", pose_elbows),
         ("hands_face", pose_hands_face), ("legs_spread", pose_legs_spread), ("crouch", pose_crouch),
         ("squat_deep", pose_squat_deep), ("lean", pose_lean), ("twist", pose_twist), ("sidebend", pose_sidebend),
         ("reach", pose_reach), ("ears_tail", pose_ears_tail)]
