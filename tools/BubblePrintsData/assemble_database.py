#!/usr/bin/env python3
"""Verify the repository's stock BubblePrints archives and assemble their database offline."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import sys
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parent
BLOCK = 1024 * 1024


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(BLOCK), b""):
            h.update(block)
    return h.hexdigest()


def verify_archives(manifest, directory):
    for part in manifest["parts"]:
        name = part["archive"]
        if Path(name).name != name or Path(part["name"]).name != part["name"]:
            raise ValueError("Part names must be simple filenames")
        path = directory / name
        if not path.is_file():
            raise ValueError("Missing archive: " + str(path))
        if path.stat().st_size != part["archive_bytes"] or digest(path) != part["archive_sha256"]:
            raise ValueError("Archive checksum/size mismatch: " + str(path))
        with zipfile.ZipFile(path) as archive:
            if archive.namelist() != [part["name"]]:
                raise ValueError("Unexpected archive contents: " + str(path))
    print(f"Verified {len(manifest['parts'])} archive hashes and member names")


def assemble(manifest, directory, output):
    if output.exists():
        if output.stat().st_size == manifest["bytes"] and digest(output) == manifest["sha256"]:
            print("Existing database checksum verified: " + str(output))
            return
        raise ValueError("Existing output differs; move it aside before assembling: " + str(output))
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        full = hashlib.sha256()
        total = 0
        with tempfile.NamedTemporaryFile(dir=output.parent, prefix=output.name + ".", suffix=".tmp", delete=False) as dest:
            temporary = Path(dest.name)
            for part in manifest["parts"]:
                count = 0
                piece = hashlib.sha256()
                with zipfile.ZipFile(directory / part["archive"]) as archive, archive.open(part["name"]) as source:
                    for block in iter(lambda: source.read(BLOCK), b""):
                        count += len(block)
                        if count > part["bytes"]:
                            raise ValueError("Part exceeds its recorded size: " + part["name"])
                        piece.update(block)
                        full.update(block)
                        dest.write(block)
                if count != part["bytes"] or piece.hexdigest() != part["sha256"]:
                    raise ValueError("Part checksum/size mismatch: " + part["name"])
                total += count
        if total != manifest["bytes"] or full.hexdigest() != manifest["sha256"]:
            raise ValueError("Assembled database checksum/size mismatch")
        os.replace(temporary, output)
        temporary = None
        print(f"Assembled {total} bytes: {output}")
        print("SHA-256 " + full.hexdigest())
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", type=Path, default=ROOT / "data", help="Directory holding all five repository ZIPs")
    parser.add_argument("--output", type=Path, help="Database destination (default: ignored .local directory)")
    parser.add_argument("--verify-only", action="store_true", help="Check archive hashes/member names without assembling")
    args = parser.parse_args()
    try:
        manifest = json.loads((ROOT / "DATABASE_PARTS.json").read_text())
        verify_archives(manifest, args.data_dir)
        if not args.verify_only:
            assemble(manifest, args.data_dir, args.output or ROOT / ".local" / manifest["database"])
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        print("BubblePrints data: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
