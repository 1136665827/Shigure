---
title: Fuyutsui 玩家状态
summary: 说明玩家身份、专精、战斗、生命资源、施法、队伍元数据和辅助推荐如何进入状态块。
aliases:
  - Fuyutsui Player State
  - Fuyutsui 玩家像素
tags:
  - project/fuyutsui
  - doc/feature
  - area/player
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]"
  - "[[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]"
  - "[[docs/30-Shigure/03-Shigure-配置合并与GameState构建]]"
source_files:
  - Fuyutsui/core/player.lua
  - Fuyutsui/core/stateblocks.lua
  - Fuyutsui/core/events.lua
  - Fuyutsui/core/curves.lua
source_symbols:
  - Fuyutsui:GetCharacterInfo
  - Fuyutsui:GetCharacterSpecInfo
  - Fuyutsui:UpdatePlayerBlocks
  - Fuyutsui:UpdatePlayerPower
  - Fuyutsui:UpdatePlayerAssistant
  - Fuyutsui:UpdatePlayerCastBlocks
verified_at: 2026-08-09
---

# Fuyutsui 玩家状态

上级：[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] · [[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] · [[docs/30-Shigure/03-Shigure-配置合并与GameState构建]]

## AI 快速摘要

> `core/player.lua` 维护 `Fuyutsui.state`，`core/stateblocks.lua` 把中文字段名路由到 getter。身份与专精重建布局，事件更新生命、资源、移动、战斗和施法，`OnUpdate` 补充读条、一键辅助及战斗时间。多数数值压缩到 `0..1` 的蓝通道；离散编号通常以 `/255` 编码。

## 范围与非范围

本页覆盖玩家自身状态及共享运行时字段，不覆盖目标/焦点、队伍成员、法术冷却和光环容器内部实现；分别见 [[docs/20-Fuyutsui/05-Fuyutsui-目标焦点与敌人数]]、[[docs/20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]、[[docs/20-Fuyutsui/06-Fuyutsui-法术与物品冷却]]、[[docs/20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。

职业文件只声明所需状态名。本页描述共同 schema，不逐职业列技能。

## 输入与输出

| 输入源 | 典型状态 | 写出方式 |
|---|---|---|
| `UnitClass`、专精 API | `classIndex`、`specIndex`、`specID`、`specRange` | 固定槽 2/3 与专精重建 |
| 生命、资源和符文 API | `healthPercent`、`power[powerType]`、`runeCount` | 颜色曲线或 `/255` |
| 战斗与移动事件 | `combat`、`combatTime`、`moving`、`valid` | 布尔或秒数 `/255` |
| 施法 Duration API | 施法/引导/蓄力进度、可打断、目标 | 每帧更新对应状态块 |
| Assisted Combat | `assistantSpell` | `spellsList` 中的宏序号 `/255` |
| 队伍、地下城、遭遇 API | 队伍类型/人数、地图、难度、遭遇 | 枚举或编号 `/255` |
| 配置和物品状态 | 开关、药水数量、插入法术 | 对应状态 getter |

输出位置由 [[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 决定，Shigure 再依据生成配置把 `B` 写入 `GameState`。

## 执行链路

```text
PLAYER_ENTERING_WORLD / 专精变化
  -> GetCharacterInfo()
  -> GetCharacterSpecInfo()
     -> LoadPlayerBlocks(spec)
     -> UpdateSpellKnown() / UpdateGroup() / LoadPlayerMacros()
  -> UpdatePlayerBlocks()
     -> 各 Update* 写 Fuyutsui.state
     -> UpdateStateBlock(category, name)
     -> CreateTexture(index, B)
```

`GetCharacterSpecInfo()`（`core/player.lua:19-39`）不仅读取专精：它还清空施法标志、重建像素布局、已知法术、队伍、宏和物品计数，因此是玩家状态域的重建边界。

## 核心状态族

### 身份、有效性与战斗

- 职业与专精直接使用 API 数字除以 255。
- `valid` 综合死亡/鬼魂、坐骑、聊天框打开、饮水和坐骑施法状态，供外部程序判断是否应执行动作。
- `combat` 由战斗事件更新；进入战斗记录 `combatStartTime`，每秒把持续秒数截到 255 秒后归一化。
- `moving` 由开始/停止移动事件写入。

### 生命与资源

- 生命百分比使用 `curve100` 的蓝通道，不手算整数百分比。
- `powerNameMap` 把 `Enum.PowerType` 名称映射成职业表使用的中文字段；每种资源拥有自己的动态曲线。
- `UpdatePlayerPower()` 同时尝试“能量”和兼容旧表的“状态”分类。
- 符文数量单独按可用符文累计后 `/255`，也通过裸键兼容两个分类。

### 施法状态

- 普通施法、引导和蓄力分别维护进度；`GetUnitCastPixel()` 与 `GetUnitInterruptiblePixel()` 从 WoW Duration/Curve 对象求颜色。
- 施法开始/停止等事件只切换状态并刷新；连续进度由每帧 `UpdatePlayerCastBlocks()` 推进。
- `state.castTargetIndex`、`state.castingSpell` 等值来自宏法术映射，用于告诉外部端“正在施放哪个自动化动作、指向哪个单位”。

### 一键辅助与宏索引

`UpdatePlayerAssistant()` 获取 Assisted Combat 推荐 SpellID，再查 `Fuyutsui.spellsList[spellID].index`。这个 `spellsList` 是宏/快捷键语义表，与 `blocks.spells` 的冷却像素表不是同一对象。蓄力推荐结束后还会强制输出 0 持续 0.5 秒，避免旧推荐残留。

### 队伍与场景元数据

- 团队中输出玩家的团队序号；小队输出 46；无队伍输出 0。
- 队伍人数、地图 ID、遭遇 ID、难度、英雄天赋等均作为离散状态供外部策略判断。
- `UpdateHeroTalent()` 使用延迟回调读取天赋配置，专精切换后不是同步快照。

### 职业扩展状态

共同 getter 还覆盖醉拳、神圣军备、姿态、饮水、天启骑士等少量职业机制。职业 Lua 只需在 `ClassBlocks.states` 声明相应名字；更新入口仍在 `player.lua`，写出仍走统一状态路由。

## 核心数据与不变量

- `Fuyutsui.state` 存储的是准备写进 `B` 的归一化值，而非统一的原始业务值。
- getter 返回 `nil` 时 `UpdateStateBlock()` 不写纹理；旧像素因此可能保留，调用方应在“未知/不存在”时明确给出 0 或约定哨兵。
- 离散编号必须小于等于 255，且 C# 端应以相同缩放反解。
- `spellsList` 的 index 代表宏按钮顺序；`blocks.spells` 的 index 代表屏幕位置，禁止互换。
- 专精切换后所有持有旧 `blocks`、旧 AuraContainer 或旧宏序号的对象都必须重建。
- 状态字段名称是配置生成契约；更名等价于跨项目 schema 变更。

## 失败模式与当前风险

1. **已在战斗中启用插件。** `UpdatePlayerCombatTime()` 在 `state.combat=true` 时直接用 `GetTime() - state.combatStartTime`；若未收到进入战斗事件，起始时间可能为 `nil`（`player.lua:64-74`）。
2. **坐骑/姿态比较量纲错误。** `UpdateShapeshiftForm()` 保存 `formID/255`，而 `UpdatePlayerMounted()` 将它与原始 `27/3/29` 比较（`player.lua:243-245,302-305`），德鲁伊形态兜底不会按预期命中。
3. **混合时刻快照。** 初始化先重建多域对象再逐项刷新；外部端可能短暂读到新索引配旧值，应依赖锚点/职业/专精稳定后再消费。
4. **异步英雄天赋。** 延迟回调可能跨越一次快速专精切换；修改时应在回调内重新确认当前专精。
5. **未知资源或 getter。** `powerNameMap`、职业声明与 getter 名称任一不匹配都会静默跳过。

## 修改影响

- 新增通用状态：同时修改 `stateBlockGetters`、事件/轮询更新器和所需职业 `ClassBlocks`，再重新生成 Shigure 配置。
- 更改 `/255`、曲线或布尔语义：同步 [[docs/30-Shigure/03-Shigure-配置合并与GameState构建]] 的解码/业务判断。
- 更改专精重建顺序：检查光环、队伍、宏和定时器是否仍持有旧对象。
- 修复姿态/坐骑逻辑时，应选定“保存原始 ID”或“比较归一化值”一种一致表示，并审计所有 getter。

## 源码索引

- `Fuyutsui/core/player.lua:12-55`：角色与专精重建入口。
- `Fuyutsui/core/player.lua:57-132`：有效性、战斗、施法、生命与资源。
- `Fuyutsui/core/player.lua:134-262`：辅助推荐、队伍/遭遇元数据、配置与条布局。
- `Fuyutsui/core/player.lua:264-421`：职业扩展、姿态、聊天框和坐骑施法。
- `Fuyutsui/core/stateblocks.lua:12-30,44-291`：资源名称、getter schema 与状态写出。
- `Fuyutsui/core/events.lua:81-188,419-442`：事件状态机和分频刷新。

## 知识图谱

本页接收 [[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] 的更新时机，经 [[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 写成像素，并由 [[docs/30-Shigure/03-Shigure-配置合并与GameState构建]] 还原为运行时状态；宏索引来源另见 [[docs/20-Fuyutsui/10-Fuyutsui-命令快捷按钮与存档]]。
