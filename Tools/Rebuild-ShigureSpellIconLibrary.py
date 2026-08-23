#!/usr/bin/env python3
"""Move every locally mapped spell icon into Assets/Spell and rebuild the manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import tempfile
from datetime import date
from pathlib import Path


CUSTOM_FILES = {
    "auto-insert-spell.png",
    "crusader-strike.png",
    "last-rule-row.png",
    "light-infused-mana-potion.jpg",
    "lights-potential.jpg",
    "one-key-spell.png",
    "pause.png",
    "recklessness-potion.jpg",
    "silvermoon-city-health-potion.png",
    "stop-casting.png",
}


def digest(path: Path) -> str:
    result = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def normalized_target(icon_file: str) -> str:
    source_name = Path(icon_file)
    return f"icon-{source_name.stem.casefold()}.jpg"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--classification", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--target", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    source_root = args.source.resolve()
    target_root = args.target.resolve()
    manifest_path = args.manifest.resolve()
    classification_path = args.classification.resolve()
    if source_root == target_root or target_root.parent != manifest_path.parent:
        raise SystemExit("Refusing unexpected source/target/manifest paths.")
    if source_root.name != "all_icons" or target_root.name != "Spell":
        raise SystemExit("Refusing to operate outside all_icons and Assets/Spell.")

    payload = json.loads(classification_path.read_text(encoding="utf-8"))
    mapped_spells = payload.get("mapped_spells", [])
    if mapped_spells:
        spell_rows = mapped_spells
        if len(spell_rows) < 100_000:
            raise SystemExit(f"Classification has too few mapped spell rows: {len(spell_rows)}")
    else:
        # Compatibility with classification files generated before the complete
        # spell catalog was added.
        spell_rows = payload.get("class_spells", [])
        if len(spell_rows) < 4000:
            raise SystemExit(f"Classification has too few class spell rows: {len(spell_rows)}")

    rows_by_spell_id: dict[int, dict] = {}
    required_icons: dict[str, str] = {}
    source_files_by_key = {
        path.name.casefold(): path for path in source_root.glob("*.jpg") if path.is_file()
    }
    for row in spell_rows:
        spell_id = int(row["spell_id"])
        icon_file = str(row["icon_file"])
        target_name = normalized_target(icon_file)
        rows_by_spell_id[spell_id] = row
        required_icons[icon_file.casefold()] = target_name

    missing: list[str] = []
    for icon_file_key, target_name in required_icons.items():
        source_path = source_files_by_key.get(icon_file_key, source_root / icon_file_key)
        if not source_path.exists() and not (target_root / target_name).exists():
            missing.append(icon_file_key)
    missing_custom = sorted(name for name in CUSTOM_FILES if not (target_root / name).exists())
    if missing or missing_custom:
        raise SystemExit(
            f"Preflight failed: missing classified icons={len(missing)}, "
            f"missing custom icons={missing_custom[:10]}"
        )

    generated_existing = {
        path.name for path in target_root.glob("icon-*.jpg") if path.is_file()
    }
    required_targets = set(required_icons.values())
    obsolete = sorted(generated_existing - required_targets)
    source_moves = sum(icon_file_key in source_files_by_key for icon_file_key in required_icons)
    print(
        f"Preflight: spell IDs={len(rows_by_spell_id)}; unique icons={len(required_targets)}; "
        f"source moves={source_moves}; obsolete generated icons={len(obsolete)}; "
        f"custom files={len(CUSTOM_FILES)}"
    )
    if not args.apply:
        print("Dry run only. Pass --apply to rebuild the library.")
        return

    target_root.mkdir(parents=True, exist_ok=True)
    moved = 0
    reused = 0
    for icon_file_key, target_name in sorted(required_icons.items()):
        source_path = source_files_by_key.get(icon_file_key, source_root / icon_file_key)
        target_path = target_root / target_name
        if source_path.exists():
            file_descriptor, temporary_name = tempfile.mkstemp(
                prefix=f".{target_name}.", suffix=".moving", dir=target_root
            )
            os.close(file_descriptor)
            temporary = Path(temporary_name)
            try:
                shutil.copy2(source_path, temporary)
                if digest(source_path) != digest(temporary):
                    raise IOError(f"Hash mismatch while moving {source_path.name}")
                os.replace(temporary, target_path)
                source_path.unlink()
                moved += 1
            finally:
                temporary.unlink(missing_ok=True)
        elif target_path.exists():
            reused += 1
        else:
            raise FileNotFoundError(icon_file_key)

    for name in obsolete:
        path = (target_root / name).resolve()
        if path.parent != target_root or not name.startswith("icon-") or path.suffix.casefold() != ".jpg":
            raise SystemExit(f"Refusing to prune unexpected path: {path}")
        path.unlink(missing_ok=True)

    manifest_spells = []
    for spell_id, row in sorted(rows_by_spell_id.items()):
        icon_file = str(row["icon_file"])
        manifest_entry = {
            "spellId": spell_id,
            "name": str(row.get("spell_name", "")),
            "target": f"Spell/{normalized_target(icon_file)}",
        }
        optional_fields = {
            "classes": row.get("classes"),
            "specializations": row.get("specializations"),
            "skillLines": row.get("skill_lines"),
            "traitSubtrees": row.get("trait_subtrees"),
            "classificationSources": row.get("classification_sources"),
        }
        for field_name, value in optional_fields.items():
            if value:
                manifest_entry[field_name] = str(value)
        if row.get("configured_aura"):
            manifest_entry["configuredAura"] = True
        manifest_spells.append(manifest_entry)

    manifest = {
        "game": "World of Warcraft",
        "updated": date.today().isoformat(),
        "source": {
            "classificationBuild": payload.get("summary", {}).get("build", ""),
            "locale": payload.get("summary", {}).get("locale", ""),
            "db2": "https://wago.tools/db2",
            "listfile": "https://github.com/wowdev/wow-listfile",
            "localIcons": "artifacts/all_icons",
        },
        "spells": manifest_spells,
    }
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    file_descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{manifest_path.name}.", suffix=".json", dir=manifest_path.parent
    )
    os.close(file_descriptor)
    temporary_manifest = Path(temporary_name)
    try:
        temporary_manifest.write_text(
            json.dumps(manifest, ensure_ascii=False, separators=(",", ":")) + "\n",
            encoding="utf-8",
        )
        os.replace(temporary_manifest, manifest_path)
    finally:
        temporary_manifest.unlink(missing_ok=True)

    final_generated = sum(1 for path in target_root.glob("icon-*.jpg") if path.is_file())
    final_custom = sum(1 for name in CUSTOM_FILES if (target_root / name).exists())
    print(
        f"Applied: moved={moved}; reused={reused}; pruned={len(obsolete)}; "
        f"generated={final_generated}; custom={final_custom}; manifest spells={len(manifest_spells)}"
    )


if __name__ == "__main__":
    main()
