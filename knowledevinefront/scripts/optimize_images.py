"""Generate the website's compressed assets: python3 scripts/optimize_images.py.

Requires Pillow. Original artwork remains unchanged for future updates.
"""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
assets = root / 'src/assets/images'
limits = {
    'banner.jpg': 1600, 'banner2.jpg': 1200, 'Env.png': 1000,
    'Independence.png': 600, 'Respect.png': 600, 'Hands.png': 600,
    'Exploration.png': 600, 'logo.png': 640, 'visitbanner.png': 1200,
    'whybanner.png': 1200,
}
original_bytes = optimized_bytes = 0
for name, width in limits.items():
    source = assets / name
    with Image.open(source) as image:
        image.thumbnail((width, round(width * image.height / image.width)), Image.Resampling.LANCZOS)
        output = source.with_suffix('.webp')
        image.save(output, 'WEBP', quality=82, method=6)
    original_bytes += source.stat().st_size
    optimized_bytes += output.stat().st_size

with Image.open(root / 'design/open-day-2026-10-31.png') as image:
    image.thumbnail((1200, 1200), Image.Resampling.LANCZOS)
    image.save(assets / 'open-day-invitation.webp', 'WEBP', quality=88, method=6)
with Image.open(assets / 'topLogo.png') as image:
    image.thumbnail((64, 64), Image.Resampling.LANCZOS)
    image.save(assets / 'favicon.png', optimize=True)
print(f'Existing image assets: {original_bytes:,} → {optimized_bytes:,} bytes ({100 * (1 - optimized_bytes / original_bytes):.1f}% smaller).')
