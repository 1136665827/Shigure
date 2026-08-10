---
title: "Fuyutsui 优化建议"
summary: "保留 2026-07-25 Fuyutsui 静态审计的历史建议；行号和缺陷状态必须回到当前源码复核。"
language: "zh-CN"
target_client: "World of Warcraft 12.1 (Midnight)"
verified_build: "12.1.0.68914"
ptr_iteration: "PTR 7"
generated_from: "2026-07-25 当前工作区源码静态审计"
aura_constraint: "光环实现只允许使用 AuraContainer_AI_Reference_zh-CN.md 的方案"
tags:
  - "project/fuyutsui"
  - "doc/audit"
  - "status/historical"
project: "Fuyutsui"
doc_type: "historical-audit"
status: "historical"
authority: "historical-audit"
up:
  - "[[50-参考资料/00-参考资料-MOC|参考资料 MOC]]"
related:
  - "[[20-Fuyutsui/00-Fuyutsui-MOC|Fuyutsui MOC]]"
  - "[[40-跨项目/04-Shingen-兼容性变更检查清单|兼容性变更检查清单]]"
source_files:
  - "Fuyutsui/main.lua"
  - "Fuyutsui/core/*.lua"
  - "Fuyutsui/class/*.lua"
source_symbols: []
verified_at: "2026-07-25"
---

# Fuyutsui 优化建议

> [!warning] 历史审计
> 本文保留 2026-07-25 的静态审计结论与当时行号，不是当前缺陷清单。代码已拆分且部分问题可能已修复；实施任何条目前必须回到当前源码复核，并使用 [[40-跨项目/04-Shingen-兼容性变更检查清单|兼容性变更检查清单]] 评估双项目影响。

本文档基于 2026-07-25 工作区源码的**静态审计**，不含游戏内实测结论。

阅读约定：

- **[必修]** = 当前代码在运行时一定或极可能出错。
- **[加固]** = 12.1 秘密值（secret value）相关，不改会在特定情形下报错或输出脏数据。
- **[迁移]** = 光环体系必须收敛到 `AuraContainer`，参见 `AuraContainer_AI_Reference_zh-CN.md`。
- **[性能]** = 不影响正确性，影响帧时间或事件风暴表现。
- **[协议]** = 像素输出协议的健壮性，影响外部读取程序。
- 每条都标注 `静态检查` 或 `需游戏内验证`。

当前仓库开发约定见 `CLAUDE.md`。不要格式化整个仓库或 `libs/`；改 `core/block.lua` 前先确认工作区没有未提交改动。本文原始审计时期引用的 `AGENTS.md` 未随当前内置插件保留。

---

## 1. 必修：运行时缺陷

### 1.1 `Fuyutsui:Print` 未定义 —— 斜杠命令会直接报错

- 位置：`core/core.lua:332`、`core/core.lua:337`、`core/core.lua:356`
- 现状：三处调用 `self:Print(...)`，但整个插件（含 `libs/`）没有任何 `function Fuyutsui:Print` 定义，也没有混入 AceConsole。
- 后果：`/fu delay <秒>` 与任何未识别命令都会抛 `attempt to call method 'Print' (a nil value)`，`delay` 恢复计时器的回调也会中断。
- 建议：在 `core/core.lua` 顶部补一个最小实现，或把三处改成裸 `print`。补实现更好，因为其他地方将来也会用到统一前缀。

```lua
function Fuyutsui:Print(...)
    print("|cff33ff99Fuyutsui|r:", ...)
end
```

- 判定：静态检查（已 grep 全仓确认无定义）。

### 1.2 `UNIT_SPELLCAST_EMPOWER_STOP` 条件反了

- 位置：`main.lua:1514-1524`
- 现状：

```lua
if unitTarget ~= "player" then
    state.empowering = false
    ...
elseif unitTarget == "target" then
    target.empowering = false
end
```

- 后果两条：
  1. 玩家自己的赋能结束时 `state.empowering` **永远不会**被清零 —— 只能靠 `OnUpdate` 里的 `UpdatePlayerEmpowerInfo` 兜底，赋能像素位会残留。
  2. `elseif unitTarget == "target"` 是死分支（`~= "player"` 已经吞掉了 `"target"`），`target.empowering` 永远不清零。
- 建议改为：

```lua
if unitTarget == "player" then
    state.empowering = false
    state.castTargetUnit = nil
    state.castTargetName = nil
    state.castTargetIndex = 0
    self:UpdatePlayerCasting(0)
elseif unitTarget == "target" then
    target.empowering = false
end
```

- 对照 `UNIT_SPELLCAST_EMPOWER_START`（`main.lua:1510` 附近）的写法，语义应当一致。
- 判定：静态检查；修完建议用唤魔者/牧师赋能技能 **需游戏内验证**。

### 1.3 `shapeshiftFormID` 先除 255 再和原始 ID 比较

- 位置：`main.lua:800-801` 写入，`main.lua:728-729` 读取
- 现状：

```lua
state.shapeshiftFormID = shapeshiftFormID / 255   -- 归一化后的像素值
...
state.mounted = IsMounted() or state.shapeshiftFormID == 27 or state.shapeshiftFormID == 3 or
    state.shapeshiftFormID == 29
```

- 后果：`27/255 ≈ 0.1059`，永远不等于 `27`。飞行形态 / 旅行形态判定完全失效，德鲁伊变形时 `有效性` 像素位会错。
- 建议：把「原始值」和「像素值」分成两个字段，不要让同一个键既当业务值又当颜色值。

```lua
function Fuyutsui:UpdateShapeshiftForm()
    local formID = GetShapeshiftFormID() or 0
    state.shapeshiftFormRaw = formID
    state.shapeshiftFormID = formID / 255
    if blocks and blocks.state["姿态"] then
        self:CreateTexture(blocks.state["姿态"], state.shapeshiftFormID)
    end
end

local TRAVEL_FORMS = { [3] = true, [27] = true, [29] = true }
function Fuyutsui:UpdatePlayerMounted()
    state.mounted = IsMounted() or TRAVEL_FORMS[state.shapeshiftFormRaw or 0] == true
    self:UpdatePlayerValid()
end
```

- 附带问题：`UpdatePlayerMounted` 在 `PLAYER_LOGIN` 路径（`main.lua:397`）先于任何 `UpdateShapeshiftForm` 调用，此时 `state.shapeshiftFormRaw` 为 `nil`，上面的 `or 0` 是必要的。
- 判定：静态检查。

### 1.4 `ClearGroupBlocks()` 上限写死 255，且没有任何调用点

- 位置：`main.lua:1315-1322`
- 现状：`for index = startIndex, 255 do` —— 但像素总数是 `BLOCK_FIX_COUNT = 510`（`core/block.lua:43` 一带的配置）。队伍块起始索引通常在 60~70，成员数 × 每人字段数很容易越过 255。同时全仓 grep 没有任何地方调用它。
- 后果：要么是"写了但没接上"的死代码，要么将来接上后只清一半，256..510 段残留旧成员数据。
- 建议二选一：
  - **删掉**（若队伍清理已由 `RefreshGroupAuraContainers` 的 disable/hide 分支覆盖）；
  - 或改成按实际范围清理并在 `UpdateGroup()` 缩员时调用：

```lua
function Fuyutsui:ClearGroupBlocks()
    local g = blocks and blocks.groups
    if not (g and g.start and g.num) then return end
    local last = g.start + g.num * (g.numMax or g.num) - 1
    for index = g.start, math.min(last, 510) do
        self:CreateTexture(index, 0)
    end
end
```

- 注意：`ReleaseGroupAuraContainers` 只销毁 AuraContainer 帧，**不会**把对应像素刷回 0。专精切换时旧成员的光环像素会停在最后一次写入的颜色上，直到有新值覆盖。这正是需要一个可用的 `ClearGroupBlocks()` 的原因。
- 判定：静态检查；像素残留 **需游戏内验证**（切专精后观察 256..510 段）。

### 1.5 `UpdateEncounterID` 的 `else` 分支不可达

- 位置：`main.lua:684-691`
- 现状：

```lua
local id = self.bossID and self.bossID[encounterID] or 0
if id then ... else ... end
```

- 后果：Lua 中 `0` 为真，`else` 永远走不到。两个分支行为其实一致，属于无害冗余，但会误导后续读者以为有"未知首领"分支。
- 建议：直接去掉 `if/else`，只留一行 `state.bossID = id / 255`。
- 判定：静态检查。

### 1.6 `.toc` 的 `Interface` 行含全角逗号

- 位置：`Fuyutsui.toc:1`
- 现状：`## Interface: 120000, 120001, 120005, 120007， 120010`（`120007` 后面是 `，` 而非 `,`）。
- 后果：解析取决于客户端实现，最坏情况是 `120010` 这一档被丢弃，在最新 PTR 上被标为过期插件。
- 建议：改成半角逗号。这是零风险一字修复，优先做。
- 判定：静态检查；**需游戏内验证**（看插件列表是否还标 out of date）。

---

## 2. 加固：12.1 秘密值

12.1 大幅扩展了返回秘密值的 API。参照 `AuraContainer_AI_Reference_zh-CN.md` §19.1，PTR 7 起以下函数**可能**返回秘密值：`UnitClass`、`UnitClassBase`、`UnitSex`、`UnitPhaseReason`、`UnitGroupRolesAssigned`（含 Enum 变体）、`UnitInRaid`、`UnitIsPVP`、`UnitRace`、`UnitIsGroupLeader/Assistant`、`UnitGetAvailableRoles`、`GetInspectSpecialization`，以及当光环本身是秘密值时的 `UnitIsCharmed` / `UnitIsPossessed`。

秘密值的关键性质：**可以传递，不能观测**。参与比较、算术、`tostring`、`print`、`format` 都会报错。

当前 `main.lua` 只在 4 处用了 `isSec`（1441、1527、1549、1662），覆盖面明显不足。

### 2.1 `UnitGroupRolesAssigned(unit)` 未防护 —— 最高风险点

- 位置：`main.lua:1333`（`UpdateGroup`），下游 `main.lua:1234`（`UpdateGroupInRangeAndHealth`）
- 现状：`role` 被直接存进 `group[unit].role`，之后用 `roleMap[obj.role]` 做表索引，并参与 `/255` 算术。
- 后果：只要 `role` 是秘密值，`roleMap[obj.role]` 的哈希查找即触发观测错误，而这段代码在 **`OnUpdate` 每帧**执行 —— 会变成持续报错刷屏，队伍血量/职责像素全线中断。
- 建议：在 `UpdateGroup` 入口就把秘密值收敛成安全默认值，不要让它流入 `group` 表。

```lua
local role = UnitGroupRolesAssigned(unit)
if isSec(role) then
    role = "NONE"
end
if unit == "player" then
    role = self.state.specRole
end
```

- 若确实需要"秘密职责"参与显示，正确做法是走 `C_CurveUtil` 的颜色曲线（`EvaluateColorFromBoolean` 之类）把值直接塞进颜色通道，全程不观测；参见 §5.3。
- 判定：静态检查；**需游戏内验证**（进 5 人本/团本，观察是否报错）。

### 2.2 `UnitInRaid("player")` 被调用两次且直接参与算术

- 位置：`main.lua:656-661`
- 现状：

```lua
if UnitInRaid("player") then
    index = UnitInRaid("player") or 0
end
...
state.groupType = index / 255 or 0
```

- 三个问题：
  1. `UnitInRaid` 现在可能返回秘密值，`if` 判真本身就是观测；
  2. 调了两次，浪费且可能拿到不一致结果；
  3. 返回值是 raid index（1..40），除 255 得到的是"我在第几个团队栏位"，语义上和函数名 `UpdateGroupType`（队伍类型）不符 —— 注释里的 `46` 表示小队，但团队分支输出的却是栏位号，外部程序无法区分"团队"和"某个栏位号恰好是 46"。
- 建议：改用不返回秘密值的 `IsInRaid()` / `IsInGroup()`，并给团队一个固定编码：

```lua
function Fuyutsui:UpdateGroupType()
    local index = 0
    if IsInRaid() then
        index = 47
    elseif IsInGroup() then
        index = 46
    end
    state.groupType = index / 255
    self:CreateTexture(blocks.state["队伍类型"], state.groupType)
end
```

- 注意：这会改变外部程序的读值约定，需要同步告知消费端。若必须保留栏位号语义，至少要 `isSec` 兜底。
- 判定：静态检查。

### 2.3 类文件首行 `UnitClassBase("player")` 的字符串比较

- 位置：`class/*.lua:1`（13 个文件）、`core/core.lua:2`
- 现状：`if UnitClassBase("player") ~= "DEATHKNIGHT" then return end`
- 说明：这是加载期（`ADDON_LOADED` 之前）对 `player` 自身的调用。参考文档把 `UnitClassBase` 列为**可能**返回秘密值，但对自身单位在加载期通常是明值。这里更可能是"暂时安全但没有保障"。
- 建议（低优先，但值得做一次性收敛）：在 `core/core.lua` 里算一次玩家职业并缓存到全局，类文件改成读缓存，这样将来 API 语义变化只需改一处：

```lua
-- core/core.lua（需在 .toc 里先于 class/*.lua 加载）
local ok, classFile = pcall(UnitClassBase, "player")
Fuyutsui.playerClassFile = (ok and not issecretvalue(classFile)) and classFile or nil

-- class/DeathKnight.lua
if Fuyutsui.playerClassFile ~= "DEATHKNIGHT" then return end
```

- 同时 `core/core.lua:2` 的 `local className, classFilename, classId = UnitClass("player")` 里 `classId` 后续参与 `self.state.classId / 255`（`main.lua:401`、`main.lua:418`）—— 这是**算术观测**，比字符串比较更危险，应优先加 `isSec` 兜底。
- 判定：静态检查；秘密值边界 **需游戏内验证**。

### 2.4 `libs/LibRangeCheck-3.0` 的秘密值处理

- 位置：`libs/LibRangeCheck-3.0/LibRangeCheck-3.0.lua:4064`（已有 `isMidnight and issecretvalue(guid)` 分支）、4246/4247/4640/4794/4816/4970（`UnitClass` / `UnitRace`）
- 说明：该库已经开始适配 Midnight（缓存键会在 GUID 为秘密值时退化成 unit token），但多处 `UnitClass("player")` / `UnitRace("player")` 仍未防护。
- 建议：**不要直接改 vendored 库**；当前约定以根 `CLAUDE.md` 为准。改为：
  1. 升级到上游最新版本；
  2. 若上游未修，在 `Fuyutsui` 侧包一层，捕获 `UpdateUnitRange` 的错误，失败时退化到 `UnitInRange` / `IsSpellInRange`，避免单个库错误打断 `OnUpdate`（`UpdateEnemyCount` 每 0.2s 对每个铭牌调一次，见 `main.lua:1155`）。
- 判定：静态检查；**需游戏内验证**。

### 2.5 施法目标名字匹配

- 位置：`main.lua:1441`（`if not isSec(targetName) then`）
- 现状：这里已经正确防护了。**保留这个模式**，并把它作为其他单位名相关代码的模板。
- 相关：`main.lua:1339` 的 `name = GetUnitName(unit, true)` 存入 `group` 表。若下游只是当不透明值传递不会出问题，但一旦有人加了 `print` 或字符串拼接就会炸。建议在存入时就判断：`local name = GetUnitName(unit, true); if isSec(name) then name = nil end`。
- 判定：静态检查。

---

## 3. 迁移：光环体系必须全量收敛到 AuraContainer

**硬约束：光环只能用 `AuraContainer_AI_Reference_zh-CN.md` 的方案。**不允许 `C_UnitAuras` + `OnUpdate` 轮询，不允许基于 `UNIT_AURA` 自建状态机。

### 3.1 84 处遗留 `auraName` / `showKey` 声明当前**完全不输出像素**

- 现状：`main.lua:321-327` 的解析逻辑里，`type == "aura"` 的条目**必须**带 `spellId` 或 `spellIds`，否则被静默忽略（注释写明"旧 auraName/showKey 逻辑光环已移除，忽略"）。
- 统计（grep `auraName|showKey`）：

| 文件 | 遗留条目数 |
| --- | --- |
| `class/DeathKnight.lua` | 21 |
| `class/Paladin.lua` | 18 |
| `class/Monk.lua` | 9 |
| `class/Shaman.lua` | 9 |
| `class/Druid.lua` | 8 |
| `class/Mage.lua` | 6 |
| `class/Warrior.lua` | 5 |
| `class/Priest.lua` | 4 |
| `class/Hunter.lua` | 3 |
| `class/DemonHunter.lua` | 1 |
| **合计** | **84** |

- 后果：这些像素位常年为 0。外部程序读到的是"光环不存在"，而不是"未实现"—— 无法区分，会导致决策错误。
- 迁移模板（`class/Priest.lua` 专精 1 已完成，照抄它）：

```lua
-- 旧（无输出）
[25] = { type = "aura", name = "杀戮机器", auraName = "杀戮机器", showKey = "count" },
[26] = { type = "aura", name = "白霜", auraName = "白霜", showKey = "remaining" },

-- 新（剩余时间 → 像素蓝通道）
[26] = { type = "aura", name = "白霜", spellId = 51124 },

-- 新（层数 → 第二行横向层数条，不占像素位）
[25] = { type = "aura", name = "杀戮机器", spellId = 51128, maxApps = 1 },
```

- **关键语义差异，不要机械替换**：
  - `showKey = "remaining"` → 只给 `spellId` / `spellIds`，剩余时间由 `SetDurationText` 的颜色曲线写进蓝通道。这是 1:1 映射。
  - `showKey = "count"` → 在新方案里层数走的是 `FuyutsuiCountBars` 第二行的**层数条**（`maxApps`），**不是**原来的像素位。原来的像素索引会空出来，外部程序的读取地址必须同步调整。请为每个 `count` 条目查证实际最大层数填 `maxApps`，不要填 1 了事。
- 迁移顺序建议：按遗留数量从多到少，但**每个职业每次只提交一个专精**，方便逐个在游戏内核对像素。
- `spellId` 需要逐条查证（同一光环在不同天赋下可能有多个 ID，用 `spellIds = { id1, id2 }`，任一命中即显示；`class/Priest.lua` 的 `虚空之盾 = { 17, 1253593 }` 就是这个用法）。
- 判定：静态检查（漏输出已确认）；每条 spellId 正确性 **需游戏内验证**。

### 3.2 `class/Priest.lua` 专精 2（神圣）仍是遗留写法

- 位置：`class/Priest.lua:83-86`
- 说明：同一个文件里专精 1 已迁移、专精 2 未迁移，是最容易做的对照迁移，建议作为第一个提交样板。
- 判定：静态检查。

### 3.3 专精 / 天赋变化时没有释放玩家光环容器

- 位置：`main.lua:361-363`（`LoadPlayerBlocks` 尾部）、`core/block.lua:446-449`（`RefreshPlayerAuraContainers`）
- 现状：`LoadPlayerBlocks` 只调 `ReleaseGroupAuraContainers()`，**没有**调 `ReleasePlayerAuraContainers()`。而 `RefreshPlayerAuraContainers` 开头是：

```lua
if Fuyutsui.PlayerAuraContainer then
    return
end
```

- 后果链：
  1. `PLAYER_TALENT_UPDATE`（`main.lua:1394`）→ `UpdatePlayerSpecInfo` → `LoadPlayerBlocks` 换了新的 `blocks.auras`；
  2. 但 `Fuyutsui.PlayerAuraContainer` 还在，`RefreshPlayerAuraContainers` 直接 return；
  3. 于是玩家光环槽仍绑定**旧专精**的 `spellId` 集合与**旧像素索引** —— 输出到错误的像素位。
  4. `LayoutAuraApplicationBars`（`core/block.lua:460`）同理被 `auraBarLaidOut` 挡住，层数条不重排，`nextAvailableIndex` 也不回退 —— 反复切天赋会持续消耗 500 个 bar 单元直至 `ReserveHorizontalBarUnits` 打印"空间不足"。
- 为什么 `UNIT_SPELLCAST_SUCCEEDED` 那条路能工作：`main.lua:1532-1543` 对 `384255` / `200749` 显式调了 `ClearAllFuyutsuiBars()`，而它内部（`core/block.lua:263-264`）会调 `ReleasePlayerAuraContainers()`。也就是说**只有通过这两个特定法术切换时**才是干净的；通过其他路径（天赋树直接应用、进入战场自动换天赋、`PLAYER_SPECIALIZATION_CHANGED`）都会留下脏状态。
- 建议：把释放动作提到 `LoadPlayerBlocks`，让所有路径统一：

```lua
-- main.lua，LoadPlayerBlocks 尾部
self.blocks = blocks
if self.ReleaseGroupAuraContainers then
    self:ReleaseGroupAuraContainers()
end
if self.ReleasePlayerAuraContainers then
    self:ReleasePlayerAuraContainers()
end
```

- 同时把 `main.lua:1532-1543` 的两个特例分支简化为只调 `UpdatePlayerSpecInfo()`，避免两套清理路径互相干扰。
- PTR 7 起**允许战斗中创建 AuraContainer**（参考文档 §0），所以重建不必再等 `PLAYER_REGEN_ENABLED`，可以直接做。
- 判定：静态检查；**需游戏内验证**（切天赋后核对像素索引与层数条起点）。

### 3.4 `SetupClippedDuration` 在 `initializeFrame` 里对 AuraButton 调 `SetPoint`

- 位置：`core/block.lua:346-360`（`button:SetPoint("TOPLEFT", UIParent, ...)`）、`core/block.lua:381-395`（`button:SetPoint("TOPLEFT", countBars, ...)`）
- 现状：直接在 AuraButton 上调原生 `SetPoint` / `SetSize`。
- 约束：参考文档 §0 明确 —— AuraButton 上的原生 `SetPoint` / `SetSize` **只允许在 `PLAYER_LOGIN` 之前**使用。`RefreshPlayerAuraContainers` 由 `UpdatePlayerBarInfo`（`main.lua:713`）驱动，而后者在 `PLAYER_LOGIN` 之后的 `UpdatePlayerBlocks`（`main.lua:265`）里被调用，**已经过了窗口**；`PLAYER_TALENT_UPDATE` 触发的重建更是远在登录之后。
- 后果：在 PTR 7 上可能直接抛错或静默不生效，光环像素全体错位。这是本文档里**最需要优先实测**的一条。
- 建议：改用 `AddAuraSlot` 的定位选项 / 容器侧布局，把逐 button 的原生锚定去掉。若确实无替代 API，则必须把容器与槽位的创建全部前移到 `PLAYER_LOGIN` 之前一次性完成（预分配 510 个槽，之后只改 `candidateFilters`），或使用 PTR 7 新增的 `ResizeToBoundsRect`。
- 相关：`core/block.lua` 注释提到 `AURA_DURATION_STRATA = "TOOLTIP"` / level 5003，主条 `"BACKGROUND"` / 5001 —— 层级设置本身没问题，问题只在 `SetPoint` 的时机。
- 判定：静态检查（时序上确定越界）；实际是报错还是静默失效 **需游戏内验证**。

### 3.5 `AuraSlotFilters` 不设 `maxDuration` —— 保持现状，不要"优化"

- 位置：`core/block.lua:318-323`
- 说明：这里的注释是对的 —— 任何非 `nil` 的 `maxDuration` 都会排除持续时间为 0 的永久光环，而 `maxDuration` 语义是**总时长**而非剩余时长。
- 建议：**不要动**。如果后续有人为了"过滤长 buff"想加 `maxDuration`，把这条注释指向参考文档 §8。
- 判定：静态检查。

### 3.6 队伍减益槽用 `includeDispelTypes` —— 正确，记录原因

- 位置：`core/block.lua:600-615`
- 说明：友方单位上的 `HARMFUL` 光环**不允许**用 `includeSpellIDs` 做身份过滤（参考文档 §8：spellID 身份过滤只对"可协助单位上的 HELPFUL"和"不可协助单位上的 HARMFUL"开放）。这里用 `includeDispelTypes` 是唯一合法路径。
- 建议：在该处加一行注释说明"此处不能改用 includeSpellIDs"，防止将来被"优化"成按 ID 过滤。
- 相关缺陷与当前接线边界见 [[20-Fuyutsui/08-Fuyutsui-光环容器本地集成|光环容器本地集成]]；`getTargetDispelType()` 只输出 0/1/11，注释里声明的 2/3/12/13/14/15 都没有写入路径。这一项应当改为由 AuraContainer 的 dispel 槽驱动，而不是在 Lua 侧判断。
- 判定：静态检查。

### 3.7 `UNIT_AURA` 是死注册

- 位置：`core/core.lua:142` 注册，但全仓没有 `function Fuyutsui:UNIT_AURA`
- 后果：事件分发（`Fuyutsui[event](Fuyutsui, event, ...)`）会对 `nil` 取索引调用，取决于分发实现，可能每次光环变化都报错，或者只是白白唤醒事件帧。在战斗中 `UNIT_AURA` 是最高频事件之一。
- 建议：**删掉这行注册**。光环状态一律由 AuraContainer 驱动，插件侧不需要 `UNIT_AURA`。
- 判定：静态检查；是否报错取决于分发代码，**需游戏内验证**。

### 3.8 未使用的 PTR 7 新能力

参考文档 §0 列出的新特性中，以下与本插件直接相关但尚未采用：

| 特性 | 用途 |
| --- | --- |
| `AuraContainerSortMethod.AuraInstanceIDOnly` | 像素协议只关心"某 ID 是否存在"，不需要按到期时间排序。改用它可以避免光环刷新时槽位重排导致的像素抖动。**这条价值最高。** |
| 战斗中创建 AuraContainer | 使 §3.3 的重建不必等出战 |
| `ResizeToBoundsRect` | 可能替代 §3.4 里违规的手工 `SetSize` |
| 公开 AuraButton API 可在 `initializeFrame` 外调用 | 允许把定位逻辑移出 `initializeFrame`，配合 §3.4 |
| 多次 `AddDispelTypeTexture` | 若将来要在一个槽里区分多种驱散类型 |

- 具体建议：把 `core/block.lua` 里 4 处 `sortMethod = AuraContainerSortMethod.Expiration`（435-441、496、591、610）评估改为 `AuraInstanceIDOnly`。因为每个槽的 `candidateFilters` 已经窄到特定 spellID 集合，正常情况下只会有一个候选，排序方式无实质影响，但 `AuraInstanceIDOnly` 的重排更稳定。
- 例外：`core/block.lua:610` 的 dispel 槽用 `includeDispelTypes`，候选可能多个，此处保留 `Expiration` 更合理（优先显示最快到期的可驱散减益）。
- 判定：静态检查；行为差异 **需游戏内验证**。

---

## 4. 性能：刷新分频与无效工作

### 4.1 `CreateTexture` 无脏值缓存 —— 每帧重复写同色

- 位置：`core/block.lua:102-108`
- 现状：每次调用都无条件 `tex:SetColorTexture(r, g, b, 1)`。
- 场景：`OnUpdate` 每帧调 `UpdatePlayerCastingInfo` / `UpdatePlayerChannelingInfo` / `UpdatePlayerEmpowerInfo` / `UpdateGroupInRangeAndHealth`；不施法时这些值绝大多数帧都不变，但仍在写。
- 建议：加一层数值缓存，只在蓝通道变化时才写。r/g 由索引决定，不会变，只需比 b。

```lua
local lastBlue = {}

function Fuyutsui:CreateTexture(i, b)
    if lastBlue[i] == b then return end
    local tex = createTextureByIndex(i)
    if tex then
        local r, g = EncodeBlockChannels(i)
        tex:SetColorTexture(r, g, b, 1)
        lastBlue[i] = b
    end
end
```

- **重要**：`ClearAllTextures()`（`core/block.lua:110-114`）与 `ClearAllFuyutsuiBars()` 必须 `wipe(lastBlue)`，否则清屏后缓存会挡住重写。这是加缓存最容易踩的坑。
- 另外，AuraContainer 通过颜色曲线直接写贴图的路径**不经过** `CreateTexture`，缓存对它无影响，不会冲突。
- 判定：静态检查；收益幅度 **需游戏内验证**（团本战斗中对比帧时间）。

### 4.2 `UpdatePlayerAssistant` 在关闭辅助时仍每 0.2s 调用

- 位置：`main.lua:1768`（`OnUpdate`）、实现在 `main.lua:586-591`
- 现状：无条件调用 `C_AssistedCombat.GetNextCastSpell()`。而 `dpsMode`（`core/core.lua:163`、`core/quickbutton.lua:29`）本来就是这个功能的开关。
- 建议：按开关短路。

```lua
function Fuyutsui:UpdatePlayerAssistant()
    if not blocks or not blocks.state["一键辅助"] then return end
    local c = self.db and self.db.char
    if c and c.dpsMode ~= 0 then return end
    local spellId = C_AssistedCombat.GetNextCastSpell()
    ...
end
```

- 注意：短路时要把像素刷成 0 一次，不要留残值（配合 §4.1 的缓存，只会写一次）。
- 判定：静态检查（需确认 `dpsMode == 0` 是"开启辅助"—— `core/quickbutton.lua:29` 的 `dpsAssistant = (c.dpsMode or 0) == 0` 支持这个读法）。

### 4.3 `UpdateEnemyCount` 对每个铭牌做距离检测

- 位置：`main.lua:1150-1170`，每 0.2s 一次
- 现状：循环内对每个铭牌调 `UpdateUnitRange(unit)`（走 LibRangeCheck）+ `UnitAffectingCombat(unit)`。团本 AOE 场景铭牌数可达 20~40，即每秒 100~200 次距离检测。
- 建议：
  - 先用便宜的判断短路：`if not data.canAttack then` 直接跳过，不做距离检测（当前是先算距离再判 `canAttack`，顺序反了）；
  - 把频率降到 0.3~0.5s（敌人数量不需要 5Hz 精度）；
  - `data.canAttack` 只在 `NAME_PLATE_UNIT_ADDED` 时更新即可，不必每轮重算。
- 判定：静态检查；实际开销 **需游戏内验证**。

### 4.4 `UpdateKnightStatusCount` 对所有职业每秒执行

- 位置：`main.lua:974-981`，由 `OnUpdate` 1s 档调用（`main.lua:1778`）
- 现状：无条件调 `GetActiveKnightsCount()`。这是死亡骑士专属。函数内确实检查了 `blocks.state["天启骑士数量"]`，但**先调 API 后检查**。
- 建议：把 `blocks.state["天启骑士数量"]` 的检查提到 API 调用之前。同理检查 `UpdateRune`（`main.lua:784-796`）—— 它的顺序是对的，可以作为模板。
- 判定：静态检查。

### 4.5 `ReadKeybindings` 无防抖，被四个事件触发

- 位置：`core/keybinds.lua`
- 现状：`ReadKeybindings()` 先 wipe，再 `C_Timer.After(0.5, ...)` 做 180 个动作条槽位的扫描。注册来源：`UPDATE_BINDINGS`、`SPELLS_CHANGED`、`ACTIONBAR_SHOWGRID`、`ACTIONBAR_HIDEGRID`。
- 后果：`ACTIONBAR_SHOWGRID` / `HIDEGRID` 在拖动技能时成对高频触发，每次都排一个 0.5s 定时器 → 短时间内堆叠数十个 180 槽扫描。更糟的是 wipe 是**立即**执行的，所以在 0.5s 窗口内绑定表是空的，像素输出会掉零。
- 建议：单定时器防抖 + 不要提前 wipe。

```lua
local pendingTimer
local function ScheduleReadKeybindings()
    if pendingTimer then
        pendingTimer:Cancel()
    end
    pendingTimer = C_Timer.NewTimer(0.5, function()
        pendingTimer = nil
        DoReadKeybindings()   -- 在这里面才 wipe + 重扫
    end)
end
```

- 另一个已知问题见 [[20-Fuyutsui/09-Fuyutsui-动作条键位扫描|动作条键位扫描]]：`GetActionInfo` 的第二个返回值对 `macro` 和 `spell` 两种类型语义不同，当前代码一视同仁当 spellId 用，宏槽位会产生错误的 spellId 映射。
- 判定：静态检查；抖动现象 **需游戏内验证**（拖动技能时观察绑定像素）。

### 4.6 空事件处理函数：注册了但什么都不做

- 位置：`main.lua:1643`（`SPELL_UPDATE_USES`）、`main.lua:1667`（`SPELL_RANGE_CHECK_UPDATE`）、`main.lua:1671`（`ACTION_RANGE_CHECK_UPDATE`）、`main.lua:1732/1736/1739`（`ENCOUNTER_TIMELINE_*`）
- 现状：注册于 `core/core.lua:119/122/123/139/140/141`，处理函数体为空。
- 说明：`SPELL_UPDATE_USES` 例外 —— `core/block.lua:139` 的 `BAR_EVENTS` 用它刷新层数条，那是 bar 自己的事件帧，与 `main.lua` 的空函数无关。所以 `core/core.lua:119` 的注册可以删。
- 建议：删掉 5 个纯占位的注册（保留空函数不注册也可以，但注册了就要付事件分发成本）。`ACTION_RANGE_CHECK_UPDATE` 在战斗中相当高频。
  若这些是"待实现"的占位，加 `-- TODO` 注释并保留函数、删掉注册，等实现时再注册。
- 判定：静态检查。

### 4.7 调试残留

- 位置：`main.lua:101`（`DebugPrintNewSpellEntry`）、`main.lua:108`（`DebugPrintSpellBlockLine`）、`main.lua:113-118`（`GetSpellChargesInfo`，硬编码 spellID `1247378`）
- 现状：调用点已注释（`main.lua:1529-1530`），但函数体保留。`main.lua:116` 的 `print(k, v, issecretvalue(v))` 若被启用会在秘密值上直接报错（`print` 是观测）。
- 建议：删掉这三个函数，或移到一个不随 `.toc` 加载的 `debug/` 文件。
- 判定：静态检查。

### 4.8 `state.mapID` / `bossID` 的测试用开关

- 位置：`main.lua:1152-1153`（`testMap[state.mapID]`、`testEncounter[state.encounterID]`）
- 说明：`UpdateEnemyCount` 里用 `inTestMap` / `inTestEncounter` 放宽"敌人计数"的进战条件。这是刻意的测试逃生门。
- 建议：确认这两张表在正式使用时是空的，或者把它变成 `db` 里的显式开关，避免"某张地图上敌人数莫名偏高"这种难查的问题。
- 判定：静态检查；**需游戏内验证**（确认 `testMap` 内容）。

---

## 5. 协议：像素输出健壮性

### 5.1 `GetScreenWidth()` 只在文件加载时取一次

- 位置：`core/block.lua:2`（`local screenWidth = GetScreenWidth()`），用于 `core/block.lua:43`（`blockWidth = screenWidth / 510`）、`core/block.lua:51`（`width = screenWidth / 500`）、`core/block.lua:83`、`core/block.lua:127`
- 后果：分辨率切换、窗口化/全屏切换、UI 缩放变更后，色块宽度与实际屏幕不匹配 —— 外部程序按等分算出的采样点会读到相邻色块，**整个协议错位**。这是对外部读取程序最致命的一类失效，且用户不会意识到是插件问题。
- 建议：注册 `DISPLAY_SIZE_CHANGED` 与 `UI_SCALE_CHANGED`，重算宽度并重排所有贴图。

```lua
local function RelayoutForScreenSize()
    screenWidth = GetScreenWidth()
    BLOCK_FIX_CONFIG.blockWidth = screenWidth / BLOCK_FIX_COUNT
    BAR_CONFIG.width = screenWidth / BAR_UNIT_COUNT
    colorBars:SetSize(screenWidth, BLOCK_FIX_CONFIG.blockHeight)
    countBars:SetSize(screenWidth, BAR_FRAME_HEIGHT)
    for i, tex in pairs(pixelTextures) do
        tex:SetSize(BLOCK_FIX_CONFIG.blockWidth, BLOCK_FIX_CONFIG.blockHeight)
        tex:ClearAllPoints()
        tex:SetPoint("TOPLEFT", colorBars, "TOPLEFT",
            GetXOffset(i - 1, BLOCK_FIX_CONFIG.blockWidth, BLOCK_FIX_CONFIG.blockSpacing), 0)
    end
    -- 层数条与光环 button 的重排同理
end
```

- 注意：第二行的层数条与 AuraContainer button 都用了 `BAR_CONFIG.width` / `blockWidth` 做偏移，重排要覆盖到（这与 §3.4 的 `SetPoint` 时机限制冲突 —— 若 AuraButton 确实不能在登录后重锚，那么这个插件在**分辨率切换后必须重载 UI**，应当在文档和 `/fu` 帮助里明确告知用户）。
- 判定：静态检查；**需游戏内验证**（切分辨率后用取色工具核对色块边界）。

### 5.2 `blockWidth` 是小数，色块边界会有亚像素抖动

- 位置：`core/block.lua:43`（`screenWidth / 510`）、`core/block.lua:51`（`screenWidth / 500`）
- 举例：1920 / 510 ≈ 3.765px。逐个累加偏移后，实际渲染边界受浮点与舍入影响，外部程序若采样色块中心点通常安全，但采样边界会读错。
- 建议：
  - 保持中心采样约定，并在给外部程序的说明里写明"必须采中心点"；
  - 或者放弃屏幕等分，改用**固定整数宽度**（如每块 3px，510 块 = 1530px，小于任何常见分辨率宽度），彻底消除舍入问题。第二种更稳，但需要外部程序同步改。
- 判定：静态检查。

### 5.3 `GetItemRemainingTime` 用 255 表达"不可用"，与"无冷却"撞车

- 位置：`main.lua:866-874`，调用点 `main.lua:882-930`
- 现状：

```lua
if not enableCooldownTimer then return 255 end
...
self:CreateTexture(blocks.state["大红冷却"], math.min(1, remainingTime / 255))
```

- 后果：`enableCooldownTimer` 为假时返回 255，`math.min(1, 255/255)` = `1`；而数量为 0 的分支（`main.lua:886`）也写 `1`。两种完全不同的状态（"物品可用且无冷却" vs "没有这个物品"）编码相同，外部程序无法区分。此外 `remainingTime` 超过 255 秒的长冷却也会被 `math.min` 截到 1，同样撞车。
- 建议：明确三态编码，并让"无物品"用一个不可能由冷却产生的值。

```lua
-- 约定：0 = 可立即使用；(0,1) = 冷却中（剩余秒数/255）；1 = 不可用/无物品
function Fuyutsui:GetItemRemainingTime(itemID)
    local start, duration, enable = C_Item.GetItemCooldown(itemID)
    if not enable then return nil end            -- nil 表示不可用，由调用方写 1
    if start > 0 then
        return math.max(0, duration - (GetTime() - start))
    end
    return 0
end
```

调用方：

```lua
local remainingTime = self:GetItemRemainingTime(241304)
if remainingTime and (self.state.HealthPotionCount or 0) > 0 then
    self:CreateTexture(blocks.state["大红冷却"], math.min(254 / 255, remainingTime / 255))
else
    self:CreateTexture(blocks.state["大红冷却"], 1)
end
```

- 这样 `1` 唯一表示"不可用"，冷却值最大 `254/255`。需同步告知外部程序。
- 附带：`main.lua:883` 的 `self.state.HealthPotionCount > 0` 在 `GetItemCount()` 失败时仍可能是 `nil`，会报错比较 nil。加 `or 0`。
- 判定：静态检查。

### 5.4 `UpdateSpellCooldown` 把原始索引当绿通道

- 位置：`main.lua:833`：`ColorValue255:SetRGBA(0, index, 254 / 255)`
- 现状：绿通道被赋原始索引（可能 > 1），而协议约定绿通道是 `index / 255`。颜色分量会被夹到 1.0，所有索引 ≥ 1 的法术冷却色块绿通道都变成同一个值。
- 建议：确认此处 `ColorValue255` 是作为曲线的端点颜色传给 `EvaluateRemainingDuration`（`main.lua:832`），而不是直接写贴图 —— 如果是端点颜色，绿通道也应当是 `index / 255` 才能让最终颜色符合协议。
- 判定：静态检查；**需游戏内验证**（用取色工具读法术冷却色块的绿通道值）。

### 5.5 `CreateTexture` 对越界索引静默无操作

- 位置：`core/block.lua:90`：`if i <= 0 or i > BLOCK_FIX_CONFIG.blockCount then return nil end`
- 现状：索引越界（含 `nil` 索引，`nil <= 0` 会报错所以其实是抛错）时不告警。
- 场景：`blocks.state["xxx"]` 拼错名字 → 得到 `nil` → `CreateTexture(nil, v)` 在 `nil <= 0` 处报错；索引超 510（如 §1.4 的队伍越界）→ 静默丢弃。
- 建议：加显式告警，且只在首次告警（避免 `OnUpdate` 刷屏）。

```lua
local warnedIndex = {}
local function createTextureByIndex(i)
    if type(i) ~= "number" or i <= 0 or i > BLOCK_FIX_CONFIG.blockCount then
        if not warnedIndex[tostring(i)] then
            warnedIndex[tostring(i)] = true
            print(("Fuyutsui: 像素索引越界 %s，已忽略"):format(tostring(i)))
        end
        return nil
    end
    ...
end
```

- 判定：静态检查。

### 5.6 队伍块的容量没有上界校验

- 位置：`main.lua:1205`（`UpdateUnitHealthInfo` 的索引计算）、`main.lua:1228`
- 现状：`index = groups.start + (obj.index - 1) * groups.num + groups.healthPercent`，没有和 510 比较。`core/block.lua:585` 的 AuraContainer 路径**有**上界检查（`pixelIndex <= BLOCK_FIX_COUNT`），但 Lua 直写路径没有。
- 后果：40 人团 × 每人 5 字段 + start = 可能远超 510，超出部分被 `CreateTexture` 静默丢弃 —— 表现为"部分团员数据永远为 0"，很难定位。
- 建议：在 `LoadPlayerBlocks` 解析 `type == "group"` 时（`main.lua:347-357`）就校验一次并告警：

```lua
elseif v.type == "group" then
    local last = k + (v.num or 0) * 40
    if last > 510 then
        print(("LoadPlayerBlocks: 队伍块最多可容纳 %d 人，超出部分不会输出")
            :format(math.floor((510 - k) / (v.num or 1))))
    end
    ...
```

- 判定：静态检查。

---

## 6. 建议的实施顺序

| 顺序 | 内容 | 理由 |
| --- | --- | --- |
| 1 | §1.6 toc 全角逗号 | 一字修复，零风险 |
| 2 | §1.1 补 `Fuyutsui:Print` | 修掉必然报错的斜杠命令 |
| 3 | §1.2 赋能条件、§1.3 姿态比较、§1.5 死分支 | 独立的小 bug，互不影响 |
| 4 | §2.1 `UnitGroupRolesAssigned` 防护 | 每帧路径上的秘密值风险，优先级高于性能 |
| 5 | §3.4 AuraButton `SetPoint` 时机 | **先游戏内验证**是否真的失效；若失效则整个光环显示都是坏的，必须先解决再谈迁移 |
| 6 | §3.3 专精变化释放光环容器 | 是后续迁移能被正确验证的前提 |
| 7 | §3.7 删 `UNIT_AURA`、§4.6 删空注册、§4.7 删调试残留 | 纯删除，降噪 |
| 8 | §2.2 队伍类型、§5.3 物品冷却编码 | 会改变对外协议，需同步外部程序 |
| 9 | §4.1 脏值缓存 | 收益明显但要小心 wipe 时机，单独一次提交 |
| 10 | §3.1 84 处光环迁移 | 工作量最大；每职业每专精单独提交并逐个核对像素 |
| 11 | §5.1 分辨率重排 | 依赖 §3.4 的结论（能否重锚决定是重排还是要求重载 UI） |

---

## 7. 需要游戏内验证的清单

以下结论只做了静态推断，必须在 12.1 客户端上确认：

1. §3.4 —— AuraButton 在 `PLAYER_LOGIN` 之后调 `SetPoint` 是报错还是静默失效。**最高优先。**
2. §2.1 —— 组队/团队中 `UnitGroupRolesAssigned` 是否真的返回秘密值。
3. §2.3 —— 加载期 `UnitClassBase("player")` / `UnitClass("player")` 是否为明值。
4. §1.4 —— 切专精后 256..510 段像素是否残留旧值。
5. §3.7 —— `UNIT_AURA` 死注册是否触发分发错误。
6. §3.8 —— `AuraInstanceIDOnly` 与 `Expiration` 在单候选槽上的行为是否一致。
7. §5.1 / §5.2 —— 切分辨率后色块边界是否错位；中心采样是否仍准确。
8. §5.4 —— 法术冷却色块绿通道的实际取值。
9. §4.5 —— 拖动技能时绑定像素是否掉零。
10. §1.6 —— 修正 toc 后插件是否不再标记为过期。

---

## 8. 与既有文档的关系

- `AuraContainer_AI_Reference_zh-CN.md` 是光环实现的**唯一**权威。本文档第 3 节的每条都可追溯到它的对应章节（§0 变更索引、§8 candidateFilters、§9 排序、§12 时长颜色曲线、§19.1 秘密值扩展、§20 常见错误）。
- 当前事实与功能边界以 [[20-Fuyutsui/00-Fuyutsui-MOC|Fuyutsui MOC]] 及对应源码派生功能页为准；本文只保留历史审计问题和当时的改进建议。
- 本文档不修改任何代码。落地时请逐条独立提交，便于回退。
