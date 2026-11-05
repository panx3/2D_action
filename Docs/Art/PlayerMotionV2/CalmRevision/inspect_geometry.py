"""Read-only geometry inspection. Does not alter or write image pixels."""
from PIL import Image
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[4]


def inspect(path, columns, rows):
    image = Image.open(ROOT / path).convert("RGBA")
    w, h = image.size
    data = image.load()
    result = []
    for index in range(columns * rows):
        x0, x1 = index % columns * w // columns, (index % columns + 1) * w // columns
        y0, y1 = index // columns * h // rows, (index // columns + 1) * h // rows
        opaque = [(x, y) for y in range(y0, y1) for x in range(x0, x1) if data[x, y][3] >= 192]
        if not opaque:
            continue
        left, right = min(p[0] for p in opaque), max(p[0] for p in opaque)
        top, bottom = min(p[1] for p in opaque), max(p[1] for p in opaque)
        skin = set()
        for x, y in opaque:
            r, g, b, a = data[x, y]
            if y < top + (bottom - top) * 0.67 and r > 180 and g > 130 and b > 110 and 0 <= r - g < 65 and g - b > -20 and r - b > 8:
                skin.add((x, y))
        components = []
        while skin:
            seed = skin.pop()
            pending, component = [seed], [seed]
            while pending:
                x, y = pending.pop()
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (-1, 1), (1, -1)):
                    p = (x + dx, y + dy)
                    if p in skin:
                        skin.remove(p)
                        pending.append(p)
                        component.append(p)
            components.append(component)
        components.sort(key=len, reverse=True)
        pink = {(x, y) for x, y in opaque if data[x,y][0] > 75 and data[x,y][0]-data[x,y][1] > 30
                and data[x,y][2] > data[x,y][1] and data[x,y][2] < data[x,y][0]*.98}
        pink_components = []
        while pink:
            pending = [pink.pop()]
            component = list(pending)
            while pending:
                x, y = pending.pop()
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(-1,-1),(-1,1),(1,-1)):
                    point = (x+dx,y+dy)
                    if point in pink:
                        pink.remove(point)
                        pending.append(point)
                        component.append(point)
            pink_components.append(component)
        pink_components.sort(key=len, reverse=True)
        pairs=[]
        for hair in pink_components[:8]:
            hx0,hy0,hx1,hy1=min(x for x,y in hair),min(y for x,y in hair),max(x for x,y in hair),max(y for x,y in hair)
            if hy0 > top+(bottom-top)*.45 or hy1-hy0 < (bottom-top)*.1:
                continue
            for face in components:
                fx0,fy0,fx1,fy1=min(x for x,y in face),min(y for x,y in face),max(x for x,y in face),max(y for x,y in face)
                if fx0 >= hx0-(hx1-hx0)*.1 and fx1 <= hx1+(hx1-hx0)*.4 and fy0 >= hy0+(hy1-hy0)*.3 and fy1 <= hy1+2:
                    pairs.append((len(face),hair,[fx0,fy0,fx1,fy1]))
        head=None
        if pairs:
            _,hair,face=max(pairs,key=lambda p:p[0])
            cx=(face[0]+face[2])*.5
            half=(face[2]-face[0])*.75
            pink_rows={}
            for x,y in hair:
                if abs(x-cx)<=half:
                    pink_rows[y]=pink_rows.get(y,0)+1
            crown=next((y for y in sorted(pink_rows) if pink_rows[y] >= max(3,half*1.4)
                        and pink_rows.get(y+1,0)>=max(3,half*1.4)),min(y for x,y in hair))
            head=dict(face=[v-(x0 if k%2==0 else y0) for k,v in enumerate(face)],
                      crown=crown-y0, height=face[3]-crown+1,
                      center=[cx-x0,(crown+face[3])*.5-y0])
        result.append(dict(index=index, bounds=[left-x0, top-y0, right-x0, bottom-y0], head=head,
                           faces=[dict(area=len(c), box=[min(p[0] for p in c)-x0, min(p[1] for p in c)-y0,
                                                         max(p[0] for p in c)-x0, max(p[1] for p in c)-y0])
                                  for c in components[:12]]))
    return dict(path=path, size=[w, h], frames=result)


if __name__ == "__main__":
    sources = [("Docs/Art/PlayerMotionV2/idle-reference.png", 4, 1),
               ("Assets/image_/run_test_run3-export.png", 6, 1)]
    for p in sorted((ROOT / "Docs/Art/PlayerMotionV2/CalmSources").glob("*.png")):
        sources.append((str(p.relative_to(ROOT)), 3 if "Throw" in p.stem or p.stem == "ChargeRun" else 4, 2))
    for name in ("ThrowForward", "Utility"):
        sources.append((f"Docs/Art/PlayerMotionV2/ProductionSources/{name}.png",3,2))
    for p in sorted((ROOT / "Assets/Art/Player/MotionV2").glob("*.png")):
        sources.append((str(p.relative_to(ROOT)), 2 if p.stem == "Hanging" else 4,
                        1 if p.stem in ("Hanging", "Climbing") else 2))
    print(json.dumps([inspect(*source) for source in sources], ensure_ascii=False, indent=2))
