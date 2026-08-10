---
title: Fuyutsui 光环容器本地集成
summary: 说明当前直接位于 core/block.lua 的 Blizzard AuraContainer 集成、持续/永久槽配对、反应过滤、层数条和队伍光环。
aliases:
  - Fuyutsui AuraContainer
  - Fuyutsui 光环槽
tags:
  - project/fuyutsui
  - doc/feature
  - area/aura
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[docs/AuraContainer_AI_Reference_zh-CN]]"
  - "[[docs/20-Fuyutsui/06-Fuyutsui-法术与物品冷却]]"
  - "[[docs/20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]"
source_files:
  - Fuyutsui/core/block.lua
  - Fuyutsui/main.lua
  - Fuyutsui/core/events.lua
  - Fuyutsui/Fuyutsui.toc
source_symbols:
  - EnsureAuraContainerLoaded
  - AddDurationAuraSlotPair
  - Fuyutsui:RefreshUnitAuraContainers
  - Fuyutsui:LayoutAuraApplicationBars
  - Fuyutsui:RefreshGroupAuraContainers
  - Fuyutsui:RebindAuraSpellFilters
verified_at: 2026-08-09
---

# Fuyutsui 光环容器本地集成

上级：[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[docs/AuraContainer_AI_Reference_zh-CN]] · [[docs/20-Fuyutsui/06-Fuyutsui-法术与物品冷却]] · [[docs/20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]

## AI 快速摘要

> 当前光环实现全部在 `Fuyutsui/core/block.lua:459-1201`。`Fuyutsui.toc` 不加载 `auracontainer.lua`，磁盘也不存在该文件；插件运行时只按需加载 Blizzard 的 `Blizzard_AuraContainer`。每个定义的顶部光环索引由“限时槽优先、永久槽后备”配对占用，玩家 `maxApps` 另追加 CountBars，队伍 aura/dispel 则写进成员块。

## 范围与非范围

本页描述 Fuyutsui 如何使用 Blizzard AuraContainer API。API 的更完整背景和历史审计见 [[docs/AuraContainer_AI_Reference_zh-CN]]；但历史审计不是当前事实，发生冲突时以 `core/block.lua` 和当前 `.toc` 为准。

本页不解释所有职业光环清单，也不逐职业罗列技能。

## 当前文件边界

- `Fuyutsui/Fuyutsui.toc:26-59` 的当前加载清单包含 `core/block.lua`，没有 `auracontainer.lua`。
- 仓库中不存在 `Fuyutsui/auracontainer.lua` 或同名大小写变体。
- `EnsureAuraContainerLoaded()` 检查并调用 `C_AddOns.LoadAddOn("Blizzard_AuraContainer")`；这是 Blizzard 自带按需插件，不是本仓库文件。

任何新增独立 `auracontainer.lua` 的方案都必须先改 `.toc`，否则不会执行；当前维护入口应直接定位 `core/block.lua:459-1201`。

## 输入与输出

| 输入定义 | 创建对象 | 输出位置/语义 |
|---|---|---|
| `ClassBlocks.auras.player` | 玩家 AuraContainer 槽对 | 顶部持续/永久光环像素 |
| target/focus harmful/helpful | 对应单位容器及反应过滤 | 顶部目标/焦点光环像素 |
| 玩家光环 `maxApps` | 额外应用层数 StatusBar | `FuyutsuiCountBars` |
| `group.aura[offset]` | 每成员 `HELPFUL|PLAYER` 槽 | 成员块中的持续/永久像素 |
| `group.dispel` + 当前驱散能力 | 每成员 HARMFUL dispel 槽 | 成员块中的固定驱散类型色 |
| 神圣牧师特殊状态槽 | 最多两个其他牧师容器 | `B=raidIndex/255` |

## 配置展开

`LoadPlayerBlocks()` 按以下单位/过滤顺序把职业光环表写入 `blocks.auras`：

1. `player`。
2. `target` 的 `HARMFUL`。
3. `target` 的 `HELPFUL`。
4. `focus` 的 `HARMFUL`。
5. `focus` 的 `HELPFUL`。

单项可以给 `spellId` 或 `spellIds`，还可带 `maxApps`。缺少 SpellID 集合的条目会打印警告并跳过，因此后续索引会相对“包含坏条目的静态预期”前移。

## 单位光环执行链

```text
LoadPlayerBlocks(spec)
  -> blocks.auras[index] = {unit, filter, spellId(s), maxApps}
  -> 释放旧容器

UpdatePlayerBarInfo()
  -> RefreshUnitAuraContainers()
     -> CollectAuraSpellSlots(unit)
     -> CreateUnitAuraDurationSlots(unit, slots)
        -> 每个 index 添加 timed 槽，再添加 permanent 槽
  -> LayoutAuraApplicationBars()

target/focus 变化
  -> UpdateUnitAuraContainer(unit)
  -> SetUnit -> 重设候选过滤 -> UpdateAllAuras
```

AuraContainer 自己响应 `UNIT_AURA`；总 `OnUpdate` 不轮询光环。

## 限时与永久槽配对

`AddDurationAuraSlotPair()` 对同一逻辑索引按固定顺序增加两个候选槽：

1. **timed 槽先添加**：带最大持续时间过滤，按到期排序，按钮背景保持该索引的 R/G，`B` 随剩余时间曲线变化。
2. **permanent 槽后添加**：无最大持续时间限制，命中时整格 `B=1`。

限时优先是重要不变量：AuraContainer 可能对同一个 auraInstance 做互斥分配，若永久槽先抢到，限时曲线就不会出现。

## 目标/焦点反应过滤

WoW 在敌对单位上处理 `HELPFUL`、友方单位上处理 `HARMFUL` 时，SpellID 候选过滤可能被忽略。当前实现先检查单位反应：

- 允许的单位/filter 组合使用真实 SpellID 集合。
- 不允许的组合改用 `maxDuration=0` 的“永不匹配”过滤，而不是空 SpellID 集合。
- 切换 target/focus 时按 `SetUnit → SetAuraSlotCandidateFilters → UpdateAllAuras` 顺序强制重绑。

这套顺序用于避免槽错误落到单位的“第一个任意光环”。

## 玩家应用层数条

只有 `maxApps` 的玩家光环会生成层数条：

- 在技能 CountBars 之后预留空间。
- 使用同一 SpellID 候选过滤。
- StatusBar 当前值为 applications，最大值为 `maxApps`。
- 全部技能条和光环层数条之后才更新灰色终点标记。

`auraBarLaidOut` 防止重复布局；专精重建时清理流程必须把该状态和已占空间一并恢复。

## 队伍光环与驱散

`RefreshGroupAuraContainers()` 依据当前 `groupList` 为每名成员创建/复用一个容器：

- `group.aura` 按 offset 排序，地址为 `start + (memberIndex-1)*num + offset`，使用 `HELPFUL|PLAYER` 及限时/永久槽对。
- `group.dispel` 只包含玩家当前 `includeDispelTypes` 会处理的减益；像素显示驱散类别固定色，不显示剩余时间。
- roster 重建后重新 `SetUnit` 并强制全量刷新；未使用的旧容器禁用并隐藏。

成员地址及 30 人边界见 [[docs/20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]。

## 其他牧师特殊槽

`救赎之魂1/2` 不是 `stateBlockGetters` 的普通 getter。神圣牧师职业表声明这两个状态名后，`RefreshOtherPriestAuraContainers()` 在团队中寻找除玩家外前两名牧师；当对方存在 SpellID `194384` 光环时，相应顶部像素写其 `raidIndex/255`。这是一条直接由 AuraContainer 覆盖状态槽的例外链路。

## 过场后的重绑

影片或过场结束后，事件层延迟调用 `RebindAuraSpellFilters()`，依次重绑玩家/目标/焦点、玩家层数条、队伍和其他牧师容器。原因是过场可能使候选过滤丢失或容器重新分配；只调用 `SetUnit` 不足以恢复 SpellID 精确过滤。

## 核心数据与不变量

- 当前实现文件是 `core/block.lua`；不要把不存在的 `auracontainer.lua` 当入口。
- 每个光环定义必须有非空 `spellId` 或 `spellIds`，集合内容必须是实际 Aura SpellID。
- 限时槽必须先于永久槽创建。
- target/focus 的身份不允许时必须使用不匹配过滤，不能用空 SpellID 表。
- Aura 按钮必须与普通顶部纹理使用同一个 `EncodeBlockChannels(index)`。
- `maxApps` 只适用于玩家层数条；它还会改变 CountBars 后续几何位置。
- 队伍 offset 不得超过 `group.num`，计算后的像素不得超过 510。

## 失败模式与当前风险

1. **误找不存在的文件。** 把修改写进新建但未加入 `.toc` 的 `auracontainer.lua`，运行时不会生效。
2. **候选过滤退化为任意光环。** 修改 reaction 或重绑顺序，可能让目标/焦点槽显示第一个无关 aura。
3. **永久槽抢占限时 aura。** 对调槽创建顺序会丢失持续时间输出。
4. **SpellID/显示技能 ID 混淆。** 技能施放 ID、buff/debuff Aura ID 可能不同；错误 ID 通常表现为始终为 0。
5. **应用条空间泄漏或重复。** 不正确维护 `auraBarLaidOut` 与 CountBars 游标会覆盖相邻条。
6. **驱散能力过时。** 学会/遗忘技能后若未运行 `UpdateSpellKnown()`，队伍 dispel 过滤仍用旧集合。
7. **过场/单位切换竞态。** 延迟重绑时 unit 已变化，回调必须使用容器当前绑定单位。

## 修改影响

- 修改 AuraContainer API 调用前核对当前 Blizzard 模板/API；这是版本敏感区。
- 新增单位类型需扩展容器 key、收集顺序、反应过滤、释放和重绑五条路径。
- 新增 `maxApps` 会改变横向扫描布局，需同步 [[docs/30-Shigure/02-Shigure-像素扫描与协议解码]] 和生成配置。
- 修改 group aura/dispel offset 同步 [[docs/40-跨项目/02-Shingen-ClassBlocks到config同步契约]]。
- 若未来拆出文件，必须把它加入 `.toc` 且保持在 `block.lua` 所需常量与 `main.lua` 调用之前加载。

## 源码索引

- `Fuyutsui/Fuyutsui.toc:26-59`：当前真实加载清单。
- `Fuyutsui/main.lua:87-126,183-195`：光环配置展开与容器重建触发。
- `Fuyutsui/core/block.lua:75-79`：加载 Blizzard AuraContainer。
- `Fuyutsui/core/block.lua:459-792`：候选集合、槽配对、过滤、持续时间和层数初始化。
- `Fuyutsui/core/block.lua:807-940`：单位容器与玩家层数条。
- `Fuyutsui/core/block.lua:946-1201`：队伍、驱散、其他牧师和全量重绑。
- `Fuyutsui/core/events.lua:330-360,390-404`：单位切换与过场恢复入口。

## 知识图谱

本页把 [[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 分配的光环索引绑定到 Blizzard AuraContainer，并从 [[docs/20-Fuyutsui/06-Fuyutsui-法术与物品冷却]] 接收驱散能力、向 [[docs/20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]] 填充成员槽；专项背景由 [[docs/AuraContainer_AI_Reference_zh-CN]] 补充。
