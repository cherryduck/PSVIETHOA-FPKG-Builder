#!/usr/bin/env python3
"""Tạo bộ icon ứng dụng (PNG các cỡ, .ico cho Windows, .iconset cho macOS) bằng Pillow.

Cách dùng:  python3 scripts/make-icons.py
Kết quả:    src/PsViethoa.FpkgBuilder.App/Assets/icon-*.png, app.ico, AppIcon.iconset/
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:  # pragma: no cover
    sys.exit("Cần Pillow: pip3 install pillow")

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "PsViethoa.FpkgBuilder.App" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)

SIZE = 1024
BG_TOP = (30, 41, 59)      # #1E293B
BG_BOTTOM = (15, 23, 42)   # #0F172A
ACCENT = (34, 197, 94)     # #22C55E
ACCENT_DARK = (21, 128, 61)
WHITE = (248, 250, 252)


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def vertical_gradient(size: int, top, bottom) -> Image.Image:
    img = Image.new("RGB", (size, size), top)
    px = img.load()
    for y in range(size):
        t = y / (size - 1)
        col = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(size):
            px[x, y] = col
    return img


def draw_icon(size: int = SIZE) -> Image.Image:
    s = size
    base = vertical_gradient(s, BG_TOP, BG_BOTTOM).convert("RGBA")
    mask = rounded_mask(s, int(s * 0.22))

    d = ImageDraw.Draw(base)

    # Hộp gói (package) – khối lập phương isometric đơn giản.
    cx, cy = s * 0.5, s * 0.53
    w = s * 0.30
    h = s * 0.17
    depth = s * 0.30

    top_face = [(cx, cy - h), (cx + w, cy), (cx, cy + h), (cx - w, cy)]
    left_face = [(cx - w, cy), (cx, cy + h), (cx, cy + h + depth), (cx - w, cy + depth)]
    right_face = [(cx, cy + h), (cx + w, cy), (cx + w, cy + depth), (cx, cy + h + depth)]

    d.polygon(left_face, fill=(148, 163, 184))
    d.polygon(right_face, fill=(203, 213, 225))
    d.polygon(top_face, fill=WHITE)

    # Băng dán màu accent trên mặt trên + mặt trước.
    band = s * 0.055
    d.polygon([(cx - band, cy - h + band * 1.0), (cx + band, cy - h + band * 1.0),
               (cx + w - band, cy - band * 0.0), (cx + w - band * 3, cy + band * 0.0)], fill=ACCENT)
    d.polygon([(cx - band, cy + h - band), (cx + band, cy + h + band),
               (cx + band, cy + h + depth), (cx - band, cy + h + depth - band * 0)], fill=ACCENT_DARK)

    # Chấm "play" nhỏ góc phải trên (dấu hiệu build/run).
    r = s * 0.075
    px, py = s * 0.79, s * 0.24
    d.ellipse((px - r, py - r, px + r, py + r), fill=ACCENT)
    tri = [(px - r * 0.35, py - r * 0.5), (px - r * 0.35, py + r * 0.5), (px + r * 0.55, py)]
    d.polygon(tri, fill=BG_BOTTOM)

    # Bóng mềm phía dưới hộp.
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.ellipse((cx - w * 1.05, cy + h + depth - s * 0.02, cx + w * 1.05, cy + h + depth + s * 0.07), fill=(0, 0, 0, 110))
    shadow = shadow.filter(ImageFilter.GaussianBlur(s * 0.02))
    base = Image.alpha_composite(shadow, base) if False else base
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    out.paste(base, (0, 0), mask)
    return out


def main() -> None:
    icon = draw_icon()
    icon.save(ASSETS / "icon-1024.png")
    for sz in (16, 32, 48, 64, 128, 256, 512):
        icon.resize((sz, sz), Image.LANCZOS).save(ASSETS / f"icon-{sz}.png")

    # Windows .ico (nhiều cỡ, PNG-compressed cho 256)
    icon.resize((256, 256), Image.LANCZOS).save(
        ASSETS / "app.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    # macOS .iconset (chuyển sang .icns bằng: iconutil -c icns AppIcon.iconset)
    iconset = ASSETS / "AppIcon.iconset"
    iconset.mkdir(exist_ok=True)
    for sz in (16, 32, 128, 256, 512):
        icon.resize((sz, sz), Image.LANCZOS).save(iconset / f"icon_{sz}x{sz}.png")
        icon.resize((sz * 2, sz * 2), Image.LANCZOS).save(iconset / f"icon_{sz}x{sz}@2x.png")

    print(f"Đã tạo icon trong {ASSETS}")


if __name__ == "__main__":
    main()
