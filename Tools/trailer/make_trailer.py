"""Cuts the Fart Online trailer from the footage filmed by TrailerDirector (ArtSource/Trailer/raw).

  python Tools/trailer/make_trailer.py            # full render -> ArtSource/Trailer/FartOnline_Trailer.mp4
  python Tools/trailer/make_trailer.py preview    # a still from the middle of every segment -> ArtSource/Trailer/preview.jpg

The edit (which frames, how fast, which caption) is the EDIT list below. Sound is SFX only, mixed in make_audio().
"""
import math, os, subprocess, sys, wave
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
RAW = os.path.join(ROOT, 'ArtSource', 'Trailer', 'raw')
OUT = os.path.join(ROOT, 'ArtSource', 'Trailer')
FONT = os.path.join(ROOT, 'Assets', 'Game', 'UI', 'Resources', 'UI', 'Fonts', 'LilitaOne-Regular.ttf')
FONT2 = os.path.join(ROOT, 'Assets', 'Game', 'UI', 'Resources', 'UI', 'Fonts', 'Fredoka-SemiBold.ttf')
SFX = os.path.join(ROOT, 'Assets', 'Game', 'Audio', 'Resources', 'SFX')
FFMPEG = os.path.expandvars('%LOCALAPPDATA%/Microsoft/WinGet/Links/ffmpeg.exe')
W, H, FPS = 1920, 1080, 60
SR = 48000

BROWN = (74, 44, 26)
YELLOW = (255, 201, 60)
CREAM = (255, 246, 222)
GREEN = (176, 222, 60)

# ---------------------------------------------------------------- the edit
# src: first raw frame; dur: seconds on screen; speed: raw frames per output frame (0.5 = half speed);
# zoom: (start, end) punch-in; cap: caption; shake: impact shake at this time in the segment; flash: white flash in
EDIT = []  # filled in by edit.py (kept separate so the cut can change without touching the renderer)
OUT_W, OUT_H, OUT_NAME, SRC_DIR = 1920, 1080, 'FartOnline_Trailer', 'raw'
EDIT_FILE = os.environ.get('TRAILER_EDIT', 'edit.py')   # TRAILER_EDIT=edit_vertical.py for the 9:16 cut
exec(open(os.path.join(os.path.dirname(__file__), EDIT_FILE), encoding='utf-8').read())
W, H = OUT_W, OUT_H


def ease_out_back(t, s=1.9):
    t = min(max(t, 0.0), 1.0) - 1.0
    return t * t * ((s + 1) * t + s) + 1.0


def ease_out(t):
    t = min(max(t, 0.0), 1.0)
    return 1 - (1 - t) ** 3


_text_cache = {}


def text_sprite(text, size, fill, outline=BROWN, stroke=None, font=FONT, shadow=True, tilt=0.0):
    """Cartoon lettering like the game's UI: fat outline, hard drop shadow."""
    key = (text, size, fill, outline, stroke, font, shadow, tilt)
    if key in _text_cache:
        return _text_cache[key]
    f = ImageFont.truetype(font, size)
    stroke = stroke if stroke is not None else max(4, size // 9)
    pad = stroke * 2 + size // 4
    box = ImageDraw.Draw(Image.new('L', (8, 8))).textbbox((0, 0), text, font=f, stroke_width=stroke)
    w, h = box[2] - box[0] + pad * 2, box[3] - box[1] + pad * 2
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    x, y = pad - box[0], pad - box[1]
    if shadow:
        off = max(3, size // 12)
        d.text((x, y + off), text, font=f, fill=outline + (255,), stroke_width=stroke, stroke_fill=outline + (255,))
    d.text((x, y), text, font=f, fill=fill + (255,), stroke_width=stroke, stroke_fill=outline + (255,))
    # soft top highlight inside the letters
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).text((x, y), text, font=f, fill=255)
    grad = np.linspace(70, 0, h).clip(0, 255).astype(np.uint8)
    hl = Image.fromarray(np.repeat(grad[:, None], w, 1), 'L')
    white = Image.new('RGBA', (w, h), (255, 255, 255, 255))
    white.putalpha(Image.fromarray((np.asarray(mask, np.uint16) * np.asarray(hl, np.uint16) // 255).astype(np.uint8), 'L'))
    img = Image.alpha_composite(img, white)
    if tilt:
        img = img.rotate(tilt, resample=Image.BICUBIC, expand=True)
    _text_cache[key] = img
    return img


def paste_scaled(canvas, sprite, cx, cy, scale=1.0, alpha=1.0):
    if scale <= 0.01 or alpha <= 0.01:
        return
    if abs(scale - 1.0) > 0.003:
        sprite = sprite.resize((max(1, int(sprite.width * scale)), max(1, int(sprite.height * scale))), Image.BICUBIC)
    if alpha < 0.999:
        sprite = sprite.copy()
        sprite.putalpha(sprite.getchannel('A').point(lambda v: int(v * alpha)))
    canvas.alpha_composite(sprite, (int(cx - sprite.width / 2), int(cy - sprite.height / 2))) if _inside(canvas, sprite, cx, cy) \
        else _paste_clipped(canvas, sprite, cx, cy)


def _inside(canvas, sprite, cx, cy):
    x, y = int(cx - sprite.width / 2), int(cy - sprite.height / 2)
    return x >= 0 and y >= 0 and x + sprite.width <= canvas.width and y + sprite.height <= canvas.height


def _paste_clipped(canvas, sprite, cx, cy):
    x, y = int(cx - sprite.width / 2), int(cy - sprite.height / 2)
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    layer.paste(sprite, (x, y))
    canvas.alpha_composite(layer)


_vig = None


def vignette():
    global _vig
    if _vig is None:
        yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
        r = np.sqrt(((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2) / math.sqrt(2)
        _vig = (1.0 - 0.34 * np.clip((r - 0.45) / 0.55, 0, 1) ** 2)[..., None].astype(np.float32)
    return _vig


def grade(img):
    """A little more punch: contrast curve, saturation, vignette."""
    a = np.asarray(img, np.float32) / 255.0
    a = a + 0.10 * (a - 0.5) * (1 - np.abs(a - 0.5) * 2 * 0.3)       # gentle S-curve
    lum = a.mean(axis=2, keepdims=True)
    a = lum + (a - lum) * 1.12
    a = a * vignette()
    return Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGB')


def raw_frame(idx, d='raw'):
    return Image.open(os.path.join(OUT, d, '%06d.jpg' % idx)).convert('RGB')


def source(seg, t):
    """The graded 1080p picture of a segment at local time t (seconds)."""
    k = t * FPS * seg.get('speed', 1.0)
    idx = seg['src'] + int(k)
    hold = seg.get('freeze')
    if hold is not None and t >= hold:
        idx = seg['src'] + int(hold * FPS * seg.get('speed', 1.0))
    img = raw_frame(min(idx, seg.get('last', 10 ** 9)), seg.get('dir', SRC_DIR))
    z0, z1 = seg.get('zoom', (1.0, 1.0))
    u = t / seg['dur']
    z = z0 + (z1 - z0) * u
    fx, fy = seg.get('focus', (0.5, 0.5))
    sx = sy = 0.0
    sh = seg.get('shake')
    if sh is not None and t >= sh:
        d = math.exp(-(t - sh) * 7.0) * 26.0
        sx = math.sin((t - sh) * 71.0) * d
        sy = math.cos((t - sh) * 57.0) * d
        z = max(z, 1.04)
    cw, ch = img.width / z, img.height / z
    cx = fx * img.width + sx * img.width / W
    cy = fy * img.height + sy * img.height / W
    x0 = min(max(cx - cw / 2, 0), img.width - cw)
    y0 = min(max(cy - ch / 2, 0), img.height - ch)
    img = img.resize((W, H), Image.LANCZOS, box=(x0, y0, x0 + cw, y0 + ch))
    return grade(img) if seg.get('grade', True) else img


def caption(canvas, seg, t):
    cap = seg.get('cap')
    if not cap:
        return
    t0 = seg.get('cap_at', 0.12)
    if t < t0:
        return
    lines = cap if isinstance(cap, (list, tuple)) else [cap]
    y = seg.get('cap_y', 0.80) * H
    size = seg.get('cap_size', 150)
    out = max(0.0, (t - (seg['dur'] - 0.10)) / 0.10)
    for i, line in enumerate(lines):
        lt = t - t0 - i * 0.09
        if lt < 0:
            continue
        col = seg.get('cap_cols', [CREAM, YELLOW])[i % 2]
        spr = text_sprite(line, size, col, tilt=seg.get('cap_tilt', -3.0))
        s = ease_out_back(lt / 0.22) * (1.0 + 0.03 * min(1.0, lt))
        paste_scaled(canvas, spr, W / 2, y + i * size * 1.02, s, 1.0 - out)


def end_card(canvas, seg, t):
    """Logo over the darkened, blurred last shot."""
    bg = canvas.convert('RGB').filter(ImageFilter.GaussianBlur(14))
    a = np.asarray(bg, np.float32) * (0.50 - 0.06 * ease_out(t / 1.5))
    tint = np.array([255, 190, 90], np.float32) * 0.10
    canvas.paste(Image.fromarray(np.clip(a + tint, 0, 255).astype(np.uint8), 'RGB'))
    # sun rays behind the logo
    rays = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(rays)
    cx, cy = W / 2, H * (0.36 if H > W else 0.43)
    n = 18
    rot = t * 0.12
    for i in range(n):
        a0 = rot + i * 2 * math.pi / n
        a1 = a0 + math.pi / n
        R = 2400
        d.polygon([(cx, cy), (cx + math.cos(a0) * R, cy + math.sin(a0) * R), (cx + math.cos(a1) * R, cy + math.sin(a1) * R)],
                  fill=(255, 226, 140, int(34 * ease_out(t / 0.6))))
    canvas.alpha_composite(rays.filter(ImageFilter.GaussianBlur(3)))
    if H > W:
        return end_card_tall(canvas, t, cx, cy)
    fart = text_sprite('FART', 330, YELLOW, stroke=30, tilt=-4.0)
    online = text_sprite('ONLINE', 250, CREAM, stroke=26, tilt=-4.0)
    s1 = ease_out_back(t / 0.30, 2.6)
    s2 = ease_out_back((t - 0.16) / 0.30, 2.6) if t > 0.16 else 0
    breathe = 1.0 + 0.012 * math.sin(t * 2.2)
    paste_scaled(canvas, fart, W / 2 - 40, H * 0.30, s1 * breathe)
    paste_scaled(canvas, online, W / 2 + 30, H * 0.535, s2 * breathe)
    if t > 0.75:
        tag = text_sprite('endless river  •  everybody farts  •  one bump and you\'re out', 64, CREAM, stroke=10, font=FONT, tilt=0)
        u = ease_out((t - 0.75) / 0.35)
        paste_scaled(canvas, tag, W / 2, H * 0.75 + (1 - u) * 30, 1.0, u)
    if t > 1.25:
        u = ease_out((t - 1.25) / 0.35)
        for i, (label, col) in enumerate([('PC', GREEN), ('WEB', GREEN), ('MOBILE', GREEN)]):
            spr = text_sprite(label, 84, col, stroke=13, tilt=0)
            paste_scaled(canvas, spr, W / 2 + (i - 1) * 360, H * 0.85 + (1 - u) * 30, 1.0, u)
        co = text_sprite('1 – 4 PLAYER ONLINE CO-OP', 48, CREAM, stroke=8, font=FONT, tilt=0)
        u2 = ease_out((t - 1.55) / 0.35) if t > 1.55 else 0
        paste_scaled(canvas, co, W / 2, H * 0.935, 1.0, u2)


def end_card_tall(canvas, t, cx, cy):
    fart = text_sprite('FART', 340, YELLOW, stroke=30, tilt=-4.0)
    online = text_sprite('ONLINE', 250, CREAM, stroke=26, tilt=-4.0)
    s1 = ease_out_back(t / 0.30, 2.6)
    s2 = ease_out_back((t - 0.16) / 0.30, 2.6) if t > 0.16 else 0
    breathe = 1.0 + 0.012 * math.sin(t * 2.2)
    paste_scaled(canvas, fart, W / 2 - 30, H * 0.285, s1 * breathe)
    paste_scaled(canvas, online, W / 2 + 20, H * 0.415, s2 * breathe)
    for i, line in enumerate(['endless river', 'everybody farts', "one bump and you're out"]):
        t0 = 0.75 + i * 0.16
        if t > t0:
            u = ease_out((t - t0) / 0.35)
            spr = text_sprite(line, 74, CREAM, stroke=11, tilt=0)
            paste_scaled(canvas, spr, W / 2, H * (0.565 + i * 0.052) + (1 - u) * 30, 1.0, u)
    if t > 1.5:
        u = ease_out((t - 1.5) / 0.35)
        for i, label in enumerate(['PC', 'WEB', 'MOBILE']):
            spr = text_sprite(label, 86, GREEN, stroke=13, tilt=0)
            paste_scaled(canvas, spr, W / 2 + (i - 1) * 300, H * 0.775 + (1 - u) * 30, 1.0, u)
    if t > 1.8:
        u = ease_out((t - 1.8) / 0.35)
        co = text_sprite('1 – 4 PLAYER ONLINE CO-OP', 56, CREAM, stroke=9, tilt=0)
        paste_scaled(canvas, co, W / 2, H * 0.84 + (1 - u) * 30, 1.0, u)


def tap(canvas, seg, t):
    """A finger tap: dot and expanding ring at (x, y) of the frame."""
    tp = seg.get('tap')
    if not tp or not (tp[2] <= t < tp[2] + 0.45):
        return
    u = (t - tp[2]) / 0.45
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x, y = tp[0] * W, tp[1] * H
    r = 30 + 110 * ease_out(u)
    d.ellipse([x - r, y - r, x + r, y + r], outline=(255, 255, 255, int(230 * (1 - u))), width=12)
    r2 = 46 * (1 - 0.4 * u)
    d.ellipse([x - r2, y - r2, x + r2, y + r2], fill=(255, 255, 255, int(190 * (1 - u))))
    canvas.alpha_composite(layer)


def timeline():
    t = 0.0
    out = []
    for seg in EDIT:
        out.append((t, seg))
        t += seg['dur']
    return out, t


def render_frame(tl, total, n):
    T = n / FPS
    start, seg = tl[-1]
    for st, sg in tl:
        if st <= T < st + sg['dur']:
            start, seg = st, sg
            break
    t = T - start
    if seg.get('black'):
        canvas = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    else:
        canvas = source(seg, t).convert('RGBA')
    if seg.get('end'):
        end_card(canvas, seg, t)
    caption(canvas, seg, t)
    tap(canvas, seg, t)
    a = np.asarray(canvas.convert('RGB'), np.float32)
    fl = seg.get('flash', 0.0)
    if fl and t < 0.14:
        a = a + (255 - a) * fl * (1 - t / 0.14)
    hit = seg.get('hit')
    if hit is not None and hit <= t < hit + 0.20:
        a = a + (255 - a) * 0.9 * (1 - (t - hit) / 0.20)
    if T > total - 0.5:
        a = a * max(0.0, (total - T) / 0.5)
    if seg.get('fade_in') and t < seg['fade_in']:
        a = a * (t / seg['fade_in'])
    return np.clip(a, 0, 255).astype(np.uint8)


# ---------------------------------------------------------------- sound
def load(rel, gain=1.0):
    p = os.path.join(SFX, rel)
    raw = subprocess.run([FFMPEG, '-v', 'error', '-i', p, '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-'],
                         stdout=subprocess.PIPE, check=True).stdout
    return np.frombuffer(raw, np.float32).reshape(-1, 2) * gain


def make_audio(total, path):
    n = int(total * SR)
    mix = np.zeros((n + SR, 2), np.float32)

    def put(clip, at, gain=1.0, length=None, fade=0.05, pan=0.0, rate=1.0):
        c = clip
        if rate != 1.0:
            idx = np.arange(0, len(c) - 1, rate)
            c = np.stack([np.interp(idx, np.arange(len(c)), c[:, k]) for k in (0, 1)], 1).astype(np.float32)
        if length is not None:
            m = int(length * SR)
            while len(c) < m:
                c = np.concatenate([c, c])
            c = c[:m].copy()
        else:
            c = c.copy()
        f = min(int(fade * SR), len(c) // 2)
        if f > 0:
            c[:f] *= np.linspace(0, 1, f)[:, None]
            c[-f:] *= np.linspace(1, 0, f)[:, None]
        c[:, 0] *= gain * (1 - max(0, pan))
        c[:, 1] *= gain * (1 + min(0, pan))
        s = int(at * SR)
        e = min(len(mix), s + len(c))
        if s < len(mix) and s >= 0:
            mix[s:e] += c[:e - s]

    for cue in CUES:
        put(load(cue['f']), cue['at'], cue.get('g', 1.0), cue.get('len'), cue.get('fade', 0.03), cue.get('pan', 0.0), cue.get('rate', 1.0))
    mix = mix[:n]
    f = int(0.5 * SR)
    mix[-f:] *= np.linspace(1, 0, f)[:, None]
    peak = np.abs(mix).max()
    mix = np.tanh(mix / max(peak, 1e-6) * 3.2) / math.tanh(3.2) * 0.84       # soft limiter
    with wave.open(path, 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((mix * 32767).astype(np.int16).tobytes())


def main():
    tl, total = timeline()
    frames = int(round(total * FPS))
    print('trailer length %.2f s, %d frames' % (total, frames))
    if len(sys.argv) > 1 and sys.argv[1] == 'preview':
        TILE = (270, 480) if H > W else (640, 360)
        tiles = []
        for start, seg in tl:
            for u in ((0.5,) if seg['dur'] < 1.2 else (0.3, 0.8)):
                tiles.append(Image.fromarray(render_frame(tl, total, int((start + seg['dur'] * u) * FPS))).resize(TILE, Image.BILINEAR))
        cols = 10 if H > W else 4
        sheet = Image.new('RGB', (TILE[0] * cols, TILE[1] * math.ceil(len(tiles) / cols)))
        for i, tile in enumerate(tiles):
            sheet.paste(tile, ((i % cols) * TILE[0], (i // cols) * TILE[1]))
        sheet.save(os.path.join(OUT, 'preview_' + OUT_NAME + '.jpg'), quality=88)
        return
    wav = os.path.join(OUT, OUT_NAME + '_audio.wav')
    make_audio(total, wav)
    if len(sys.argv) > 1 and sys.argv[1] == 'audio':
        return
    out = os.path.join(OUT, OUT_NAME + '.mp4')
    ff = subprocess.Popen([FFMPEG, '-y', '-v', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (W, H), '-r', str(FPS),
                           '-i', '-', '-i', wav, '-c:v', 'libx264', '-preset', 'slow', '-crf', '15', '-pix_fmt', 'yuv420p',
                           '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709',
                           '-c:a', 'aac', '-b:a', '256k', '-movflags', '+faststart', '-shortest', out], stdin=subprocess.PIPE)
    for n in range(frames):
        ff.stdin.write(render_frame(tl, total, n).tobytes())
        if n % 120 == 0:
            print('frame', n, flush=True)
    ff.stdin.close()
    ff.wait()
    print('wrote', out)


if __name__ == '__main__':
    main()
