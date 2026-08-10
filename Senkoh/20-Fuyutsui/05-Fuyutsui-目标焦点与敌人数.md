---
title: Fuyutsui 目标焦点与敌人数
summary: 说明目标/焦点的类型、距离、生命和施法状态，以及姓名板驱动的敌人数与仇恨统计。
aliases:
  - Fuyutsui Target Focus
  - Fuyutsui 敌人数
tags:
  - project/fuyutsui
  - doc/feature
  - area/targeting
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]"
  - "[[docs/20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]"
  - "[[docs/30-Shigure/03-Shigure-配置合并与GameState构建]]"
source_files:
  - Fuyutsui/core/target.lua
  - Fuyutsui/core/stateblocks.lua
  - Fuyutsui/core/events.lua
  - Fuyutsui/core/core.lua
source_symbols:
  - Fuyutsui:GetUnitRange
  - Fuyutsui:UpdateUnitFullInfo
  - Fuyutsui:UpdateUnitType
  - Fuyutsui:UpdateUnitRangeBlock
  - Fuyutsui:AddNameplate
  - Fuyutsui:UpdateEnemyCount
verified_at: 2026-08-09
---

# Fuyutsui 目标焦点与敌人数

上级：[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] · [[docs/20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] · [[docs/30-Shigure/03-Shigure-配置合并与GameState构建]]

## AI 快速摘要

> `core/target.lua` 为 `target` 和 `focus` 分别维护缓存，输出类型、可攻击/协助、距离、死亡、生命和施法状态。姓名板事件维护另一张敌人缓存，0.2 秒轮询距离与战斗状态后输出总敌人数，并在仇恨事件中拆分有/无仇恨数量。目标/焦点状态使用带分类前缀的键，不与玩家裸状态键冲突。

## 范围与非范围

本页覆盖单位快照与姓名板聚合，不覆盖目标/焦点光环的筛选与持续时间，后者见 [[docs/20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。队伍距离和治疗范围见 [[docs/20-Fuyutsui/07-Fuyutsui-队伍与治疗吸收]]。

## 输入与输出

| 输入 | 缓存/计算 | 输出字段族 |
|---|---|---|
| `target`、`focus` 单位 API | `Fuyutsui.target`、`Fuyutsui.focus` | 类型、距离、生命、施法/引导、可打断 |
| LibRangeCheck | `minRange`、`maxRange`、`inRange` | 目标/焦点距离与类型中的范围位 |
| 姓名板增删事件 | `Fuyutsui.nameplate[unit]` | 当前可计数单位集合 |
| 仇恨和战斗 API | `threatStatus`、`affectingCombat` | 敌人数、有仇恨、无仇恨 |
| 地图/遭遇状态 | 特例过滤 | 是否把特定单位计入聚合 |

所有计数以 `count/255` 写入蓝通道；单位生命使用 `curve100` 的蓝通道。

## 执行链路

### 目标与焦点

```text
PLAYER_TARGET_CHANGED / PLAYER_FOCUS_CHANGED
  -> UpdateUnitFullInfo(unit)
     -> UpdateUnitCanAttack(unit) -> UpdateUnitType(unit)
     -> UpdateUnitDeathStatus(unit) -> UpdateUnitType(unit)
     -> UpdateUnitHealthBlock(unit)

每 0.2 秒
  -> UpdateUnitRangeBlock(target/focus)
     -> LibRangeCheck -> cache -> 距离像素
```

施法开始/停止事件会单独调用 `UpdateUnitCastingOrChannelingInfo()`；它刷新施法、可打断、引导和引导可打断四个状态，不重建整个单位快照。

### 姓名板敌人数

```text
NAME_PLATE_UNIT_ADDED
  -> AddNameplate(unit)
  -> 缓存名称、GUID、阵营、距离等

UNIT_THREAT_SITUATION_UPDATE
  -> UpdateNameplateThreat(unit)
  -> UpdateThreatEnemyCounts()

每 0.2 秒
  -> UpdateEnemyCount()
  -> 重读范围/战斗状态 -> count/255

NAME_PLATE_UNIT_REMOVED
  -> 从缓存删除
```

## 单位类型编码

`UpdateUnitType()` 根据死亡、敌友关系、驱散能力和范围计算离散值：

- 不存在、死亡或无法分类时为 0。
- 可攻击敌对单位以 `1/255` 为基础；具体驱散/范围信息由 `target.enemyCurve` 编入。
- 可协助友方单位以 `11/255` 为基础；可驱散类型由 `target.friendCurve` 编入。

这些曲线在 `UpdateSpellKnown()` 中按当前角色实际驱散能力重建，因此目标“类型”同时依赖法术已知状态；它不是单纯的敌友布尔值。

## 距离、生命与施法

- `GetUnitRange()` 直接返回 LibRangeCheck 的最小/最大估计。
- 对敌人，`maxRange <= state.specRange` 才标记专精射程内；对友方，阈值固定为 40。
- 距离状态 getter 输出缓存的最大距离并归一化；无法估计时应视为未知，不应自行解释为 0 米。
- 生命值使用 `UnitHealthPercent(..., curve100)`。
- 施法进度与可打断状态共享 [[docs/20-Fuyutsui/04-Fuyutsui-玩家状态]] 描述的 Duration/Curve 机制。

## 敌人数口径

姓名板缓存不是“附近所有敌人”的权威列表，而是当前客户端已经创建姓名板且通过 `IsCountedEnemy()` 过滤的单位：

- 必须是可攻击目标。
- 必须有可用最大距离且 `maxRange <= state.specRange`，并处于战斗关联状态；硬编码地图/遭遇可绕过战斗关联要求。
- `testMap` 与 `testEncounter` 对地图 `2393`、遭遇 `2563` 有硬编码特例。
- 仇恨统计把 `threatStatus >= 2` 记为有仇恨，其余可计数单位记为无仇恨。

因此输出适合战斗策略提示，不等价于战斗日志意义上的全场敌人总数。

## 核心数据与不变量

- `GetUnitCache()` 当前只为 `target` 和 `focus` 返回缓存；虽然 `unitZHMap` 列出 `boss1..boss5`，这些 boss token 目前不会进入该通用更新链。
- 状态键为 `目标类型`、`焦点类型` 这类“分类+名称”组合，必须与 `LoadPlayerBlocks()` 完全一致。
- 类型曲线必须在角色法术已知状态改变后重建，否则驱散能力位会过时。
- 姓名板缓存必须在移除事件删除；否则单位 token 重用会污染计数。
- 敌人数除以 255，消费端不能把 `B` 直接当原始整数。

## 失败模式与当前风险

1. **初始类型可能长期使用空缓存字段。** `UpdatePlayerBlocks()` 调用目标/焦点类型、距离和生命更新，但没有先调用 `UpdateUnitCanAttack()`；若启用时已有单位且未再触发 target/focus change，类型可能保持 0。
2. **距离未知时像素陈旧。** LibRangeCheck 可返回 `nil`；getter/更新器没有为所有未知分支强制写哨兵，旧距离像素可能保留。
3. **boss 映射是未完成入口。** `unitZHMap` 有 `boss1..5`，但 `GetUnitCache()` 不支持它们；仅增加事件调用不会产生输出。
4. **姓名板覆盖不完整。** 未显示姓名板、客户端未创建或刚被移除的敌人不会计数。
5. **硬编码副本规则会过时。** `testMap`/`testEncounter` 需要随版本验证，不能泛化为所有副本。
6. **受保护值。** 新版 WoW 的 secret value 若被直接比较、索引或打印会报错；新增单位逻辑需沿用安全检查策略。

## 修改影响

- 增加 boss 单位支持需同时新增缓存、状态声明、事件入口和 C# 配置字段。
- 更改类型枚举或驱散曲线时同步 [[docs/20-Fuyutsui/06-Fuyutsui-法术与物品冷却]] 与 Shigure 的业务判断。
- 更改敌人数口径时记录地图、战斗和范围边界；它会直接改变 AoE/目标选择策略。
- 更改目标/焦点字段名或顺序需重新生成配置，遵循 [[docs/40-跨项目/02-Shingen-ClassBlocks到config同步契约]]。

## 源码索引

- `Fuyutsui/core/target.lua:10-38`：距离 API、单位名映射、缓存和类型基础值。
- `Fuyutsui/core/target.lua:40-114`：类型、射程、施法、死亡和生命通用更新。
- `Fuyutsui/core/target.lua:116-164`：target/focus 包装入口。
- `Fuyutsui/core/target.lua:166-234`：姓名板缓存、仇恨与敌人数。
- `Fuyutsui/core/stateblocks.lua:229-253`：目标/焦点 getter。
- `Fuyutsui/core/events.lua:204-250,361-389,425-435`：相关事件与 0.2 秒轮询。

## 知识图谱

本页由 [[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] 驱动，通过 [[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 输出单位像素；类型曲线依赖 [[docs/20-Fuyutsui/06-Fuyutsui-法术与物品冷却]] 的驱散能力，目标/焦点光环由 [[docs/20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] 补充。
