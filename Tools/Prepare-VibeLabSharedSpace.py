#!/usr/bin/env python3
"""Copy an already precompiled Release export into a new portable Method B directory."""

import argparse
import copy
import hashlib
import json
import re
import shutil
import sys
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone
from pathlib import Path


PROJECT = Path(__file__).resolve().parents[1]
EXCLUDED_DIRECTORIES = {".git", "build", ".gradle", ".cxx", "symbols", "jniStaticLibs", "Il2CppOutputProject"}
EXCLUDED_SUFFIXES = {".apk", ".aab", ".keystore", ".jks"}
LOCAL_PROPERTIES = {"unityProjectPath", "unity.projectPath", "unity.androidSdkPath", "unity.androidNdkPath", "unity.jdkPath"}
HOST_FILE_LIMIT = 100 * 1024 * 1024
NATIVE = Path("unityLibrary/src/main/jniLibs/arm64-v8a")
SPATIAL = Path("unityLibrary/src/main/assets/SpatialAdapterExport")
METADATA = Path("unityLibrary/src/main/assets/bin/Data/Managed/Metadata/global-metadata.dat")


class PreparationError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise PreparationError(message)


def excluded(path):
    return (
        path.name in EXCLUDED_DIRECTORIES
        or path.name.endswith("BurstDebugInformation_DoNotShip")
        or path.name == "local.properties"
        or path.suffix.lower() in EXCLUDED_SUFFIXES
    )


def copy_ignore(directory, names):
    return [name for name in names if excluded(Path(directory) / name)]


def sha256(path):
    with path.open("rb") as stream:
        return stream_sha256(stream)


def stream_sha256(stream):
    sha = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        sha.update(block)
    return sha.hexdigest()


def inventory(root, paths):
    return {path.relative_to(root).as_posix(): {"size": path.stat().st_size, "sha256": sha256(path)} for path in sorted(paths)}


def properties(text):
    return dict(line.split("=", 1) for line in text.splitlines() if "=" in line and not line.lstrip().startswith("#"))


def baseline_native_inventory(apk):
    result = {}
    with zipfile.ZipFile(apk) as archive:
        for info in archive.infolist():
            if re.fullmatch(r"lib/arm64-v8a/[^/]+\.so", info.filename):
                name = Path(info.filename).name
                require(name not in result, "Duplicate baseline native library: " + name)
                require(info.file_size <= HOST_FILE_LIMIT, "Baseline native library exceeds GitHub's 100 MiB limit: " + name)
                with archive.open(info) as stream:
                    result[name] = {"size": info.file_size, "sha256": stream_sha256(stream)}
    require(result, "Baseline Release APK contains no ARM64 native libraries")
    return result


def verify_aar_native(raw, final, source_names):
    origins = {}
    for aar in sorted((raw / "unityLibrary/libs").glob("*.aar")):
        with zipfile.ZipFile(aar) as archive:
            for info in archive.infolist():
                if not re.fullmatch(r"jni/arm64-v8a/[^/]+\.so", info.filename):
                    continue
                name = Path(info.filename).name
                require(name not in origins and name not in source_names, "Duplicate native source requires explicit resolution: " + name)
                with archive.open(info) as stream:
                    entry = {"size": info.file_size, "sha256": stream_sha256(stream)}
                require(final.get(name) == entry, "AAR native bytes differ from the baseline Release APK: " + name)
                origins[name] = {"aar": aar.relative_to(raw).as_posix(), **entry}
    require(set(final) == source_names | set(origins), "Baseline APK native libraries are not fully accounted for by jniLibs and AAR inputs")
    return origins


def keep_native_bytes(text):
    if not re.search(r"keepDebugSymbols\.add\(['\"]\*\*/\*\.so['\"]\)", text):
        text = text.rstrip() + "\n\n// Native inputs are the already verified final Release APK bytes.\nandroid.packaging.jniLibs.keepDebugSymbols.add('**/*.so')\n"
    return text


def portable_library(text):
    text = text.replace("\r\n", "\n")
    require(text.count("def getSdkDir()") == 1, "Expected the Unity template's single IL2CPP helper tail")
    text = text.split("def getSdkDir()", 1)[0]
    text, count = re.subn(
        r"^tasks\.register\(['\"]buildIl2Cpp['\"]\)\s*\{.*?^\}",
        "tasks.register('buildIl2Cpp') {}\ntasks.named('buildIl2Cpp').configure { enabled = false }",
        text, count=1, flags=re.M | re.S,
    )
    require(count == 1, "Expected the original buildIl2Cpp task")
    text, count = re.subn(r"^(\s*)jni\.srcDirs\s*=\s*\[[^\r\n]*\]\s*$", r"\1jni.srcDirs = []", text, count=1, flags=re.M)
    require(count == 1, "Expected a single Unity jni.srcDirs declaration")
    if not re.search(r"res\.srcDirs\s*\+=.*launcher/src/main/res", text):
        text = text.replace("jni.srcDirs = []", "jni.srcDirs = []\n           res.srcDirs += rootProject.file('launcher/src/main/res')", 1)
    return keep_native_bytes(re.sub(r"^\s*ndkPath\s+[^\n]*\n", "", text, flags=re.M).rstrip() + "\n")


def merge_strings(output):
    launcher = output / "launcher/src/main/res/values/strings.xml"
    library = output / "unityLibrary/src/main/res/values/strings.xml"
    target = ET.parse(launcher).getroot()
    existing = {node.get("name"): node for node in target}
    if library.exists():
        for node in ET.parse(library).getroot():
            name = node.get("name")
            require(name is not None, "Unnamed library string resource")
            candidate = copy.deepcopy(node)
            candidate.tail = None
            if name in existing:
                prior = copy.deepcopy(existing[name])
                prior.tail = None
                require(ET.tostring(prior) == ET.tostring(candidate), "Conflicting string resource: " + name)
            else:
                target.append(candidate)
                existing[name] = candidate
        library.unlink()
    require({"app_name", "game_view_content_description"} <= existing.keys(), "Shared launcher resources lack app_name or game_view_content_description")
    ET.indent(target, space="  ")
    ET.ElementTree(target).write(launcher, encoding="utf-8", xml_declaration=True)


def prepare(raw, output):
    require(raw.is_dir(), "Export root does not exist")
    require(not output.exists(), "Output must be a new, nonexistent directory")
    require(not output.is_relative_to(raw), "Output must be outside the raw export")
    for relative in ["settings.gradle", "build.gradle", "gradle.properties", "unityLibrary/build.gradle", "launcher/build.gradle", METADATA.as_posix(), (NATIVE / "libil2cpp.so").as_posix(), (SPATIAL / "SpatialAssetBundle.bundle").as_posix(), "launcher/build/outputs/apk/release/launcher-release.apk"]:
        require((raw / relative).is_file(), "Required precompiled Release export file is missing: " + relative)

    copied = []
    for path in raw.rglob("*"):
        relative = path.relative_to(raw)
        if any(excluded(Path(part)) for part in relative.parts):
            continue
        require(not path.is_symlink(), "Export contains a nonportable symlink: " + relative.as_posix())
        if path.is_file():
            require(path.stat().st_size <= HOST_FILE_LIMIT, "File exceeds GitHub's 100 MiB limit; no binary alteration was performed: " + relative.as_posix())
            copied.append(path)
    source_inventory = inventory(raw, copied)
    native = inventory(raw, list((raw / NATIVE).glob("*.so")))
    spatial = inventory(raw, [path for path in (raw / SPATIAL).rglob("*") if path.is_file()])
    require(native and spatial, "Native or Spatial runtime inventory is empty")
    baseline_apk = raw / "launcher/build/outputs/apk/release/launcher-release.apk"
    final_native = baseline_native_inventory(baseline_apk)
    source_names = {Path(name).name for name in native}
    require(source_names <= final_native.keys(), "Baseline Release APK lacks an exported jniLibs native library")
    aar_native = verify_aar_native(raw, final_native, source_names)
    props = properties((raw / "gradle.properties").read_text(encoding="utf-8-sig"))
    required_properties = {"unity.applicationId", "unity.versionName", "unity.versionCode", "unity.abiFilters", "unity.targetSdkVersion", "unity.androidNdkVersion", "unityTemplateVersion"}
    require(required_properties <= props.keys(), "Unity export identity/toolchain properties are incomplete")
    require(props["unity.abiFilters"] == "arm64-v8a", "Export must contain ARM64 only")

    shutil.copytree(raw, output, ignore=copy_ignore)
    library_gradle = output / "unityLibrary/build.gradle"
    library_gradle.write_text(portable_library(library_gradle.read_text(encoding="utf-8-sig")), encoding="utf-8")
    launcher_gradle = output / "launcher/build.gradle"
    launcher_gradle.write_text(keep_native_bytes(re.sub(r"^\s*ndkPath\s+[^\n]*\n", "", launcher_gradle.read_text(encoding="utf-8-sig"), flags=re.M)), encoding="utf-8")
    gradle_properties = output / "gradle.properties"
    lines = gradle_properties.read_text(encoding="utf-8-sig").splitlines()
    gradle_properties.write_text("\n".join(line for line in lines if line.split("=", 1)[0].strip() not in LOCAL_PROPERTIES) + "\n", encoding="utf-8")
    root_gradle = output / "build.gradle"
    text = root_gradle.read_text(encoding="utf-8-sig")
    if not re.search(r"tasks\.register\(['\"]assembleRelease['\"]", text):
        text = text.rstrip() + "\n\ntasks.register('assembleRelease') {\n    dependsOn(':launcher:assembleRelease')\n}\n"
        root_gradle.write_text(text, encoding="utf-8")
    merge_strings(output)

    with zipfile.ZipFile(baseline_apk) as archive:
        for relative in native:
            name = Path(relative).name
            with archive.open("lib/arm64-v8a/" + name) as source, (output / relative).open("wb") as destination:
                shutil.copyfileobj(source, destination)
    processed_native = inventory(output, list((output / NATIVE).glob("*.so")))
    require(all(entry == final_native[Path(relative).name] for relative, entry in processed_native.items()), "Processed native bytes differ from the verified baseline Release APK")
    require(spatial == inventory(output, [path for path in (output / SPATIAL).rglob("*") if path.is_file()]), "Copied Spatial asset bytes differ from the raw export")
    for path in list(output.rglob("*.gradle")) + list(output.rglob("*.properties")):
        require(not re.search(r"(?<![A-Za-z0-9+.-])[A-Za-z]:[/\\]", path.read_text(encoding="utf-8-sig")), "Remaining Windows absolute path in " + path.relative_to(output).as_posix())
    version_file = (PROJECT / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8-sig")
    version = re.search(r"^m_EditorVersion: (\S+)$", version_file, re.M)
    require(version is not None, "Unity Editor version is missing")
    manifest = {
        "schema": 1,
        "processor_revision": "orbital-shared-space-method-b-v2-final-native-bytes",
        "route": "PICO Shared Space / Spatial Adapter",
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "unity_version": version[1],
        "unity_template_version": props["unityTemplateVersion"],
        "ndk_version": props["unity.androidNdkVersion"],
        "abi": ["arm64-v8a"],
        "target_sdk": int(props["unity.targetSdkVersion"]),
        "application_id": props["unity.applicationId"],
        "version_name": props["unity.versionName"],
        "version_code": int(props["unity.versionCode"]),
        "il2cpp_so_sha256": processed_native[(NATIVE / "libil2cpp.so").as_posix()]["sha256"],
        "global_metadata_sha256": sha256(output / METADATA),
        "native_byte_source": "Final bytes from the verified baseline Release APK; packaging is configured to preserve them without another strip pass.",
        "native_inventory": [{"name": Path(name).name, **entry} for name, entry in processed_native.items()],
        "raw_source_native_inventory": [{"name": Path(name).name, **entry} for name, entry in native.items()],
        "baseline_apk_native_inventory": {"lib/arm64-v8a/" + name: entry for name, entry in sorted(final_native.items())},
        "aar_native_inventory": aar_native,
        "spatial_asset_inventory": spatial,
        "source_identity": {
            "application_id": props["unity.applicationId"],
            "version_name": props["unity.versionName"],
            "version_code": int(props["unity.versionCode"]),
            "copied_file_count": len(source_inventory),
            "copied_file_inventory_sha256": hashlib.sha256(json.dumps(source_inventory, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest(),
            "baseline_apk_sha256": sha256(baseline_apk),
        },
        "post_slim_release_build": "required; not executed by this script",
    }
    (output / ".vibelab-prebuilt.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return {"ok": True, "application_id": manifest["application_id"], "version_name": manifest["version_name"], "native_library_count": len(native), "spatial_file_count": len(spatial), "manifest": ".vibelab-prebuilt.json", "post_slim_release_build": "required"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--export-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = prepare(args.export_root.resolve(), args.output.resolve())
    except (PreparationError, ET.ParseError, UnicodeDecodeError, ValueError, zipfile.BadZipFile) as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=True))
        return 1
    except OSError as error:
        print(json.dumps({"ok": False, "error": f"File operation failed: {type(error).__name__}; any new output was preserved for inspection"}))
        return 1
    print(json.dumps(result, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
