#!/usr/bin/env python3
"""Generate src/AppIcon.ico with the Python standard library only.

A blue rounded tile, a white context-menu card and an orange "remove" badge.
No Pillow / no external assets required, so the repo stays dependency-free.
"""

import os
import struct
import zlib

BASE = 256          # design coordinate system
SS = 2              # supersampling factor for smooth edges
SIZES = (256, 128, 64, 48, 32, 16)
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "AppIcon.ico")


def rounded(x, y, x0, y0, x1, y1, r):
    """True when point (x, y) lies inside the rounded rectangle."""
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    dx, dy = x - cx, y - cy
    if dx * dx + dy * dy <= r * r:
        return True
    # straight parts of the rectangle
    if x0 + r <= x <= x1 - r or y0 + r <= y <= y1 - r:
        return True
    return False


def line(x, y, x0, y0, x1, y1, w):
    """True when point is within a thick segment (used for the cross)."""
    vx, vy = x1 - x0, y1 - y0
    length2 = vx * vx + vy * vy
    if length2 == 0:
        return False
    t = ((x - x0) * vx + (y - y0) * vy) / length2
    t = max(0.0, min(1.0, t))
    px, py = x0 + t * vx, y0 + t * vy
    return (x - px) ** 2 + (y - py) ** 2 <= (w / 2.0) ** 2


def blend(buf, i, rgb, alpha):
    if alpha <= 0:
        return
    a = alpha / 255.0
    for c in range(3):
        buf[i + c] = int(buf[i + c] * (1 - a) + rgb[c] * a)
    buf[i + 3] = max(buf[i + 3], int(alpha))


def paint(size):
    """Render one RGBA image at `size` px using supersampling."""
    s = size * SS
    scale = s / float(BASE)
    buf = bytearray(s * s * 4)

    # geometry in design coordinates, scaled once
    def rr(x, y, box, r):
        return rounded(x / scale, y / scale, box[0], box[1], box[2], box[3], r)

    def ln(x, y, seg, w):
        return line(x / scale, y / scale, seg[0], seg[1], seg[2], seg[3], w)

    for py in range(s):
        row = py * s * 4
        for px in range(s):
            i = row + px * 4
            x, y = px + 0.5, py + 0.5

            # tile: vertical blue gradient
            if rr(x, y, (2, 2, 254, 254), 56):
                t = max(0.0, min(1.0, (y / scale) / 256.0))
                col = (
                    int(58 - 26 * t),
                    int(126 - 56 * t),
                    int(255 - 46 * t),
                )
                blend(buf, i, col, 255)
                # subtle top-left sheen
                if rr(x, y, (2, 2, 254, 110), 56):
                    blend(buf, i, (255, 255, 255), 16)

            # card drop shadow
            if rr(x, y, (48, 60, 198, 214), 18):
                blend(buf, i, (8, 24, 70), 70)

            # white menu card
            if rr(x, y, (44, 52, 194, 206), 16):
                blend(buf, i, (255, 255, 255), 255)

            # three menu rows: icon chip + text bar
            for top in (74, 108, 142):
                if rr(x, y, (62, top, 82, top + 18), 5):
                    blend(buf, i, (214, 228, 255), 255)
                if rr(x, y, (92, top + 6, 172, top + 12), 3):
                    blend(buf, i, (203, 216, 238), 255)

            # orange "remove" badge
            dx, dy = x / scale - 196, y / scale - 62
            if dx * dx + dy * dy <= 34 * 34:
                blend(buf, i, (255, 118, 62), 255)
                if ln(x, y, (183, 49, 209, 75), 7) or ln(x, y, (209, 49, 183, 75), 7):
                    blend(buf, i, (255, 255, 255), 255)

    # box downsample SS x SS -> 1 px
    out = bytearray(size * size * 4)
    n = SS * SS
    for oy in range(size):
        for ox in range(size):
            r = g = b = a = 0
            for sy in range(SS):
                src = ((oy * SS + sy) * s + ox * SS) * 4
                for sx in range(SS):
                    j = src + sx * 4
                    r += buf[j]
                    g += buf[j + 1]
                    b += buf[j + 2]
                    a += buf[j + 3]
            k = (oy * size + ox) * 4
            out[k:k + 4] = bytes((r // n, g // n, b // n, a // n))
    return bytes(out)


def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data +
            struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def png_bytes(size, rgba):
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw.append(0)  # filter: none
        raw.extend(rgba[y * stride:(y + 1) * stride])
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) +
            chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def main():
    images = [(size, png_bytes(size, paint(size))) for size in SIZES]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for size, data in images:
        entries += struct.pack("<BBBBHHII", 0 if size == 256 else size,
                               0 if size == 256 else size, 0, 0, 1, 32,
                               len(data), offset)
        blobs += data
        offset += len(data)
    target = os.path.abspath(OUT)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "wb") as fh:
        fh.write(header + entries + blobs)
    print("wrote %s (%d bytes)" % (target, os.path.getsize(target)))


if __name__ == "__main__":
    main()
