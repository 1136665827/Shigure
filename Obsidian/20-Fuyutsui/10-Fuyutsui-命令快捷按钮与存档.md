---
title: Fuyutsui 命令快捷按钮与存档
summary: 说明 /fu 命令、角色级配置开关、临时延迟/插入法术状态、快捷按钮交互和 FuyutsuiADB 兼容存档。
aliases:
  - Fuyutsui Commands
  - Fuyutsui SavedVariables
tags:
  - project/fuyutsui
  - doc/feature
  - area/config
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[20-Fuyutsui/04-Fuyutsui-玩家状态]]"
  - "[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]"
  - "[[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]"
source_files:
  - Fuyutsui/core/core.lua
  - Fuyutsui/core/commands.lua
  - Fuyutsui/core/quickbutton.lua
  - Fuyutsui/core/spells.lua
  - Fuyutsui/Fuyutsui.toc
source_symbols:
  - InitDB
  - Fuyutsui:SlashCommand
  - Fuyutsui:SwitchCharFlag
  - Fuyutsui:InsertSpellCommand
  - Fuyutsui:SetInsertSpell
  - Fuyutsui:InitQuickToggleButton
verified_at: 2026-08-30
---

# Fuyutsui 命令快捷按钮与存档

上级：[[20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[20-Fuyutsui/04-Fuyutsui-玩家状态]] · [[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]] · [[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]

## AI 快速摘要

> `/fu` 与 `/fuyutsui` 修改 `FuyutsuiADB` 的角色级开关并立即刷新相应状态像素；屏幕快捷按钮提供同一组开关的鼠标入口。`delay` 是会自动复位的临时角色标志，`i` 命令把 `spellsList` 宏序号编码 1.5 秒。存档保留旧 AceDB 的 `char/profiles/profileKeys` 外形，但当前逻辑主要使用角色表。

## 范围与非范围

本页覆盖用户控制面和持久化，不覆盖 C# 编辑器如何修改仓库/发布目录内置的 Lua、生成配置并部署游戏副本；后者见 [[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]。安全宏按钮的创建和键序见 [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]]。

## 存档模型

`.toc` 将 `FuyutsuiADB` 声明为 SavedVariables。`InitDB()` 兼容原 AceDB 形状：

```text
FuyutsuiADB
├─ char["角色名 - 服务器"]
├─ profileKeys["角色名 - 服务器"] -> profileName
└─ profiles[profileName]
```

`Fuyutsui.db.char` 指向当前角色表，`Fuyutsui.db.profile` 指向选择的 profile。默认值通过递归 `CopyDefaults()` 只补缺项，不覆盖用户已有值。

当前角色默认字段：

| 字段 | 默认 | 用途 |
|---|---:|---|
| `level` | 0 | 兼容字段 |
| `aoeMode` | 0 | 0 自动、1 单体/非自动 AoE |
| `cooldowns` | 0 | 爆发开关 |
| `dpsMode` | 0 | 0 官方一键辅助、1 手写逻辑 |
| `delay` | 0 | 临时暂停/延迟标志 |
| `potion` | 0 | 爆发药水开关 |
| `quickButtonCX/CY` | 180/-100 | 首次显示的旧式中心偏移 |
| `quickButtonShow` | true | 快捷按钮是否显示 |

profile 当前只有占位 `someInput`；不要假设开关会跨角色共享。

## 命令输入与输出

| 命令族 | 数据变更 | 像素/界面输出 |
|---|---|---|
| `/fu cd [on|off]` | `cooldowns` 0/1 | “爆发开关” + 快捷按钮 |
| `/fu aoemode [auto|aoe]` | `aoeMode` 0/1 | “AOE开关” + 快捷按钮 |
| `/fu dpsmode [assistant|manual]` | `dpsMode` 0/1 | “输出模式” + 快捷按钮 |
| `/fu potion [on|off]` | `potion` 0/1 | “爆发药水开关” + 快捷按钮 |
| `/fu delay [秒]` | `delay=1`，timer 后复位 0 | “延迟”状态 |
| `/fu i 技能名或spellId` | 从 `spellsList` 解析并临时写入 `insertSpell=index/255` | “插入法术”状态和聊天提示 |
| `/fu help` 或空输入 | 无 | 打印帮助 |

开关状态由 `stateBlockGetters["配置开关"]` 读取；旧职业表把它们放在“状态”分类时，`UpdateBareStateBlock()` 仍兼容。

## 开关执行链

```text
SlashCommand 或快捷按钮点击
  -> 先设置/翻转 db.char[key]
  -> SwitchCooldown/AoeMode/DpsMode/Potion()
     -> SwitchCharFlag()
        -> 打印当前值对应消息
        -> UpdateBareStateBlock(blockName)
        -> NormalizeCharConfig()
        -> RefreshQuickToggleAppearance()
```

`SwitchCharFlag()` 本身不翻转值；调用者必须先赋值。直接调用 `SwitchCooldown()` 只会同步和显示当前状态，不会切换。

配置值必须保持整数 0/1，因为多处逻辑使用 `c[key] == 0` 而不是 Lua truthiness；字符串、布尔或其他数字会被当成开启分支。

## 临时 delay

`/fu delay` 默认 1 秒，也接受任意正数：

- 已有 timer 会先取消，再从新秒数重新计时。
- 立即写 `c.delay=1` 和像素。
- timer 到期写回 0 并刷新。
- 重复延长时不重复打印“已生效”，但最终仍打印恢复。

timer 是会话运行时对象；SavedVariables 可能在退出时留下 `delay=1`，下次初始化只补默认值而不自动依据旧 timer 复位。

## 插入法术命令

`/fu i 技能名或spellId` 的解析链：

1. 纯十进制正整数按 spellId 直接读取 `Fuyutsui.spellsList[spellId]`。
2. 其他输入按中文显示名称精确查找 `Fuyutsui.spellsList`；同名条目使用 `pairs()` 首个命中项。
3. 只要条目具有 `index` 和 `name`，`SetInsertSpell()` 就写 `state.insertSpell = index/255`。
4. 聊天提示实际技能名称、spellId 和本地序号。
5. 1.5 秒后自动清零；若对应 SpellID 先施放成功，也会提前清零。

该命令不接受单位参数，也不检查 `ClassMacros`。推荐使用 spellId 规避“奥术洪流”等多个 spellId 共用同一名称时的名称查找歧义。例如 `/fu i 232633` 会精确取得该 ID 在当前 `spellsList` 中的本地序号；`/fu i 奥术洪流` 继续保留。

## 快捷按钮

`InitQuickToggleButton()` 创建 50×64、限制在屏幕内的可拖动 Frame：

- 左键：爆发开关；拖动也使用左键，通过位移阈值区分点击。
- 右键：AOE 模式。
- 中键：DPS 模式。
- Button4：药水开关。
- 文本同时显示爆发、群体、模式、药水的当前状态。

拖动后保存 `quickButtonPoint/RelPoint/X/Y`；首次没有这些字段时使用默认的 `quickButtonCX/CY`。`quickButtonShow=false` 会隐藏 Frame，但当前命令帮助没有提供显式显示/隐藏命令入口。

## 核心数据与不变量

- 角色键格式为 `name .. " - " .. realm`；迁服/改名会形成新的角色配置键。
- 配置开关保持数值 0/1，并与 `stateBlockGetters` 字段名一致。
- `SwitchCharFlag()` 是“同步当前值”，不是 toggle；调用顺序不能颠倒。
- `spellsList.index` 是当前职业技能列表的本地序号，必须与 Shigure 的“本地索引 → spellId”映射一致。
- 临时 timer 在设置新值前应取消旧 timer，避免旧回调提前清零新状态。
- 快捷按钮位置的四个新字段应成组存在；不完整记录会退回旧默认位置。

## 失败模式与当前风险

1. **delay 跨会话残留。** 在 timer 到期前退出游戏，`delay=1` 可能持久化；下次没有对应 timer 自动复位。
2. **同名技能非确定。** `FindSpellListByName()` 遍历哈希表并返回第一个同名项；重复名称时结果没有稳定保证，应优先使用 spellId。
3. **SavedVariables 类型污染。** `NormalizeCharConfig()` 只用 `or 0` 补空，不把 `true/false`、字符串或越界数字规范成 0/1。
4. **位置字段代际不一致。** 默认表只有 `quickButtonCX/CY`，拖动后使用另一组 point 字段；迁移/清理逻辑需同时考虑两代字段。

## 修改影响

- 新增开关需同时增加默认值、命令、getter、职业状态声明、快捷按钮（若需要）和 Shigure 配置字段。
- 修改插入法术的 spellId 或本地序号语义时，必须同步 `spellsList`、Shigure 的技能列表映射和相关状态条件。
- 改 SavedVariables 形状时提供显式迁移，保留现有角色数据；不要依赖 `CopyDefaults()` 完成类型迁移。
- 为快捷按钮增加显示命令时复用 `UpdateQuickToggleVisibility()`，并明确是否角色级保存。

## 源码索引

- `Fuyutsui/core/core.lua:21-84`：默认复制、AceDB 兼容初始化与 slash 注册。
- `Fuyutsui/core/core.lua:168-183`：默认存档 schema。
- `Fuyutsui/core/commands.lua:5-80`：配置规范化、开关同步和 delay 写出。
- `Fuyutsui/core/commands.lua:82-183`：插入法术解析与校验。
- `Fuyutsui/core/commands.lua:185-297`：完整 slash 命令路由。
- `Fuyutsui/core/spells.lua:60-97`：插入法术 1.5 秒状态机。
- `Fuyutsui/core/quickbutton.lua:5-180`：可见性、外观、拖动和点击。

## 知识图谱

本页把用户输入和 `FuyutsuiADB` 转为 [[20-Fuyutsui/04-Fuyutsui-玩家状态]] 的配置像素；插入法术序号直接来自当前 `Fuyutsui.spellsList`，其动作条扫描关系见 [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]]。
