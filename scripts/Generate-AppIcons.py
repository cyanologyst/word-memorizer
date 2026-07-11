"""Generate all Windows icon assets from one transparent square PNG."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


def normalized_artwork(source: Path) -> Image.Image:
    image = Image.open(source).convert("RGBA")
    bounds = image.getchannel("A").getbbox()
    if bounds is None:
        raise ValueError("The source image is fully transparent.")

    left, top, right, bottom = bounds
    safety = max(2, round(max(right - left, bottom - top) * 0.01))
    crop = (
        max(0, left - safety),
        max(0, top - safety),
        min(image.width, right + safety),
        min(image.height, bottom + safety),
    )
    return image.crop(crop)


def render(source: Image.Image, size: tuple[int, int], fill: float) -> Image.Image:
    width, height = size
    max_width = max(1, round(width * fill))
    max_height = max(1, round(height * fill))
    scale = min(max_width / source.width, max_height / source.height)
    artwork = source.resize(
        (max(1, round(source.width * scale)), max(1, round(source.height * scale))),
        Image.Resampling.LANCZOS,
    )
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    position = ((width - artwork.width) // 2, (height - artwork.height) // 2)
    canvas.alpha_composite(artwork, position)
    return canvas


def save_png(source: Image.Image, assets: Path, name: str, size: tuple[int, int], fill: float) -> None:
    render(source, size, fill).save(assets / name, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument(
        "--assets",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "WordReviewReminder" / "Assets",
    )
    args = parser.parse_args()
    args.assets.mkdir(parents=True, exist_ok=True)
    artwork = normalized_artwork(args.source)

    targets = {
        "AppLogo.png": ((1024, 1024), 0.92),
        "LockScreenLogo.scale-200.png": ((48, 48), 0.88),
        "SplashScreen.scale-200.png": ((1240, 600), 0.70),
        "Square150x150Logo.scale-200.png": ((300, 300), 0.82),
        "Square44x44Logo.scale-200.png": ((88, 88), 0.90),
        "Square44x44Logo.targetsize-24_altform-unplated.png": ((24, 24), 0.94),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": ((48, 48), 0.94),
        "StoreLogo.png": ((50, 50), 0.90),
        "TrayIcon16.png": ((16, 16), 0.94),
        "TrayIcon32.png": ((32, 32), 0.94),
        "TrayIcon48.png": ((48, 48), 0.94),
        "Wide310x150Logo.scale-200.png": ((620, 300), 0.78),
    }
    for name, (size, fill) in targets.items():
        save_png(artwork, args.assets, name, size, fill)

    icon = render(artwork, (256, 256), 0.94)
    icon.save(
        args.assets / "AppIcon.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    main()
