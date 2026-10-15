"""Turn build.py --preview renders into the documentation images.

  python3 Tools/StageConcepts/variations/catalog.py RENDERS

RENDERS holds <room>_play1..3.png and <room>_overview.png. Writes, under Documentation/StageConcepts/Previews/Variations:
  <room>.jpg            the mid-room gameplay camera (z = 18), 1280 x 800, labelled
  <Theme>_Catalog.jpg   the seven overviews of a theme plus a legend tile
Labels use Assets/Liminal/Art/Fonts/NotoSansKR-Regular.ttf (run `git lfs pull` first).
"""
import json
import pathlib
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / 'Documentation/StageConcepts/Previews/Variations'
LAYOUTS = ROOT / 'Assets/StageConcepts/Layouts'
FONT = ROOT / 'Assets/Liminal/Art/Fonts/NotoSansKR-Regular.ttf'
THEMES = {'Forest': '판타지 숲 · 달빛 고목의 숲', 'Digital': '프로그램 감옥', 'Ruins': '멸망한 지구', 'Cave': '심연의 수정 동굴'}
KIND = {'Arrival': '도착', 'Combat': '전투', 'Threshold': '출구', 'Exploration': '탐색', 'Boss': '보스'}
ROUTE = {1: '시작 고정', 5: '끝 고정'}


def font(size):
    return ImageFont.truetype(str(FONT), size)


def label(image, lines, size=26, pad=14):
    draw = ImageDraw.Draw(image, 'RGBA')
    fonts = [font(size), font(int(size * 0.72))]
    widths = [draw.textbbox((0, 0), line, font=fonts[min(i, 1)])[2] for i, line in enumerate(lines)]
    heights = [int(size * 1.35)] + [int(size * 1.0)] * (len(lines) - 1)
    draw.rectangle((0, 0, max(widths) + pad * 2, sum(heights) + pad * 1.6), fill=(8, 12, 16, 178))
    y = pad * 0.8
    for i, line in enumerate(lines):
        draw.text((pad, y), line, font=fonts[min(i, 1)], fill=(236, 242, 238, 255) if i == 0 else (182, 198, 196, 255))
        y += heights[i]


def room_info(room_id):
    layout = json.loads((LAYOUTS / f'{room_id}.json').read_text(encoding='utf-8'))
    index = int(room_id.split('_')[1])
    role = ROUTE.get(index, '중간 후보')
    return layout['displayName'], f"{KIND.get(layout['kind'], layout['kind'])} · {role}"


def main(renders):
    renders = pathlib.Path(renders)
    OUT.mkdir(parents=True, exist_ok=True)
    for theme, title in THEMES.items():
        tiles = []
        for index in range(1, 8):
            room_id = f'{theme}_{index:02d}'
            name, role = room_info(room_id)
            play = Image.open(renders / f'{room_id}_play2.png').convert('RGB').resize((1280, 800), Image.LANCZOS)
            label(play, [f'{room_id}  {name}', role])
            play.save(OUT / f'{room_id}.jpg', quality=88, optimize=True, progressive=True)
            overview = Image.open(renders / f'{room_id}_overview.png').convert('RGB').resize((800, 500), Image.LANCZOS)
            label(overview, [f'{index:02d}  {name}', role], size=24, pad=12)
            tiles.append(overview)
        legend = Image.new('RGB', (800, 500), (14, 18, 22))
        draw = ImageDraw.Draw(legend)
        draw.text((40, 44), title, font=font(40), fill=(236, 242, 238))
        body = ['방 7종 · 한 판 5개', '01 도착 → 중간 3개 → 05 출구', '중간 후보: 02 · 03 · 04 · 06 · 07',
                '26 × 36.4m 방, 게임 카메라 거리 24m', '전체 시점: 거리 54m · 피치 48° · 요 32°']
        for i, line in enumerate(body):
            draw.text((40, 132 + i * 58), line, font=font(28), fill=(182, 198, 196))
        tiles.append(legend)
        sheet = Image.new('RGB', (3200, 1000), (0, 0, 0))
        for i, tile in enumerate(tiles):
            sheet.paste(tile, ((i % 4) * 800, (i // 4) * 500))
        sheet.save(OUT / f'{theme}_Catalog.jpg', quality=86, optimize=True, progressive=True)
        print('wrote', OUT / f'{theme}_Catalog.jpg')


if __name__ == '__main__':
    main(sys.argv[1])
