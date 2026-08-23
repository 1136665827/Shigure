#!/usr/bin/env python3
"""Build the single-file, read-only Shigure spell icon package."""

from __future__ import annotations

import argparse
import gc
import hashlib
import json
import os
import struct
import tempfile
from pathlib import Path


MAGIC = b"SHGICN1\0"
VERSION = 1
HEADER = struct.Struct("<8sIIIIqqqq")
SPELL_RECORD = struct.Struct("<qI")
ICON_RECORD = struct.Struct("<qI")
NAME_RECORD = struct.Struct("<qI")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--classification", type=Path, required=True)
    parser.add_argument("--asset-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    classification = json.loads(args.classification.read_text(encoding="utf-8"))
    names_by_spell: dict[int, str] = {}
    for row in classification.get("class_spells", []):
        spell_id = int(row["spell_id"])
        name = str(row.get("spell_name", "")).strip()
        if spell_id > 0 and name:
            names_by_spell.setdefault(spell_id, name)
    del classification
    gc.collect()

    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    spells = sorted(
        (
            int(row["spellId"]),
            str(row["target"]).replace("\\", "/"),
        )
        for row in manifest.get("spells", [])
    )
    if len(spells) < 100_000:
        raise SystemExit(f"Manifest has too few spell rows: {len(spells)}")
    if len({spell_id for spell_id, _ in spells}) != len(spells):
        raise SystemExit("Manifest contains duplicate spell IDs.")

    targets = sorted({target for _, target in spells}, key=str.casefold)
    if len(targets) < 10_000:
        raise SystemExit(f"Manifest has too few unique icon targets: {len(targets)}")
    target_index = {target: index for index, target in enumerate(targets)}

    asset_root = args.asset_root.resolve()
    icon_paths: list[Path] = []
    for target in targets:
        path = (asset_root / target).resolve()
        if (
            path.parent != (asset_root / "Spell").resolve()
            or not path.name.startswith("icon-")
            or path.suffix.casefold() != ".jpg"
            or not path.is_file()
        ):
            raise SystemExit(f"Invalid or missing package icon target: {target}")
        icon_paths.append(path)

    # Names are only needed for name-based module rules. Keep the compact
    # class/aura catalog; every spell ID remains available through the fixed map.
    seen_names: set[str] = set()
    name_records: list[tuple[int, bytes]] = []
    for spell_id, name in sorted(names_by_spell.items()):
        if name in seen_names:
            continue
        encoded = name.encode("utf-8")
        if len(encoded) > 4096:
            raise SystemExit(f"Unexpectedly long spell name for {spell_id}")
        seen_names.add(name)
        name_records.append((spell_id, encoded))

    spell_map_offset = HEADER.size
    icon_index_offset = spell_map_offset + len(spells) * SPELL_RECORD.size
    name_index_offset = icon_index_offset + len(icon_paths) * ICON_RECORD.size
    data_offset = name_index_offset + sum(NAME_RECORD.size + len(name) for _, name in name_records)

    icon_records: list[tuple[int, int]] = []
    next_offset = data_offset
    for path in icon_paths:
        length = path.stat().st_size
        if length < 512 or length > 10 * 1024 * 1024:
            raise SystemExit(f"Unexpected icon size: {path.name} ({length})")
        icon_records.append((next_offset, length))
        next_offset += length

    args.output.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{args.output.name}.", suffix=".building", dir=args.output.parent
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        with temporary.open("wb") as output:
            output.write(HEADER.pack(
                MAGIC,
                VERSION,
                len(spells),
                len(icon_paths),
                len(name_records),
                spell_map_offset,
                icon_index_offset,
                name_index_offset,
                data_offset,
            ))
            for spell_id, target in spells:
                output.write(SPELL_RECORD.pack(spell_id, target_index[target]))
            for offset, length in icon_records:
                output.write(ICON_RECORD.pack(offset, length))
            for spell_id, name in name_records:
                output.write(NAME_RECORD.pack(spell_id, len(name)))
                output.write(name)
            for path in icon_paths:
                with path.open("rb") as icon:
                    while chunk := icon.read(1024 * 1024):
                        output.write(chunk)

        if temporary.stat().st_size != next_offset:
            raise IOError(
                f"Package size mismatch: expected {next_offset}, got {temporary.stat().st_size}"
            )
        os.replace(temporary, args.output)
    finally:
        temporary.unlink(missing_ok=True)

    digest_builder = hashlib.sha256()
    with args.output.open("rb") as package:
        while chunk := package.read(1024 * 1024):
            digest_builder.update(chunk)
    digest = digest_builder.hexdigest()
    print(
        f"Built {args.output}: spells={len(spells)}; icons={len(icon_paths)}; "
        f"names={len(name_records)}; bytes={args.output.stat().st_size}; sha256={digest}"
    )


if __name__ == "__main__":
    main()
