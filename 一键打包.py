from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path


OLD_NAME = "Shigure"
ADDON_OLD_NAME = "Fuyutsui"
OLD_NAMES = (OLD_NAME, ADDON_OLD_NAME)
PROTECTED_TEXTS = (
    "https://www.shigure.club",
    "访问 Shigure 官网，浏览并获取可用模块",
)

SKIP_DIRS = {
    ".git",
    ".vs",
    ".vscode",
    ".agents",
    ".claude",
    "__pycache__",
    "Obsidian",
    "artifacts",
    "bin",
    "cache",
    "obj",
}

TEXT_EXTENSIONS = {
    ".bat",
    ".cmd",
    ".config",
    ".cs",
    ".csproj",
    ".editorconfig",
    ".gitignore",
    ".json",
    ".lua",
    ".md",
    ".props",
    ".ps1",
    ".py",
    ".resx",
    ".manifest",
    ".sln",
    ".slnx",
    ".targets",
    ".toc",
    ".txt",
    ".xaml",
    ".xml",
    ".yaml",
    ".yml",
}

NAME_PATTERN = re.compile(r"^[A-Za-z][A-Za-z0-9_]*$")
NAME_REPLACEMENT_PATTERN = re.compile(
    "|".join(re.escape(name) for name in OLD_NAMES),
    re.IGNORECASE,
)
SLASH_COMMAND_PATTERN = re.compile(r"/fu\b", re.IGNORECASE)


def configure_console() -> None:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


def get_app_paths() -> tuple[Path, Path]:
    if getattr(sys, "frozen", False):
        exe_path = Path(sys.executable).resolve()
        return exe_path.parent, exe_path

    script_path = Path(__file__).resolve()
    return script_path.parent, script_path


def ask_new_name() -> str:
    while True:
        new_name = input("请输入新的项目名称（英文开头，只能包含英文字母/数字/下划线）: ").strip()
        if NAME_PATTERN.fullmatch(new_name):
            return new_name
        print("名称格式不正确：必须以英文字母开头，只能包含英文字母、数字、下划线。")


def is_in_skipped_dir(path: Path, root: Path) -> bool:
    rel_parts = path.relative_to(root).parts
    return any(part in SKIP_DIRS for part in rel_parts)


def iter_text_files(root: Path, script_path: Path):
    for dirpath, dirnames, filenames in os.walk(root):
        current_dir = Path(dirpath)
        dirnames[:] = [name for name in dirnames if name not in SKIP_DIRS]

        for filename in filenames:
            path = current_dir / filename
            if path == script_path:
                continue
            if is_in_skipped_dir(path, root):
                continue
            if path.suffix.lower() not in TEXT_EXTENSIONS and filename.lower() not in TEXT_EXTENSIONS:
                continue
            yield path


def read_text(path: Path) -> tuple[str, str] | None:
    for encoding in ("utf-8", "gbk"):
        try:
            return path.read_text(encoding=encoding), encoding
        except UnicodeDecodeError:
            continue
    print(f"跳过无法识别编码的文件: {path}")
    return None


def match_name_case(old_name: str, new_name: str) -> str:
    if old_name.isupper():
        return new_name.upper()
    if old_name.islower():
        return new_name.lower()
    if old_name[:1].isupper() and old_name[1:].islower():
        return new_name[:1].upper() + new_name[1:]
    return new_name


def replace_names(value: str, new_name: str) -> str:
    protected_ranges = [
        match.span()
        for protected_text in PROTECTED_TEXTS
        for match in re.finditer(re.escape(protected_text), value)
    ]

    def replace_match(match: re.Match[str]) -> str:
        if any(start <= match.start() < end for start, end in protected_ranges):
            return match.group(0)
        return match_name_case(match.group(0), new_name)

    return NAME_REPLACEMENT_PATTERN.sub(
        replace_match,
        value,
    )


def replace_text(value: str, new_name: str) -> str:
    value = replace_names(value, new_name)
    slash_command = f"/{new_name[:2].lower()}"
    return SLASH_COMMAND_PATTERN.sub(slash_command, value)


def contains_text_replacement(value: str) -> bool:
    return bool(
        NAME_REPLACEMENT_PATTERN.search(value)
        or SLASH_COMMAND_PATTERN.search(value)
    )


def collect_replacements(root: Path, script_path: Path) -> dict[Path, tuple[str, str]]:
    backups: dict[Path, tuple[str, str]] = {}

    for path in iter_text_files(root, script_path):
        result = read_text(path)
        if result is None:
            continue

        text, encoding = result
        if contains_text_replacement(text):
            backups[path] = (text, encoding)

    return backups


def apply_replacements(backups: dict[Path, tuple[str, str]], new_name: str) -> None:
    for path, (text, encoding) in backups.items():
        path.write_text(replace_text(text, new_name), encoding=encoding, newline="")


def collect_path_renames(
    root: Path,
    script_path: Path,
    new_name: str,
) -> list[tuple[Path, Path]]:
    paths: list[Path] = []

    for dirpath, dirnames, filenames in os.walk(root):
        current_dir = Path(dirpath)
        dirnames[:] = [name for name in dirnames if name not in SKIP_DIRS]

        for dirname in dirnames:
            path = current_dir / dirname
            if NAME_REPLACEMENT_PATTERN.search(dirname):
                paths.append(path)

        for filename in filenames:
            path = current_dir / filename
            if path == script_path:
                continue
            if NAME_REPLACEMENT_PATTERN.search(filename):
                paths.append(path)

    paths.sort(key=lambda path: len(path.relative_to(root).parts), reverse=True)
    return [
        (path, path.with_name(replace_names(path.name, new_name)))
        for path in paths
        if replace_names(path.name, new_name) != path.name
    ]


def validate_path_renames(root: Path, rename_plan: list[tuple[Path, Path]]) -> None:
    target_paths: dict[Path, Path] = {}

    for old_path, new_path in rename_plan:
        previous_source = target_paths.get(new_path)
        if previous_source is not None:
            raise FileExistsError(
                f"多个路径将被重命名为同一目标，已停止避免覆盖: "
                f"{previous_source.relative_to(root)}、{old_path.relative_to(root)} -> "
                f"{new_path.relative_to(root)}"
            )
        target_paths[new_path] = old_path

        if new_path.exists():
            raise FileExistsError(f"目标路径已存在，已停止避免覆盖: {new_path}")


def apply_path_renames(
    rename_plan: list[tuple[Path, Path]],
    completed_renames: list[tuple[Path, Path]],
) -> None:
    for old_path, new_path in rename_plan:
        old_path.rename(new_path)
        completed_renames.append((old_path, new_path))


def publish(root: Path, new_name: str) -> None:
    command = [
        "dotnet",
        "publish",
        f".\\{new_name}.csproj",
        "-c",
        "Release",
        "-r",
        "win-x64",
        "--self-contained",
        "true",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=true",
        "-o",
        ".\\artifacts\\publish\\win-x64",
    ]

    print()
    print("开始执行发布命令:")
    print(subprocess.list2cmdline(command))
    subprocess.run(command, cwd=root, check=True)


def open_publish_folder(root: Path) -> None:
    publish_dir = root / "artifacts" / "publish" / "win-x64"
    if not publish_dir.exists():
        print(f"打包目录不存在，无法打开: {publish_dir}")
        return

    os.startfile(publish_dir)


def main() -> int:
    configure_console()

    root, script_path = get_app_paths()

    new_name = ask_new_name()
    should_rename = new_name != OLD_NAME

    if should_rename:
        source_csproj = root / f"{OLD_NAME}.csproj"
        if not source_csproj.exists():
            print(f"找不到需要打包的项目文件: {source_csproj}")
            return 1

        backups = collect_replacements(root, script_path)
        preview_files = list(backups)
        rename_plan = collect_path_renames(root, script_path, new_name)
        try:
            validate_path_renames(root, rename_plan)
        except (FileExistsError, ValueError) as exc:
            print(exc)
            return 1

        print()
        print(
            f"将把文本和路径中的 {OLD_NAME}、{ADDON_OLD_NAME} 按原文大小写形式 "
            f"替换为 {new_name}，并把 /fu 替换为 /{new_name[:2].lower()}。"
        )
        print(f"预计永久修改 {len(preview_files)} 个文本文件，打包结束后不会恢复。")
        for path in preview_files:
            print(f"- {path.relative_to(root)}")
        print(f"预计永久重命名 {len(rename_plan)} 个文件或目录，打包结束后不会恢复。")
        for old_path, new_path in rename_plan:
            print(f"- {old_path.relative_to(root)} -> {new_path.relative_to(root)}")
    else:
        backups = {}
        rename_plan = []
        print()
        print(f"新名称和原名称相同，将直接使用 {OLD_NAME}.csproj 打包。")

    confirm = input("确认继续？输入 Y/y 继续，其它任意内容取消: ").strip()
    if confirm.casefold() != "y":
        print("已取消。")
        return 0

    completed_renames: list[tuple[Path, Path]] = []
    try:
        if should_rename:
            apply_replacements(backups, new_name)
            apply_path_renames(rename_plan, completed_renames)

            print()
            print(f"名称替换完成，已永久修改 {len(backups)} 个文本文件。")
            print(f"已重命名 {len(completed_renames)} 个文件或目录。")

        publish(root, new_name)
    except BaseException as exc:
        if isinstance(exc, KeyboardInterrupt):
            print("执行已中断。")
        else:
            print(f"执行失败: {exc}")
        if should_rename:
            print("名称替换已保留，不会恢复。")
        return 1

    print()
    print("打包完成。")
    open_publish_folder(root)
    return 0


if __name__ == "__main__":
    sys.exit(main())
