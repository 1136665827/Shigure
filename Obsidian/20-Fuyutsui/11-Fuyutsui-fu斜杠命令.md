---
title: Fuyutsui /fu 斜杠命令
summary: /fu 与 /fuyutsui 命令的用户用法、参数语义、角色配置副作用、状态像素同步和插入法术校验流程。
aliases:
  - Fuyutsui /fu 命令
  - /fu 命令
  - /fuyutsui 命令
tags:
  - project/fuyutsui
  - doc/feature
  - area/command
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[20-Fuyutsui/10-Fuyutsui-命令快捷按钮与存档]]"
  - "[[20-Fuyutsui/04-Fuyutsui-玩家状态]]"
  - "[[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]]"
  - "[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]"
source_files:
  - Fuyutsui/core/commands.lua
  - Fuyutsui/core/core.lua
  - Fuyutsui/core/quickbutton.lua
  - Fuyutsui/core/spells.lua
  - Fuyutsui/core/stateblocks.lua
source_symbols:
  - Fuyutsui:SlashCommand
  - Fuyutsui:InsertSpellCommand
  - Fuyutsui:SwitchCharFlag
  - Fuyutsui:SwitchDelay
  - Fuyutsui:SetInsertSpell
verified_at: 2026-08-16
---

# Fuyutsui `/fu` 斜杠命令

上级：[[20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[20-Fuyutsui/10-Fuyutsui-命令快捷按钮与存档]] · [[20-Fuyutsui/04-Fuyutsui-玩家状态]] · [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]]

> [!summary] AI 快速摘要
> `/fu` 是 Fuyutsui 的游戏内命令入口，`/fuyutsui` 是完全等价的长别名。命令可以修改角色级爆发、AOE、输出和药水开关，显示或隐藏快捷控件，临时置位 `delay`，以及按名称向像素状态写入一个待执行法术序号。输入 `/fu` 或 `/fu help` 可在聊天框打印内置帮助。

## 快速使用

```text
/fu
/fu help
/fu cd on
/fu aoemode auto
/fu dpsmode assistant
/fu potion off
/fu delay 3
/fu i 真言术：盾 mouseover
/fu hide
/fu show
```

命令关键字和单位 token 不区分大小写；技能名称从原始输入中提取，并按 `spellsList` 中的显示名称精确匹配。命令首尾空白会被去除。

## 完整命令表

| 命令 | 当前行为 | 写入字段/状态 |
| --- | --- | --- |
| `/fu`、`/fu help` | 在聊天框打印命令帮助 | 无 |
| `/fu cd` | 在开启与关闭之间切换爆发 | `db.char.cooldowns` |
| `/fu cd on` | 开启爆发 | `cooldowns = 1` |
| `/fu cd off` | 关闭爆发 | `cooldowns = 0` |
| `/fu aoemode` | 在自动与单体模式之间切换 | `db.char.aoeMode` |
| `/fu aoemode auto` | 使用自动模式 | `aoeMode = 0` |
| `/fu aoemode aoe` | **当前源码实际切到单体模式** | `aoeMode = 1` |
| `/fu dpsmode` | 在官方一键辅助与手写逻辑之间切换 | `db.char.dpsMode` |
| `/fu dpsmode assistant` | 使用官方一键辅助 | `dpsMode = 0` |
| `/fu dpsmode manual` | 使用手写逻辑 | `dpsMode = 1` |
| `/fu potion` | 切换爆发药水开关 | `db.char.potion` |
| `/fu potion on` | 开启爆发药水 | `potion = 1` |
| `/fu potion off` | 关闭爆发药水 | `potion = 0` |
| `/fu hide` | 隐藏快捷控件，并保存可见性 | `quickButtonShow = false` |
| `/fu show` | 显示快捷控件，并保存可见性 | `quickButtonShow = true` |
| `/fu delay [秒]` | 临时启用延迟标志，超时后自动恢复 | `delay = 1 → 0` |
| `/fu i 技能名称 [单位]` | 校验并短暂写入技能对应的宏序号 | `state.insertSpell = index / 255` |

除 `/fu` 外，所有示例也可将前缀替换为 `/fuyutsui`。

> [!warning] `aoemode aoe` 的命名与实际结果不一致
> 当前 `commands.lua` 将 `aoemode aoe` 写成 `aoeMode = 1`，随后提示“单体模式”。因此应以“单体”结果和数值 `1` 为准；不要仅根据参数名 `aoe` 推断它会强制群体模式。

## 配置开关如何生效

四类常驻开关的执行链相同：

```text
SlashCommand
  → 翻转或显式写入 db.char 字段
  → SwitchCooldown / SwitchAoeMode / SwitchDpsMode / SwitchPotion
  → SwitchCharFlag
  → 打印结果
  → 刷新对应配置状态块
  → 刷新快捷控件外观
```

配置状态块的对应关系如下：

| 角色字段 | 状态块名称 | `0` 的含义 | `1` 的含义 |
| --- | --- | --- | --- |
| `cooldowns` | 爆发开关 | 关闭 | 开启 |
| `aoeMode` | AOE开关 | 自动 | 单体 |
| `dpsMode` | 输出模式 | 官方一键辅助 | 手写逻辑 |
| `potion` | 爆发药水开关 | 关闭 | 开启 |
| `delay` | 延迟 | 正常 | 延迟生效 |

这些字段存放在当前角色的 `FuyutsuiADB.char["角色名 - 服务器"]` 中，不是账号内所有角色共享。常驻开关和快捷控件可见性会随 SavedVariables 保存。若当前职业配置未声明对应状态块，数据库和快捷控件仍会更新，但 `UpdateBareStateBlock()` 找不到像素索引时不会产生该项屏幕输出。

`SwitchCharFlag()` 只负责显示并同步当前值，本身不会翻转字段。调用它之前必须先由命令或快捷控件写入新值。

## `/fu delay [秒]`

`delay` 用于把角色字段 `db.char.delay` 临时置为 `1`：

1. 省略秒数时默认持续 1 秒。
2. 秒数通过 `tonumber()` 解析，必须大于 `0`；整数和正小数都可接受。
3. 命令立即取消旧 timer、写入 `delay = 1` 并刷新“延迟”状态块。
4. 新 timer 到期后写回 `delay = 0`，打印“延迟已恢复。”并再次刷新状态块。
5. 延迟已经生效时再次执行命令会重置倒计时，但不会再次打印“延迟已生效”。

示例：

```text
/fu delay       # 持续 1 秒
/fu delay 5     # 持续 5 秒
/fu delay 0.25  # 持续 0.25 秒
```

> [!note] timer 不是持久化状态
> timer 只存在于本次游戏会话中，而 `delay` 字段属于 SavedVariables。如果在倒计时结束前退出或重载，数据库可能留下 `delay = 1`，下次加载时没有旧 timer 负责自动清零。

> [!warning] 当前路由使用宽松前缀匹配
> 分支条件是 `command:match("^delay")`。因此 `/fu delayfoo` 也会进入 `delay` 分支，并因没有解析到秒数而按默认 1 秒执行。调用方应只生成规范形式 `/fu delay` 或 `/fu delay 正数`。

## `/fu i 技能名称 [单位]`

该命令不是直接施法，而是将当前职业宏列表中的一个动作序号短暂写入“插入法术”像素，供下游逻辑识别。

### 可选单位

末尾参数匹配下列 token 时才被视为单位：

- 固定单位：`player`、`target`、`focus`、`mouseover`、`cursor`、`pet`
- 编号单位：`partyN`、`raidN`、`bossN`，其中 `N` 是一个或多个数字

代码只验证 token 的文本格式，不验证该单位此刻存在，也不限制编号是否是 WoW 的有效范围。

### 校验与写入流程

```text
/fu i 技能名称 [单位]
  → 从末尾识别可选单位
  → 以技能显示名称精确查找 spellsList
  → 在当前 ClassMacros 的 dynamic/static/special 列表中验证技能
  → SetInsertSpell(index, spellName, unit)
  → state.insertSpell = index / 255
  → 1.5 秒后清零，或匹配的法术成功施放后提前清零
```

单位参数只参与宏条目校验和聊天提示，最终像素只包含 `index / 255`，不包含单位本身。需要还原目标语义的消费者只能依据这个宏序号所对应的 `ClassMacros` 约定判断。

常见失败提示：

| 提示 | 原因 |
| --- | --- |
| `用法: /fu i 技能名称 [单位]` | 没有提供技能名称 |
| `未在 spellsList 中找到技能` | 技能显示名称与当前 `spellsList` 不完全一致 |
| `未在 ClassMacros 中找到技能` | 技能虽在 `spellsList`，但当前职业宏列表没有匹配项 |
| `未在 ClassMacros 中找到技能（或单位不匹配）` | 提供了单位，但宏列表校验失败 |

### 当前实现边界

- `FindSpellListByName()` 使用 `pairs()` 返回第一个同名项；若 `spellsList` 有重复显示名称，选择结果没有稳定顺序保证。
- 宏技能判断除了清理条件和分支后比较，还允许任意字面子串命中，可能出现短名称误匹配。
- `MacroEntryMatchesUnit()` 当前用纯文本模式搜索字面量 `%[`，通常无法识别普通宏中的 `[`；带单位的命令因而可能绕过预期的 `@unit`/`target=unit` 检查。
- 新的插入命令会取消旧的 1.5 秒 timer，并以新序号覆盖旧状态。

## 输入解析与错误行为

- `/fu` 注册于插件初始化阶段；`/fuyutsui` 指向同一个 `SlashCommand()`。
- 命令路由使用转为小写后的输入，因此 `CD ON`、`DPSMODE MANUAL` 等关键字可正常识别。
- 插入法术从原始输入解析，避免改写中文或技能名称中的大小写。
- 未知命令只打印 `输入 /fu help 查看命令。`，不会修改状态。
- 除 `delay` 和 `i` 的参数解析外，其余命令按完整字符串匹配；应使用表格中的规范空格和拼写。
- 所有依赖角色配置的命令在 `Fuyutsui.db.char` 尚未建立时直接返回。

## 修改与验证清单

新增或修改 `/fu` 子命令时至少检查：

1. `core/commands.lua` 中的路由、帮助文本和错误提示是否一致。
2. 新字段是否加入 `core/core.lua` 的 `Fuyutsui.defaults.char`。
3. 是否需要在 `core/stateblocks.lua` 增加 getter，并在职业 `ClassBlocks` 中声明像素位置。
4. 是否需要同步 `core/quickbutton.lua` 的显示或鼠标操作。
5. 若改变插入法术索引或单位语义，是否同步 [[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]。
6. 在 WoW 内验证聊天命令、SavedVariables、像素刷新、timer 和快捷控件；静态检查无法完整模拟 WoW API。

## 源码索引

- `Fuyutsui/core/core.lua:74-81`：注册 `/fu` 与 `/fuyutsui`。
- `Fuyutsui/core/core.lua:171-185`：角色配置默认值。
- `Fuyutsui/core/commands.lua:5-80`：角色配置读取、规范化与状态同步。
- `Fuyutsui/core/commands.lua:82-183`：单位、技能名称、ClassMacros 校验。
- `Fuyutsui/core/commands.lua:185-303`：全部命令路由与帮助文本。
- `Fuyutsui/core/quickbutton.lua:5-32`：`show`/`hide` 的持久化与可见性刷新。
- `Fuyutsui/core/spells.lua:64-100`：插入法术的写入、timer 和施法成功清理。
- `Fuyutsui/core/stateblocks.lua:135-139,292-297`：配置 getter 与兼容分类刷新。

## 知识图谱

本页只描述 `/fu` 命令入口。角色存档结构和快捷控件交互详见 [[20-Fuyutsui/10-Fuyutsui-命令快捷按钮与存档]]；配置值如何进入玩家状态见 [[20-Fuyutsui/04-Fuyutsui-玩家状态]]；插入法术所依赖的宏序号、动作条与按键契约见 [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描]] 和 [[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]。
