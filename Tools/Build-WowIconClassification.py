#!/usr/bin/env python3
"""Build complete spell-to-icon mappings and class/aura classification metadata."""

from __future__ import annotations

import argparse
import csv
import json
import re
from collections import defaultdict
from pathlib import Path


def read_csv(path: Path):
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        yield from csv.DictReader(handle)


def as_int(value: str | None, default: int = 0) -> int:
    try:
        return int(value or default)
    except ValueError:
        return default


def decode_class_mask(mask_value: str | None, class_ids: list[int]) -> set[int]:
    mask = as_int(mask_value)
    if mask < 0:
        return set(class_ids)
    return {class_id for class_id in class_ids if mask & (1 << (class_id - 1))}


def joined(values, limit: int = 200) -> str:
    values = [str(value) for value in values if str(value)]
    if len(values) <= limit:
        return "; ".join(values)
    return "; ".join(values[:limit]) + f"; …（另有 {len(values) - limit} 项）"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--icons", type=Path, required=True)
    parser.add_argument("--existing-target", type=Path)
    parser.add_argument("--raw", type=Path, required=True)
    parser.add_argument("--class-root", type=Path)
    parser.add_argument("--build", required=True)
    parser.add_argument("--locale", default="zhCN")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    suffix = f"-{args.locale}.csv"
    table = lambda name: args.raw / f"{name}{suffix}"

    icon_names_by_key = {
        path.name.casefold(): path.name for path in args.icons.glob("*.jpg") if path.is_file()
    }
    if args.existing_target and args.existing_target.is_dir():
        for path in args.existing_target.glob("icon-*.jpg"):
            # Assets/Spell uses icon-{original stem}.jpg. Reconstruct the logical
            # all_icons name so rebuilding classification remains idempotent after moves.
            logical_name = path.name[len("icon-"):]
            icon_names_by_key.setdefault(logical_name.casefold(), logical_name)
    icon_files = sorted(icon_names_by_key.values(), key=str.casefold)
    icon_name_to_file = {Path(name).stem.casefold(): name for name in icon_files}

    file_data_to_icon: dict[int, str] = {}
    icon_to_file_data: dict[str, set[int]] = defaultdict(set)
    with (args.raw / "community-listfile.csv").open("r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            file_id_text, separator, game_path = line.rstrip("\r\n").partition(";")
            if not separator or not game_path.casefold().startswith("interface/icons/"):
                continue
            stem = Path(game_path).stem.casefold()
            actual_file = icon_name_to_file.get(stem)
            if actual_file is None:
                continue
            file_id = as_int(file_id_text)
            if file_id:
                file_data_to_icon[file_id] = actual_file
                icon_to_file_data[actual_file].add(file_id)

    class_names: dict[int, str] = {}
    class_id_by_filename: dict[str, int] = {}
    class_by_spell_class_set: dict[int, int] = {}
    for row in read_csv(table("ChrClasses")):
        class_id = as_int(row.get("ID"))
        if not class_id:
            continue
        if class_id > 13:
            continue
        class_names[class_id] = row.get("Name_lang", "").strip()
        class_id_by_filename[row.get("Filename", "").strip().casefold()] = class_id
        spell_class_set = as_int(row.get("SpellClassSet"))
        if spell_class_set:
            class_by_spell_class_set[spell_class_set] = class_id
    class_ids = sorted(class_names)

    spec_names: dict[int, str] = {}
    spec_class: dict[int, int] = {}
    for row in read_csv(table("ChrSpecialization")):
        spec_id = as_int(row.get("ID"))
        if not spec_id:
            continue
        spec_names[spec_id] = row.get("Name_lang", "").strip()
        spec_class[spec_id] = as_int(row.get("ClassID"))

    spell_names = {
        as_int(row.get("ID")): row.get("Name_lang", "").strip()
        for row in read_csv(table("SpellName"))
        if as_int(row.get("ID"))
    }

    classes_by_spell: dict[int, set[int]] = defaultdict(set)
    specs_by_spell: dict[int, set[int]] = defaultdict(set)
    sources_by_spell: dict[int, set[str]] = defaultdict(set)
    for row in read_csv(table("SpecializationSpells")):
        spell_id = as_int(row.get("SpellID"))
        spec_id = as_int(row.get("SpecID"))
        class_id = spec_class.get(spec_id, 0)
        if not spell_id:
            continue
        if spec_id:
            specs_by_spell[spell_id].add(spec_id)
        if class_id:
            classes_by_spell[spell_id].add(class_id)
        sources_by_spell[spell_id].add("SpecializationSpells")

    for row in read_csv(table("SpellClassOptions")):
        spell_id = as_int(row.get("SpellID"))
        class_id = class_by_spell_class_set.get(as_int(row.get("SpellClassSet")), 0)
        if spell_id and class_id:
            classes_by_spell[spell_id].add(class_id)
            sources_by_spell[spell_id].add("SpellClassOptions")

    skill_names: dict[int, str] = {}
    skill_categories: dict[int, int] = {}
    for row in read_csv(table("SkillLine")):
        skill_id = as_int(row.get("ID"))
        if skill_id:
            skill_names[skill_id] = row.get("DisplayName_lang", "").strip()
            skill_categories[skill_id] = as_int(row.get("CategoryID"))
    classes_by_skill: dict[int, set[int]] = defaultdict(set)
    for row in read_csv(table("SkillRaceClassInfo")):
        skill_id = as_int(row.get("SkillID"))
        if skill_id:
            classes_by_skill[skill_id].update(decode_class_mask(row.get("ClassMask"), class_ids))

    # Category 7 contains the player class skill lines. Their localized display names
    # match ChrClasses and give a cleaner mapping than broad all-class masks.
    for skill_id, skill_name in skill_names.items():
        if skill_categories.get(skill_id) != 7:
            continue
        for class_id, class_name in class_names.items():
            if skill_name == class_name:
                classes_by_skill[skill_id] = {class_id}

    skill_lines_by_spell: dict[int, set[int]] = defaultdict(set)
    player_skill_spells: set[int] = set(specs_by_spell)
    for row in read_csv(table("SkillLineAbility")):
        spell_id = as_int(row.get("Spell"))
        skill_id = as_int(row.get("SkillLine"))
        if not spell_id:
            continue
        skill_lines_by_spell[spell_id].add(skill_id)
        direct_classes = decode_class_mask(row.get("ClassMask"), class_ids)
        if direct_classes and len(direct_classes) <= 4:
            classes_by_spell[spell_id].update(direct_classes)
            sources_by_spell[spell_id].add("SkillLineAbility.ClassMask")
        skill_classes = classes_by_skill.get(skill_id, set())
        if skill_classes and len(skill_classes) <= 4:
            classes_by_spell[spell_id].update(skill_classes)
            sources_by_spell[spell_id].add("SkillRaceClassInfo")
        if (direct_classes and len(direct_classes) <= 4) or (skill_classes and len(skill_classes) <= 4):
            player_skill_spells.add(spell_id)

    trait_subtree_names = {
        as_int(row.get("ID")): row.get("Name_lang", "").strip()
        for row in read_csv(table("TraitSubTree"))
        if as_int(row.get("ID"))
    }
    classes_by_trait_tree: dict[int, set[int]] = defaultdict(set)
    for row in read_csv(table("SkillLineXTraitTree")):
        skill_id = as_int(row.get("SkillLineID"))
        tree_id = as_int(row.get("TraitTreeID"))
        skill_classes = classes_by_skill.get(skill_id, set())
        if tree_id and skill_categories.get(skill_id) == 7 and len(skill_classes) == 1:
            classes_by_trait_tree[tree_id].update(skill_classes)

    tree_and_subtree_by_node: dict[int, tuple[int, int]] = {}
    for row in read_csv(table("TraitNode")):
        node_id = as_int(row.get("ID"))
        if node_id:
            tree_and_subtree_by_node[node_id] = (
                as_int(row.get("TraitTreeID")),
                as_int(row.get("TraitSubTreeID")),
            )
    definition_by_entry = {
        as_int(row.get("ID")): as_int(row.get("TraitDefinitionID"))
        for row in read_csv(table("TraitNodeEntry"))
        if as_int(row.get("ID"))
    }
    nodes_by_definition: dict[int, set[int]] = defaultdict(set)
    for row in read_csv(table("TraitNodeXTraitNodeEntry")):
        node_id = as_int(row.get("TraitNodeID"))
        definition_id = definition_by_entry.get(as_int(row.get("TraitNodeEntryID")), 0)
        if node_id and definition_id:
            nodes_by_definition[definition_id].add(node_id)

    trait_subtrees_by_spell: dict[int, set[int]] = defaultdict(set)
    for row in read_csv(table("TraitDefinition")):
        definition_id = as_int(row.get("ID"))
        nodes = nodes_by_definition.get(definition_id, set())
        if not nodes:
            continue
        spell_ids = {
            as_int(row.get("SpellID")),
            as_int(row.get("OverridesSpellID")),
            as_int(row.get("VisibleSpellID")),
        } - {0}
        for node_id in nodes:
            tree_id, subtree_id = tree_and_subtree_by_node.get(node_id, (0, 0))
            trait_classes = classes_by_trait_tree.get(tree_id, set())
            if not trait_classes:
                continue
            for spell_id in spell_ids:
                classes_by_spell[spell_id].update(trait_classes)
                sources_by_spell[spell_id].add("TraitTree")
                player_skill_spells.add(spell_id)
                if subtree_id:
                    trait_subtrees_by_spell[spell_id].add(subtree_id)

    configured_aura_spells: set[int] = set()
    if args.class_root and args.class_root.is_dir():
        local_patterns = [
            re.compile(r"spellId\s*=\s*(?P<id>\d+).*?name\s*=\s*[\"'](?P<name>[^\"']+)[\"']"),
            re.compile(r"\[(?P<id>\d+)\]\s*=\s*\{\s*index\s*=\s*\d+\s*,\s*name\s*=\s*[\"'](?P<name>[^\"']+)[\"']"),
        ]
        aura_spell_pattern = re.compile(
            r"\{\s*name\s*=\s*[\"'](?P<name>[^\"']+)[\"'][^{}]*?"
            r"\bspellId\s*=\s*(?P<id>\d+)",
            re.DOTALL,
        )
        aura_spell_ids_pattern = re.compile(
            r"\{\s*name\s*=\s*[\"'](?P<name>[^\"']+)[\"'][^{}]*?"
            r"\bspellIds\s*=\s*\{(?P<ids>[^{}]*)\}",
            re.DOTALL,
        )
        for lua_path in args.class_root.glob("*.lua"):
            class_id = class_id_by_filename.get(lua_path.stem.casefold(), 0)
            if not class_id:
                continue
            source = lua_path.read_text(encoding="utf-8-sig")
            for pattern in local_patterns:
                for match in pattern.finditer(source):
                    spell_id = as_int(match.group("id"))
                    if not spell_id:
                        continue
                    classes_by_spell[spell_id].add(class_id)
                    sources_by_spell[spell_id].add("Shigure.class")
                    player_skill_spells.add(spell_id)
                    if spell_id not in spell_names:
                        spell_names[spell_id] = match.group("name").strip()
            for match in aura_spell_pattern.finditer(source):
                spell_id = as_int(match.group("id"))
                if not spell_id:
                    continue
                classes_by_spell[spell_id].add(class_id)
                sources_by_spell[spell_id].add("Shigure.aura")
                configured_aura_spells.add(spell_id)
                if spell_id not in spell_names:
                    spell_names[spell_id] = match.group("name").strip()
            for match in aura_spell_ids_pattern.finditer(source):
                name = match.group("name").strip()
                for id_text in re.findall(r"\d+", match.group("ids")):
                    spell_id = as_int(id_text)
                    if not spell_id:
                        continue
                    classes_by_spell[spell_id].add(class_id)
                    sources_by_spell[spell_id].add("Shigure.aura")
                    configured_aura_spells.add(spell_id)
                    if spell_id not in spell_names:
                        spell_names[spell_id] = name

    # Prefer the default-difficulty icon row, then the first usable row.
    spell_icon_candidates: dict[int, tuple[int, int]] = {}
    for row in read_csv(table("SpellMisc")):
        spell_id = as_int(row.get("SpellID"))
        file_data_id = as_int(row.get("SpellIconFileDataID"))
        difficulty = as_int(row.get("DifficultyID"), 999)
        if not spell_id or file_data_id not in file_data_to_icon:
            continue
        current = spell_icon_candidates.get(spell_id)
        priority = 0 if difficulty == 0 else 1
        if current is None or priority < current[0]:
            spell_icon_candidates[spell_id] = (priority, file_data_id)

    # Trigger/buff aura IDs are sometimes absent from SpellMisc while another spell
    # with the same localized name carries the client icon. Use that icon only when
    # every same-name candidate resolves to one logical image.
    icon_file_ids_by_name: dict[str, set[int]] = defaultdict(set)
    for spell_id, (_, file_data_id) in spell_icon_candidates.items():
        name = spell_names.get(spell_id, "").strip()
        if name:
            icon_file_ids_by_name[name].add(file_data_id)
    for spell_id in sorted(configured_aura_spells - spell_icon_candidates.keys()):
        name = spell_names.get(spell_id, "").strip()
        file_data_ids = icon_file_ids_by_name.get(name, set())
        icon_files_for_name = {file_data_to_icon[value] for value in file_data_ids}
        if len(icon_files_for_name) == 1:
            file_data_id = min(file_data_ids)
            spell_icon_candidates[spell_id] = (2, file_data_id)
            sources_by_spell[spell_id].add("Shigure.aura.NameFallback")

    spells_by_icon: dict[str, list[int]] = defaultdict(list)
    class_spell_rows: list[dict] = []
    mapped_spell_rows: list[dict] = []
    for spell_id, (_, file_data_id) in sorted(spell_icon_candidates.items()):
        icon_file = file_data_to_icon[file_data_id]
        spells_by_icon[icon_file].append(spell_id)
        class_set = sorted(classes_by_spell.get(spell_id, set()))
        specs = sorted(specs_by_spell.get(spell_id, set()))
        skills = sorted(skill_lines_by_spell.get(spell_id, set()))
        trait_subtrees = sorted(trait_subtrees_by_spell.get(spell_id, set()))
        sources = sorted(sources_by_spell.get(spell_id, set()))
        likely_player_spell = bool(specs) or spell_id in player_skill_spells
        configured_aura = spell_id in configured_aura_spells
        row = {
            "icon_file": icon_file,
            "icon_file_data_id": file_data_id,
            "spell_id": spell_id,
            "spell_name": spell_names.get(spell_id, ""),
            "class_ids": joined(class_set),
            "classes": joined(class_names.get(value, str(value)) for value in class_set),
            "specialization_ids": joined(specs),
            "specializations": joined(spec_names.get(value, str(value)) for value in specs),
            "skill_line_ids": joined(skills),
            "skill_lines": joined(skill_names.get(value, str(value)) for value in skills),
            "trait_subtree_ids": joined(trait_subtrees),
            "trait_subtrees": joined(trait_subtree_names.get(value, str(value)) for value in trait_subtrees),
            "classification_sources": joined(sources),
            "likely_player_class_spell": likely_player_spell,
            "configured_aura": configured_aura,
        }
        mapped_spell_rows.append({
            "spell_id": spell_id,
            "spell_name": spell_names.get(spell_id, ""),
            "icon_file_data_id": file_data_id,
            "icon_file": icon_file,
        })
        if likely_player_spell or configured_aura:
            class_spell_rows.append(row)

    icon_rows: list[dict] = []
    for icon_name in icon_files:
        spell_ids = spells_by_icon.get(icon_name, [])
        class_set: set[int] = set()
        specs: set[int] = set()
        likely_count = 0
        for spell_id in spell_ids:
            class_set.update(classes_by_spell.get(spell_id, set()))
            specs.update(specs_by_spell.get(spell_id, set()))
            if specs_by_spell.get(spell_id) or spell_id in player_skill_spells:
                likely_count += 1
        icon_rows.append({
            "icon_file": icon_name,
            "file_data_ids": joined(sorted(icon_to_file_data.get(icon_name, set()))),
            "db2_spell_count": len(spell_ids),
            "player_class_spell_count": likely_count,
            "spell_ids": joined(spell_ids),
            "spell_names": joined((spell_names.get(value, "") for value in spell_ids), limit=100),
            "class_ids": joined(sorted(class_set)),
            "classes": joined(class_names.get(value, str(value)) for value in sorted(class_set)),
            "specialization_ids": joined(sorted(specs)),
            "specializations": joined(spec_names.get(value, str(value)) for value in sorted(specs)),
            "status": "已关联职业技能" if likely_count else ("已关联其他法术" if spell_ids else "未关联 DB2 法术"),
        })

    summary = {
        "build": args.build,
        "locale": args.locale,
        "icon_files": len(icon_files),
        "icons_with_file_data_id": sum(bool(icon_to_file_data.get(name)) for name in icon_files),
        "icons_with_spells": sum(bool(spells_by_icon.get(name)) for name in icon_files),
        "icons_with_player_class_spells": sum(
            any(specs_by_spell.get(spell_id) or spell_id in player_skill_spells
                for spell_id in spells_by_icon.get(name, []))
            for name in icon_files
        ),
        "configured_aura_spells": len(configured_aura_spells),
        "mapped_configured_auras": sum(spell_id in spell_icon_candidates for spell_id in configured_aura_spells),
        "mapped_spells": len(mapped_spell_rows),
        "likely_player_class_spells": sum(row["likely_player_class_spell"] for row in class_spell_rows),
        "retained_spells": len(class_spell_rows),
        "classes": len(class_names),
        "specializations": sum(class_id in class_names for class_id in spec_class.values()),
    }

    payload = {
        "summary": summary,
        "mapped_spells": mapped_spell_rows,
        "class_spells": class_spell_rows,
        "icons": icon_rows,
        "sources": [
            ["Wago DB2", f"https://wago.tools/db2?build={args.build}", "技能名称、图标 FileDataID、职业、专精、技能线"],
            ["WoWDBDefs", "https://github.com/wowdev/WoWDBDefs", "DB2 字段定义与表关系"],
            ["WoW community listfile", "https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv", "FileDataID 到 interface/icons 文件名"],
            ["Shigure class Lua", "本地 Fuyutsui/class/*.lua", "补充项目当前使用技能的职业归属"],
        ],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
