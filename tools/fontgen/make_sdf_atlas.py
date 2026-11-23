#!/usr/bin/env python3
"""Bake a signed-distance-field font atlas for Spatail's world-space text.

Usage: make_sdf_atlas.py <font.otf> <name> <out-dir>
Writes <name>_atlas.bytes (8-bit greyscale PNG; .bytes so Unity keeps it as raw data) and <name>_metrics.json.
Requires pillow, numpy, scipy, fonttools.

The runtime (SpatialText.cs) reads glyph quads in em units and samples distance from the atlas, so one atlas
stays crisp at any size and distance.
"""
import json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage
from fontTools.ttLib import TTFont

EM = 64          # glyph em size in atlas pixels
RANGE = 8        # distance range in atlas pixels on each side of the outline
SUPER = 4        # supersampling factor when rasterising
SIZE = 1024      # atlas is SIZE x SIZE
CHARS = [chr(c) for c in range(32, 127)] + list("°²³×·•–—…‘’“”←→↑↓✓")

def main():
    font_path, name, out_dir = sys.argv[1:4]
    cmap = TTFont(font_path).getBestCmap()
    chars = [c for c in CHARS if ord(c) in cmap]
    missing = [c for c in CHARS if ord(c) not in cmap]
    if missing:
        print("not in font, skipped:", " ".join(missing))

    big = ImageFont.truetype(font_path, EM * SUPER)
    ascent, descent = big.getmetrics()
    atlas = np.zeros((SIZE, SIZE), dtype=np.uint8)
    glyphs = {}
    pen_x, pen_y, row_h = 1, 1, 0
    pad = RANGE

    for ch in chars:
        advance = big.getlength(ch) / (EM * SUPER)
        left, top, right, bottom = big.getbbox(ch)   # relative to the pen origin at the ascender line
        w, h = right - left, bottom - top
        if w <= 0 or h <= 0:
            glyphs[str(ord(ch))] = {"a": round(advance, 4)}
            continue
        margin = pad * SUPER
        img = Image.new("L", (w + 2 * margin, h + 2 * margin), 0)
        ImageDraw.Draw(img).text((margin - left, margin - top), ch, font=big, fill=255)
        inside = np.asarray(img) > 127
        dist = ndimage.distance_transform_edt(inside) - ndimage.distance_transform_edt(~inside)
        # Box-filter down to atlas resolution.
        H = (dist.shape[0] // SUPER) * SUPER
        W = (dist.shape[1] // SUPER) * SUPER
        small = dist[:H, :W].reshape(H // SUPER, SUPER, W // SUPER, SUPER).mean(axis=(1, 3)) / SUPER
        cell = np.clip(0.5 + small / (2 * RANGE), 0, 1)
        ch_h, ch_w = cell.shape
        if pen_x + ch_w + 1 > SIZE:
            pen_x, pen_y, row_h = 1, pen_y + row_h + 1, 0
        if pen_y + ch_h + 1 > SIZE:
            raise SystemExit("atlas is full; raise SIZE or lower EM")
        atlas[pen_y:pen_y + ch_h, pen_x:pen_x + ch_w] = (cell * 255 + 0.5).astype(np.uint8)
        # Plane bounds in em units, relative to the baseline, y up. They include the distance-range padding.
        glyphs[str(ord(ch))] = {
            "a": round(advance, 4),
            "l": round((left / SUPER - pad) / EM, 4),
            "r": round((left / SUPER - pad + ch_w) / EM, 4),
            "t": round((ascent / SUPER - (top / SUPER - pad)) / EM, 4),
            "b": round((ascent / SUPER - (top / SUPER - pad) - ch_h) / EM, 4),
            # UVs with v up (Unity convention): v0 is the bottom edge of the glyph cell.
            "u0": round(pen_x / SIZE, 5), "u1": round((pen_x + ch_w) / SIZE, 5),
            "v0": round(1 - (pen_y + ch_h) / SIZE, 5), "v1": round(1 - pen_y / SIZE, 5),
        }
        pen_x += ch_w + 1
        row_h = max(row_h, ch_h)

    used = pen_y + row_h + 1
    print(f"{name}: {len(glyphs)} glyphs, {used}/{SIZE} rows used")
    Image.fromarray(atlas, "L").save(f"{out_dir}/{name}_atlas.bytes", format="PNG", optimize=True)
    metrics = {
        "name": name, "em": EM, "range": RANGE, "size": SIZE,
        "ascender": round(ascent / SUPER / EM, 4), "descender": round(descent / SUPER / EM, 4),
        "lineHeight": round((ascent + descent) / SUPER / EM * 1.08, 4),
        "capHeight": round(-big.getbbox("H")[1] / SUPER / EM + ascent / SUPER / EM, 4),
        "glyphs": glyphs,
    }
    with open(f"{out_dir}/{name}_metrics.json", "w") as f:
        json.dump(metrics, f, separators=(",", ":"))

if __name__ == "__main__":
    main()
