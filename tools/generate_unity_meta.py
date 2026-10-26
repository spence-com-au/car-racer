#!/usr/bin/env python3
"""Create missing Unity .meta files under Unity/Assets with deterministic GUIDs.

Unity creates .meta files (holding each asset's GUID) when the editor imports a new file.
Code written outside the editor (e.g. by CI-only contributors) would otherwise get random
GUIDs on each machine's first import, causing churn and broken references. This script
derives the GUID from the asset path, so everyone gets the same one.

Usage: tools/generate_unity_meta.py [--check]
  --check  exit non-zero if any .meta file is missing or orphaned (for CI)
"""

import hashlib
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "Unity" / "Assets"

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

MONO_SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

ASSEMBLY_DEFINITION = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEXT_SCRIPT = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# Sound effects: decompressed on load (they're short and played often), mono, 3D-capable.
AUDIO = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.7
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# Music (anything under a Music folder): streamed rather than held in memory, stereo, 2D.
MUSIC = (
    AUDIO.replace("loadType: 0", "loadType: 2")
    .replace("quality: 0.7", "quality: 0.5")
    .replace("preloadAudioData: 1", "preloadAudioData: 0")
    .replace("forceToMono: 1", "forceToMono: 0")
    .replace("3D: 1", "3D: 0")
)

# Images (the app icon): no mipmaps, alpha kept as transparency; Unity fills in the rest.
TEXTURE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
  textureType: 0
  alphaIsTransparency: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""

DEFAULT = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

AUDIO_SUFFIXES = (".ogg", ".wav")

TEMPLATES = {
    ".cs": MONO_SCRIPT,
    ".asmdef": ASSEMBLY_DEFINITION,
    ".json": TEXT_SCRIPT,
    ".txt": TEXT_SCRIPT,
    ".md": TEXT_SCRIPT,
    ".bytes": TEXT_SCRIPT,
    ".ogg": AUDIO,
    ".wav": AUDIO,
    ".png": TEXTURE,
}


def template_for(path: Path) -> str:
    if path.suffix in AUDIO_SUFFIXES and "Music" in path.parts:
        return MUSIC
    return TEMPLATES.get(path.suffix, DEFAULT)


def guid_for(path: Path) -> str:
    relative = path.relative_to(ROOT).as_posix()
    return hashlib.md5(f"car-racer:{relative}".encode()).hexdigest()


def assets():
    for path in sorted(ASSETS.rglob("*")):
        if path.suffix == ".meta" or path.name.startswith("."):
            continue
        yield path


def main() -> int:
    check = "--check" in sys.argv[1:]
    missing = []
    for path in assets():
        meta = path.with_name(path.name + ".meta")
        if meta.exists():
            continue
        missing.append(meta)
        if not check:
            template = FOLDER if path.is_dir() else template_for(path)
            meta.write_text(template.format(guid=guid_for(path)), encoding="utf-8")

    orphaned = [
        meta for meta in sorted(ASSETS.rglob("*.meta")) if not meta.with_name(meta.name[: -len(".meta")]).exists()
    ]

    for meta in missing:
        print(f"{'missing' if check else 'created'}: {meta.relative_to(ROOT)}")
    for meta in orphaned:
        print(f"orphaned: {meta.relative_to(ROOT)}")

    if check and (missing or orphaned):
        print("Run tools/generate_unity_meta.py (and delete orphaned .meta files).", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
