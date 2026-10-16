"""Generate sgm.ico (app/exe icon) — run once; the .ico is committed."""
from pathlib import Path

from PIL import Image, ImageDraw

BG = "#1b2838"
BLUE = "#66c0f4"
size = 256
img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle([0, 0, size - 1, size - 1], radius=54, fill=BG)
# controller-ish: two grips + a plus in the middle
d.rounded_rectangle([26, 84, 230, 172], radius=44, fill=BLUE)
d.ellipse([30, 92, 110, 176], fill=BG)
d.ellipse([146, 92, 226, 176], fill=BG)
d.rounded_rectangle([118, 96, 138, 160], radius=8, fill=BG)
d.rounded_rectangle([92, 118, 164, 138], radius=8, fill=BG)
img.save(Path(__file__).with_name("sgm.ico"),
         sizes=[(256, 256), (64, 64), (48, 48), (32, 32), (16, 16)])
print("wrote sgm.ico")
