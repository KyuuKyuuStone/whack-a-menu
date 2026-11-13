#!/usr/bin/env python3
"""Turn a generated logo PNG (solid white background) into a transparent multi-size ICO.

Only the Python standard library is used. White shapes INSIDE the artwork (a menu
card, sparkles, highlights) must stay opaque, so background removal is done by
scanline flood from the four image edges instead of a global white colour key.

A detailed mascot turns into mush at 16/32 px. Small icon sizes therefore use a
tight square crop around the character's face ("responsive icon" approach) while
64 px and above keep the full composition.

Usage:
    python tools/build_icon_from_image.py <source.png> [output.ico] [preview.png]
        [--recolor HEX]              optional flat-colour remap (logo v2)
        [--small-crop x,y,w,h]       relative face crop for small sizes
        [--mask circle|none]         circle = feathered round avatar for tight crops
                                     that are otherwise a solid square colour tile;
                                     none = keep the transparent margin (roomy art)
        [--sheet path.png]           light/dark contact sheet of every size
        [--embed path.png]           256px transparent PNG for embedding in the exe
"""
import os
import struct
import sys
import zlib

SIZES = (256, 128, 64, 48, 32, 16)
SMALL_SIZES = (64, 48, 32, 16)
# Default relative square around the mascot's head/face; override per artwork
# with --small-crop (the orange cat sits left of centre, earlier art was centred).
DEFAULT_SMALL_CROP = (0.27, 0.30, 0.46, 0.46)
WHITE_THRESHOLD = 244


# ------------------------------------------------------------------ PNG decode

def read_png(path):
    with open(path, "rb") as fh:
        data = fh.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG file")

    pos = 8
    idat = bytearray()
    width = height = bitdepth = colortype = interlace = 0
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if tag == b"IHDR":
            width, height, bitdepth, colortype, comp, filt, interlace = \
                struct.unpack(">IIBBBBB", payload)
        elif tag == b"IDAT":
            idat.extend(payload)
        elif tag == b"IEND":
            break

    if bitdepth != 8 or colortype not in (2, 6) or interlace != 0:
        raise ValueError("unsupported PNG: bitdepth=%s colortype=%s interlace=%s"
                         % (bitdepth, colortype, interlace))
    bpp = 3 if colortype == 2 else 4
    stride = width * bpp
    raw = zlib.decompress(bytes(idat))
    pixels = bytearray(width * height * 4)
    previous = bytearray(stride)
    offset = 0

    for y in range(height):
        filter_type = raw[offset]
        offset += 1
        line = bytearray(raw[offset:offset + stride])
        offset += stride

        if filter_type == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 255
        elif filter_type == 2:
            for i in range(stride):
                line[i] = (line[i] + previous[i]) & 255
        elif filter_type == 3:
            for i in range(stride):
                left = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((left + previous[i]) >> 1)) & 255
        elif filter_type == 4:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                b = previous[i]
                c = previous[i - bpp] if i >= bpp else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                predictor = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[i] = (line[i] + predictor) & 255
        elif filter_type != 0:
            raise ValueError("unknown PNG filter %d" % filter_type)

        row = y * width * 4
        if bpp == 3:
            for x in range(width):
                j = x * 3
                k = row + x * 4
                pixels[k] = line[j]
                pixels[k + 1] = line[j + 1]
                pixels[k + 2] = line[j + 2]
                pixels[k + 3] = 255
        else:
            pixels[row:row + stride] = line
        previous = line

    return width, height, pixels


# ------------------------------------------------------------------- recolour

def detect_base_blue(width, height, pixels):
    """Most frequent strongly-blue colour = the flat brand colour of the source art.

    Using the darkest pixel instead would pick a stray anti-aliased edge pixel and
    shift the whole remap, so a histogram is the robust choice.
    """
    histogram = {}
    for i in range(width * height):
        k = i * 4
        r, g, b, a = pixels[k], pixels[k + 1], pixels[k + 2], pixels[k + 3]
        if a < 250 or b < r + 40 or b < g + 20 or r > 130:
            continue
        key = (r, g, b)
        histogram[key] = histogram.get(key, 0) + 1
    if not histogram:
        return (11, 71, 168)
    return max(histogram, key=lambda color: histogram[color])


def recolour(width, height, pixels, target_hex):
    """Linear remap keeps anti-aliased edges correct and leaves white highlights white."""
    target = (int(target_hex[0:2], 16), int(target_hex[2:4], 16),
              int(target_hex[4:6], 16))
    base = detect_base_blue(width, height, pixels)
    scale = tuple((255 - target[c]) / float(max(1, 255 - base[c])) for c in range(3))
    for i in range(width * height):
        k = i * 4
        if pixels[k + 3] == 0:
            continue
        for c in range(3):
            value = target[c] + (pixels[k + c] - base[c]) * scale[c]
            pixels[k + c] = max(0, min(255, int(round(value))))
    return base, target


def composite_dark(size, rgba, background=(28, 32, 42)):
    out = bytearray(size * size * 4)
    for i in range(size * size):
        k = i * 4
        a = rgba[k + 3] / 255.0
        for c in range(3):
            out[k + c] = int(rgba[k + c] * a + background[c] * (1 - a))
        out[k + 3] = 255
    return out


# ------------------------------------------------------- white-background keying

def remove_white_background(width, height, pixels):
    """Mark only near-white pixels reachable from an image edge as transparent."""
    background = bytearray(width * height)
    t = WHITE_THRESHOLD

    for y in range(height):
        base = y * width
        k = y * width * 4
        x = 0
        while x < width:
            j = k + x * 4
            if pixels[j] >= t and pixels[j + 1] >= t and pixels[j + 2] >= t:
                background[base + x] = 1
                x += 1
            else:
                break
        x = width - 1
        while x >= 0:
            j = k + x * 4
            if pixels[j] >= t and pixels[j + 1] >= t and pixels[j + 2] >= t:
                background[base + x] = 1
                x -= 1
            else:
                break

    for x in range(width):
        y = 0
        while y < height:
            j = (y * width + x) * 4
            if pixels[j] >= t and pixels[j + 1] >= t and pixels[j + 2] >= t:
                background[y * width + x] = 1
                y += 1
            else:
                break
        y = height - 1
        while y >= 0:
            j = (y * width + x) * 4
            if pixels[j] >= t and pixels[j + 1] >= t and pixels[j + 2] >= t:
                background[y * width + x] = 1
                y -= 1
            else:
                break

    for i in range(width * height):
        if background[i]:
            k = i * 4
            pixels[k] = 255
            pixels[k + 1] = 255
            pixels[k + 2] = 255
            pixels[k + 3] = 0
    return sum(background)


# ------------------------------------------------------------------- resampling

def resize(pixels, sw, sh, dw, dh):
    """Area-average downsample with alpha-weighted colour (no white halos)."""
    out = bytearray(dw * dh * 4)
    for dy in range(dh):
        y0 = dy * sh // dh
        y1 = max(y0 + 1, (dy + 1) * sh // dh)
        for dx in range(dw):
            x0 = dx * sw // dw
            x1 = max(x0 + 1, (dx + 1) * sw // dw)
            r = g = b = a_sum = count = 0
            for y in range(y0, y1):
                row = (y * sw + x0) * 4
                for x in range(x0, x1):
                    j = row + (x - x0) * 4
                    alpha = pixels[j + 3]
                    r += pixels[j] * alpha
                    g += pixels[j + 1] * alpha
                    b += pixels[j + 2] * alpha
                    a_sum += alpha
                    count += 1
            k = (dy * dw + dx) * 4
            if a_sum > 0:
                out[k] = min(255, r // a_sum)
                out[k + 1] = min(255, g // a_sum)
                out[k + 2] = min(255, b // a_sum)
            else:
                out[k] = out[k + 1] = out[k + 2] = 255
            out[k + 3] = a_sum // max(1, count)
    return out


# ---------------------------------------------------------------- PNG / ICO out

def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data +
            struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def png_bytes(width, rgba, height=None):
    height = height or width
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)
        raw.extend(rgba[y * stride:(y + 1) * stride])
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) +
            chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def write_ico(path, images):
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for size, data in images:
        entries += struct.pack("<BBBBHHII", 0 if size == 256 else size,
                               0 if size == 256 else size, 0, 0, 1, 32,
                               len(data), offset)
        blobs += data
        offset += len(data)
    with open(path, "wb") as fh:
        fh.write(header + entries + blobs)


def round_mask(rgba, size):
    """Small face crops are solid artwork with no transparent margin; cut them
    into a feathered circular avatar so they don't look like square colour tiles
    on a dark taskbar."""
    out = bytearray(rgba)
    centre = (size - 1) / 2.0
    radius = size * 0.49
    feather = max(0.8, size * 0.04)
    for y in range(size):
        for x in range(size):
            distance = ((x - centre) ** 2 + (y - centre) ** 2) ** 0.5
            k = (y * size + x) * 4
            if distance > radius:
                out[k + 3] = 0
            elif distance > radius - feather:
                out[k + 3] = int(out[k + 3] * (radius - distance) / feather)
    return out


def crop_region(pixels, sw, sh, rect):
    """Copy a relative (x, y, w, h) rectangle into a standalone RGBA buffer."""
    x0, y0 = int(rect[0] * sw), int(rect[1] * sh)
    x1, y1 = int((rect[0] + rect[2]) * sw), int((rect[1] + rect[3]) * sh)
    cw, ch = x1 - x0, y1 - y0
    out = bytearray(cw * ch * 4)
    for y in range(ch):
        start = ((y0 + y) * sw + x0) * 4
        out[y * cw * 4:(y + 1) * cw * 4] = pixels[start:start + cw * 4]
    return cw, ch, out


def make_sheet(rendered, path, sizes=(128, 64, 32, 16)):
    """Contact sheet so tiny-icon legibility can be checked without installing."""
    pad, cell = 24, 144
    width = pad + len(sizes) * (cell + pad)
    height = pad + 2 * (cell + pad)
    backgrounds = ((245, 247, 251), (28, 32, 42))
    canvas = bytearray(width * height * 4)
    for row in range(2):
        top = pad + row * (cell + pad)
        bg = bytes(backgrounds[row])
        for y in range(top, top + cell):
            for x in range(pad, width):
                k = (y * width + x) * 4
                canvas[k:k + 3] = bg
                canvas[k + 3] = 255
    for col, size in enumerate(sizes):
        src = rendered[size]
        ox = pad + col * (cell + pad) + (cell - size) // 2
        for row in range(2):
            oy = pad + row * (cell + pad) + (cell - size) // 2
            bg = backgrounds[row]
            for y in range(size):
                s = y * size * 4
                d = ((oy + y) * width + ox) * 4
                for x in range(size):
                    a = src[s + x * 4 + 3] / 255.0
                    q = d + x * 4
                    for c in range(3):
                        canvas[q + c] = int(src[s + x * 4 + c] * a + bg[c] * (1 - a))
                    canvas[q + 3] = 255
    with open(path, "wb") as fh:
        fh.write(png_bytes(width, canvas, height))
    print("size sheet:", os.path.abspath(path))


def parse_flags(argv):
    options, positional = {}, []
    i = 0
    while i < len(argv):
        arg = argv[i]
        if arg.startswith("--"):
            key = arg[2:]
            options[key] = argv[i + 1] if i + 1 < len(argv) else None
            i += 2
        else:
            positional.append(arg)
            i += 1
    return positional, options


def main(argv):
    args, opts = parse_flags(argv[1:])
    source = args[0]
    ico_path = args[1] if len(args) > 1 else os.path.join(
        os.path.dirname(__file__), "..", "src", "AppIcon.ico")
    preview_path = args[2] if len(args) > 2 else None
    # Legacy positional 4th argument was a recolour hex; keep it working.
    recolor_hex = opts.get("recolor") or (args[3] if len(args) > 3 else None)

    crop = DEFAULT_SMALL_CROP
    if opts.get("small-crop"):
        crop = tuple(float(v) for v in opts["small-crop"].split(","))
        if len(crop) != 4:
            raise ValueError("--small-crop expects x,y,w,h")
    mask_mode = opts.get("mask", "circle")
    if mask_mode not in ("circle", "none"):
        raise ValueError("--mask expects circle or none")

    width, height, pixels = read_png(source)
    print("decoded %s: %dx%d" % (source, width, height))
    if recolor_hex:
        old, new = recolour(width, height, pixels, recolor_hex)
        print("recoloured base rgb%s -> rgb%s" % (old, new))
    removed = remove_white_background(width, height, pixels)
    print("transparent background pixels: %d (%.1f%%)"
          % (removed, removed * 100.0 / (width * height)))

    base = resize(pixels, width, height, 512, 512)
    if preview_path:
        with open(preview_path, "wb") as fh:
            fh.write(png_bytes(512, base))
        print("preview:", os.path.abspath(preview_path))
        dark_path = os.path.splitext(preview_path)[0] + "-dark.png"
        with open(dark_path, "wb") as fh:
            fh.write(png_bytes(512, composite_dark(512, base)))
        print("dark preview:", os.path.abspath(dark_path))

    cw, ch, face = crop_region(pixels, width, height, crop)
    rendered = {}
    for size in SIZES:
        if size in SMALL_SIZES:
            small = resize(face, cw, ch, size, size)
            rendered[size] = (round_mask(small, size)
                              if mask_mode == "circle" else small)
        else:
            rendered[size] = resize(base, 512, 512, size, size)
    print("small sizes %s use face crop %s (mask=%s)"
          % (",".join(str(s) for s in SMALL_SIZES), crop, mask_mode))

    write_ico(os.path.abspath(ico_path),
              [(size, png_bytes(size, rendered[size])) for size in SIZES])
    print("wrote %s (%d bytes, sizes %s)" %
          (os.path.abspath(ico_path), os.path.getsize(ico_path),
           ",".join(str(s) for s in SIZES)))

    if opts.get("sheet"):
        make_sheet(rendered, opts["sheet"])
    if opts.get("embed"):
        with open(opts["embed"], "wb") as fh:
            fh.write(png_bytes(256, resize(base, 512, 512, 256, 256)))
        print("embed asset:", os.path.abspath(opts["embed"]))


if __name__ == "__main__":
    main(sys.argv)
