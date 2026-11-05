"""Verify the two standalone lunar material GLBs against the authored AO PNG.

Reads only; optional --out writes a JSON report. The main scene remains covered
by verify_native_pbr.py. Reuses that verifier's GLB and bufferView parser.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import numpy as np
from PIL import Image
from verify_native_pbr import read_glb, view_bytes


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/SpaceBall/Art/LunarSurvey/Moon_AO_LOLA.png"
DEFAULT_MATERIALS = ROOT / "Temp/SpatialAdapterExport/Assets/SpaceBall/Materials"
NAMES = ("MoonSurface", "MoonCrater")


def inspect(materials):
    source_bytes = SOURCE.read_bytes()
    source_hash = hashlib.sha256(source_bytes).hexdigest()
    with Image.open(io.BytesIO(source_bytes)) as image:
        expected = np.asarray(image.convert("RGB"))
    results = []
    for name in NAMES:
        path = materials / (name + ".mat.glb")
        document, binary, artifact, _ = read_glb(path)
        matching = [material for material in document["materials"] if material.get("name") == name]
        if len(matching) != 1:
            raise ValueError("Expected one named material in " + str(path))
        binding = matching[0]["occlusionTexture"]
        record = document["images"][document["textures"][binding["index"]]["source"]]
        if record.get("mimeType") != "image/png":
            raise ValueError("Lunar occlusion must reference an embedded PNG: " + str(path))
        encoded = view_bytes(document, binary, record["bufferView"])
        with Image.open(io.BytesIO(encoded)) as image:
            actual = np.asarray(image.convert("RGB"))
        same_shape = actual.shape == expected.shape
        same_pixels = same_shape and np.array_equal(actual, expected)
        encoded_hash = hashlib.sha256(encoded).hexdigest()
        exact_bytes = encoded == source_bytes
        results.append({
            "material": name, "artifact": artifact,
            "occlusionTextureIndex": binding["index"], "occlusionStrength": binding.get("strength", 1),
            "imageName": record.get("name"), "imageDimensions": [actual.shape[1], actual.shape[0]],
            "encodedImageSha256": encoded_hash, "sourceImageSha256": source_hash,
            "sourceBytesEqual": exact_bytes, "pixelsEqual": bool(same_pixels),
            "pixelMeanAbsoluteError": float(np.abs(actual.astype(float) - expected).mean() / 255) if same_shape else None,
            "sourceMean": float(expected.mean() / 255), "exportedMean": float(actual.mean() / 255),
            "passed": bool(same_pixels and exact_bytes),
        })
    return {"status": "passed" if all(item["passed"] for item in results) else "failed",
            "verificationKind": "Offline standalone lunar AO byte/pixel comparison; not native visual acceptance",
            "source": {"path": str(SOURCE), "sha256": source_hash,
                       "dimensions": [expected.shape[1], expected.shape[0]]},
            "materials": results}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--materials", type=Path, default=DEFAULT_MATERIALS,
                        help="Directory containing MoonSurface.mat.glb and MoonCrater.mat.glb")
    parser.add_argument("--out", type=Path, help="Optional JSON report path")
    args = parser.parse_args()
    if args.out and (args.out.suffix.lower() != ".json" or args.out.resolve().is_relative_to(args.materials.resolve())):
        parser.error("--out must be a JSON report outside the material export directory")
    report = inspect(args.materials.resolve())
    text = json.dumps(report, indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(text, encoding="utf-8")
    print(text, end="")
    raise SystemExit(0 if report["status"] == "passed" else 1)


if __name__ == "__main__":
    main()
