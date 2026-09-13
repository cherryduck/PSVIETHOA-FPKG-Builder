#!/usr/bin/env python3
"""Tạo docs/banner.png (1280×448) cho README từ icon ứng dụng hiện tại (chạy make-icons.py trước)."""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
W, H = 1280, 448
BG_TOP = (15, 23, 42)
BG_BOT = (11, 17, 32)
ACCENT = (34, 197, 94)
WHITE = (248, 250, 252)
SUB = (203, 213, 225)


def vgrad(w, h, a, b):
    img = Image.new("RGB", (w, h), a)
    px = img.load()
    for y in range(h):
        t = y / (h - 1)
        c = tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    return img


def font(size, bold=True):
    candidates = [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else "/System/Library/Fonts/Supplemental/Arial.ttf",
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ]
    for c in candidates:
        if Path(c).exists():
            return ImageFont.truetype(c, size)
    return ImageFont.load_default()


def main():
    base = vgrad(W, H, BG_TOP, BG_BOT).convert("RGBA")

    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((-120, -160, 360, 220), fill=(34, 197, 94, 40))
    gd.ellipse((W - 360, H - 180, W + 140, H + 180), fill=(37, 99, 235, 32))
    base = Image.alpha_composite(base, glow.filter(ImageFilter.GaussianBlur(90)))

    icon = Image.open(ROOT / "src/PsViethoa.FpkgBuilder.App/Assets/icon-256.png").convert("RGBA").resize((150, 150), Image.LANCZOS)
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((W // 2 - 75, 96, W // 2 + 75, 246), radius=34, fill=(0, 0, 0, 120))
    base = Image.alpha_composite(base, shadow.filter(ImageFilter.GaussianBlur(22)))
    base.alpha_composite(icon, (W // 2 - 75, 86))
    d = ImageDraw.Draw(base)

    def ctext(y, txt, fnt, fill):
        w = d.textbbox((0, 0), txt, font=fnt)[2]
        d.text(((W - w) // 2, y), txt, font=fnt, fill=fill)

    ctext(262, "PSVIETHOA FPKG Builder", font(58), WHITE)
    ctext(336, "PS5 FPKG (FIH debug) builder for macOS & Windows", font(24, False), SUB)

    pill = "Vietnamese / English  •  Folder · .exfat · .ffpfsc · GP5  •  Extract PKG  •  PFS v2 / v3"
    pf = font(18, False)
    pw = d.textbbox((0, 0), pill, font=pf)[2]
    px0 = (W - pw) // 2 - 18
    py0 = 384
    d.rounded_rectangle((px0, py0, px0 + pw + 36, py0 + 38), radius=19, fill=(26, 58, 46, 255), outline=ACCENT, width=1)
    d.text(((W - pw) // 2, py0 + 9), pill, font=pf, fill=(74, 222, 128))

    out = ROOT / "docs" / "banner.png"
    base.convert("RGB").save(out, quality=95)
    print("banner ok:", out)


if __name__ == "__main__":
    main()
