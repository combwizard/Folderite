"""Draws the Folderite icon and writes assets/icon.ico, icon-256.png and preview.png.

Run: python assets/make_icon.py   (needs Pillow)
Each size is drawn separately at 4x and reduced with LANCZOS; the ICO is written by hand.
"""
import io
import os
import struct

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
SS = 4                                          # supersampling factor

BACK = ((0xE8, 0xA3, 0x17), (0xF2, 0xB6, 0x32))   # back panel + tab, top -> bottom
FRONT = ((0xFF, 0xCF, 0x4A), (0xFF, 0xD9, 0x66))  # front panel, top -> bottom
BADGE = ((0x0A, 0x6C, 0xD6), (0x3D, 0x9B, 0xFF))  # badge gradient, bottom-left -> top-right feel via vertical
RIM = (0x07, 0x4E, 0x9E)
WHITE = (255, 255, 255)
LIGHT_BG, DARK_BG = (0xF3, 0xF3, 0xF3), (0x20, 0x20, 0x20)


def layout(n):
    """Shapes in final-pixel units: rects are (x0, y0, x1, y1, radius)."""
    # Hand-tuned tiny sizes: integer coordinates, chunky shapes, no shadow / rim.
    if n == 16:
        return dict(tab=(1, 1, 7, 4, 1), back=(1, 2, 14, 13, 1), front=(1, 4, 14, 14, 1),
                    badge=(10.5, 10.5, 4.5),
                    lines=[(8, 8, 11, 9), (8, 10, 13, 11), (8, 12, 13, 13)], shadow=0, rim=0)
    if n == 20:
        return dict(tab=(1, 2, 9, 5, 1.5), back=(1, 3, 17, 16, 1.5), front=(1, 6, 17, 17, 1.5),
                    badge=(13.5, 13.5, 5.5),
                    lines=[(11, 11, 14, 12), (11, 13, 16, 14), (11, 15, 16, 16)], shadow=0, rim=0)
    if n == 24:
        return dict(tab=(1, 2, 10, 6, 1.5), back=(1, 4, 21, 19, 2), front=(1, 7, 21, 20, 2),
                    badge=(16, 16, 7),
                    lines=[(12, 11, 16, 13), (12, 15, 20, 17), (12, 19, 20, 21)], shadow=0, rim=0)
    # Larger sizes: proportional layout; snapped to whole pixels while still small.
    snap = (lambda v: round(v)) if n <= 40 else (lambda v: v)
    u = lambda v: snap(v * n)
    t = max(1.0, snap(0.052 * n)) if n <= 48 else 0.052 * n     # menu line thickness
    gap = max(2.0, snap(0.086 * n)) if n <= 48 else 0.086 * n   # line pitch
    cx, cy, r = u(0.69), u(0.68), u(0.265)
    x0, ln = cx - 0.145 * n, 0.29 * n
    x0 = snap(x0)
    lines = []
    for i, frac in enumerate((0.58, 1.0, 1.0)):
        y = cy - gap + i * gap - t / 2
        lines.append((x0, y, x0 + snap(ln * frac), y + t))
    return dict(tab=(u(0.06), u(0.10), u(0.44), u(0.29), 0.05 * n),
                back=(u(0.06), u(0.18), u(0.94), u(0.79), 0.07 * n),
                front=(u(0.06), u(0.29), u(0.94), u(0.81), 0.07 * n),
                badge=(cx, cy, r), lines=lines,
                shadow=n >= 48, rim=(0 if n < 48 else (1 if n < 128 else 2)))


def gradient_layer(S, y0, y1, c0, c1, mask):
    """Vertical gradient c0 -> c1 between y0 and y1, clipped by mask."""
    y0, y1 = int(y0), max(int(y1), int(y0) + 1)
    grad = Image.new("L", (S, S), 0)
    grad.paste(Image.linear_gradient("L").resize((S, y1 - y0)), (0, y0))
    grad.paste(255, (0, y1, S, S))
    col = Image.composite(Image.new("RGBA", (S, S), c1 + (255,)), Image.new("RGBA", (S, S), c0 + (255,)), grad)
    col.putalpha(mask)
    return col


def rrect_mask(S, rect, rad=None):
    m = Image.new("L", (S, S), 0)
    x0, y0, x1, y1, r = rect
    ImageDraw.Draw(m).rounded_rectangle([x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1], r * SS, fill=255)
    return m


def disc_mask(S, cx, cy, r):
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS - 1, (cy + r) * SS - 1], fill=255)
    return m


def draw(n):
    S = n * SS
    L = layout(n)
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # back panel + tab (one silhouette)
    back = rrect_mask(S, L["back"])
    back.paste(rrect_mask(S, L["tab"]), mask=rrect_mask(S, L["tab"]))
    im.alpha_composite(gradient_layer(S, L["tab"][1] * SS, L["back"][3] * SS, *BACK, back))

    front = rrect_mask(S, L["front"])
    if L["shadow"]:
        sh = Image.new("L", (S, S), 0)
        sh.paste(front.point(lambda v: v * 0.30), (0, int(0.018 * S)))
        sh = sh.filter(ImageFilter.GaussianBlur(0.02 * S))
        shadow = Image.new("RGBA", (S, S), (90, 50, 0, 255))
        shadow.putalpha(sh)
        im.alpha_composite(shadow)
    im.alpha_composite(gradient_layer(S, L["front"][1] * SS, L["front"][3] * SS, *FRONT, front))

    # badge
    cx, cy, r = L["badge"]
    if L["shadow"]:
        bs = disc_mask(S, cx, cy + 0.012 * n, r).filter(ImageFilter.GaussianBlur(0.015 * S))
        bs = bs.point(lambda v: v * 0.35)
        sh = Image.new("RGBA", (S, S), (0, 20, 60, 255))
        sh.putalpha(bs)
        im.alpha_composite(sh)
    rim = L["rim"]
    if rim:
        im.alpha_composite(gradient_layer(S, (cy - r) * SS, (cy + r) * SS, RIM, RIM, disc_mask(S, cx, cy, r)))
    # blue runs light at the top, deep at the bottom
    im.alpha_composite(gradient_layer(S, (cy - r) * SS, (cy + r) * SS, BADGE[1], BADGE[0],
                                      disc_mask(S, cx, cy, r - rim)))

    # menu lines
    d = ImageDraw.Draw(im)
    for x0, y0, x1, y1 in L["lines"]:
        rad = min((y1 - y0) / 2, 1e3) * SS if (y1 - y0) >= 1.5 else 0
        d.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1], rad, fill=WHITE + (255,))
    # pixel-snapped tiny sizes use BOX: LANCZOS ringing would soften the aligned strokes
    return im.resize((n, n), Image.BOX if n <= 40 else Image.LANCZOS)


def write_ico(path, frames):
    """ICONDIR + ICONDIRENTRYs + PNG payloads (size byte 0 means 256)."""
    blobs = []
    for f in frames:
        buf = io.BytesIO()
        f.save(buf, format="PNG")
        blobs.append(buf.getvalue())
    out = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    for f, b in zip(frames, blobs):
        w = f.width if f.width < 256 else 0
        out += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(b), offset)
        offset += len(b)
    with open(path, "wb") as fh:
        fh.write(out + b"".join(blobs))


def make_preview(frames):
    pad, zoom = 16, 8
    row_w = sum(f.width for f in frames) + pad * (len(frames) + 1)
    zs = [f for f in frames if f.width in (16, 24, 32)]
    zrow_w = sum(f.width * zoom for f in zs) + pad * (len(zs) + 1)
    W = max(row_w, zrow_w * 2)
    H1, H2 = 256 + 2 * pad, 256 + 2 * pad
    sheet = Image.new("RGBA", (W, 2 * H1 + H2), LIGHT_BG + (255,))
    for row, bg in enumerate((LIGHT_BG, DARK_BG)):
        sheet.paste(bg + (255,), (0, row * H1, W, (row + 1) * H1))
        x = pad
        for f in frames:
            sheet.alpha_composite(f, (x, row * H1 + pad + (256 - f.height) // 2))
            x += f.width + pad
    for half, bg in enumerate((LIGHT_BG, DARK_BG)):
        x = half * (W // 2) + pad
        sheet.paste(bg + (255,), (half * (W // 2), 2 * H1, (half + 1) * (W // 2), 2 * H1 + H2))
        for f in zs:
            tile = Image.new("RGBA", f.size, bg + (255,))
            tile.alpha_composite(f)
            big = tile.resize((f.width * zoom, f.height * zoom), Image.NEAREST)
            sheet.paste(big, (x, 2 * H1 + pad + (256 - big.height) // 2))
            x += big.width + pad
    sheet.convert("RGB").save(os.path.join(HERE, "preview.png"))


def main():
    frames = [draw(s) for s in SIZES]
    write_ico(os.path.join(HERE, "icon.ico"), frames)
    frames[-1].save(os.path.join(HERE, "icon-256.png"))
    make_preview(frames)
    print("wrote icon.ico, icon-256.png, preview.png")


if __name__ == "__main__":
    main()
