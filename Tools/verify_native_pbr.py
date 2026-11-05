"""Read one exported GLB and verify this project's native PBR texture channels.

Usage: python Tools/verify_native_pbr.py --glb PATH --out REPORT.json
Requires numpy and Pillow. Never modifies the GLB, source textures, or SDK.
File timestamps are recorded as metadata, never used to infer scene content.
"""
import argparse
import hashlib
import io
import json
import struct
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from PIL import Image


REPOSITORY = Path(__file__).resolve().parents[1]
SOURCE_ORM = REPOSITORY / "Assets/SpaceBall/Art/Generated/OrbitalDock_Assets/PreparedTextures/SourceORM.jpg"
TOLERANCE = {
    "meanAbsolutePixelError": 10.0 / 255,
    "p95AbsolutePixelError": 30.0 / 255,
    "absoluteMeanDrift": 6.0 / 255,
    "p01AndP99Drift": 15.0 / 255,
}


def identity(path, payload):
    return {
        "path": str(path.resolve()), "bytes": len(payload),
        "sha256": hashlib.sha256(payload).hexdigest(),
        "lastWriteUtc": datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat(),
    }


def read_glb(path):
    payload = path.read_bytes()
    if len(payload) < 20:
        raise ValueError("GLB is shorter than its header and JSON chunk.")
    magic, version, length = struct.unpack_from("<III", payload)
    if magic != 0x46546C67 or version != 2 or length != len(payload):
        raise ValueError("Invalid GLB 2.0 header or declared length.")
    chunks = []
    offset = 12
    while offset < len(payload):
        if offset + 8 > len(payload):
            raise ValueError("Truncated GLB chunk header.")
        size, kind = struct.unpack_from("<II", payload, offset)
        offset += 8
        if offset + size > len(payload):
            raise ValueError("GLB chunk extends beyond the file.")
        chunks.append((kind, payload[offset:offset + size]))
        offset += size
    if not chunks or chunks[0][0] != 0x4E4F534A:
        raise ValueError("GLB first chunk must be JSON.")
    document = json.loads(chunks[0][1].decode("utf-8"))
    binary = [data for kind, data in chunks if kind == 0x004E4942]
    if len(binary) != 1 or len(document.get("buffers", [])) != 1:
        raise ValueError("This verifier requires a GLB with one embedded binary buffer.")
    declared = document["buffers"][0]["byteLength"]
    if document["buffers"][0].get("uri") or not declared <= len(binary[0]) <= declared + 3:
        raise ValueError("Invalid embedded GLB buffer length or external buffer URI.")
    return document, binary[0], identity(path, payload), len(chunks[0][1])


def view_bytes(document, binary, index):
    view = document["bufferViews"][index]
    start, length = view.get("byteOffset", 0), view["byteLength"]
    if view.get("buffer", 0) != 0 or start < 0 or length < 0 or start + length > len(binary):
        raise ValueError("Image or mesh bufferView lies outside the embedded buffer.")
    return binary[start:start + length]


def texture_image(document, binary, info):
    texture_index = info["index"]
    texture = document["textures"][texture_index]
    image_index = texture["source"]
    record = document["images"][image_index]
    if "bufferView" not in record:
        raise ValueError("This verifier requires embedded texture images.")
    data = view_bytes(document, binary, record["bufferView"])
    with Image.open(io.BytesIO(data)) as image:
        pixels = np.asarray(image.convert("RGBA"), dtype=np.float64) / 255.0
    return pixels, {
        "textureIndex": texture_index, "imageIndex": image_index,
        "name": record.get("name"), "mimeType": record.get("mimeType"),
        "width": pixels.shape[1], "height": pixels.shape[0],
        "encodedBytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
    }


def statistics(channel):
    percentiles = np.percentile(channel, [0, 1, 50, 99, 100])
    return {
        "minimum": float(percentiles[0]), "p01": float(percentiles[1]),
        "median": float(percentiles[2]), "p99": float(percentiles[3]),
        "maximum": float(percentiles[4]), "mean": float(channel.mean()),
        "standardDeviation": float(channel.std()),
        "zeroFraction": float(np.mean(channel <= .5 / 255)),
        "oneFraction": float(np.mean(channel >= 254.5 / 255)),
    }


def source_at_pixel_centers(source, width, height):
    """Match ConvertOrm's normalized half-pixel bilinear sampling, no gamma transform."""
    x = (np.arange(width) + .5) * source.shape[1] / width - .5
    y = (np.arange(height) + .5) * source.shape[0] / height - .5
    x = np.clip(x, 0, source.shape[1] - 1)
    y = np.clip(y, 0, source.shape[0] - 1)
    x0, y0 = x.astype(int), y.astype(int)
    x1, y1 = np.minimum(x0 + 1, source.shape[1] - 1), np.minimum(y0 + 1, source.shape[0] - 1)
    fx, fy = (x - x0)[None, :, None], (y - y0)[:, None, None]
    top = source[y0[:, None], x0[None, :]] * (1 - fx) + source[y0[:, None], x1[None, :]] * fx
    bottom = source[y1[:, None], x0[None, :]] * (1 - fx) + source[y1[:, None], x1[None, :]] * fx
    return top * (1 - fy) + bottom * fy


def compare_channel(exported, expected):
    actual_stats, expected_stats = statistics(exported), statistics(expected)
    error = np.abs(exported - expected)
    metrics = {
        "meanAbsolutePixelError": float(error.mean()),
        "p95AbsolutePixelError": float(np.percentile(error, 95)),
        "absoluteMeanDrift": abs(actual_stats["mean"] - expected_stats["mean"]),
        "p01Drift": abs(actual_stats["p01"] - expected_stats["p01"]),
        "p99Drift": abs(actual_stats["p99"] - expected_stats["p99"]),
        "verticallyFlippedSourceMeanAbsoluteError": float(np.abs(exported - expected[::-1]).mean()),
    }
    issues = []
    if actual_stats["maximum"] <= 1 / 255 < expected_stats["p99"]:
        issues.append("Exported channel is entirely zero or almost zero while the source contains values.")
    if metrics["meanAbsolutePixelError"] > TOLERANCE["meanAbsolutePixelError"]:
        issues.append("Pixel mean absolute error exceeds compression/resampling tolerance.")
    if metrics["p95AbsolutePixelError"] > TOLERANCE["p95AbsolutePixelError"]:
        issues.append("Pixel p95 absolute error exceeds compression/resampling tolerance.")
    if metrics["absoluteMeanDrift"] > TOLERANCE["absoluteMeanDrift"]:
        issues.append("Channel mean drift exceeds tolerance.")
    if max(metrics["p01Drift"], metrics["p99Drift"]) > TOLERANCE["p01AndP99Drift"]:
        issues.append("Channel p01/p99 range differs beyond tolerance.")
    return {"status": "failed" if issues else "passed", "actual": actual_stats,
            "expectedResampledSource": expected_stats, "comparison": metrics, "issues": issues}


def allocation_summary(document, binary, json_bytes):
    image_payloads = {}
    image_bytes = 0
    image_views = set()
    for image in document.get("images", []):
        data = view_bytes(document, binary, image["bufferView"])
        image_views.add(image["bufferView"])
        image_bytes += len(data)
        digest = hashlib.sha256(data).hexdigest()
        if digest not in image_payloads:
            image_payloads[digest] = {"sha256": digest, "eachBytes": len(data), "copies": 0, "names": []}
        group = image_payloads[digest]
        group["copies"] += 1
        if image.get("name") not in group["names"]:
            group["names"].append(image.get("name"))
    vertex_accessors, index_accessors, position_accessors = set(), set(), set()
    primitive_count = 0
    for mesh in document.get("meshes", []):
        for primitive in mesh["primitives"]:
            primitive_count += 1
            vertex_accessors.update(primitive["attributes"].values())
            position_accessors.add(primitive["attributes"]["POSITION"])
            if "indices" in primitive:
                index_accessors.add(primitive["indices"])
    def buffer_views(accessors):
        return {document["accessors"][i]["bufferView"] for i in accessors
                if "bufferView" in document["accessors"][i]}
    vertices, indices = buffer_views(vertex_accessors), buffer_views(index_accessors)
    def byte_count(views):
        return sum(len(view_bytes(document, binary, i)) for i in views)
    unique_image_bytes = sum(group["eachBytes"] for group in image_payloads.values())
    return {
        "jsonChunkBytes": json_bytes, "embeddedBinaryBytes": len(binary),
        "nodeCount": len(document.get("nodes", [])), "meshCount": len(document.get("meshes", [])),
        "primitiveCount": primitive_count,
        "uniquePositionAccessorVertexCount": sum(document["accessors"][i]["count"] for i in position_accessors),
        "meshVertexBufferViewBytes": byte_count(vertices),
        "meshIndexBufferViewBytes": byte_count(indices),
        "meshBufferViewBytesCombined": byte_count(vertices | indices),
        "imageCount": len(document.get("images", [])),
        "imagePayloadBytesIncludingDuplicates": image_bytes,
        "imageBufferViewBytesUniqueViews": byte_count(image_views),
        "uniqueImagePayloadCount": len(image_payloads), "uniqueImagePayloadBytes": unique_image_bytes,
        "duplicateImagePayloadBytes": image_bytes - unique_image_bytes,
        "largestDuplicateImageGroups": sorted(image_payloads.values(), key=lambda item: item["eachBytes"] * item["copies"], reverse=True)[:8],
    }


def verify(glb):
    document, binary, artifact, json_bytes = read_glb(glb)
    source_bytes = SOURCE_ORM.read_bytes()
    with Image.open(io.BytesIO(source_bytes)) as image:
        source = np.asarray(image.convert("RGB"), dtype=np.float64) / 255
    report = {
        "artifact": artifact, "sourceORM": identity(SOURCE_ORM, source_bytes),
        "timeInterpretation": "Timestamps are metadata only; save/export time ordering does not establish scene content freshness.",
        "method": "Compare glTF G=roughness/B=metallic against source ORM G/B, bilinearly resampled at normalized pixel centers. No gamma conversion. Vertical-flip error is diagnostic only and is never substituted to obtain a pass.",
        "toleranceNormalized0To1": TOLERANCE,
        "sourceChannelStatistics": {"roughnessG": statistics(source[:, :, 1]), "metallicB": statistics(source[:, :, 2])},
        "materials": [], "moonOcclusion": [],
        "allocation": allocation_summary(document, binary, json_bytes),
    }
    for index, material in enumerate(document.get("materials", [])):
        if material.get("name") == "OrbitalDock_Lit":
            pbr = material.get("pbrMetallicRoughness", {})
            result = {"materialIndex": index, "name": material["name"],
                      "metallicFactor": pbr.get("metallicFactor", 1), "roughnessFactor": pbr.get("roughnessFactor", 1)}
            if "metallicRoughnessTexture" not in pbr:
                result.update(status="failed", issues=["Material has no metallicRoughnessTexture."])
            else:
                pixels, info = texture_image(document, binary, pbr["metallicRoughnessTexture"])
                expected = source_at_pixel_centers(source, pixels.shape[1], pixels.shape[0])
                result.update(texture=info, roughnessG=compare_channel(pixels[:, :, 1], expected[:, :, 1]),
                              metallicB=compare_channel(pixels[:, :, 2], expected[:, :, 2]))
                result["status"] = "passed" if all(result[key]["status"] == "passed" for key in ["roughnessG", "metallicB"]) else "failed"
            report["materials"].append(result)
        if material.get("name") == "MoonSurface":
            moon = {"materialIndex": index, "name": material["name"], "requiredForMetallicMappingCheck": False}
            if "occlusionTexture" not in material:
                moon["status"] = "not_present"
            else:
                info = material["occlusionTexture"]
                pixels, image_info = texture_image(document, binary, info)
                strength = info.get("strength", 1)
                moon.update(status="reported" if np.isfinite(strength) and 0 <= strength <= 1 else "invalid_strength",
                            strength=strength, texture=image_info, redChannel=statistics(pixels[:, :, 0]),
                            effectiveOcclusion=statistics(1 + strength * (pixels[:, :, 0] - 1)))
            report["moonOcclusion"].append(moon)
    report["status"] = "passed" if report["materials"] and all(item["status"] == "passed" for item in report["materials"]) else "failed"
    if not report["materials"]:
        report["issues"] = ["No material named OrbitalDock_Lit was found in this artifact."]
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--glb", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    if args.out.resolve() == args.glb.resolve():
        parser.error("--out must not overwrite the GLB being verified.")
    try:
        report = verify(args.glb)
    except (OSError, ValueError, KeyError, IndexError, TypeError, struct.error) as error:
        report = {"status": "error", "artifactPath": str(args.glb.resolve()),
                  "errorType": type(error).__name__, "error": str(error)}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
    print(json.dumps({"status": report["status"], "report": str(args.out.resolve()),
                      "artifactSha256": report.get("artifact", {}).get("sha256")}, ensure_ascii=True))
    return 0 if report["status"] == "passed" else 2 if report["status"] == "error" else 1


if __name__ == "__main__":
    raise SystemExit(main())
