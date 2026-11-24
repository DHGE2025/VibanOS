#!/usr/bin/env python3
"""Erzeugt die Schrift-Atlanten (VibanOS/Resources/*.vfont) aus Inter und JetBrains Mono (beide SIL OFL).

Aufruf:  python3 tools/make-fonts.py [schriftordner]
Braucht Pillow. Nur noetig, wenn eine Schriftgroesse im Designsheet geaendert wird.

Format: [Zeilenhoehe][Ascent][erstes Zeichen][Anzahl], dann je Zeichen
[Vorschub in 1/4 px][Breite][Hoehe][x-Versatz sbyte][y-Versatz sbyte] + Breite*Hoehe Alphawerte.
"""
import glob, struct, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = sys.argv[1] if len(sys.argv) > 1 else "/usr/share/fonts"
FIRST, COUNT = 32, 224  # Latin-1, deckt deutsche Umlaute ab

# Rolle -> (Schriftdatei, Groesse in px). Muss zu docs/design/designsheet.md passen.
ROLES = {
    "body":    ("Inter-Regular.otf", 13),
    "strong":  ("Inter-SemiBold.otf", 13),
    "caption": ("Inter-Medium.otf", 11),
    "display": ("InterDisplay-SemiBold.otf", 26),
    "mono":    ("JetBrainsMono-Regular.ttf", 13),
}

for role, (file, size) in ROLES.items():
    path = glob.glob(f"{ROOT}/**/{file}", recursive=True)[0]
    font = ImageFont.truetype(path, size, layout_engine=ImageFont.Layout.BASIC)
    ascent, descent = font.getmetrics()
    out = bytearray(struct.pack("BBBB", ascent + descent, ascent, FIRST, COUNT))
    for code in range(FIRST, FIRST + COUNT):
        ch = chr(code)
        adv = min(255, round(font.getlength(ch) * 4))
        l, t, r, b = font.getbbox(ch)
        w, h = max(0, r - l), max(0, b - t)
        if code in range(127, 161) or w == 0 or h == 0:  # Steuerzeichen und Leerzeichen
            out += struct.pack("BBBbb", 0 if code in range(127, 160) else adv, 0, 0, 0, 0)
            continue
        img = Image.new("L", (w, h), 0)
        ImageDraw.Draw(img).text((-l, -t), ch, font=font, fill=255)
        out += struct.pack("BBBbb", adv, w, h, l, t) + img.tobytes()
    open(f"VibanOS/Resources/{role}.vfont", "wb").write(out)
    print(f"{role}: {file} {size}px -> {len(out)} Bytes")
