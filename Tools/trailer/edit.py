# The cut. Times are seconds; src is the first raw frame (dir 'raw' unless given).
EDIT = [
    dict(black=True, dur=0.5),
    dict(src=1186, dur=1.9, zoom=(1.0, 1.07), cap='ONE RAFT', flash=0.8),
    dict(src=4250, dur=1.7, zoom=(1.0, 1.10), focus=(0.5, 0.45), cap='FOUR PIGS', flash=0.35),
    dict(src=4602, dur=1.6, zoom=(1.0, 1.05), cap='ZERO ENGINES', flash=0.35),
    dict(src=690, dur=1.5, zoom=(1.12, 1.0), cap='FART TO STEER', flash=0.35),
    dict(src=3968, dur=0.9, zoom=(1.0, 1.10), cap='DODGE', cap_at=0.04, cap_size=190, flash=0.5),
    dict(src=872, dur=0.8, zoom=(1.0, 1.06), cap='EVERY', cap_at=0.04, cap_size=190, flash=0.3),
    dict(dir='rawcrash', src=205, dur=0.8, zoom=(1.0, 1.06), cap='SINGLE', cap_at=0.04, cap_size=190, flash=0.3),
    dict(src=4790, dur=0.8, zoom=(1.0, 1.08), cap='SPIKE', cap_at=0.04, cap_size=190, flash=0.3),
    dict(src=20, dur=2.0, zoom=(1.0, 1.06), cap=['PLAY SOLO', 'OR 4-PLAYER ONLINE'], cap_y=0.70, cap_size=120, flash=0.35),
    dict(dir='rawcrash', src=3750, dur=2.6, zoom=(1.0, 1.12), focus=(0.5, 0.42), hit=0.86, shake=0.86, cap='OOPS.', cap_at=1.45, cap_size=200, flash=0.3),
    dict(src=262, dur=4.9, speed=0.5, zoom=(1.0, 1.10), end=True, flash=0.9),
]

_t = [0.0]
for _s in EDIT: _t.append(_t[-1] + _s['dur'])
CUT = _t  # CUT[i] = start of segment i

F = 'Fart/'; W_ = 'PendulumWhoosh/'
CUES = [
    dict(f='RiverLoop/loop_water_02.ogg', at=0.45, len=14.7, g=0.22, fade=0.4),
    dict(f='RiverLoop/loop_water_02.ogg', at=15.0, len=5.0, g=0.12, fade=0.5),
    # cold open on black
    dict(f='FartCharged/gastric_big_00.wav', at=0.02, g=1.0),
    # cut whooshes
    dict(f=W_ + 'whoosh2_0.wav', at=CUT[1] - 0.10, g=0.8),
    dict(f=W_ + 'swish-1.wav', at=CUT[2] - 0.06, g=0.7),
    dict(f=W_ + 'swish-4.wav', at=CUT[3] - 0.06, g=0.7),
    dict(f=W_ + 'swish-7.wav', at=CUT[4] - 0.06, g=0.7),
    dict(f=W_ + 'swish-1.wav', at=CUT[5] - 0.06, g=0.9, pan=-0.4),
    dict(f=W_ + 'swish-4.wav', at=CUT[6] - 0.06, g=0.9, pan=0.4),
    dict(f=W_ + 'swish-7.wav', at=CUT[7] - 0.06, g=0.9, pan=-0.4),
    dict(f=W_ + 'swish-1.wav', at=CUT[8] - 0.06, g=0.9, pan=0.4),
    dict(f=W_ + 'whoosh2_0.wav', at=CUT[9] - 0.10, g=0.8),
    dict(f=W_ + 'swish-4.wav', at=CUT[10] - 0.06, g=0.8),
    # caption pops
    dict(f='UIClick/drop_002.ogg', at=CUT[1] + 0.12, g=0.9),
    dict(f='UIClick/drop_002.ogg', at=CUT[2] + 0.12, g=0.9),
    dict(f='UIClick/drop_002.ogg', at=CUT[3] + 0.12, g=0.9),
    dict(f='UIClick/drop_002.ogg', at=CUT[4] + 0.12, g=0.9),
    dict(f='RaftCollision/wood_hit_01.ogg', at=CUT[5] + 0.04, g=0.8),
    dict(f='RaftCollision/wood_hit_02.ogg', at=CUT[6] + 0.04, g=0.8),
    dict(f='RaftCollision/wood_hit_03.ogg', at=CUT[7] + 0.04, g=0.8),
    dict(f='RaftCollision/wood_hit_04.ogg', at=CUT[8] + 0.04, g=0.8),
    dict(f='UIClick/drop_002.ogg', at=CUT[9] + 0.12, g=0.9),
    dict(f='UIClick/drop_002.ogg', at=CUT[9] + 0.21, g=0.9, rate=1.2),
    # farts
    dict(f=F + 'gastric_short_04.wav', at=CUT[1] + 0.75, g=0.6, pan=0.3),
    dict(f=F + 'gastric_short_07.wav', at=CUT[2] + 0.45, g=0.9),
    dict(f='FartLoop/gastric_long_01.wav', at=CUT[3] + 0.15, len=1.35, g=0.9, fade=0.08),
    dict(f=F + 'gastric_short_06.wav', at=CUT[4] + 0.35, g=0.7, pan=-0.3),
    dict(f=F + 'gastric_short_09.wav', at=CUT[4] + 0.95, g=0.7, pan=0.3),
    dict(f=F + 'gastric_short_08.wav', at=CUT[5] + 0.35, g=0.6),
    dict(f=F + 'gastric_short_04.wav', at=CUT[6] + 0.30, g=0.6),
    dict(f=F + 'gastric_short_06.wav', at=CUT[7] + 0.30, g=0.6),
    dict(f=F + 'gastric_short_09.wav', at=CUT[8] + 0.30, g=0.6),
    dict(f='FartLoop/gastric_long_02.wav', at=CUT[9] + 0.2, len=1.7, g=0.75, fade=0.1),
    dict(f='FartLoop/gastric_long_05.wav', at=CUT[10] + 0.05, len=0.75, g=0.7, fade=0.06),
    # the crash
    dict(f='RaftCollisionHeavy/wood_slam_01.ogg', at=CUT[10] + 0.86, g=2.4),
    dict(f='RaftCollisionHeavy/bfh1_wood_hit_01.ogg', at=CUT[10] + 0.86, g=2.0),
    dict(f='Splash/splash_05.ogg', at=CUT[10] + 0.92, g=1.6),
    dict(f='Splash/splash_02.ogg', at=CUT[10] + 1.25, g=0.6, rate=0.7),
    dict(f='FartEmpty/gastric_short_03.wav', at=CUT[10] + 1.5, g=0.8, rate=0.75),
    # logo
    dict(f='RaftCollisionHeavy/wood_slam_02.ogg', at=CUT[11] + 0.02, g=2.0),
    dict(f='FartCharged/gastric_big_10.wav', at=CUT[11] + 0.05, g=0.9),
    dict(f='RaftCollisionHeavy/wood_slam_03.ogg', at=CUT[11] + 0.20, g=1.8),
    dict(f='Finish/WinSteelDrums.ogg', at=CUT[11] + 0.45, g=1.4, fade=0.02),
    dict(f='UIOpen/open_002.ogg', at=CUT[11] + 0.75, g=0.7),
    dict(f='CountdownGo/confirmation_002.ogg', at=CUT[11] + 1.25, g=0.6),
    dict(f='FartEmpty/bubble_02.ogg', at=CUT[11] + 3.6, g=0.8),
]
