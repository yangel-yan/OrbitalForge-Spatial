#!/usr/bin/env python3
"""Compare exported GLB material factors with an independently captured manifest."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys


TOLERANCE = 1e-5
DEFAULTS = {
    "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
    "emissiveFactor": [0.0, 0.0, 0.0],
    "metallicFactor": 1.0,
    "roughnessFactor": 1.0,
}
VECTOR_LENGTHS = {"baseColorFactor": 4, "emissiveFactor": 3}


def reject_nonfinite_json(value):
    raise ValueError(f"Non-finite JSON number: {value}")


def finite_json_float(value):
    number = float(value)
    if not math.isfinite(number):
        raise ValueError(f"JSON number exceeds finite float range: {value}")
    return number


def read_glb(raw):
    if len(raw) < 20:
        raise ValueError("GLB header or JSON chunk is truncated.")
    magic, version, length = struct.unpack_from("<III", raw)
    if magic != 0x46546C67 or version != 2 or length != len(raw):
        raise ValueError("Expected a GLB 2 file with a matching declared length.")
    offset = 12
    document = None
    while offset < length:
        if length - offset < 8:
            raise ValueError("Truncated GLB chunk header.")
        chunk_length, kind = struct.unpack_from("<II", raw, offset)
        offset += 8
        if chunk_length % 4 or offset + chunk_length > length:
            raise ValueError("Invalid GLB chunk length or alignment.")
        if kind == 0x4E4F534A:
            if document is not None or offset != 20:
                raise ValueError("GLB must start with exactly one JSON chunk.")
            text = raw[offset:offset + chunk_length].decode("utf-8").rstrip(" \t\r\n\0")
            document = json.loads(text, parse_float=finite_json_float, parse_constant=reject_nonfinite_json)
        offset += chunk_length
    if not isinstance(document, dict):
        raise ValueError("GLB JSON must contain an object.")
    # A material-only export can have only JSON; no BIN payload is required here.
    return document


def normalize_factor(field, value, label):
    count = VECTOR_LENGTHS.get(field)
    if count is not None:
        if not isinstance(value, list) or len(value) != count:
            raise ValueError(f"{label} must contain {count} numeric components.")
        values = value
    else:
        values = [value]
    if any(type(item) not in (int, float) or not math.isfinite(item) for item in values):
        raise ValueError(f"{label} must contain finite numbers.")
    return [float(item) for item in values] if count is not None else float(value)


def read_manifest(path):
    document = json.loads(path.read_text(encoding="utf-8-sig"),
                          parse_float=finite_json_float, parse_constant=reject_nonfinite_json)
    if not isinstance(document, dict) or not isinstance(document.get("materials"), list) or not document["materials"]:
        raise ValueError("Manifest must contain a nonempty materials array.")
    owned = {}
    for index, material in enumerate(document["materials"]):
        if not isinstance(material, dict) or not isinstance(material.get("name"), str) or not material["name"]:
            raise ValueError(f"Manifest materials[{index}] needs a nonempty name.")
        name = material["name"]
        if name in owned:
            raise ValueError(f"Duplicate owned material name in manifest: {name}")
        owned[name] = {
            field: normalize_factor(field, material.get(field), f"Manifest {name}.{field}")
            for field in DEFAULTS
        }
    return owned


def compare_materials(document, owned):
    materials = document.get("materials", [])
    if not isinstance(materials, list):
        raise ValueError("GLB materials must be an array.")
    checks, mismatches, passed_materials = [], [], []
    skipped = 0
    found = set()
    for index, material in enumerate(materials):
        if not isinstance(material, dict):
            raise ValueError(f"GLB materials[{index}] must be an object.")
        name = material.get("name")
        if not isinstance(name, str) or name not in owned:
            skipped += 1
            continue
        found.add(name)
        pbr = material.get("pbrMetallicRoughness", {})
        if not isinstance(pbr, dict):
            raise ValueError(f"GLB materials[{index}] {name}.pbrMetallicRoughness must be an object.")
        expected = owned[name]
        actual, differences = {}, {}
        material_passed = True
        for field, default in DEFAULTS.items():
            source = material if field == "emissiveFactor" else pbr
            value = source.get(field, default)
            try:
                actual[field] = normalize_factor(field, value, f"GLB {name}.{field}")
            except ValueError as error:
                actual[field] = value
                differences[field] = None
                mismatches.append({"index": index, "name": name, "field": field,
                                   "actual": value, "expected": expected[field], "reason": str(error)})
                material_passed = False
                continue
            if field in VECTOR_LENGTHS:
                delta = [abs(a - e) for a, e in zip(actual[field], expected[field])]
                maximum = max(delta)
            else:
                delta = abs(actual[field] - expected[field])
                maximum = delta
            differences[field] = delta
            if maximum > TOLERANCE:
                mismatches.append({"index": index, "name": name, "field": field,
                                   "actual": actual[field], "expected": expected[field],
                                   "max_abs_error": maximum})
                material_passed = False
        checks.append({"index": index, "name": name, "passed": material_passed,
                       "actual": actual, "expected": expected, "absolute_errors": differences})
        if material_passed:
            passed_materials.append({"index": index, "name": name})
    errors = [] if checks else ["No GLB material name matched the owned-material manifest."]
    return {
        "passed": bool(checks) and not mismatches,
        "owned_material_count": len(checks),
        "skipped_material_count": skipped,
        "missing_manifest_names": sorted(set(owned) - found),
        "passed_materials": passed_materials,
        "mismatches": mismatches,
        "checks": checks,
        "errors": errors,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--glb", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    glb, manifest, out = args.glb.resolve(), args.manifest.resolve(), args.out.resolve()
    for source in (glb, manifest):
        if out == source or (out.exists() and source.exists() and out.samefile(source)):
            parser.error("--out must not overwrite the GLB or the expected-value manifest.")
    report = {"status": "error", "passed": False, "glb": str(glb),
              "manifest": str(manifest), "glb_sha256": None, "tolerance": TOLERANCE,
              "passed_materials": [], "mismatches": [], "errors": []}
    try:
        raw = glb.read_bytes()
        report["glb_sha256"] = hashlib.sha256(raw).hexdigest()
        document = read_glb(raw)
        owned = read_manifest(manifest)
        report.update(compare_materials(document, owned))
        report["status"] = "passed" if report["passed"] else "failed"
        exit_code = 0 if report["passed"] else 1
    except (OSError, ValueError, OverflowError) as error:
        report["errors"] = [str(error)]
        exit_code = 2
    try:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    except OSError as error:
        print(f"Could not write verification report: {error}", file=sys.stderr)
        return 2
    print(json.dumps({"status": report["status"], "owned_material_count": report.get("owned_material_count", 0),
                      "passed_materials": report["passed_materials"], "mismatches": report["mismatches"],
                      "errors": report["errors"], "glb_sha256": report["glb_sha256"], "report": str(out)},
                     ensure_ascii=False, allow_nan=False))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
