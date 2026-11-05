"""Rebuild the compact NASA lunar textures. Requires Pillow and numpy.

Sources are downloaded into Source~ (ignored by Unity). No albedo-to-height
inference is performed: all geometry/normal information comes from LOLA DEM.
"""
from pathlib import Path
import hashlib
import json
import urllib.request
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "Source~"
BASE = "https://svs.gsfc.nasa.gov/vis/a000000/a004700/a004720/"
RADIUS_KM = 1737.4
RELIEF = 2.0
HEIGHT_MIN_KM, HEIGHT_MAX_KM = -12.0, 12.0


def download(name):
    SOURCE.mkdir(parents=True, exist_ok=True)
    target = SOURCE / name
    if not target.exists() or target.stat().st_size == 0:
        with urllib.request.urlopen(BASE + name, timeout=60) as response:
            data = response.read()
        if len(data) < 100000:
            raise ValueError("Incomplete NASA source download: " + name)
        temporary = target.with_suffix(target.suffix + ".partial")
        temporary.write_bytes(data)
        temporary.replace(target)
    return target


def main():
    color_path = download("lroc_color_2k.jpg")
    dem_path = download("ldem_4.tif")
    albedo = Image.open(color_path).convert("RGB")
    if albedo.size != (2048, 1024):
        raise ValueError("Unexpected NASA albedo dimensions")
    # Preserve the official albedo bytes without another JPEG generation.
    (ROOT / "Moon_Albedo_2k.jpg").write_bytes(color_path.read_bytes())
    dem = np.asarray(Image.open(dem_path), dtype=np.float64)
    if dem.shape != (720, 1440) or not np.isfinite(dem).all():
        raise ValueError("Unexpected LOLA floating-point DEM")
    if dem.min() < HEIGHT_MIN_KM or dem.max() > HEIGHT_MAX_KM:
        raise ValueError("LOLA DEM outside documented encoding range")

    # DEM rows are north to south; Unity texture v increases toward north.
    height = np.array(Image.fromarray(dem.astype(np.float32), mode="F").resize(
        (720, 360), Image.Resampling.LANCZOS), dtype=np.float64)
    height[0, :] = height[0, :].mean()
    height[-1, :] = height[-1, :].mean()
    encoded = np.rint(np.clip((height - HEIGHT_MIN_KM) /
                      (HEIGHT_MAX_KM - HEIGHT_MIN_KM), 0, 1) * 65535).astype(np.uint16)
    packed = np.zeros((360, 720, 3), dtype=np.uint8)
    packed[:, :, 0] = encoded >> 8
    packed[:, :, 1] = encoded & 255
    Image.fromarray(packed, mode="RGB").save(ROOT / "Moon_Height_RG16.png")

    # Tangent-space normal derived only from physical LOLA elevations.
    # Differentiate in angular coordinates and account for longitude's smaller
    # surface distance near the poles. Clamp only the singular polar band.
    rows, cols = dem.shape
    longitude_step, latitude_step = 2 * np.pi / cols, np.pi / rows
    latitude = np.pi / 2 - (np.arange(rows) + .5) * latitude_step
    d_lon = (np.roll(dem, -1, axis=1) - np.roll(dem, 1, axis=1)) / (2 * longitude_step)
    d_lat = -np.gradient(dem, latitude_step, axis=0)
    radius = RADIUS_KM + RELIEF * dem
    east_slope = RELIEF * d_lon / (radius * np.maximum(np.cos(latitude)[:, None], .05))
    north_slope = RELIEF * d_lat / radius
    pole_fade = np.clip((np.pi / 2 - np.abs(latitude)) / np.deg2rad(3), 0, 1)[:, None]
    normal = np.stack((-east_slope * pole_fade, -north_slope * pole_fade,
                       np.ones_like(dem)), axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    normal_rgb = np.rint((normal * .5 + .5) * 255).clip(0, 255).astype(np.uint8)
    Image.fromarray(normal_rgb, mode="RGB").save(ROOT / "Moon_Normal_LOLA.png")

    report = {
        "sourcePage": "https://svs.gsfc.nasa.gov/4720/",
        "sourceDemUnits": "km relative to a 1737.4 km sphere",
        "sourceDemMinMaxKm": [float(dem.min()), float(dem.max())],
        "heightEncoding": "uint16 = R*256+G; km=uint16/65535*24-12; B=0",
        "geometryReliefMultiplier": RELIEF,
        "albedoSize": list(albedo.size),
        "normalSize": [cols, rows], "heightSize": [720, 360],
        "files": {p.name: {"bytes": p.stat().st_size,
                            "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
                  for p in [color_path, dem_path, ROOT / "Moon_Albedo_2k.jpg",
                            ROOT / "Moon_Height_RG16.png", ROOT / "Moon_Normal_LOLA.png"]},
    }
    (ROOT / "preparation-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
