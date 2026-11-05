"""Bake a restrained terrain AO estimate from the credited NASA LOLA DEM.

Requires numpy and Pillow. No albedo is read or modified and no light direction
is used. This is a finite-radius, sampled art bake, not physical lunar lighting.
"""
from pathlib import Path
import hashlib
import json
import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "Source~" / "ldem_4.tif"
RADIUS_KM = 1737.4
RELIEF = 2.0
WIDTH, HEIGHT = 720, 360
AZIMUTHS = 24
# The closest sample resolves source DEM detail; distant samples can see the
# rims of large basins. Distances are geodesic arc lengths, not map-pixel steps.
DISTANCES_KM = np.array([7.5, 15, 22.5, 30, 45, 60, 90, 120, 180, 240])
ART_STRENGTH = .55
ART_EXPONENT = .5
AO_FLOOR = .78


def sample(field, longitude, latitude):
    """Bilinear equirectangular data sampling: U Repeat, V Clamp."""
    rows, cols = field.shape
    x = (longitude / (2 * np.pi) + .5) * cols - .5
    y = np.clip((.5 - latitude / np.pi) * rows - .5, 0, rows - 1)
    x0, y0 = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
    fx, fy = x - x0, y - y0
    x0 %= cols
    x1, y1 = (x0 + 1) % cols, np.minimum(y0 + 1, rows - 1)
    return (field[y0, x0] * (1 - fx) + field[y0, x1] * fx) * (1 - fy) + \
           (field[y1, x0] * (1 - fx) + field[y1, x1] * fx) * fy


def sky_integral(normal_tangent, normal_up, lower):
    """Cosine irradiance integral over elevation [lower, pi/2].

    For L=t*cos(b)+up*sin(b), (n dot L)*dOmega is
    (nt*cos(b)+nu*sin(b))*cos(b) db da. The local-normal horizon
    excludes directions for which n dot L is negative.
    """
    return normal_tangent * (np.pi / 4 - lower / 2 - np.sin(2 * lower) / 4) + \
           normal_up * (.5 - np.sin(lower) ** 2 / 2)


def main():
    dem = np.asarray(Image.open(SOURCE), dtype=np.float64)
    if dem.shape != (720, 1440) or not np.isfinite(dem).all() or dem.min() < -12 or dem.max() > 12:
        raise ValueError("Expected the documented 1440x720 LOLA floating-point kilometer DEM.")

    longitude, latitude = np.meshgrid((np.arange(WIDTH) + .5) * 2 * np.pi / WIDTH - np.pi,
                                     np.pi / 2 - (np.arange(HEIGHT) + .5) * np.pi / HEIGHT)
    sin_lat, cos_lat = np.sin(latitude), np.cos(latitude)
    elevation = sample(dem, longitude, latitude)
    radius = RADIUS_KM + RELIEF * elevation

    # Match the relief and angular-gradient frame used by Moon_Normal_LOLA.png.
    source_step = np.pi / dem.shape[0]
    source_lat = np.pi / 2 - (np.arange(dem.shape[0]) + .5) * source_step
    d_lon = (np.roll(dem, -1, axis=1) - np.roll(dem, 1, axis=1)) / (2 * source_step)
    d_lat = -np.gradient(dem, source_step, axis=0)
    source_radius = RADIUS_KM + RELIEF * dem
    east_slope = RELIEF * d_lon / (source_radius * np.maximum(np.cos(source_lat)[:, None], .05))
    north_slope = RELIEF * d_lat / source_radius
    pole_fade = np.clip((np.pi / 2 - np.abs(latitude)) / np.deg2rad(3), 0, 1)
    ne = -sample(east_slope, longitude, latitude) * pole_fade
    nn = -sample(north_slope, longitude, latitude) * pole_fade
    nu = np.ones_like(elevation)
    norm = np.sqrt(ne * ne + nn * nn + 1)
    ne, nn, nu = ne / norm, nn / norm, nu / norm

    visible = np.zeros_like(elevation)
    unblocked = np.zeros_like(elevation)
    maximum_horizon = np.full_like(elevation, -np.pi / 2)
    for azimuth in (np.arange(AZIMUTHS) + .5) * 2 * np.pi / AZIMUTHS:
        # Azimuth zero points east, pi/2 north. The destination follows a
        # great circle, so geographic pole crossings do not need fake wrapping.
        east, north = np.cos(azimuth), np.sin(azimuth)
        horizon = np.full_like(elevation, -np.pi / 2)
        for distance in DISTANCES_KM:
            delta = distance / RADIUS_KM
            sd, cd = np.sin(delta), np.cos(delta)
            sample_lat = np.arcsin(np.clip(sin_lat * cd + cos_lat * sd * north, -1, 1))
            sample_lon = longitude + np.arctan2(east * sd * cos_lat, cd - sin_lat * np.sin(sample_lat))
            sample_radius = RADIUS_KM + RELIEF * sample(dem, sample_lon, sample_lat)
            # Exact radial/tangential projection relative to the center's
            # spherical tangent plane. A level sphere has horizon=-delta/2,
            # rather than incorrectly occluding its own distant surface.
            angle = np.arctan2(sample_radius * cd - radius, sample_radius * sd)
            horizon = np.maximum(horizon, angle)
        maximum_horizon = np.maximum(maximum_horizon, horizon)
        nt = ne * east + nn * north
        normal_horizon = np.maximum(0, np.arctan2(-nt, nu))
        unblocked += sky_integral(nt, nu, normal_horizon)
        visible += sky_integral(nt, nu, np.maximum(normal_horizon, horizon))

    raw_ao = np.clip(visible / unblocked, 0, 1)
    # Expose weak basin occlusion without introducing a baked light direction.
    # This explicit artistic remap is intentionally not an irradiance claim.
    ao = np.clip(1 - ART_STRENGTH * (1 - raw_ao) ** ART_EXPONENT, AO_FLOOR, 1)
    # The spherical normal frame is singular at the poles. Match the normal
    # map's 3-degree fade and force each terminal row to a single value.
    ao = ao.mean(axis=1, keepdims=True) + pole_fade * (ao - ao.mean(axis=1, keepdims=True))
    ao[0] = ao[0].mean()
    ao[-1] = ao[-1].mean()
    gray = np.rint(ao * 255).astype(np.uint8)
    rgb = np.repeat(gray[:, :, None], 3, axis=2)
    output = ROOT / "Moon_AO_LOLA.png"
    Image.fromarray(rgb).save(output)

    # Independent numeric sanity checks at this asset-generation boundary.
    # A level sphere's analytic horizon is negative at every sample distance.
    sphere_horizon = np.arctan2(np.cos(DISTANCES_KM / RADIUS_KM) - 1,
                                np.sin(DISTANCES_KM / RADIUS_KM))
    assert np.isfinite(ao).all() and ao.min() >= AO_FLOOR and ao.max() <= 1 + 1e-12
    assert np.all(sphere_horizon < 0)
    assert np.array_equal(rgb[:, :, 0], rgb[:, :, 1]) and np.array_equal(rgb[:, :, 1], rgb[:, :, 2])
    assert np.ptp(gray[0]) == 0 and np.ptp(gray[-1]) == 0
    q = gray.astype(np.float64) / 255
    report = {
        "sourcePage": "https://svs.gsfc.nasa.gov/4720/",
        "sourceFile": "Source~/ldem_4.tif",
        "sourceSha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        "sourceDemUnits": "kilometers relative to radius 1737.4 km",
        "sourceDemRangeKm": [float(dem.min()), float(dem.max())],
        "radiusKm": RADIUS_KM, "reliefMultiplier": RELIEF,
        "size": [WIDTH, HEIGHT], "azimuths": AZIMUTHS,
        "sampleDistancesKm": DISTANCES_KM.tolist(),
        "geodesicSamplesPerTexel": int(AZIMUTHS * len(DISTANCES_KM)),
        "method": "Finite-distance great-circle terrain horizons, exact spherical curvature projection, cosine-weighted sky visibility normalized against the local DEM normal's radial hemisphere",
        "rawAoRange": [float(raw_ao.min()), float(raw_ao.max())],
        "rawAoMean": float(raw_ao.mean()),
        "artRemap": {"formula": "AO=clamp(1-0.55*sqrt(1-rawAO),0.78,1)",
                     "poleHandling": "3-degree row-mean fade; each terminal row constant"},
        "aoRangeQuantized": [float(q.min()), float(q.max())],
        "aoPercentile01": float(np.percentile(q, 1)),
        "aoPercentile05": float(np.percentile(q, 5)),
        "aoMedian": float(np.median(q)), "aoMean": float(q.mean()),
        "fullWhiteFraction": float(np.mean(gray == 255)),
        "pixelsAtFloorFraction": float(np.mean(q <= AO_FLOOR + 1 / 255)),
        "maximumTerrainHorizonDegrees": float(np.rad2deg(maximum_horizon.max())),
        "poleRowPeakToPeakBytes": [int(np.ptp(gray[0])), int(np.ptp(gray[-1]))],
        "longitudeEdgeMeanDifferenceBytes": float(np.abs(gray[:, 0].astype(float) - gray[:, -1]).mean()),
        "equalRgbChannels": True, "usesAlbedo": False, "usesDirectionalLight": False,
        "output": {"file": output.name, "bytes": output.stat().st_size,
                   "sha256": hashlib.sha256(output.read_bytes()).hexdigest()},
        "limits": "Sampled artistic AO estimate, not physical lunar illumination or proof of native rendering. No sub-grid detail, multiple scattering, or shadows from artificial scene structures are included.",
        "import": "Default texture; sRGB=false; U Repeat; V Clamp; npotScale=None. Identical RGB permits URP-G and glTF-R occlusion sampling.",
    }
    (ROOT / "ao-preparation-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
