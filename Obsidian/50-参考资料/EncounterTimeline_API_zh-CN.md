---
title: "World of Warcraft EncounterTimeline API：中文参考"
summary: "WoW 12.0+ C_EncounterTimeline 系统的函数、事件、数据结构、职责筛选和 Secret Values 使用边界。"
language: "zh-CN"
game_flavor: "Mainline"
interface_version: "12.0.7+"
verified_date: "2026-08-24"
stability: "外部 API 参考；以当前游戏客户端和 live UI 源码为准"
primary_topic: "C_EncounterTimeline"
tags:
  - "project/shigure"
  - "doc/reference"
  - "area/world-of-warcraft"
  - "area/encounter-timeline"
project: "Shigure"
doc_type: "external-reference"
status: "version-pinned"
authority: "external-reference"
up:
  - "[[50-参考资料/00-参考资料-MOC|参考资料 MOC]]"
related: []
source_files: []
source_symbols:
  - "C_EncounterTimeline"
  - "ENCOUNTER_TIMELINE_EVENT_ADDED"
  - "ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED"
  - "EncounterTimelineEventInfo"
verified_at: "2026-08-24"
---

# World of Warcraft EncounterTimeline API：中文参考

> 本文整理 WoW 12.0+ 的 `EncounterTimeline` 系统，面向需要阅读、封装或审查 Boss 技能时间轴的插件开发者。
>
> **版本边界：** Warcraft Wiki 的分类页是入口索引；本文以该索引、单项 API 页面和当前 `wow-ui-source` 的 `live` 分支交叉核对。分类页本身可能落后于单项 API 页面，因此文末单独列出补充接口。

## 1. 资料来源

- [Warcraft Wiki：System: EncounterTimeline](https://warcraft.wiki.gg/wiki/Category:API_systems/EncounterTimeline)
- [Warcraft Wiki：`C_EncounterTimeline.GetEventInfo`](https://warcraft.wiki.gg/wiki/API_C_EncounterTimeline.GetEventInfo)
- [Warcraft Wiki：`C_EncounterTimeline.GetTrackInfo`](https://warcraft.wiki.gg/wiki/API_C_EncounterTimeline.GetTrackInfo)
- [Warcraft Wiki：`C_EncounterTimeline.GetSortedEventList`](https://warcraft.wiki.gg/wiki/API_C_EncounterTimeline.GetSortedEventList)
- [Gethe/wow-ui-source：EncounterTimelineDocumentation.lua](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/EncounterTimelineDocumentation.lua)
- [Gethe/wow-ui-source：EncounterTimelineConstants.lua](https://github.com/Gethe/wow-ui-source/blob/live/Interface/AddOns/Blizzard_EncounterTimeline/EncounterTimelineConstants.lua)

## 2. 系统定位

命名空间：

```lua
C_EncounterTimeline
```

它是 Blizzard 在 12.0 引入的 Boss/Encounter 时间轴系统。系统负责维护一组即将发生、正在发生或已经结束的时间轴事件，并提供：

- 事件列表和剩余时间；
- 事件状态、轨道和排序位置；
- 技能名称、Spell ID、图标、严重度和职责/效果图标；
- 阶段条件不满足时的 `blocked` 状态；
- 时间轴 UI、计时条和 Edit Mode 事件；
- 供插件监听的时间轴事件通知。

## 3. 关键数据类型

### 3.1 `EncounterTimelineEventInfo`

由 `C_EncounterTimeline.GetEventInfo(eventID)` 返回。

| 字段 | 类型 | 说明 | Secret 注意 |
|---|---|---|---|
| `id` | `EncounterTimelineEventID` | 时间轴事件实例 ID | `NeverSecret` |
| `source` | `Enum.EncounterTimelineEventSource` | 事件来源 | `NeverSecret` |
| `spellName` | `string` | 技能名称；脚本事件可能使用 `overrideName` | 可能是 Secret |
| `spellID` | `number` | 技能 Spell ID | 可能是 Secret |
| `iconFileID` | `fileID` | 图标文件 ID | 可能是 Secret |
| `duration` | `DurationSeconds` | 事件进入时间轴时的基础时长 | `NeverSecret` |
| `maxQueueDuration` | `DurationSeconds` | 事件进入 queued 轨道后的保留时长 | `NeverSecret` |
| `icons` | `Enum.EncounterEventIconmask` | 职责和效果图标位掩码 | 可能是 Secret |
| `severity` | `Enum.EncounterEventSeverity` | 事件严重度 | 可能是 Secret |
| `isApproximate` | `boolean` | 是否为近似时间；为 `true` 时不保证精准施放 | 可能是 Secret |

`GetEventInfo` 在 Boss encounter 中受 `SecretWhenEncounterEvent` 保护。不能因为某个字段的文档类型是普通 `number` 或 `string`，就把它当作可自由读取的 Lua 值。

### 3.2 `Enum.EncounterTimelineEventSource`

| 值 | 名称 | 说明 |
|---:|---|---|
| `0` | `Encounter` | 副本首领 encounter 自动加入的事件 |
| `1` | `Script` | Lua 通过 `AddScriptEvent` 加入的事件 |
| `2` | `EditMode` | Edit Mode 预览事件 |

`PauseScriptEvent`、`CancelScriptEvent` 和 `ResumeScriptEvent` 只适用于 `Script` 来源的事件。

### 3.3 `Enum.EncounterEventIconmask`

当前单项 API 文档列出的位掩码如下：

| 位 | 名称 | 用途 |
|---:|---|---|
| `0x001` | `DeadlyEffect` | 致命/重要效果 |
| `0x002` | `EnrageEffect` | 狂暴效果 |
| `0x004` | `BleedEffect` | 流血效果 |
| `0x008` | `MagicEffect` | 魔法效果 |
| `0x010` | `DiseaseEffect` | 疾病效果 |
| `0x020` | `CurseEffect` | 诅咒效果 |
| `0x040` | `PoisonEffect` | 中毒效果 |
| `0x080` | `TankRole` | 坦克相关 |
| `0x100` | `HealerRole` | 治疗相关 |
| `0x200` | `DpsRole` | DPS 相关 |

当前公开的 `EncounterEventIconmask` 没有独立的 `Interrupt` 位。打断技能不能仅凭一个通用的 Timeline 位掩码筛选，通常需要使用首领数据标注、施法条信息或独立的 Spell ID 数据库。

### 3.4 `Enum.EncounterEventSeverity`

| 值 | 名称 | 建议理解 |
|---:|---|---|
| `0` | `Low` | 低优先级提示 |
| `1` | `Medium` | 一般处理提示 |
| `2` | `High` | 高优先级/重要处理提示 |

### 3.5 `Enum.EncounterTimelineEventState`

| 值 | 名称 | 说明 |
|---:|---|---|
| `0` | `Active` | 事件处于活动状态 |
| `1` | `Paused` | 事件暂停 |
| `2` | `Finished` | 事件完成 |
| `3` | `Canceled` | 事件取消 |

### 3.6 时间轴轨道

`Enum.EncounterTimelineTrack`：

| 值 | 名称 | 说明 |
|---:|---|---|
| `0` | `Queued` | 已到达时间轴末端、等待完成或超时移除 |
| `1` | `Short` | 短时间范围；最小时长为 0 |
| `2` | `Medium` | 中等时间范围 |
| `3` | `Long` | 长时间范围 |
| `4` | `Indeterminate` | 尚未计算位置，不应显示为正常时间轴事件 |

`Enum.EncounterTimelineTrackType`：

| 值 | 名称 |
|---:|---|
| `0` | `Hidden` |
| `1` | `Sorted` |
| `2` | `Linear` |

## 4. 函数参考

### 4.1 创建、取消和控制脚本事件

#### `C_EncounterTimeline.AddEditModeEvents()`

向时间轴加入一组由系统预定义的 Edit Mode 预览事件。

```lua
local loopTimerDuration = C_EncounterTimeline.AddEditModeEvents()
```

返回 Edit Mode 预览循环的时长。

#### `C_EncounterTimeline.CancelEditModeEvents()`

移除 Edit Mode 预览事件。

#### `C_EncounterTimeline.AddScriptEvent(eventInfo)`

加入一个自定义脚本事件。

```lua
local eventID = C_EncounterTimeline.AddScriptEvent({
    spellID = spellID,
    iconFileID = iconFileID,
    duration = 5,
    maxQueueDuration = 0,
    overrideName = "自定义事件",
    severity = Enum.EncounterEventSeverity.Medium,
    paused = false,
})
```

`eventInfo` 类型为 `EncounterTimelineScriptEventRequest`，主要字段：

| 字段 | 必需 | 说明 |
|---|---:|---|
| `spellID` | 是 | 关联的技能 ID |
| `iconFileID` | 是 | 图标文件 ID |
| `duration` | 是 | 事件时长 |
| `maxQueueDuration` | 否 | queued 阶段保留时长，默认 0 |
| `overrideName` | 否 | 非空时作为显示名称 |
| `icons` | 否 | 效果/职责图标掩码 |
| `severity` | 否 | 默认 `Medium` |
| `paused` | 否 | 默认 `false` |

#### `CancelScriptEvent(eventID)`

取消一个脚本事件并从时间轴移除。只接受 `Script` 来源事件。

#### `FinishScriptEvent(eventID)`

将脚本事件标记为完成并移除。

#### `PauseScriptEvent(eventID)`

暂停脚本事件，使其从可见时间轴中隐藏；之后可通过 `ResumeScriptEvent` 恢复。

#### `ResumeScriptEvent(eventID)`

恢复一个已暂停的脚本事件。

#### `CancelAllScriptEvents()`

取消所有自定义脚本事件。

### 4.2 当前时间和事件查询

#### `GetCurrentTime()`

返回用于渲染时间轴的当前时间戳。

#### `GetEventList()`

返回当前时间轴中所有事件 ID，但不保证排序。

```lua
local eventIDs = C_EncounterTimeline.GetEventList()
```

#### `GetSortedEventList(maxEventCount, maxEventDuration, excludeTerminalStates, excludeHiddenEvents)`

按剩余时长从短到长返回事件 ID。

| 参数 | 说明 |
|---|---|
| `maxEventCount` | 最多返回多少个事件 |
| `maxEventDuration` | 只返回不超过该时长的事件 |
| `excludeTerminalStates` | 是否排除 `Finished`/`Canceled`，默认 `true` |
| `excludeHiddenEvents` | 是否排除用户设置隐藏的事件，默认 `true` |

```lua
local upcoming = C_EncounterTimeline.GetSortedEventList(8, 60, true, true)
```

#### `GetEventInfo(eventID)`

返回事件静态资料，见 [3.1](#31-encountertimelineeventinfo)。

#### `GetEventState(eventID)`

返回 `Active`、`Paused`、`Finished` 或 `Canceled`。

#### `GetEventTimeElapsed(eventID)`

返回事件已经经过的时长。

#### `GetEventTimeRemaining(eventID)`

返回事件剩余时长。

#### `GetEventTimer(eventID)`

返回一个 `LuaDurationObject`，自动根据事件状态暂停或继续推进。适合直接绑定给支持 Duration 对象的原生 UI 控件。

#### `GetEventTrack(eventID)`

返回事件所在轨道和排序索引：

```lua
local track, trackSortIndex = C_EncounterTimeline.GetEventTrack(eventID)
```

#### `GetEventColor(eventID, overrideTrigger)`

返回当前用于渲染该事件的颜色。`overrideTrigger` 可用于指定颜色覆盖场景，例如 Timeline、Timeline 高亮或文字警告。

#### `GetEventHighlightTime()`

返回时间轴事件进入高亮状态的剩余时间阈值。

#### `GetEventCountBySource(source)`

按照 `Encounter`、`Script` 或 `EditMode` 来源统计当前事件数量。

### 4.3 轨道查询

#### `GetTrackList()`

返回所有时间轴轨道 ID。

#### `GetTrackInfo(track)`

返回一个 `EncounterTimelineTrackInfo`：

| 字段 | 说明 |
|---|---|
| `id` | 轨道 ID |
| `type` | `Hidden`、`Sorted` 或 `Linear` |
| `minimumDuration` | 轨道的最小时长边界 |
| `maximumDuration` | 轨道的最大时长边界 |
| `minimumEventIntroDuration` | 事件从 Indeterminate 进入本轨道所需的最小时长 |
| `minimumEventGapDuration` | 两个候选事件之间所需的最小时间间隔 |
| `maximumEventCount` | Sorted 轨道允许的最大事件数 |
| `sortDirection` | Sorted 轨道的排序方向 |

#### `GetTrackType(track)`

返回指定轨道的 `Hidden`、`Sorted` 或 `Linear` 类型。

#### `GetTrackMaxEventDuration(track)`

返回该轨道允许的最大事件时长。该函数是在 12.0.1 添加的。

#### `GetViewType()`

返回当前时间轴视图类型，例如线性 Timeline 或计时条 Bars。

#### `SetViewType(viewType)`

切换时间轴视图类型，并让轨道布局适应新的显示模式。

### 4.4 状态和功能可用性

#### `HasActiveEvents()`

是否存在活动事件。

#### `HasAnyEvents()`

是否存在任意时间轴事件。

#### `HasPausedEvents()`

是否存在暂停事件。

#### `HasVisibleEvents()`

是否存在位于可见轨道上的事件。

#### `IsEventBlocked(eventID)`

返回事件是否处于 blocked 状态。blocked 表示该事件因为 encounter 条件未满足，可能不会实际施放。

#### `IsFeatureAvailable()`

返回当前客户端是否提供 EncounterTimeline 功能。

#### `IsFeatureEnabled()`

返回该功能当前是否启用。

### 4.5 图标显示

#### `SetEventIconTextures(eventID, includeIcons, textures)`

让原生系统为给定纹理对象设置事件支持图标。该函数会把安全数据应用到 UI 对象，适合显示坦克、治疗、DPS、致命、驱散和狂暴等指示。

不要先把事件图标位掩码读取出来，再用 Lua 自己做战斗逻辑；应尽量把数据直接交给受支持的 UI API。

## 5. 当前 API 页面中出现的补充接口

分类页当前列出了 34 个函数，但较新的单项 API 页面还包含以下接口。使用前应以本地客户端对应版本的 API 文档再次核对。

### `GetEventPosition(eventID)`

返回事件的综合位置资料：

| 字段 | 说明 |
|---|---|
| `state` | 事件状态 |
| `track` | 所在轨道 |
| `section` | 轨道分段 |
| `order` | 可选排序位置 |
| `timeRemaining` | 剩余时长 |

当事件状态、轨道或位置变化事件触发时，优先使用该函数一次性读取位置相关信息，而不是分别调用多个查询函数。

### `GetTrackSectionInfo(section)`

返回一个轨道分段的资料。分段包括：

`Finishing`、`Imminent`、`Short`、`Medium`、`Long`、`Indeterminate`。

返回字段包括所属轨道、轨道类型、最小时长和最大时长。

## 6. 时间轴事件

分类页列出 11 个事件：

| 事件 | 参数/载荷 | 用途 |
|---|---|---|
| `ENCOUNTER_TIMELINE_EVENT_ADDED` | `eventInfo` | 新事件加入 |
| `ENCOUNTER_TIMELINE_EVENT_BLOCK_STATE_CHANGED` | `eventID` | blocked 状态变化 |
| `ENCOUNTER_TIMELINE_EVENT_COLOR_CHANGED` | `eventID` | 颜色变化 |
| `ENCOUNTER_TIMELINE_EVENT_HIGHLIGHT` | `eventID` | 进入高亮时机 |
| `ENCOUNTER_TIMELINE_EVENT_REMOVED` | `eventID` | 事件被移除 |
| `ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED` | `eventID` | Active/Paused/Finished/Canceled 变化 |
| `ENCOUNTER_TIMELINE_EVENT_TRACK_CHANGED` | `eventID` | 轨道或轨道内排序变化 |
| `ENCOUNTER_TIMELINE_LAYOUT_UPDATED` | 无 | 轨道布局更新 |
| `ENCOUNTER_TIMELINE_STATE_UPDATED` | 无 | 时间轴可见性条件更新 |
| `ENCOUNTER_TIMELINE_VIEW_ACTIVATED` | `viewType` | 新视图启用 |
| `ENCOUNTER_TIMELINE_VIEW_DEACTIVATED` | `viewType` | 当前视图停用 |

事件驱动的典型结构：

```lua
local frame = CreateFrame("Frame")

frame:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_ADDED")
frame:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED")
frame:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_TRACK_CHANGED")
frame:RegisterEvent("ENCOUNTER_TIMELINE_EVENT_REMOVED")

frame:SetScript("OnEvent", function(_, event, ...)
    if event == "ENCOUNTER_TIMELINE_EVENT_ADDED" then
        local eventInfo = ...
        -- 仅做受支持的 UI 更新；不要对可能为 Secret 的字段做 Lua 比较。
    else
        local eventID = ...
        -- 重新查询并刷新对应 UI 对象。
    end
end)
```

### 事件顺序注意事项

- `ENCOUNTER_TIMELINE_EVENT_ADDED` 的载荷受 `SecretWhenEncounterEvent` 保护。
- `ENCOUNTER_TIMELINE_EVENT_REMOVED` 发生时，事件数据已经被移除；不要在移除事件后继续调用该事件 ID 查询资料。
- `ENCOUNTER_TIMELINE_EVENT_STATE_CHANGED`、`TRACK_CHANGED` 和 `BLOCK_STATE_CHANGED` 只提供事件 ID，需要重新查询当前状态。
- `ENCOUNTER_TIMELINE_VIEW_DEACTIVATED` 时应清理插件保存的事件帧和缓存引用。

## 7. 职责筛选与技能类型

原生 Timeline 使用事件的 `icons` 位掩码提供职责/效果标记。当前可表达：

- 坦克：`TankRole`
- 治疗：`HealerRole`
- DPS：`DpsRole`
- 致命：`DeadlyEffect`
- 驱散类型：魔法、疾病、诅咒、中毒
- 狂暴：`EnrageEffect`
- 流血：`BleedEffect`

此外，客户端提供 `encounterTimelineHideForOtherRoles` 设置，可以隐藏与玩家当前职责无关的事件；`encounterTimelineIconographyHiddenMask` 可以隐藏某些支持图标，但这通常只影响图标显示，不等于删除时间轴事件。

**打断技能：** 当前 `EncounterEventIconmask` 没有独立 `Interrupt` 位。不要把 `DpsRole` 或 `High` severity 误解为“可打断”。如果应用需要打断筛选，应建立单独的技能分类层，并考虑 Secret Values 限制。

## 8. Secret Values 使用边界

### 8.1 不能做的事情

在 encounter、战斗或其他受限制路径中，不要对可能是 Secret 的值进行：

```lua
-- 不要这样做
if info.spellID == 123456 then end
if info.duration < 5 then end
local seconds = info.duration - GetTime()
table.sort(events, function(a, b) return a.spellID < b.spellID end)
```

这些操作可能触发 `attempt to compare ... secret value`、`attempt to perform arithmetic on a secret value` 或 taint 错误。

### 8.2 推荐做法

1. 优先让 Blizzard 原生 Timeline/UI 消费时间轴数据。
2. 使用 `GetSortedEventList` 获得事件 ID 顺序，而不是自己按剩余时间排序。
3. 使用 `GetEventTimer`、原生计时条或支持 Secret 的 UI 控件显示时间。
4. 使用 `SetEventIconTextures` 显示职责和效果图标。
5. 对 `isApproximate` 为真的事件保留“估算”语义，不要当作固定秒表。
6. 用 `IsEventBlocked` 处理“当前可能不会发生”的技能。
7. 只在明确标记为 `NeverSecret` 的字段上执行普通 Lua 逻辑；并以当前版本 API 文档为准。

### 8.3 `SecretArguments` 和 `SecretWhenEncounterEvent`

API 文档中的常见标记：

- `SecretWhenEncounterEvent`：在 encounter 事件上下文中，返回值或事件载荷可能包含 Secret。
- `SecretArguments = "NotAllowed"`：函数不接受 Secret 参数。
- `SecretArguments = "AllowedWhenUntainted"`：只有未污染调用路径可以传入 Secret。
- `NeverSecret`：字段在该 API 语义下不会被标记为 Secret。

`GetEventInfo`、`GetEventColor` 和 `ENCOUNTER_TIMELINE_EVENT_ADDED` 是阅读时间轴插件时必须优先检查的 Secret 边界。

## 9. 一个安全的实现模型

推荐将插件分成三层：

```text
原生 EncounterTimeline
        │
        ├─ 事件 ID、状态变化、轨道变化
        ├─ 原生剩余时间/Duration 对象
        └─ 原生职责与效果图标
                │
                ▼
        显示层：计时条、图标、颜色、音效
                │
                ▼
        非战斗配置层：布局、隐藏类别、颜色和尺寸
```

应避免构建一条“读取所有技能数据 → 在 Lua 中自行推理 → 自动做战斗决策”的路径；这正是 12.0 Secret Values 重点限制的模式。

## 10. 相关 CVar

| CVar | 作用 |
|---|---|
| `combatWarningsEnabled` | 开关战斗警告系统 |
| `encounterTimelineEnabled` | 开关 Encounter Timeline |
| `encounterTimelineHideForOtherRoles` | 隐藏其他职责相关事件 |
| `encounterTimelineHideLongCountdowns` | 隐藏长倒计时 |
| `encounterTimelineHideQueuedCountdowns` | 隐藏 queued 事件 |
| `encounterTimelineIconographyEnabled` | 开关职责/效果支持图标 |
| `encounterTimelineIconographyHiddenMask` | 隐藏指定支持图标 |
| `encounterTimelineHighlightDuration` | 设置进入高亮的提前时间，单位毫秒 |

## 11. 版本核对清单

升级 WoW 客户端或维护时间轴模块时，至少核对：

- `EncounterTimelineDocumentation.lua` 是否增加或删除函数；
- `EncounterTimelineEventInfo` 哪些字段标记为 `NeverSecret`；
- `EncounterEventIconmask` 是否增加新的职责/效果位；
- `EncounterTimelineEventState` 是否增加状态；
- `GetEventInfo`、`GetEventPosition` 的返回字段是否变化；
- `ENCOUNTER_TIMELINE_EVENT_ADDED` 是否仍为同步且受 Secret 保护；
- CVar 名称和默认值是否变化；
- `isApproximate` 和 `blocked` 的语义是否变化；
- API 变更是否来自正式版本，而不是 PTR。

## 12. 摘要

- `C_EncounterTimeline` 是 12.0+ 的原生 Boss 技能时间轴 API。
- `GetSortedEventList` 适合取得即将发生的事件顺序。
- `GetEventTimer` 适合把时间交给原生 UI 进行可视化。
- `icons` 可表达坦克、治疗、DPS、致命、驱散、狂暴等分类。
- 当前公开位掩码没有独立的打断分类。
- Boss encounter 中的事件资料可能是 Secret，不能在 Lua 中随意比较、计算或排序。
- 分类页是入口索引；实现时必须同时核对当前客户端的 API 文档和 live UI 源码。
