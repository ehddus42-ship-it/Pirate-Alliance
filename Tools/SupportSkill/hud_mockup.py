"""Draw the support skill HUD card over a gameplay capture with the same layout numbers as SupportCharacterSkill.cs
(reference resolution 1280 x 720, anchored bottom-left). Output: Documentation/Liminal/SupportSkill/HUD_Mockup.png.
The cooldown state shown is a mid-cooldown frame (7 s left) while 1UP has 3.2 s left."""
import pathlib
from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
shot = Image.open(ROOT / 'Documentation/Liminal/Previews/Backrooms/Play_Combat.png').convert('RGBA')
W, H = shot.size
k = min(W / 1280, H / 720) ** .5 * max(W / 1280, H / 720) ** .5  # CanvasScaler match 0.5
font = str(ROOT / 'Assets/Liminal/Art/Fonts/NotoSansKR-Regular.ttf')
layer = Image.new('RGBA', shot.size, (0, 0, 0, 0))
d = ImageDraw.Draw(layer)

def c(rgb, a=1): return tuple(int(v * 255) for v in rgb) + (int(a * 255),)
PANEL, PAPER, ACCENT, TUBE, WALL = (.045, .075, .075), (.89, .91, .82), (.72, .86, .55), (.97, .92, .62), (.78, .71, .43)

def box(x, y, w, h):  # UI coords: bottom-left origin, reference pixels
    return (x * k, H - (y + h) * k, (x + w) * k, H - y * k)

def text(x, y, w, h, s, size, color, align='l', anchor_v='m'):
    f = ImageFont.truetype(font, int(size * k))
    x0, y0, x1, y1 = box(x, y, w, h)
    lines = s.split('\n')
    lh = size * k * 1.2
    top = (y0 + y1) / 2 - lh * len(lines) / 2 if anchor_v == 'm' else y0
    for i, line in enumerate(lines):
        tw = d.textlength(line, font=f)
        tx = x0 if align == 'l' else x1 - tw if align == 'r' else (x0 + x1 - tw) / 2
        d.text((tx, top + i * lh), line, font=f, fill=c(color))

X, Y = 22, 58
d.rectangle(box(X, Y, 338, 164), fill=c(PANEL, .92))
d.rectangle(box(X, Y + 161, 338, 3), fill=c(TUBE))
# portrait window with wallpaper stripes
px0, py0, px1, py1 = [int(v) for v in box(X + 8, Y + 8, 142, 150)]
wall = Image.new('RGBA', (px1 - px0, py1 - py0), c(WALL))
wd = ImageDraw.Draw(wall)
for sx in range(0, wall.width, max(2, int(16 * k))):
    wd.rectangle((sx, 0, sx + max(1, int(2 * k)), wall.height), fill=c(tuple(v * .9 for v in WALL)))
portrait = Image.open(ROOT / 'Assets/Liminal/Resources/SupportSkill/support_yuni_portrait.png').convert('RGBA')
aw, ah = int(250 * k), int(313 * k)
art = portrait.resize((aw, ah), Image.LANCZOS)
# Illustration rect at (-50, -140) inside the frame, size 250 x 313 (pivot bottom-left)
ox, oy = int(-50 * k), wall.height - int((-140 + 313) * k)
wall.alpha_composite(art, (ox, oy)) if ox >= 0 and oy >= 0 else wall.paste(art, (ox, oy), art)
layer.alpha_composite(wall, (px0, py0))
d.rectangle(box(X + 8, Y + 8, 142, 4), fill=c(ACCENT))
d.rectangle(box(X + 8, Y + 12, 142, 24), fill=c((.03, .05, .05), .82))
text(X + 16, Y + 12, 70, 24, 'SUPPORT', 12, ACCENT)
text(X + 70, Y + 12, 74, 24, '유니', 15, PAPER, 'r')
text(X + 160, Y + 126, 172, 28, '보너스 스테이지!', 17, ACCENT)
text(X + 160, Y + 94, 172, 34, '운석 1회 · 1UP 동안\n공격마다 추가 투사체', 12, PAPER, anchor_v='t')
# skill slot
d.rectangle(box(X + 160, Y + 16, 72, 72), fill=c((.3, .36, .32)))
d.rectangle(box(X + 163, Y + 19, 66, 66), fill=c((.02, .03, .035)))
icon = Image.new('RGBA', (16, 16))
for yy in range(16):
    for xx in range(16):
        dx, dy = xx - 6.5, (15 - yy) - 7.5
        body = dx * dx + dy * dy <= 36
        mouth = dx > 0 and abs(dy) < dx * .75
        if (xx, 15 - yy) == (6, 11): icon.putpixel((xx, yy), (0, 0, 0, 255))
        elif body and not mouth: icon.putpixel((xx, yy), (255, 219, 41, 255))
        elif xx in (13, 14) and (15 - yy) in (7, 8): icon.putpixel((xx, yy), c(TUBE))
icon = icon.resize((int(40 * k), int(40 * k)), Image.NEAREST)
ix0, iy0, _, _ = box(X + 176, Y + 24, 40, 40)
layer.alpha_composite(icon, (int(ix0), int(iy0)))
text(X + 163, Y + 66, 66, 18, '1UP', 12, (.55, 1, .45), 'c')
# radial cooldown shade (7 of 16 s left)
sx0, sy0, sx1, sy1 = box(X + 163, Y + 19, 66, 66)
shade = Image.new('RGBA', layer.size, (0, 0, 0, 0))
ImageDraw.Draw(shade).pieslice((sx0 - 20 * k, sy0 - 20 * k, sx1 + 20 * k, sy1 + 20 * k), -90 - 360 * 7 / 16, -90, fill=(0, 0, 0, 184))
mask = Image.new('L', layer.size, 0); ImageDraw.Draw(mask).rectangle((sx0, sy0, sx1, sy1), fill=255)
clipped = Image.new('RGBA', layer.size, (0, 0, 0, 0)); clipped.paste(shade, (0, 0), mask)
layer.alpha_composite(clipped)
text(X + 163, Y + 19, 66, 66, '7', 22, PAPER, 'c')
d.rectangle(box(X + 210, Y + 8, 28, 22), fill=c(TUBE))
text(X + 210, Y + 8, 28, 22, 'Q', 15, (.06, .07, .06), 'c')
text(X + 242, Y + 60, 92, 26, '추가 투사체', 13, (.55, 1, .45))
text(X + 242, Y + 36, 92, 20, '1UP 3.2s', 12, PAPER)
d.rectangle(box(X + 242, Y + 26, 88, 8), fill=c((.22, .26, .23)))
d.rectangle(box(X + 242, Y + 26, 88 * 3.2 / 5, 8), fill=c((.55, 1, .45)))
shot.alpha_composite(layer)
out = ROOT / 'Documentation/Liminal/SupportSkill/HUD_Mockup.png'
shot.convert('RGB').save(out)
print('wrote', out)
