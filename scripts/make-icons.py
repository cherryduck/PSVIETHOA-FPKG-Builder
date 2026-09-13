#!/usr/bin/env python3
"""Tạo bộ icon ứng dụng từ logo PSVIETHOA (docs/logo.png — bản raster của docs/logo.svg) bằng Pillow.

Cách dùng:  python3 scripts/make-icons.py [đường/dẫn/logo.png]
Kết quả:    src/PsViethoa.FpkgBuilder.App/Assets/icon-*.png, app.ico (Windows), AppIcon.iconset/ và AppIcon.icns (macOS, qua iconutil)

Logo gốc là hình vuông nền trắng ngà có dấu hiệu (chùm sáng + khối "PS/VH") ở trên và dòng chữ bên dưới.
Icon chỉ lấy phần dấu hiệu (chữ quá nhỏ ở cỡ icon), đặt giữa một ô vuông bo góc cùng màu nền với logo.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover
    sys.exit("Cần Pillow: pip3 install pillow")

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "PsViethoa.FpkgBuilder.App" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)

SIZE = 1024
MARK_HEIGHT = 0.74          # phần dấu hiệu cao 74 % ô icon
CORNER = 0.2237             # bán kính bo góc kiểu macOS (≈ 229 px ở 1024)
SIZES = [16, 32, 48, 64, 128, 256, 512, 1024]


def load_logo(path: Path) -> Image.Image:
    return Image.open(path).convert("RGB")


def background_of(img: Image.Image) -> tuple[int, int, int]:
    return img.getpixel((4, 4))


def mark_bbox(img: Image.Image) -> tuple[int, int, int, int]:
    """Hộp bao của phần dấu hiệu: các hàng có nội dung phía trên khoảng trống lớn đầu tiên (trước dòng chữ)."""
    w, h = img.size
    bg = background_of(img)
    px = img.load()

    def is_ink(x: int, y: int) -> bool:
        p = px[x, y]
        return abs(p[0] - bg[0]) + abs(p[1] - bg[1]) + abs(p[2] - bg[2]) > 40

    rows = [any(is_ink(x, y) for x in range(0, w, 2)) for y in range(h)]
    top = next(y for y in range(h) if rows[y])
    bottom = top
    blank = 0
    for y in range(top, h):
        if rows[y]:
            bottom = y
            blank = 0
        else:
            blank += 1
            if blank > h * 0.03:      # khoảng trống > 3 % chiều cao → hết phần dấu hiệu
                break

    cols = [any(is_ink(x, y) for y in range(top, bottom + 1, 2)) for x in range(w)]
    left = next(x for x in range(w) if cols[x])
    right = next(x for x in range(w - 1, -1, -1) if cols[x])
    return left, top, right + 1, bottom + 1


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size * 4 - 1, size * 4 - 1), radius=radius * 4, fill=255)
    return mask.resize((size, size), Image.LANCZOS)


def build_icon(logo: Image.Image) -> Image.Image:
    bg = background_of(logo)
    left, top, right, bottom = mark_bbox(logo)
    mark = logo.crop((left, top, right, bottom))

    target_h = round(SIZE * MARK_HEIGHT)
    scale = target_h / mark.height
    target_w = round(mark.width * scale)
    if target_w > SIZE * 0.8:
        scale = SIZE * 0.8 / mark.width
        target_w, target_h = round(mark.width * scale), round(mark.height * scale)
    mark = mark.resize((target_w, target_h), Image.LANCZOS)

    tile = Image.new("RGB", (SIZE, SIZE), bg)
    tile.paste(mark, ((SIZE - target_w) // 2, (SIZE - target_h) // 2))

    icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    icon.paste(tile, (0, 0), rounded_mask(SIZE, round(SIZE * CORNER)))
    return icon


def main() -> None:
    logo_path = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "docs" / "logo.png"
    logo = load_logo(logo_path)
    icon = build_icon(logo)

    for sz in SIZES:
        (icon if sz == SIZE else icon.resize((sz, sz), Image.LANCZOS)).save(ASSETS / f"icon-{sz}.png")

    # Windows .ico (nhiều cỡ, 256 nén PNG)
    icon.resize((256, 256), Image.LANCZOS).save(
        ASSETS / "app.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    # macOS .iconset → .icns
    iconset = ASSETS / "AppIcon.iconset"
    iconset.mkdir(exist_ok=True)
    for old in iconset.glob("*.png"):
        old.unlink()
    for sz in (16, 32, 128, 256, 512):
        icon.resize((sz, sz), Image.LANCZOS).save(iconset / f"icon_{sz}x{sz}.png")
        icon.resize((sz * 2, sz * 2), Image.LANCZOS).save(iconset / f"icon_{sz}x{sz}@2x.png")

    if sys.platform == "darwin":
        subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o", str(ASSETS / "AppIcon.icns")], check=True)
        print("AppIcon.icns ok")
    else:
        print("Chạy trên macOS để tạo AppIcon.icns: iconutil -c icns AppIcon.iconset")

    print(f"Icon từ {logo_path.name}: dấu hiệu {mark_bbox(logo)} → {ASSETS}")


if __name__ == "__main__":
    main()
