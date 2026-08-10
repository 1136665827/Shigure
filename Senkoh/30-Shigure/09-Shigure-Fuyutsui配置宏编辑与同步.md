---
title: Shigure Fuyutsui 配置、宏编辑与同步
summary: 说明从游戏目录定位 Fuyutsui、受限 Lua 表 round-trip、ClassBlocks 到 config 及 ClassMacros 到 keymap 的转换和串行同步。
aliases:
  - Shigure Lua 转换器
  - Fuyutsui 同步
tags:
  - project/shigure
  - doc/feature
  - area/conversion
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[docs/30-Shigure/00-Shigure-MOC]]"
related:
  - "[[docs/40-跨项目/02-Shingen-ClassBlocks到config同步契约]]"
  - "[[docs/40-跨项目/03-Shingen-ClassMacros到keymap与按键契约]]"
source_files:
  - Shigure/Infrastructure/WowAddonLocator.cs
  - Shigure/Infrastructure/LuaLiteParser.cs
  - Shigure/Infrastructure/ClassBlocksStore.cs
  - Shigure/Infrastructure/ClassMacrosStore.cs
  - Shigure/Infrastructure/FuyutsuiConfigConverter.cs
  - Shigure/Infrastructure/FuyutsuiKeymapConverter.cs
  - Shigure/UI/MainForm.cs
source_symbols:
  - WowAddonLocator.FindAddonRoot
  - WowAddonLocator.FindClassDirectory
  - LuaLiteParser.TryExtractAssignedTable
  - ClassBlocksStore.Save
  - ClassMacrosStore.Save
  - FuyutsuiConfigConverter.UpdateFromClassDirectory
  - FuyutsuiKeymapConverter.UpdateFromClassMacros
  - MainForm.UpdateConfigFromAddonAsync
verified_at: 2026-08-09
---

# Shigure Fuyutsui 配置、宏编辑与同步

> [!abstract] AI 快速摘要
> Shigure 能从目标窗口进程路径向父目录回溯，寻找 `Interface/AddOns/Fuyutsui`。它用自定义的受限 Lua 数据解析器读取并重写 ClassBlocks/ClassMacros 中的指定表，再生成 `config/*.json` 和 `keymap/*.json`。解析器不执行 Lua，但 round-trip 会把表内部规范化重写；跨多个文件的转换没有事务，失败时可能只完成一部分。MainForm 用尾任务队列串行配置更新，并在启动/重启运行时前等待该队列。

## 图谱位置

- 上级：[[docs/30-Shigure/00-Shigure-MOC]]
- Fuyutsui 状态生产者：[[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]
- ClassBlocks 契约：[[docs/40-跨项目/02-Shingen-ClassBlocks到config同步契约]]
- ClassMacros 契约：[[docs/40-跨项目/03-Shingen-ClassMacros到keymap与按键契约]]

## 范围与非范围

本页覆盖目录发现、Lua 数据子集、编辑回写、两类转换器及同步并发。像素实时扫描和最终按键发送分别见 [[docs/30-Shigure/02-Shigure-像素扫描与协议解码]]、[[docs/30-Shigure/08-Shigure-Keymap解析与按键发送]]。

## 定位 Fuyutsui

1. 用完全匹配的游戏窗口标题取得 HWND。
2. 取得 PID，以 `PROCESS_QUERY_LIMITED_INFORMATION` 打开进程并查询可执行文件路径。
3. 从 EXE 目录开始沿祖先目录向上回溯。
4. 每一级检查 `Interface/AddOns/Fuyutsui`，找到即返回插件根目录。

这不是固定安装路径，但仍依赖窗口标题和进程路径可查询。它没有验证插件签名或目录内容版本。

## LuaLiteParser 的安全与语法边界

解析器只把 Lua 当作数据文本：

- 先用 ordinal `IndexOf` 找赋值名，再找其后的 `=` 和第一个 table；因此同名注释或子字符串可能误命中。
- 支持 `{}` table、键值项、数组项、字符串、十进制/指数数字、true/false/nil 和裸标识符。
- 支持 `--` 行注释、Lua 双方括号块注释和基础引号转义，并捕获条目尾注释。
- 不支持表达式、函数调用、十六进制、完整 Lua 语义或通用 long-bracket 字符串。
- 不执行插件代码，因此不存在通过 Lua 表触发 C# 动态执行的路径。

任何在支持子集之外的合法 Lua，都可能无法解析或在 round-trip 后改变表示。

## ClassBlocks round-trip

- Store 读取指定赋值表，得到专精、状态块和 `spellsList`。
- 只要任一专精含命名 table，整个文档就被判为 modern。
- 保存时只替换源文件中该 table literal，表外文本保留；表内部按 Store 支持的 schema 重新序列化，不承诺保留未知字段或原始格式。
- 保存是直接写回源 Lua，不是临时文件原子替换，也不自动备份。
- 旧稀疏专精会返回空编辑数据；若整个文档不是 modern，Store 拒绝保存。混合 modern/legacy 文件尤其危险：全局可被判 modern，但 legacy 专精仍为空，保存可能造成数据损失。
- `spellsList` 在 ClassBlocks UI 中只读。

## ClassBlocks → config

转换器遍历 13 个职业 Lua 文件：缺失文件记录警告并跳过；每个成功文件直接覆盖对应职业 JSON。因此这是可部分完成的批处理。

步骤编号从 1 顺序累加，主要顺序是：

1. 固定通用字段；它们在职业 JSON 中可跳过写入，但仍占步骤。
2. 专精普通状态；目标/焦点嵌套字段带前缀。
3. 玩家、目标和焦点的光环，按 harmful/helpful 固定顺序。
4. 每法术冷却；充能法术再占一行。
5. 每个 spell ID 的 maxCharge/castCount 动作条项。
6. 玩家光环 maxApps 动作条项。
7. 组员块，记录 `start`、每单位字段数 `num` 和相对步骤。

特殊规范化包括：光环必须有 ID；仅锚点/有效性/移动按 bool；旧 `法术失败` 改名 `插入法术`。转换器没有在写出前验证最终步骤不超过 510，同名字段还会覆盖 JSON 键但继续消耗步骤，需人工/契约验证。

## ClassMacros round-trip

- Store 同样只替换目标 table literal，表外文本保留、表内按 canonical 格式重写。
- 支持 common/spec 动态宏、数组和受支持的尾注释。
- 未建模的表内字段或格式不保证保留。
- 仍是直接覆盖 Lua 文件，无原子替换、备份或跨文件事务。

原始宏结构说明见 [[docs/CLASSMACROS_AI_Reference_zh-CN]]。

## ClassMacros → keymap

- 固定键池为 7 个修饰组合 × 39 个主键 = **273** 项；主键序列刻意不含 F4。顺序必须与 Fuyutsui 动作条扫描完全一致。
- 动态宏项每项预留/消耗 30 个单位位置，然后是静态宏和特殊宏；超过容量会警告并截断。
- 旧 flat 格式或 common+spec 格式都可转换；专精输出是“common + 当前 spec + static + special”的完整映射，以适配运行时不合并顶层的语义。
- party1..4 映射组员槽 2..5；原始 `@player` 动态语义映射组员槽 1；显式中文/player 保留单位 31；raid1..30 映射 1..30。
- 方括号中非 `@` 条件汇总为 macroCondition；尾注释可以覆盖从宏文本推导的 spell 名。
- 能识别 stopcasting、castsequence、最后一个 cast 和 item 等受支持形式；不等于完整 WoW 宏解释器。
- 输出包含全部 273 项，包括空项，以维持索引位置。

## 同步队列和运行时重启

`MainForm` 用 `_configUpdateTail` 形成串行尾任务，避免多个配置保存/转换同时写文件。定位插件并同步时，转换器在后台任务中执行，随后刷新配置/Keymap 目录并重启运行时；UI 日志只展示前 20 条警告。

启动或重启运行会先等待当前配置队列，确保新会话读取到已排队更新。窗口关闭也等待运行时释放和配置队列完成。但是队列只解决**进程内并发顺序**，不把多个 Lua/JSON 文件变成事务；中途异常之前的写入仍然存在。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 无法定位插件 | 窗口标题、进程权限、实际目录祖先是否包含标准插件路径 |
| Lua 看似合法却解析失败 | 是否用了函数/表达式/hex/long string 等不支持语法；赋值名是否被注释先命中 |
| 保存后注释/排版变化 | 表内部会 canonical 重写；仅表外文本有较强保留保证 |
| 旧 ClassBlocks 页面为空 | legacy sparse 专精不被完整建模；不要保存混合文件 |
| config 部分职业是新、部分是旧 | 13 文件转换非事务，某个文件失败后可能已有部分覆盖 |
| 某状态永远为 0 | 步骤顺序、重复名覆盖、是否超过 510 |
| Keymap 后半被截断 | 动态项每项消耗 30，整体超过 273 |
| 刚保存但运行时仍用旧数据 | 等待同步队列结束并确认会话已重启 |

## 修改影响

- 改 Lua schema 必须先扩展 parser/store 的受支持模型和 serializer，再允许 UI 保存，否则会丢未知数据。
- 改 ClassBlocks 顺序要同步 Fuyutsui 生产者、config 转换器、StateBuilder 和 510 容量检查。
- 改键池顺序、数量或目标映射要同步 Fuyutsui 动作条扫描、Keymap 转换器和 v3 单位契约。
- 若需要可靠回滚，应为 Lua 和批量 JSON 引入备份/临时文件/事务清单，不能只依赖 UI 队列。

## 源码索引

- `Shigure/Infrastructure/WowAddonLocator.cs:10-130`：标题、进程路径和祖先回溯。
- `Shigure/Infrastructure/LuaLiteParser.cs:90-480`：赋值定位、table/值/字符串/数字/注释子集。
- `Shigure/Infrastructure/ClassBlocksStore.cs:102-207`：modern 判定、替换保存与 legacy 行为。
- `Shigure/Infrastructure/ClassMacrosStore.cs:63-354`：宏表加载、直接写回和 canonical 序列化。
- `Shigure/Infrastructure/FuyutsuiConfigConverter.cs:44-483`：13 职业、步骤顺序、组员与字段规范化。
- `Shigure/Infrastructure/FuyutsuiKeymapConverter.cs:21-475`：273 键池、单位映射、宏/注释解析。
- `Shigure/UI/MainForm.cs:612-778`：串行更新尾、定位、转换、刷新和告警。
- `Shigure/UI/MainForm.cs:781-958`：运行前等待队列并重启会话。

## 知识图谱链接

- 生产者入口：[[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]
- 状态契约：[[docs/40-跨项目/02-Shingen-ClassBlocks到config同步契约]]
- 按键契约：[[docs/40-跨项目/03-Shingen-ClassMacros到keymap与按键契约]]
- 消费端：[[docs/30-Shigure/03-Shigure-配置合并与GameState构建]]、[[docs/30-Shigure/08-Shigure-Keymap解析与按键发送]]
