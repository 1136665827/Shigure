---
title: Shigure 规则、条件与特殊动作
summary: 解释 LogicRegistry 的决策优先级、模块规则短路、条件表达式子集，以及暂停、自动插入法术和一键辅助动作。
aliases:
  - ModuleLogic
  - Shigure 条件语法
tags:
  - project/shigure
  - doc/feature
  - area/logic
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[30-Shigure/05-Shigure-模块存储匹配与版本迁移]]"
  - "[[30-Shigure/07-Shigure-动态单位数量与公式]]"
source_files:
  - Modules/LogicRegistry.cs
  - Modules/ModuleStore.cs
  - Modules/ModuleSpecialActions.cs
  - Runtime/GameState.cs
  - UI/ConditionEditorForm.cs
source_symbols:
  - LogicRegistry.Evaluate
  - ModuleLogic.Run
  - ModuleConditionEvaluator.TryEvaluate
  - ModuleConditionEvaluator.TryEvaluateRule
  - ModuleSpecialActions.GetFailedSpell
  - ModuleSpecialActions.GetOneKeySpell
verified_at: 2026-08-09
---

# Shigure 规则、条件与特殊动作

> [!abstract] AI 快速摘要
> `LogicRegistry` 先选择匹配模块；有模块时模块逻辑总是优先。规则按 JSON 顺序求值，主条件与“任一子条件”组合，第一条匹配规则决定本 tick，通常不会因按键缺失再尝试后续规则。条件语言只有 OR/AND、比较、`in` 和真假简写，没有括号或通用脚本能力。特殊动作包括暂停、自动插入法术和一键辅助。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 上游模块：[[30-Shigure/05-Shigure-模块存储匹配与版本迁移]]
- 动态字段：[[30-Shigure/07-Shigure-动态单位数量与公式]]
- 下游按键：[[30-Shigure/08-Shigure-Keymap解析与按键发送]]

## 范围与非范围

本页描述“选哪条规则/哪个动作”。规则冷却和全局逻辑暂停实际由运行时执行，见 [[30-Shigure/04-Shigure-运行循环触发模式与快照]]；动态单位算法和公式解析不在此重复。

## 决策层级

1. `LogicRegistry` 用职业、专精、队伍类型和英雄天赋选择模块。
2. 如果存在匹配模块，调用 `ModuleLogic`；模块逻辑优先于任何职业专用逻辑。
3. 没有模块时才查注册的 `IClassLogic`。当前工厂没有注册任何职业逻辑，因此通常落到默认逻辑。
4. 默认逻辑仅在状态 `一键辅助 == 10` 且 Keymap 中存在 unit 0 / spell `一键辅助` 时返回按键，否则返回无键消息。
5. `runLogic=false` 时不产生动作，但模块匹配和动态值仍计算，供 UI 快照显示。

## 规则执行和短路

每条规则按顺序处理：

1. 跳过 `Enabled=false`。
2. 计算主条件，再计算子条件。
3. 条件不匹配则继续下一条；条件求值出错则**立即返回错误决策**，阻止后续规则。
4. `暂停` 返回无 Hotkey 决策并停止。
5. 动态目标解析失败时会跳过该规则继续下一条。
6. 自动插入/一键辅助未解析出动作时会跳过该规则继续下一条。
7. 普通规则若写了直接 `Hotkey`，它优先于 Keymap；否则用 unit、spell、macroCondition 查 Keymap。
8. 第一条匹配的普通规则会返回决策，即使最终 Hotkey 为空，因此后续规则通常不再求值。

运行时随后实施 `DelayMs` 与 `LogicDelayMs`。第一条决策因规则冷却被抑制时同样不会回退下一规则。

## 主条件和子条件

规则整体关系是：

```text
MainCondition AND (没有 SubConditions OR 任一 SubCondition 为真)
```

子条件是 OR 组；任一子条件解析错误会使整条规则失败。空主条件视为 true。

## 条件表达式语法

### 组合和优先级

- 先按 OR 拆分，再在每段按 AND 拆分，因此 AND 优先于 OR。
- 不支持括号、嵌套 AST、函数或算术表达式。
- UI 编辑器会把布尔简写规范化为显式比较，也没有嵌套括号能力。

### 操作符

- 比较：`==`, `!=`, `>`, `>=`, `<`, `<=`。
- 集合：`in`, `not in`；列表按逗号分割，只做基本引号感知，不支持嵌套列表或完整转义语法。
- 真值简写：`field` 与 `!field`。
- 数值比较仅在两侧都能转成数字时执行；字符串相等不区分大小写；无法解析的关系比较为 false。

### 值和路径

支持单/双引号字符串、十进制数字、中英文布尔、`null`/`nil`/`空`。左值可来自：

- 基础状态和 `state.*`。
- `spell.*` / `spells.*`、`aura.*` / `auras.*`。
- `group.N.field`。
- 动态单位名和 `动态单位名.field`。
- 数量统计名、动态单位生命名。
- `$dynamicvalues.<name>`。
- 特殊值 `自动插入法术`。

注意：已配置但扫描缺失的数字在 `StateBuilder` 中已变成 0；它不是“无法解析”。因此 `< 50` 可能命中，详见状态构建页。

## 特殊动作

| 动作 | 解析规则 | 失败行为 |
|---|---|---|
| `暂停` | 直接生成无键决策 | 停止继续规则，本 tick 不发送 |
| `自动插入法术` | 读状态 `插入法术`，用 `一键法术` 映射为法术名；要求该法术存在于 `GameState.Spells` 且冷却为 0 | 未解析则继续下一规则 |
| `一键辅助` | 读状态 `一键辅助`，用同一 `一键法术` 映射；不检查冷却 | 未解析则继续下一规则 |

一键辅助强制使用 unit 0，并在精确宏条件未命中时尝试 `nochanneling`。两个特殊动作共用配置的 `一键法术` 值映射，但冷却语义不同。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 后面的规则永远不执行 | 前面是否有条件错误、暂停、或匹配但无 Hotkey 的普通规则 |
| 条件看似正确却不匹配 | 不支持括号；确认 AND/OR 拆分、路径名和数值类型 |
| 缺失数据触发低生命规则 | 已配置缺失值可能已归一化为 0 |
| 自动插入法术未动作 | `插入法术` 映射、法术是否存在、冷却是否为 0、Keymap 是否有项 |
| 一键辅助找不到键 | unit 0 / spell 名 / macroCondition 精确项及 `nochanneling` 回退 |
| 手工 Hotkey 保存后消失 | 模块 UI 保存会清空规则 `Hotkey` 和 `Step`，不要用 UI round-trip 该手工字段 |

## 修改影响

- 扩展条件语法必须同时更新求值器、条件编辑器、错误信息和文档；引入括号意味着不能再用简单字符串拆分。
- 改规则“第一匹配即返回”会改变所有模块优先级，需给出清晰迁移语义。
- 新增特殊动作要同时定义状态来源、Keymap 查找、失败是否继续、是否检查冷却以及 Click 模式的消费行为。
- 若要允许模块 JSON 执行任意表达式，会扩大安全边界；当前实现只是受限数据解释器。

## 源码索引

- `Modules/LogicRegistry.cs:29-94`：模块优先、禁用求值和默认逻辑。
- `Modules/ModuleStore.cs:697-810`：规则顺序、错误短路、目标/Hotkey/Keymap 与特殊动作。
- `Modules/ModuleStore.cs:1165-1260`：AND/OR 拆分和主/子条件组合。
- `Modules/ModuleStore.cs:1277-1468`：操作符、路径、字面量和比较。
- `Modules/ModuleStore.cs:1471-1540`：`in` 列表解析。
- `Modules/ModuleSpecialActions.cs:36-55`：自动插入与一键辅助解析。
- `UI/ConditionEditorForm.cs:13-133`：UI 条件表示和规范化。

## 知识图谱链接

- 上游状态：[[30-Shigure/03-Shigure-配置合并与GameState构建]]
- 动态求值：[[30-Shigure/07-Shigure-动态单位数量与公式]]
- 运行节流：[[30-Shigure/04-Shigure-运行循环触发模式与快照]]
- 按键契约：[[40-跨项目/03-Shigure-ClassMacros到keymap与按键契约]]
