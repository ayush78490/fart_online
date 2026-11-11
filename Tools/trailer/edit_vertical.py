# The 9:16 cut: menu -> countdown -> HUD play -> dodges -> hits -> last crash -> game over board -> logo.
#   TRAILER_EDIT=edit_vertical.py python Tools/trailer/make_trailer.py
OUT_W, OUT_H, OUT_NAME, SRC_DIR = 1080, 1920, 'FartOnline_Trailer_9x16', 'rawvert'

CAP = dict(cap_size=128, cap_y=0.15)
EDIT = [
    # 0 the main menu, a tap on Play Solo
    dict(src=40, dur=2.0, grade=False, tap=(0.5, 0.578, 1.35), fade_in=0.25),
    # 1 countdown 3-2-1 (sped up), 2 GO and the first metres with the HUD
    dict(src=200, dur=2.0, speed=1.5, grade=False, flash=0.9),
    dict(src=381, dur=2.2, grade=False),
    # surviving
    dict(src=1121, dur=1.6, zoom=(1.0, 1.06), cap=['THREAD', 'THE MINES'], flash=0.6, **CAP),
    dict(src=1219, dur=1.05, zoom=(1.06, 1.0), flash=0.3),
    dict(src=890, dur=2.0, zoom=(1.0, 1.06), cap=['DUCK', 'THE BRIDGE'], flash=0.3, **CAP),
    dict(src=6380, dur=1.3, zoom=(1.0, 1.08), focus=(0.5, 0.42), cap=['STAY', 'CALM'], flash=0.3, **CAP),
    dict(src=4150, dur=1.8, zoom=(1.0, 1.06), cap=['SQUEEZE', 'THROUGH'], flash=0.3, **CAP),
    # getting hit (footage after the impact is already slow motion)
    dict(src=6140, dur=2.2, zoom=(1.0, 1.10), focus=(0.5, 0.52), hit=0.70, shake=0.70, cap=['OR GET', 'SMASHED'], cap_at=1.0, flash=0.3, **CAP),
    dict(src=1352, dur=1.7, zoom=(1.0, 1.08), hit=0.58, shake=0.58, cap=['SPIKED'], cap_at=0.8, flash=0.3, **CAP),
    dict(src=7150, dur=1.6, zoom=(1.0, 1.08), hit=0.67, shake=0.67, cap=['FLATTENED'], cap_at=0.85, flash=0.3, **CAP),
    # 11 the last run with the HUD: crash, CRASH! banner, the game over board
    dict(src=8660, dur=6.5, grade=False, hit=2.30, shake=2.30, flash=0.6, tap=(0.34, 0.632, 5.95)),
    # 12 logo
    dict(src=1600, dur=4.05, speed=0.5, zoom=(1.0, 1.10), end=True, flash=0.9),
]

_t = [0.0]
for _s in EDIT: _t.append(_t[-1] + _s['dur'])
CUT = _t

F = 'Fart/'; W_ = 'PendulumWhoosh/'
HIT = 'RaftCollisionHeavy/'
CUES = [
    dict(f='RiverLoop/loop_water_02.ogg', at=0.0, len=CUT[12], g=0.20, fade=0.4),
    dict(f='RiverLoop/loop_water_02.ogg', at=CUT[12] - 0.1, len=4.1, g=0.10, fade=0.5),
    # menu: tap on Play
    dict(f='UIHover/rollover2.wav', at=0.5, g=0.5),
    dict(f='UIClick/click_001.ogg', at=1.35, g=1.2),
    dict(f='UIOpen/open_002.ogg', at=1.45, g=0.8),
    # countdown (3, 2, 1 at 1.5x) and GO
    dict(f=W_ + 'whoosh2_0.wav', at=CUT[1] - 0.10, g=0.7),
    dict(f='Countdown/select_003.ogg', at=CUT[1] + 0.05, g=1.0),
    dict(f='Countdown/select_003.ogg', at=CUT[1] + 0.70, g=1.0),
    dict(f='Countdown/select_003.ogg', at=CUT[1] + 1.36, g=1.0),
    dict(f='CountdownGo/confirmation_002.ogg', at=CUT[2] + 0.02, g=1.2),
    dict(f=F + 'gastric_short_07.wav', at=CUT[2] + 0.35, g=0.8),
    dict(f='FartLoop/gastric_long_01.wav', at=CUT[2] + 0.9, len=1.2, g=0.6, fade=0.08),
    # surviving: whoosh on each cut, a caption pop, farts
    dict(f=W_ + 'swish-1.wav', at=CUT[3] - 0.06, g=0.9),
    dict(f='UIClick/drop_002.ogg', at=CUT[3] + 0.12, g=0.9),
    dict(f='FartLoop/gastric_long_02.wav', at=CUT[3] + 0.2, len=1.3, g=0.7, fade=0.08),
    dict(f=W_ + 'swish-4.wav', at=CUT[4] - 0.06, g=0.8, pan=-0.4),
    dict(f=F + 'gastric_short_04.wav', at=CUT[4] + 0.3, g=0.7),
    dict(f=W_ + 'swish-7.wav', at=CUT[5] - 0.06, g=0.9, pan=0.4),
    dict(f='UIClick/drop_002.ogg', at=CUT[5] + 0.12, g=0.9),
    dict(f=F + 'gastric_short_06.wav', at=CUT[5] + 0.5, g=0.7),
    dict(f=W_ + 'whoosh2_0.wav', at=CUT[5] + 1.35, g=0.8),
    dict(f=W_ + 'swish-1.wav', at=CUT[6] - 0.06, g=0.8),
    dict(f='UIClick/drop_002.ogg', at=CUT[6] + 0.12, g=0.9),
    dict(f=F + 'gastric_short_09.wav', at=CUT[6] + 0.45, g=0.9, rate=0.8),
    dict(f=W_ + 'swish-4.wav', at=CUT[7] - 0.06, g=0.9),
    dict(f='UIClick/drop_002.ogg', at=CUT[7] + 0.12, g=0.9),
    dict(f='FartLoop/gastric_long_05.wav', at=CUT[7] + 0.3, len=1.3, g=0.7, fade=0.08),
    # hits
    dict(f=W_ + 'whoosh2_0.wav', at=CUT[8] + 0.35, g=1.0, pan=-0.5),
    dict(f=HIT + 'wood_slam_01.ogg', at=CUT[8] + 0.70, g=2.4),
    dict(f=HIT + 'bfh1_wood_hit_01.ogg', at=CUT[8] + 0.70, g=2.0),
    dict(f='Splash/splash_05.ogg', at=CUT[8] + 0.76, g=1.5),
    dict(f='UIClick/drop_002.ogg', at=CUT[8] + 1.0, g=0.9),
    dict(f=W_ + 'swish-7.wav', at=CUT[9] - 0.06, g=0.8),
    dict(f=HIT + 'wood_slam_02.ogg', at=CUT[9] + 0.58, g=2.2),
    dict(f='Splash/splash_02.ogg', at=CUT[9] + 0.62, g=1.4),
    dict(f='FartEmpty/gastric_short_03.wav', at=CUT[9] + 1.0, g=0.8, rate=0.75),
    dict(f=W_ + 'swish-1.wav', at=CUT[10] - 0.06, g=0.8),
    dict(f=HIT + 'wood_slam_03.ogg', at=CUT[10] + 0.67, g=2.2),
    dict(f='Splash/splash_04.ogg', at=CUT[10] + 0.72, g=1.4),
    dict(f='FartEmpty/bubble_02.ogg', at=CUT[10] + 1.1, g=0.9),
    # last run: farts, the crash, the sad jingle, the board, Play Again
    dict(f=W_ + 'whoosh2_0.wav', at=CUT[11] - 0.10, g=0.7),
    dict(f='FartLoop/gastric_long_01.wav', at=CUT[11] + 0.3, len=1.5, g=0.6, fade=0.08),
    dict(f=HIT + 'wood_slam_01.ogg', at=CUT[11] + 2.30, g=2.4),
    dict(f=HIT + 'bfh1_wood_hit_01.ogg', at=CUT[11] + 2.30, g=2.0),
    dict(f='Splash/splash_07.ogg', at=CUT[11] + 2.36, g=1.4),
    dict(f='Wipeout/jingles_PIZZA07.ogg', at=CUT[11] + 2.75, g=0.9),
    dict(f='UIOpen/open_002.ogg', at=CUT[11] + 3.6, g=0.9),
    dict(f='UIClick/click_001.ogg', at=CUT[11] + 5.95, g=1.2),
    # logo
    dict(f=HIT + 'wood_slam_02.ogg', at=CUT[12] + 0.02, g=2.0),
    dict(f='FartCharged/gastric_big_10.wav', at=CUT[12] + 0.05, g=0.9),
    dict(f=HIT + 'wood_slam_03.ogg', at=CUT[12] + 0.20, g=1.8),
    dict(f='Finish/WinSteelDrums.ogg', at=CUT[12] + 0.45, g=1.4, fade=0.02),
]
