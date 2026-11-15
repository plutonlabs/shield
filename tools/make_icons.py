"""Builds the Windows .ico files of the app from one big PNG.

Usage: python make_icons.py <source.png> <out-folder>

Writes plutonshield.ico (the normal icon) and plutonshield-off.ico (the same icon in grey, used in the tray while
protection is off). Each .ico holds the sizes Windows asks for, from 16x16 (tray, title bars) to 256x256.
"""
import sys
from pathlib import Path

from PIL import Image, ImageEnhance

SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def square(image):
    """Pads a not-square picture with transparent space so that it is not stretched."""
    side = max(image.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2))
    return canvas


def grey(image):
    """Same picture without colour. The transparency stays as it is."""
    red, green, blue, alpha = image.split()
    flat = ImageEnhance.Color(Image.merge("RGB", (red, green, blue))).enhance(0.0)
    flat.putalpha(alpha)
    return flat


def save_ico(image, path):
    # Reduce in two steps (big -> 256 -> each size) for a smoother result at 16x16.
    base = image.resize((256, 256), Image.Resampling.LANCZOS)
    base.save(path, format="ICO", sizes=SIZES)
    print(f"wrote {path}")


def main():
    source = Path(sys.argv[1])
    out = Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)

    image = square(Image.open(source).convert("RGBA"))
    save_ico(image, out / "plutonshield.ico")
    save_ico(grey(image), out / "plutonshield-off.ico")


main()
