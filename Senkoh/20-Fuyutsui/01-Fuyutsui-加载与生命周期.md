---
title: Fuyutsui 加载与生命周期
summary: 说明 toc 加载顺序、数据库初始化、事件框架启用、当前职业文件生效方式与专精重建路径。
aliases:
  - Fuyutsui 生命周期
  - Fuyutsui 加载顺序
tags:
  - project/fuyutsui
  - doc/feature
  - area/lifecycle
project: Fuyutsui
doc_type: feature
status: current
authority: source-derived
up:
  - "[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]"
related:
  - "[[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]"
  - "[[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]"
source_files:
  - Fuyutsui/Fuyutsui.toc
  - Fuyutsui/core/core.lua
  - Fuyutsui/core/player.lua
  - Fuyutsui/main.lua
source_symbols:
  - Fuyutsui:OnInitialize
  - Fuyutsui:OnEnable
  - Fuyutsui:GetCharacterSpecInfo
  - Fuyutsui:UpdatePlayerSpecInfo
  - Fuyutsui:LoadPlayerBlocks
  - Fuyutsui:LoadPlayerMacros
verified_at: 2026-08-09
---

# Fuyutsui 加载与生命周期

上级：[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]

相关：[[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]] · [[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]

> [!summary] AI 快速摘要
> `Fuyutsui.toc` 是加载顺序的唯一事实来源。`core/core.lua` 先创建全局对象和事件框架；静态配置、像素输出、命令、宏和键位模块随后加载；13 个职业文件全部执行但只有当前职业越过首行卫语句；领域模块最后加载，`main.lua` 只提供布局和宏编排。当前 toc 不含、磁盘也不存在 `auracontainer.lua`。

## 范围

本页覆盖：文件加载、`ADDON_LOADED`/`PLAYER_LOGIN`、SavedVariables 初始化、启用顺序、职业/专精装载与重建。

不覆盖：事件的业务语义（见 [[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]）和像素索引规则（见 [[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]）。

## 精确加载顺序

`Fuyutsui/Fuyutsui.toc:26-59` 当前顺序：

1. `embeds.xml`、`LibRangeCheck-3.0`。
2. `core/core.lua`。
3. `core/config.lua` → `curves.lua` → `block.lua` → `stateblocks.lua` → `commands.lua` → `quickbutton.lua` → `macro.lua` → `classmacros.lua` → `keybinds.lua`。
4. `class/Warrior.lua` → … → `class/Evoker.lua`。
5. `core/player.lua` → `spells.lua` → `target.lua` → `group.lua` → `events.lua`。
6. `main.lua`。

每个 `class/*.lua` 首行均以 `UnitClassBase("player")` 过滤非当前职业；因此最终只有一个文件写入 `Fuyutsui.ClassBlocks` 和 `Fuyutsui.spellsList`。职业表必须在 `core/player.lua` 之前加载，因为专精初始化会立即消费它。

### 不存在的旁路文件

当前 toc 在 `core/keybinds.lua` 后直接加载职业文件，仓库也没有 `Fuyutsui/auracontainer.lua`。旧资料中“加载 `auracontainer.lua` 中央 Buff 演示 UI”的描述不能用于当前运行时。真实光环接线见 [[docs/20-Fuyutsui/08-Fuyutsui-光环容器本地集成]]。

## 启动状态机

### `ADDON_LOADED`

`core/core.lua:188-205` 注册并处理 `ADDON_LOADED`：

- 只接受当前 addon 名称。
- 只调用一次 `OnInitialize()`。
- 如果玩家已登录，紧接着只调用一次 `OnEnable()`。

### `PLAYER_LOGIN`

若加载阶段尚未启用，`core/core.lua:208-214` 在 `PLAYER_LOGIN` 调用 `OnEnable()`。`initialized` 和 `enabled` 是 `core.lua` 的局部布尔锁，防止两条入口重复执行。

### 统一事件分发

初始化完成后，事件框架按 `Fuyutsui[event](Fuyutsui, event, ...)` 调用同名方法（`core/core.lua:217-220`）。因此事件处理函数的第一个显式形参通常是事件名占位符 `_`。

## `OnInitialize()`

输入：`FuyutsuiADB` SavedVariables、玩家名和服务器名。

执行链（`core/core.lua:44-84`）：

1. 建立兼容旧 AceDB 布局的 `char`、`profiles`、`profileKeys`。
2. 用 `CopyDefaults()` 只填缺失字段，不覆盖已有角色配置。
3. 暴露 `Fuyutsui.db.char` 与 `Fuyutsui.db.profile`。
4. 注册 `/fu`、`/fuyutsui`。
5. 读取角色名、GUID、职业色和等级。

输出：持久化配置引用、斜杠命令入口和基础角色身份。

## `OnEnable()`

`core/core.lua:86-155` 的顺序具有依赖意义：

1. `GetCharacterSpecInfo()`：确定专精并加载当前 `ClassBlocks`、法术知识、队伍、宏。
2. 再次刷新法术知识。
3. `UpdatePlayerBlocks()`：写入初始玩家、目标、焦点、队伍、物品和配置像素。
4. 延迟扫描动作条键位。
5. 挂接聊天输入框焦点。
6. 注册领域事件。
7. 启动 `OnUpdate` 转发帧。
8. 创建快速切换按钮。

`UpdatePlayerBlocks()` 内部把 `isInitialized` 暂时设为 `false`，一秒后刷新配置像素并恢复为 `true`（`main.lua:3-36`）。当前没有其他代码以该标志作为总开关，所以它主要是可观察状态，不是严格初始化屏障。

## 专精与天赋重建

### 首次装载

`GetCharacterSpecInfo()`（`core/player.lua:19-39`）按以下顺序执行：

`specIndex/specID/role/range` → `LoadPlayerBlocks(specIndex)` → `UpdateSpellKnown()` → 坐骑状态 → `UpdateGroup()` → `LoadPlayerMacros()` → 物品数量 → 职业/专精像素。

这里的 `specIndex` 是职业内部从 1 开始的专精序号，不是全局 `specID`。

### 重建

`UpdatePlayerSpecInfo()`（`core/player.lua:41-55`）清空 510 个主色块，重新解析专精表、法术、初始状态和宏。`PLAYER_TALENT_UPDATE` 会调用它（`core/events.lua:36-39`）。两个自定义切换法术 `384255`/`200749` 成功时会先 `ClearAllFuyutsuiBars()`，一秒后再重建（`core/events.lua:171-187`）。

## 核心不变量

- 所有依赖 `_G.Fuyutsui` 的模块必须位于 `core/core.lua` 之后。
- `core/config.lua` 必须早于 `curves.lua`、`keybinds.lua` 和职业领域模块。
- `class/*.lua` 必须早于 `GetCharacterSpecInfo()` 的定义与首次调用。
- 新增运行时 Lua/XML 文件必须显式加入 toc；仅把文件放进目录不会加载。
- `main.lua` 是解析/编排层，不是事件总入口。
- `FuyutsuiADB` 是角色名与服务器名拼接键下的角色配置；改键格式会影响旧存档读取。

## 失败模式与风险

1. **普通天赋事件未清横向条。** `UpdatePlayerSpecInfo()` 只清主色块；若不是通过两个自定义切换法术触发，旧 `spellIdToBar`、`nextAvailableIndex` 和旧计数条可能继续存在或使新条向后追加。修改生命周期时须与 [[docs/20-Fuyutsui/06-Fuyutsui-法术与物品冷却]]、[[docs/20-Fuyutsui/08-Fuyutsui-光环容器本地集成]] 一起验证。
2. **没有注册 `PLAYER_SPECIALIZATION_CHANGED`。** 当前依赖 `PLAYER_TALENT_UPDATE` 和两个法术成功事件覆盖专精变化；新切换路径需要游戏内验证。
3. **toc 元数据含全角逗号。** `Fuyutsui.toc:1` 的 Interface 列表在 `120007` 后使用 `，`，可能影响严格解析器。
4. **库目录大小写。** toc/XML 使用 `Libs`，仓库目录显示为 `libs`；Windows 通常不敏感，跨文件系统打包时应检查。
5. **异步初始化不是事务。** 英雄天赋、法术知识、配置像素分别用 1 秒定时器刷新；短时间内读取端可能看到新专精与旧/空附属状态的混合快照。

## 修改影响

- 改 toc：检查所有前置表和方法是否在消费者之前存在，并更新本页及 [[docs/20-Fuyutsui/00-Fuyutsui-MOC]]。
- 改专精重建：同时验证主色条、CountBars、AuraContainer、安全宏和 Shigure 配置重载。
- 改固定启动像素 1/2/3：这是跨项目破坏性变更，必须同步 [[docs/40-跨项目/01-Shingen-像素生产消费契约]] 和 [[docs/30-Shigure/03-Shigure-配置合并与GameState构建]]。
- 改 SavedVariables：提供旧 `FuyutsuiADB` 迁移或兼容读取。

## 源码索引

- `Fuyutsui/Fuyutsui.toc:26-59`：精确加载顺序。
- `Fuyutsui/core/core.lua:21-72`：默认值复制与 DB 初始化。
- `Fuyutsui/core/core.lua:74-155`：初始化、启用和事件注册。
- `Fuyutsui/core/core.lua:185-220`：生命周期锁与事件分发。
- `Fuyutsui/core/player.lua:19-55`：首次专精装载与重建。
- `Fuyutsui/main.lua:3-36`：初始状态编排。

## 知识图谱

上游是 [[docs/10-系统/00-Shingen-双项目系统全景]]；本页向下连接 [[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]、[[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]] 和 [[docs/20-Fuyutsui/10-Fuyutsui-命令快捷按钮与存档]]。跨项目影响汇总在 [[docs/40-跨项目/00-Shingen-跨项目契约-MOC]]。
