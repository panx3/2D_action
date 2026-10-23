"""Extract aligned sprite layers from the approved sheets, without repainting them."""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np

root = Path(__file__).resolve().parents[2]
sources = root / 'output/imagegen/stone-sf-gate-20261023'
dest = root / 'Assets/Art/Gimmicks/StoneGate'
dest.mkdir(parents=True, exist_ok=True)

def clean(im):
    a = np.array(im.convert('RGBA')).copy()
    a[:, :, 3] = np.where(a[:, :, 3] >= 220, 255, 0)
    a[a[:, :, 3] == 0] = 0
    return Image.fromarray(a)

def save(im, name):
    im.resize((im.width // 4, im.height // 4), Image.Resampling.NEAREST).save(dest / name)

gate = Image.open(sources / 'Gate_ClosedOpen_EyeLevel.png').convert('RGBA')
frame = clean(gate.crop((768, 128, 1536, 876)))
closed = clean(gate.crop((0, 128, 768, 876)))
front_mask = Image.new('L', frame.size)
d = ImageDraw.Draw(front_mask)
d.rectangle((0, 0, 767, 221), fill=255)
d.polygon([(560, 200), (767, 200), (767, 747), (516, 747), (550, 700)], fill=255)
f = np.array(frame)
m = np.array(front_mask) > 0
back, front = f.copy(), f.copy()
back[m] = 0
front[~m] = 0
save(Image.fromarray(back), 'Gate_Back.png')
save(Image.fromarray(front), 'Gate_Front.png')
field_mask = Image.new('L', frame.size)
ImageDraw.Draw(field_mask).polygon([(251, 218), (517, 218), (565, 256), (565, 704), (517, 747), (248, 747), (202, 704), (202, 261)], fill=255)
b = np.array(closed)
keep = (np.array(field_mask) > 0) & (f[:, :, 3] == 0)
b[~keep] = 0
b[keep, 3] = (b[keep, 3].astype(float) * 0.80).astype('uint8')
save(Image.fromarray(b), 'Gate_Membrane.png')

switch = Image.open(sources / 'PressureSwitch_RaisedPressed.png').convert('RGBA')
off = clean(switch.crop((0, 344, 768, 736)))
on = clean(switch.crop((768, 344, 1536, 736)))
save(off, 'Switch_Raised.png')
save(on, 'Switch_Pressed.png')
a = np.array(off)
yy, xx = np.indices(a.shape[:2])
base = a.copy()
base[(yy < 210) | ((xx >= 278) & (xx <= 510) & (yy < 272))] = 0
cap = a.copy()
cap[yy >= 174] = 0
piston = a.copy()
piston[(yy < 174) | (yy >= 276) | (xx < 278) | (xx > 510)] = 0
save(Image.fromarray(base), 'Switch_Base.png')
save(Image.fromarray(cap), 'Switch_Cap.png')
save(Image.fromarray(piston), 'Switch_Piston.png')
print('Wrote 8 aligned sprites:', dest)
