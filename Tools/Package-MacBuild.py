"""Package a Unity macOS .app on Windows with Unix execution permissions intact.

Usage: python Tools/Package-MacBuild.py Builds/CatQuest-Mac-YYYYMMDD
The input directory must contain Cat Quest.app. Never modifies the application.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import stat
import struct
import time
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("build_directory", type=Path)
    args = parser.parse_args()
    root = args.build_directory.resolve(strict=True)
    app = root / "Cat Quest.app"
    with (app / "Contents/Info.plist").open("rb") as source:
        info = plistlib.load(source)
    executable = app / "Contents/MacOS" / info["CFBundleExecutable"]
    with executable.open("rb") as source:
        magic, cpu = struct.unpack("<II", source.read(8))
    if magic != 0xFEEDFACF or cpu != 0x0100000C:
        raise ValueError("Expected an Apple Silicon (ARM64) Mach-O executable.")
    if not (app / "Contents/Resources/Data/Managed/Assembly-CSharp.dll").is_file():
        raise ValueError("The Mono application assembly is missing.")
    archive = root.with_suffix(".zip")
    if archive.exists():
        raise FileExistsError(f"Refusing to overwrite {archive}")
    readme = Path(__file__).resolve().parent.parent / "docs/mac-release.zh-CN.md"
    shutil.copyfile(readme, root / "README-Mac.md")
    executable_entries = []
    entries = sorted(app.rglob("*")) + [app, root / "README-Mac.md"]
    # Python does not follow directory symlinks while globbing. Preserve links as
    # links instead of duplicating framework contents or copying external paths.
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as target:
        for path in entries:
            name = path.relative_to(root).as_posix()
            link = path.is_symlink()
            directory = path.is_dir() and not link
            mode = stat.S_IFDIR | 0o755 if directory else stat.S_IFREG | 0o644
            if link:
                mode = stat.S_IFLNK | 0o777
            elif not directory:
                with path.open("rb") as source:
                    header = source.read(4)
                if header in (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"):
                    mode = stat.S_IFREG | 0o755
                    executable_entries.append(name)
            entry = zipfile.ZipInfo(name + ("/" if directory else ""), time.localtime(path.lstat().st_mtime)[:6])
            entry.create_system = 3  # Unix; required for macOS Archive Utility.
            entry.external_attr = (mode << 16) | (0x10 if directory else 0)
            entry.compress_type = zipfile.ZIP_DEFLATED
            if link:
                target.writestr(entry, os.readlink(path).replace("\\", "/").encode("utf-8"))
            elif directory:
                target.writestr(entry, b"")
            else:
                with path.open("rb") as source, target.open(entry, "w", force_zip64=True) as output:
                    shutil.copyfileobj(source, output)
    with zipfile.ZipFile(archive) as package:
        bad = package.testzip()
        if bad:
            raise ValueError(f"ZIP CRC failure: {bad}")
        main_entry = package.getinfo(executable.relative_to(root).as_posix())
        if main_entry.create_system != 3 or not (main_entry.external_attr >> 16) & stat.S_IXUSR:
            raise ValueError("The packaged application lost its execution permission.")
        count = len(package.infolist())
    with archive.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    result = {"archive": str(archive), "bytes": archive.stat().st_size, "sha256": digest,
              "entries": count, "crc": "PASS", "architecture": "ARM64", "backend": "Mono",
              "minimumMacOS": info.get("LSMinimumSystemVersion"),
              "executableEntries": executable_entries, "macRuntimeVerified": False}
    report = root / "package-result.json"
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
