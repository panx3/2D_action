"""Read-only inspection of baton endpoints on calibrated native-size previews."""
from pathlib import Path
from PIL import Image

root = Path('Docs/Art/PlayerMotionV2/IdleThrowRevision/Preview')
# Each rectangle excludes the body; the last field identifies the end of the baton.
regions = {
    'ChargeIdleStable': [(0, 45, 125, 65, 145, 'minx'), (1, 45, 125, 65, 145, 'minx'),
                         (3, 40, 125, 65, 145, 'minx'), (7, 45, 125, 65, 145, 'minx')],
    'ThrowUpIdle': [(0, 30, 130, 50, 150, 'minx'), (1, 130, 20, 155, 45, 'miny'),
                    (2, 110, 0, 130, 25, 'miny'), (3, 30, 120, 55, 140, 'minx'),
                    (4, 130, 15, 150, 40, 'miny'), (5, 110, 0, 130, 25, 'miny')],
    'ThrowUpRightIdle': [(0, 73, 45, 95, 70, 'miny'), (1, 150, 45, 175, 70, 'maxx'),
                         (2, 150, 45, 175, 70, 'maxx'), (3, 73, 45, 95, 70, 'miny'),
                         (4, 150, 45, 175, 70, 'maxx'), (5, 150, 45, 175, 70, 'maxx')],
    'ThrowDownIdle': [(0, 70, 35, 90, 58, 'miny'), (1, 98, 145, 115, 165, 'maxy'),
                      (2, 80, 145, 100, 165, 'maxy'), (3, 70, 35, 90, 58, 'miny'),
                      (4, 100, 130, 115, 155, 'maxy'), (5, 80, 130, 100, 155, 'maxy')],
    'ThrowDownRightIdle': [(0, 55, 130, 75, 150, 'minx'), (1, 145, 115, 170, 140, 'maxx'),
                           (2, 130, 130, 155, 155, 'maxx'), (3, 40, 110, 65, 135, 'minx'),
                           (4, 145, 100, 170, 125, 'maxx'), (5, 135, 115, 160, 140, 'maxx')],
    'ThrowForwardAirIdle': [(0, 35, 105, 55, 125, 'minx'), (1, 155, 80, 180, 100, 'maxx'),
                            (2, 155, 80, 180, 100, 'maxx')],
}
for name, boxes in regions.items():
    im = Image.open(root / (name + '.png')).convert('RGBA')
    cols = im.width // 192
    print(name)
    for i, x0, y0, x1, y1, direction in boxes:
        ox, oy = i % cols * 192, i // cols * 192
        points = [(x, y) for y in range(y0, y1) for x in range(x0, x1)
                  if im.getpixel((ox + x, oy + y))[3] >= 192]
        if not points:
            print(i, 'EMPTY')
            continue
        axis = 0 if direction.endswith('x') else 1
        extreme = (min if direction.startswith('min') else max)(p[axis] for p in points)
        end = [p for p in points if p[axis] == extreme]
        print(i, direction, extreme, 'endpoint pixels', end)
