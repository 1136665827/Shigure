# 纹理排序说明

本文描述 Fuyutsui 如何把 `ClassBlocks` 映射到屏幕上的色块 / 横向条，以及外部读取时应按什么顺序理解这些纹理。

实现入口：`main.lua` 的 `Fuyutsui:LoadPlayerBlocks(specIndex)`；主色块写入：`Fuyutsui:CreateTexture(index, b)`（`core/block.lua`）。

---

## 1. 两行输出区域

| 区域 | 容器 | 用途 |
| --- | --- | --- |
| 主色块条 | `FuyutsuiColorBars`（屏幕最顶端） | 状态、光环剩余、法术冷却、队伍成员等，按 **整数索引** 从左到右排布 |
| 横向计数条 | `FuyutsuiCountBars`（紧贴主色块下方） | 充能层数、`castCount`、光环 `maxApps` 层数条；**不占用**主色块索引 |

主色块共 **510** 格。索引编码：

- `1..255`：`r=0`，`g=index/255`，业务值在 `b`
- `256..510`：`r=1/255`，`g=(index-255)/255`，业务值在 `b`

---

## 2. 主色块总排序（同一专精）

`LoadPlayerBlocks` 从索引 **1** 起连续分配，顺序固定为：

```text
states → auras → spells → group
```

没有空隙：上一段用完后，下一段紧接着递增。不同专精声明的条目数量不同，因此 **同一语义在不同职业/专精上的绝对索引可能不同**；外部程序应按「当前专精表顺序」解读，或按名称映射，而不是写死旧版固定格位。

```mermaid
flowchart LR
  States["states 连续占位"] --> Auras["auras 连续占位"]
  Auras --> Spells["spells 连续占位"]
  Spells --> Group["group.start = 下一位"]
```

---

## 3. `states`：状态像素

### 3.1 扁平写法（多数职业）

```lua
states = {
    "锚点",
    "职业",
    -- ...
    "战斗时间",
}
```

按 `ipairs` 顺序写入 `blocks.state[名称] = index`，每项占 **1** 格。

约定：每个专精 `states` 开头通常包含这 8 项（已写入各职业表，不再由 `main.lua` 单独注入）：

| 顺序 | 名称 |
| ---: | --- |
| 1 | 锚点 |
| 2 | 职业 |
| 3 | 专精 |
| 4 | 队伍类型 |
| 5 | 英雄天赋 |
| 6 | 有效性 |
| 7 | 一键辅助 |
| 8 | 法术失败 |

其后是专精自定义状态（战斗时间、生命值、能量等）。

### 3.2 分类写法（如戒律牧师）

```lua
states = {
    ["状态"] = { "锚点", "职业", ..., "敌人人数" },
    ["目标"] = { "类型", "生命值", "施法", "施法可打断" },
    ["焦点"] = { ... }, -- 可选
}
```

分配顺序固定为：**状态 → 目标 → 焦点**。

写入 `blocks.state` 的键名规则：

| 分类 | 表内名称 | `blocks.state` 键 |
| --- | --- | --- |
| `状态` | `"生命值"` | `"生命值"` |
| `目标` | `"生命值"` | `"目标生命值"`（分类名 + 名称） |
| `焦点` | `"施法"` | `"焦点施法"` |

运行时通过 `UpdateStateBlock("目标", "生命值")` 等写入对应像素。

---

## 4. `auras`：单位光环像素

```lua
auras = {
    player = {
        { name = "虚空之盾", spellId = 1253590 },
        { name = "圣光涌动", spellId = 114255, maxApps = 2 },
    },
    target = {
        harmful = { { name = "暗言术：痛", spellId = 589 } },   -- 敌对：HARMFUL
        helpful = { { name = "救赎", spellId = 194384 } },     -- 友善：HELPFUL
    },
    focus = {
        harmful = {},
        helpful = { { name = "真言术：盾", spellIds = { 17, 1253593 } } },
    },
}
```

分配顺序：`player` → `target.harmful` → `target.helpful` → `focus.harmful` → `focus.helpful`。

- 每条有效光环（含 `spellId` 或 `spellIds`）占主色块 **1** 格：由对应单位的 AuraContainer 刷新。
- `maxApps` **不**额外占主色块；层数条目前只排布 `player` 光环（见第 7 节）。
- 缺少 `spellId`/`spellIds` 的条目会跳过并打印警告，**不占位**。
- 兼容旧扁平数组：整表当作 `player` / `HELPFUL`。

---

## 5. `spells`：法术冷却 / 充能冷却

```lua
spells = {
    { spellId = 47540, name = "苦修" },
    { spellId = 47540, name = "苦修", charge = true, maxCharge = 2 },
}
```

每条 `spells` 条目占主色块 **1** 格，按数组顺序排列。

| 条目字段 | 主色块含义 | 刷新来源 |
| --- | --- | --- |
| 默认（无 `charge`） | 技能冷却 | `GetSpellCooldown` → `blocks.spells[id].index` |
| `charge = true` | 充能回充冷却 | `GetSpellChargeDuration` → `blocks.spells[id].charge` |
| `maxCharge = N` | **不占**主色块；创建横向充能层数条 `0..N` | `GetSpellCharges().currentCharges` |
| `castCount = N` | **不占**额外主色块（该条本身仍占 1 格若单独声明）；创建横向 `castCount` 条 | `GetSpellCastCount` |

以苦修为例，在 `spells` 段内的相对顺序为：

1. 第 1 条 → **冷却**像素  
2. 第 2 条（`charge = true`）→ **充能冷却**像素  
3. 同条 `maxCharge = 2` → 横向条 **充能层数**（主色块下方）

可选透传：`forcedKnown`、`inSpellBook`（影响是否视为已学会，不改变占位）。

---

## 6. `group`：队伍成员块

```lua
group = {
    num = 5,              -- 每位成员占用的格数（步长）
    healthPercent = 1,    -- 相对偏移：血量
    role = 2,             -- 相对偏移：职责
    dispel = 3,           -- 可选：可驱散
    aura = {              -- 可选：成员光环，键为偏移
        [4] = { name = "救赎", spellId = 194384 },
        [5] = { name = "真言术：盾", spellIds = { 17, 1253593 } },
    },
}
```

- `group` **只声明一次**；`blocks.groups.start` = 此时下一个可用主色块索引。
- 第 `i` 名成员（`i` 从 1 起）的基址：

```text
memberBase = groups.start + (i - 1) * groups.num
pixel      = memberBase + offset
```

例如 `num = 5`，`start = 60`：

| 成员 | 血量 | 职责 | dispel | aura[4] | aura[5] |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 61 | 62 | 63 | 64 | 65 |
| 2 | 66 | 67 | 68 | 69 | 70 |
| … | … | … | … | … | … |

成员数量由运行时队伍列表决定；配置里的 `num` 是 **每位成员的槽宽**，不是人数上限本身。槽位超出 510 的部分不会写出。

---

## 7. 横向条排序（第二行）

与主色块独立，从左到右大致为：

```text
spells 推导的计数条（CreateAutoLayoutBar）
  → 光环 maxApps 层数条（LayoutAuraApplicationBars）
  → BAR_END_COLOR 终点色块
```

### 7.1 由 `spells` 推导

`LoadPlayerBlocks` 生成 `blocks.bars`，`UpdatePlayerBarInfo` 调用 `CreateAutoLayoutBar`：

| 条件 | `valueType` | min | max |
| --- | --- | ---: | ---: |
| `charge = true` 且 `maxCharge = N` | `"charge"` | 0 | N |
| `castCount = N`（正数） | `"castCount"` | 0 | N |

同一 `spellId` 的横向条只创建一次。首条从逻辑单元 `2` 起排；单条步进约为 `max + 3`（背景单元 + 间隔）。

### 7.2 由 `auras` 的 `maxApps` 推导

带 `maxApps` 的玩家光环在主色块显示剩余时间后，再在横向条追加层数条，排在 spell 计数条之后。

---

## 8. 戒律牧师示意（相对顺序）

配置见 `class/Priest.lua` `[1]`。主色块从左到右概念顺序：

```text
[状态…] 锚点…敌人人数
  → [目标…] 目标类型、目标生命值、目标施法、目标施法可打断
  → [auras] 虚空之盾、圣光涌动、熵能裂隙、福音、祸福相依
  → [spells] 心灵尖啸 … 苦修(CD) → 苦修(充能CD) → 真言术：耀(CD) → 耀(充能CD) → …
  → [group] 从 start 起每位 5 格（血量/职责/驱散/救赎/盾）
```

横向条（示例）：

```text
苦修层数(0..2) → 真言术：耀层数(0..2) → 圣光涌动层数 → 福音层数 → 祸福相依层数 → 终点色
```

具体绝对索引随 `states`/`auras`/`spells` 长度变化；以 `/reload` 后当前专精表为准。

---

## 9. 外部读取建议

1. **主色块**：用 `(r,g)` 还原索引，用 `b` 读业务值；先确认当前专精的 `ClassBlocks` 顺序。  
2. **名称映射**：状态看 `blocks.state` 键规则（分类拼接）；法术看 `spellId` 的 `.index` / `.charge`。  
3. **横向条**：用背景色 `(r=1/255, g=相对单元/255)` 定位段，再读 StatusBar / 层数条当前值。  
4. **切专精 / 天赋**：插件会清空并重建映射，外部缓存的绝对索引需失效重读。

---

## 10. 相关代码

| 文件 | 职责 |
| --- | --- |
| `class/*.lua` | 声明 `states` / `auras` / `spells` / `group` |
| `main.lua` → `LoadPlayerBlocks` | 连续分配主色块索引，生成 `blocks.bars` |
| `core/spells.lua` → `UpdateSpellCooldown` | 写技能 CD / 充能 CD |
| `core/player.lua` → `UpdatePlayerBarInfo` | 创建横向计数条并布局光环层数条 |
| `core/block.lua` | `CreateTexture`、`CreateAutoLayoutBar`、AuraContainer |
| `core/stateblocks.lua` | `UpdateStateBlock` 与状态 getter |
