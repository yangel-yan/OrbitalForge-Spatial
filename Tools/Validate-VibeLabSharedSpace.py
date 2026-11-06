#!/usr/bin/env python3
"""Read-only validation of Orbital Forge's Shared Space APK and exported assets."""

import argparse
import hashlib
import json
import math
import re
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


PROJECT = Path(__file__).resolve().parents[1]
ANDROID = "{http://schemas.android.com/apk/res/android}"
ACTIVITY = "com.unity3d.player.SpatialAdapterPlayerActivity"
CAMERA = "Assets/SpaceBall/Scenes/Orbital_SharedSpace.asset"
RUNTIME = "Assets/Settings/SpatialAdapterRuntimeSettings.asset"
SPATIAL_PREFIX = "assets/SpatialAdapterExport/"
REQUIRED_SPATIAL = {
    SPATIAL_PREFIX + "SpatialAssetBundle.bundle",
    SPATIAL_PREFIX + "Assets/SpaceBall/Scenes/OrbitalCourier_Prototype.unity.glb",
}


class ValidationError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise ValidationError(message)


def digest(stream):
    sha = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        sha.update(block)
    return sha.hexdigest()


def aapt(aapt2, apk, *arguments):
    result = subprocess.run(
        [str(aapt2), "dump", *arguments, str(apk)], capture_output=True, timeout=60
    )
    require(result.returncode == 0, f"aapt2 dump {arguments[0]} failed (exit={result.returncode})")
    return result.stdout.decode("utf-8").replace("\r\n", "\n")


def manifest_tree(text):
    """Parse the indentation and attributes of aapt2's binary-XML dump once."""
    wrapper = ET.Element("dump")
    stack = [(-1, wrapper)]
    for line in text.splitlines():
        element = re.match(r"^(\s*)E: ([\w.-]+)(?: \(line=\d+\))?$", line)
        if element:
            depth = len(element[1])
            while stack[-1][0] >= depth:
                stack.pop()
            node = ET.SubElement(stack[-1][1], element[2])
            stack.append((depth, node))
            continue
        attribute = re.match(
            r"^\s*A: (?:http://schemas\.android\.com/apk/res/android:)?"
            r"([\w.-]+)(?:\(0x[0-9a-f]+\))?=(.*)$", line
        )
        if attribute:
            raw = attribute[2]
            if raw.startswith('"'):
                value, _ = json.JSONDecoder().raw_decode(raw)
            elif re.fullmatch(r"0x[0-9a-f]+", raw):
                value = str(int(raw, 16))
            else:
                value = raw
            stack[-1][1].set(attribute[1], value)
    manifest = wrapper.find("manifest")
    require(manifest is not None, "aapt2 did not return a manifest tree")
    return manifest


def metadata(activity, prefix=""):
    result = {}
    for node in activity.findall("meta-data"):
        name = node.get(prefix + "name", "")
        if name.startswith("pico.spatial.windowcontainer."):
            require(name not in result, f"Duplicate Shared Space metadata: {name}")
            value = node.get(prefix + "value")
            require(value is not None, f"Missing Shared Space metadata value: {name}")
            result[name] = value
    return result


def validate_camera(expected):
    runtime = (PROJECT / RUNTIME).read_text(encoding="utf-8-sig")
    camera = (PROJECT / CAMERA).read_text(encoding="utf-8-sig")
    meta = (PROJECT / (CAMERA + ".meta")).read_text(encoding="utf-8-sig")
    require(re.search(r"^  m_enableSpatialAdapter: 1$", runtime, re.M), "Spatial Adapter is disabled")
    guid = re.search(r"^guid: ([0-9a-f]+)$", meta, re.M)
    require(guid is not None, "Shared Space camera asset GUID is missing")
    reference = re.search(r"^  m_defaultSpatialCameraConfiguration:.*guid: ([0-9a-f]+)", runtime, re.M)
    require(reference is not None and reference[1] == guid[1], "Runtime settings reference another camera configuration")
    require(re.search(r"^  mode: 0$", camera, re.M), "Camera configuration is not Shared Space (mode 0)")
    dimensions = re.search(r"^  outputDimensions: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}$", camera, re.M)
    require(dimensions is not None, "Shared Space output dimensions are missing")
    values = [float(dimensions[i]) for i in (1, 2, 3)]
    require(all(math.isfinite(v) and v > 0 for v in values), "Invalid Shared Space output dimensions")
    sizes = {}
    for key in ("defaultsize", "minsize", "maxsize"):
        raw = expected.get("pico.spatial.windowcontainer." + key, "")
        require(re.fullmatch(r"[1-9]\d*x[1-9]\d*x[1-9]\d*", raw), f"Invalid container {key}")
        sizes[key] = [int(v) for v in raw.split("x")]
    ratios = [size / dimension for size, dimension in zip(sizes["defaultsize"], values)]
    require(all(math.isclose(ratio, ratios[0], rel_tol=0.001) for ratio in ratios), "Exported container proportions differ from the Shared Space camera")
    require(all(low <= default <= high for low, default, high in zip(sizes["minsize"], sizes["defaultsize"], sizes["maxsize"])), "Container default size is outside its resize bounds")
    return {"configuration": CAMERA, "runtime_settings": RUNTIME, "mode": 0, "output_dimensions": values, "container_units_per_configuration_unit": ratios[0]}


def native_inventory(archive):
    libraries = {}
    for info in archive.infolist():
        if not info.filename.startswith("lib/") or not info.filename.endswith(".so"):
            continue
        require(re.fullmatch(r"lib/arm64-v8a/[^/]+\.so", info.filename), "Unexpected native ABI or path: " + info.filename)
        with archive.open(info) as stream:
            header = stream.read(64)
            require(len(header) == 64 and header[:6] == b"\x7fELF\x02\x01" and struct.unpack_from("<H", header, 18)[0] == 183, "Native library is not ELF64 little-endian AArch64: " + info.filename)
        with archive.open(info) as stream:
            libraries[info.filename] = {"sha256": digest(stream), "size": info.file_size}
    require({"lib/arm64-v8a/" + name for name in ("libil2cpp.so", "libunity.so", "libmain.so")} <= libraries.keys(), "Required Unity ARM64 native libraries are missing")
    return libraries


def validate(args, report):
    badging = aapt(args.aapt2, args.apk, "badging")
    package = re.search(r"^package: name='([^']+)' versionCode='(\d+)' versionName='([^']+)'", badging, re.M)
    label = re.search(r"^application-label:'(.*)'$", badging, re.M)
    launch = re.search(r"^launchable-activity: name='([^']+)'", badging, re.M)
    require(package is not None and package[1] == args.expected_package and package[3] == "0.4.4", "APK package/version differs from the explicitly expected package / 0.4.4")
    require(label is not None and label[1] == "星轨重构", "APK application label must be 星轨重构")
    require(launch is not None and launch[1] == ACTIVITY, "APK launcher must be SpatialAdapterPlayerActivity")
    debuggable = "application-debuggable" in badging.splitlines()
    report["identity"] = {"package": package[1], "version_code": int(package[2]), "version_name": package[3], "label": label[1], "launcher": launch[1], "debuggable": debuggable}
    if debuggable:
        report["warnings"].append("APK is debuggable; this Shared Space check does not establish Release readiness.")

    actual_manifest = manifest_tree(aapt(args.aapt2, args.apk, "xmltree", "--file", "AndroidManifest.xml"))
    actual_activities = [node for node in actual_manifest.findall("application/activity") if node.get("name") == ACTIVITY]
    require(len(actual_activities) == 1, "Expected exactly one Spatial Adapter launcher activity")
    actual = metadata(actual_activities[0])
    source_manifest = args.export_root / "unityLibrary/src/main/AndroidManifest.xml"
    source = ET.parse(source_manifest).getroot()
    source_activities = [node for node in source.findall("application/activity") if node.get(ANDROID + "name") == ACTIVITY]
    require(len(source_activities) == 1, "Gradle export must contain the Spatial Adapter launcher activity")
    expected = metadata(source_activities[0], ANDROID)
    require(expected.get("pico.spatial.windowcontainer.style") == "2", "Gradle export must use volumetric windowcontainer.style=2")
    require(actual == expected, "APK Shared Space metadata differs from the supplied Gradle export")
    report["camera"] = validate_camera(expected)
    report["spatial_metadata"] = actual

    assets = args.export_root / "unityLibrary/src/main/assets"
    spatial = assets / "SpatialAdapterExport"
    require(spatial.is_dir(), "Gradle export has no SpatialAdapterExport assets")
    exported = {"assets/" + path.relative_to(assets).as_posix(): path for path in spatial.rglob("*") if path.is_file()}
    require(REQUIRED_SPATIAL <= exported.keys(), "Gradle export lacks the Spatial bundle or loose gameplay scene")
    with zipfile.ZipFile(args.apk) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)), "APK contains duplicate ZIP entries")
        packaged = {name for name in names if name.startswith(SPATIAL_PREFIX) and not name.endswith("/")}
        require(packaged == exported.keys(), "APK Spatial asset file set differs from the supplied Gradle export")
        inventory = {}
        for name, path in sorted(exported.items()):
            require(path.stat().st_size > 0, "Empty exported Spatial asset: " + name)
            with path.open("rb") as stream:
                expected_hash = digest(stream)
            with archive.open(name) as stream:
                actual_hash = digest(stream)
            require(actual_hash == expected_hash, "APK Spatial asset SHA256 mismatch: " + name)
            inventory[name] = {"sha256": actual_hash, "size": path.stat().st_size}
        report["spatial_assets"] = inventory
        report["native_libraries"] = native_inventory(archive)

    if args.before_apk:
        with zipfile.ZipFile(args.before_apk) as archive:
            before = native_inventory(archive)
        require(before == report["native_libraries"], "Before/after APK native library names, sizes or SHA256 differ")
        report["before_after_native_match"] = True
        report["before_apk"] = args.before_apk.name
    report["ok"] = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--export-root", type=Path, required=True)
    parser.add_argument("--aapt2", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--expected-package", required=True)
    parser.add_argument("--before-apk", type=Path)
    args = parser.parse_args()
    report = {"ok": False, "apk": args.apk.name, "expected_package": args.expected_package, "export_root": "<export-root>", "scope": "Shared Space identity, metadata, exported Spatial assets and ARM64 native libraries; not a freshness or runtime certificate", "warnings": [], "errors": []}
    try:
        validate(args, report)
    except (ValidationError, zipfile.BadZipFile, ET.ParseError, UnicodeDecodeError, ValueError) as error:
        report["errors"].append(str(error))
    except OSError as error:
        report["errors"].append(f"File/tool operation failed: {type(error).__name__}")
    except subprocess.TimeoutExpired:
        report["errors"].append("aapt2 exceeded the 60-second timeout")
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"ok": report["ok"], "spatial_file_count": len(report.get("spatial_assets", {})), "native_library_count": len(report.get("native_libraries", {})), "debuggable": report.get("identity", {}).get("debuggable"), "errors": report["errors"], "warnings": report["warnings"]}, ensure_ascii=True))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
