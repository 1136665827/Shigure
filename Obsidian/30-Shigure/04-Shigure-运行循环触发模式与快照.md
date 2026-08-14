---
title: Shigure 运行循环、触发模式与快照
summary: 描述 ShigureRuntime 的单循环所有权、Switch/Click/Hold 触发状态机、逻辑与渲染节流、延迟和 RenderSnapshot 发布。
aliases:
  - ShigureRuntime
  - Shigure 触发模式
tags:
  - project/shigure
  - doc/feature
  - area/runtime
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[30-Shigure/01-Shigure-启动随机副本与会话协调]]"
  - "[[30-Shigure/06-Shigure-规则条件与特殊动作]]"
source_files:
  - Runtime/ShigureRuntime.cs
  - Runtime/RuntimeDependencies.cs
  - Runtime/RenderSnapshot.cs
  - App/AppOptions.cs
source_symbols:
  - ShigureRuntime.RunAsync
  - ShigureRuntime.TickLogic
  - ShigureRuntime.HandleRisingEdge
  - LogicDecision
  - RenderSnapshot
verified_at: 2026-08-09
---

# Shigure 运行循环、触发模式与快照

> [!abstract] AI 快速摘要
> `ShigureRuntime` 用一个后台循环拥有启用状态、触发边沿、当前 `GameState`、规则速率时间和逻辑暂停时间。UI 只把命令放入 `ConcurrentQueue`。循环约每 25 ms 轮询全局触发键，按独立的逻辑间隔扫描/决策，按独立的渲染间隔发布不可变快照。成功发送后才记录规则冷却并启动 `LogicDelayMs`；规则因 `DelayMs` 被限速时不会继续尝试后续规则。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 上游会话：[[30-Shigure/01-Shigure-启动随机副本与会话协调]]
- 下游规则：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- 下游输出：[[30-Shigure/08-Shigure-Keymap解析与按键发送]]

## 范围与非范围

本页聚焦运行时状态机、调度和快照，不重复像素 RGB 细节或条件语法。生命周期中的跨会话串行化由 `RuntimeSessionCoordinator` 负责，不属于 `ShigureRuntime` 本体。

## 输入与输出

| 输入 | 来源 | 输出 |
|---|---|---|
| 启用/切换命令 | UI，经并发队列 | 循环内 `_enabled` 状态 |
| 全局触发键状态 | `ITriggerKeyState` | Switch/Click/Hold 状态转换 |
| 扫描结果 | `IPixelScanner` | 当前 `GameState` 或无效状态 |
| 逻辑决策 | `ILogicRegistry` | Hotkey、规则速率键、规则延迟、逻辑延迟 |
| 按键结果 | `IKeyOutput` | 发送日志、速率记录、逻辑暂停 |
| 当前状态 | 运行循环 | `RenderSnapshot` 事件给 UI |

## 单循环所有权

- `_enabled`、`_state`、`_lastSentTimes`、`_pauseUntil` 等可变运行状态只由循环修改。
- `SetEnabled` / `ToggleEnabled` 不直接改字段，而是入队命令；循环每轮先排空队列。
- 禁用会清空规则速率限制和逻辑暂停，防止下次启用继承旧冷却。
- 触发键在进入循环前解析一次；键名无效会让运行时直接结束，不会每轮重试。
- 基础循环约 25 ms，但昂贵逻辑仅在 `LogicIntervalMs` 到期时运行，快照仅在 `RenderIntervalMs` 到期时发布。

## 三种触发模式

| 模式 | 状态机语义 | 结束条件 |
|---|---|---|
| `Switch` | 检测按下的上升沿并翻转启用状态；120 ms 去抖 | 下一次有效上升沿再次翻转 |
| `Click` | 上升沿设置一次性 `clickPending` 并启用 | 得到一个非 null 决策后自动关闭，即使该决策没有 Hotkey |
| `Hold` | 启用状态持续镜像物理按键状态 | 松开即关闭 |

`Click` 的细节容易误判：扫描无效或逻辑返回 null 时，pending 会继续等待；一旦返回了决策对象，即使 Keymap 未解析到按键，也会消费这次点击并关闭。

## 逻辑 tick

1. 到逻辑间隔时调用扫描器；该步骤即使 `_enabled == false` 也会执行，以便 UI 显示实时状态。
2. `RowData == null` 时清除当前状态并发布等待/失败信息。
3. 构建 `GameState`，派生职业/专精/队伍类型/英雄天赋名称，并要求状态中的 `有效性` 为真。
4. 调用 `LogicRegistry.Evaluate(..., runLogic: _enabled)`。禁用时不执行规则动作，但仍解析匹配模块和动态字段供状态页显示。
5. 有决策时先检查 `DelayMs` 的每规则速率限制；默认速率键来自决策，模块规则使用 `moduleId:ruleIndex`。
6. 只有 `KeySender` 成功时才记录该速率键的发送时间，并设置 `LogicDelayMs` 的全局暂停。

## 两种“延迟”不能混淆

- `DelayMs` 是**规则级速率限制**。同一速率键尚在冷却时，本次决策被丢弃；运行时不会回到 `ModuleLogic` 尝试后续规则。
- `LogicDelayMs` 是**整个逻辑扫描暂停**。暂停期间触发键轮询、UI 命令和快照渲染仍继续，但不执行 `TickLogic`，所以扫描状态也保持旧值。
- 两者都只在实际按键发送成功后启动。无 Hotkey、发送失败或暂停动作不会建立这两个计时。

## RenderSnapshot

快照把后台对象转换为 UI 可安全读取的展示数据，包括启用/运行状态、错误/消息、职业专精、当前模块/规则、状态/法术/光环/组员，以及动态单位、动态生命、数量和 `$dynamicvalues`。快照按渲染间隔节流，不保证每次逻辑 tick 都对应一次 UI 更新。

会话退出的 `finally` 还会发布禁用/停止快照。协调器给事件加 session ID，UI 丢弃旧会话晚到的快照。

## 失败模式与排障

| 症状 | 解释或检查 |
|---|---|
| UI 有状态但不执行规则 | 禁用状态仍会扫描并解析动态字段；检查触发模式和 `_enabled` |
| 按一次 Click 没发送且立即关闭 | 规则返回了无 Hotkey 的决策，Click 仍被消费 |
| 第一条匹配规则冷却时没有尝试第二条 | `DelayMs` 在运行时发送阶段生效，不会回退重新求值 |
| 发送后状态像“冻结” | `LogicDelayMs` 暂停了包含扫描的整个逻辑 tick |
| 快速双击 Switch 只切换一次 | 120 ms 去抖是设计行为 |
| 禁用后仍有屏幕读取 | 禁用不停止扫描；它只阻止 `runLogic` 动作 |

## 修改影响

- 改基础循环或两个间隔会影响 CPU、扫描时延、触发响应和 UI 新鲜度，需分别度量，不能只看一个数。
- 若希望限速时回退后续规则，应把限速信息前移到规则求值层；仅在当前发送层循环无法知道下一条候选。
- 若希望 `LogicDelayMs` 只暂停动作而继续扫描，必须拆分扫描/状态刷新与规则执行计时，并明确快照语义。
- 新增触发模式要同步 `AppOptions`、MainForm 选项、运行时状态机和文档。

## 源码索引

- `Runtime/ShigureRuntime.cs:7-29`：队列、状态和计时字段。
- `Runtime/ShigureRuntime.cs:52-89`：命令入队与禁用清理。
- `Runtime/ShigureRuntime.cs:92-191`：25 ms 循环、间隔、触发模式与去抖。
- `Runtime/ShigureRuntime.cs:193-273`：扫描、状态、有效性、逻辑和 Click 消费。
- `Runtime/ShigureRuntime.cs:275-328`：规则速率、发送成功和逻辑暂停。
- `Runtime/ShigureRuntime.cs:330-417`：快照和动态字段物化。
- `Runtime/RuntimeDependencies.cs:3-54`：端口和 `LogicDecision`。

## 知识图谱链接

- 上游状态：[[30-Shigure/03-Shigure-配置合并与GameState构建]]
- 决策：[[30-Shigure/06-Shigure-规则条件与特殊动作]]
- 输出：[[30-Shigure/08-Shigure-Keymap解析与按键发送]]
- UI 消费者：[[30-Shigure/10-Shigure-UI功能地图与数据所有权]]
