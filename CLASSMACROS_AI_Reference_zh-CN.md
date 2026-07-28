---
title: "Fuyutsui core/classmacros.lua：AI 宏规则参考"
language: "zh-CN"
primary_file: "core/classmacros.lua"
related:
  - "core/macro.lua"
  - "main.lua"
  - "Keymap.md"
  - "AGENTS.md"
  - "AI_CODEBASE_GUIDE_zh-CN.md"
purpose: "供 AI 理解、审查、新增职业宏时作为单一事实来源；说明 ClassMacros 三表规则与 CreateMacro 展开逻辑"
---

# Fuyutsui `core/classmacros.lua`：AI 宏规则参考

> 本文描述如何在 `ClassMacros` 里声明职业宏，以及 `CreateMacro` 如何把它们展开成 SecureActionButton 覆盖绑定。  
> **改宏只改 `core/classmacros.lua`**，不要改 `class/*.lua`，也不要在别处硬编码职业宏列表。

## 0. 一句话定义

`classmacros.lua` 只提供数据表 `Fuyutsui.ClassMacros[classFile]`。  
运行时由 `main.lua:LoadPlayerMacros()` 按 `UnitClassBase("player")` 取出当前职业，再调用：

```lua
Fuyutsui:CreateMacro(m.dynamicSpells, m.staticSpells, m.specialSpells)
```

真正创建按钮、拼宏文本、绑定热键的逻辑在 `core/macro.lua`。

## 1. AI 必须遵守的规则

1. **键名必须是** `UnitClassBase` 返回值：`WARRIOR` / `PALADIN` / `HUNTER` / `ROGUE` / `PRIEST` / `DEATHKNIGHT` / `SHAMAN` / `MAGE` / `WARLOCK` / `MONK` / `DRUID` / `DEMONHUNTER` / `EVOKER`。
2. 每个职业表**必须有**三个字段：`dynamicSpells`、`staticSpells`、`specialSpells`（可为空表 `{}`）。
3. **需要点名队友/团队成员**的治疗、驱散、护盾等 → 放 `dynamicSpells`（数组，顺序敏感）。
4. **普通单体施法**（目标默认、或条件写在法术名里）→ 放 `staticSpells[index]`，只写法术/条件字符串，**不要**自己加 `/cast `。
5. **需要完整宏文本**（`/castsequence`、`/stopcasting`、`/cancelaura`、多行不以自动 `/cast` 开头等）→ 放 `specialSpells[index]`，**必须写完整命令**。
6. **同序号冲突时**：`specialSpells[index]` **优先于** `staticSpells[index]`；被覆盖的 static 可删掉，或留注释说明原意。
7. `staticSpells` / `specialSpells` 的 `index` 是**动态区之后的相对序号**，不是全局热键序号。
8. 战斗中 `InCombatLockdown()` 时无法创建/修改安全按钮；改宏后需在脱战后 `/reload` 或等下次非战斗加载生效。
9. 保持 Lua 5.1 / WoW 兼容；法术名用**本地化中文名**（与客户端一致），与现有表风格一致。
10. 不要格式化整个文件；只改目标职业段落。

## 2. 加载与消费链路

```text
classmacros.lua          定义 Fuyutsui.ClassMacros
        │
main.lua:LoadPlayerMacros()
        │  classFile = UnitClassBase("player")
        │  MacrosList = ClassMacros[classFile]
        ▼
macro.lua:CreateMacro(dynamic, static, special)
        │  按 macroKind[i] 生成按钮 s1..sN
        │  SetOverrideBindingClick + macrotext
        ▼
玩家按覆盖热键 → SecureActionButton 执行宏
```

触发时机：玩家数据加载路径会调用 `LoadPlayerMacros()`（见 `core/player.lua`）。专精切换等场景若重新加载宏，同样走此函数。

## 3. 热键池（`macroKind`）

`core/macro.lua` 用修饰键 × 基础键生成有序列表 `macroKind[1..N]`。

| 修饰键顺序 | 基础键（每组 39 个） |
|---|---|
| `CTRL` → `ALT` → `SHIFT` → `ALT-CTRL` → `ALT-SHIFT` → `CTRL-SHIFT` → `ALT-CTRL-SHIFT` | 小键盘 0–9 / 小数点 / ± / − / × / ÷；`F1–F3,F5–F12`（**无 F4**）；`, . / ; ' [ ] \`；`7 8 9 0 =` |

- 总槽位数 = `7 × 39 = 273`。
- 按钮名：`s1`、`s2`、…（与 `macroKind` 下标一致）。
- 人读对照表见 `Keymap.md`（ID 1 = `CTRL-NUMPAD1`，以此类推）。

**AI 改宏时**：关心的是 `dynamic` 占用多少槽、以及 static/special 的相对 `index`；不必手算全局热键，除非要核对外部程序按键映射。

## 4. 槽位分配算法（核心）

对每个热键下标 `i = 1 .. #macroKind`：

```text
dynamicSlots = #dynamicSpells * 30

if i <= dynamicSlots then
    -- 动态宏：按技能组 + raid 位展开（见 §5）
else
    index = i - dynamicSlots
    if specialSpells[index] then
        macroBody = specialSpells[index]          -- 原样使用
    elseif staticSpells[index] then
        macroBody = "/cast " .. staticSpells[index]  -- 自动加前缀
    end
end

若 macroBody 非空 → 创建/更新按钮 s{i}
```

因此：

| `dynamicSpells` 长度 | 动态占用槽 | static/special 的 `[1]` 对应全局 `i` |
|---:|---:|---:|
| 0 | 0 | 1 |
| 1 | 30 | 31 |
| 5 | 150 | 151 |
| 6 | 180 | 181 |
| 7 | 210 | 211 |

**新增或删除 `dynamicSpells` 条目会平移其后所有 static/special 的实际热键。** 改动态列表前必须评估这个偏移。

## 5. `dynamicSpells` 规则

### 5.1 数据结构

```lua
dynamicSpells = { "法术A", "法术B", "法术C" }  -- 数组，1-based，顺序 = 组号
```

- 用**纯法术名**（中文），不要写 `/cast`，不要写 `@raid`（展开逻辑会加）。
- 每组固定占 **30** 个连续热键，对应 `raid1` … `raid30`。

### 5.2 单组内 30 键展开

设组内相对位 `raidIdx = 1..30`，法术名为 `spell`：

| raidIdx | 生成的 `macrotext` |
|---:|---|
| 1 | `/cast [group:raid,@raid1]spell;[group:party,@player]spell;[nogroup,@player]spell` |
| 2..5 | `/cast [group:raid,@raidN]spell;[group:party,@party(N-1)]spell` |
| 6..30 | `/cast [group:raid,@raidN]spell` |

含义：

- 团队：始终 `@raid1` … `@raid30`。
- 小队：仅前 5 键有意义 → `@player`、`@party1`…`@party4`。
- 单人：仅第 1 键落到 `@player`。

### 5.3 何时放入 dynamic

适合：治疗术、驱散、护盾、急救类等**必须点名不同队友**的技能。

不适合：

- 只打当前目标 / 自身 / 鼠标指向 / 焦点 → 用 `staticSpells` + 条件前缀。
- 需要 sequence / stopcasting / cancelaura → 用 `specialSpells`。

### 5.4 现有职业占用（便于估算偏移）

| 职业键 | `#dynamicSpells` | 动态槽 | static `[1]` 的全局 `i` |
|---|---:|---:|---:|
| WARRIOR / HUNTER / ROGUE / DEATHKNIGHT / WARLOCK / DEMONHUNTER | 0 | 0 | 1 |
| MAGE | 1 | 30 | 31 |
| DRUID | 5 | 150 | 151 |
| PALADIN / PRIEST / SHAMAN / MONK | 6 | 180 | 181 |
| EVOKER | 7 | 210 | 211 |

## 6. `staticSpells` 规则

### 6.1 数据结构

```lua
staticSpells = {
    [1] = "英勇投掷",
    [5] = "[@mouseover]保护祝福",
    [20] = "[spec:2]圣洁鸣钟;[spec:3]灰烬觉醒",
    [37] = "item:241304\n/cast item:241305",  -- 会变成 /cast item:...\n/cast item:...
}
```

- **稀疏表**合法：缺号表示该相对序号不建宏。
- 值是**拼在 `/cast ` 后面的字符串**，不是完整宏（除非用 `\n` 续写后续行）。

### 6.2 常见写法（直接抄现有风格）

| 意图 | `staticSpells` 值示例 | 最终宏 |
|---|---|---|
| 默认目标施法 | `"审判"` | `/cast 审判` |
| 自身 | `"[@player]荣耀圣令"` | `/cast [@player]荣耀圣令` |
| 鼠标指向 | `"[@mouseover]破咒祝福"` | `/cast [@mouseover]破咒祝福` |
| 光标地面 | `"[@cursor]乱射"` | `/cast [@cursor]乱射` |
| 焦点优先 | `"[target=focus,exists] 窒息;窒息"` | `/cast [target=focus,exists] 窒息;窒息` |
| 专精分支 | `"[spec:2]圣洁鸣钟;[spec:3]灰烬觉醒"` | `/cast [spec:2]...` |
| 天赋已知 | `"[known:116844,@cursor]平心之环;[known:198898]赤精之歌"` | `/cast [known:...]...` |
| 姿态/形态 | `"[nostance:1]暗影形态"` | `/cast [nostance:1]暗影形态` |
| 双物品一行 | `"item:241304\n/cast item:241305"` | `/cast item:241304` + 第二行 `/cast item:241305` |

### 6.3 不要放进 static 的情况

若第一行**不能**以自动加的 `/cast ` 开头，或需要 `/castsequence`、`/stopcasting`、`/cancelaura` 等，改用 `specialSpells`。

## 7. `specialSpells` 规则

### 7.1 数据结构

```lua
specialSpells = {
    [36] = "/castsequence reset=0.3 真言术：耀,x",
    [39] = "/stopcasting",
    [17] = "/cancelaura [spec:4]猎豹形态\n/cast 万灵之召",
}
```

- 值是**完整 `macrotext`**，原样写入按钮，**不会**再加 `/cast `。
- `index` 与 `staticSpells` 共用同一套相对序号。
- 同 index 若 special 存在，static **被忽略**（可注释掉对应 static，如 DH 的恶魔变形 / 烈火烙印）。

### 7.2 典型用途

| 模式 | 示例（文件中已有） |
|---|---|
| castsequence + 哑元 | `"/castsequence reset=0.5 死亡之握,x"` |
| 停施法 | `"/stopcasting"` |
| 取消光环再施法 | `"/cancelaura [spec:4]猎豹形态\n/cast 万灵之召"` |

`castsequence ...,x` 中的 `x` 是占位，用于快速连按重置序列的常见写法；改此类宏时保持现有 reset 秒数风格，除非有明确需求。

## 8. 决策树：新技能放哪

```text
需要按队友/团队槽位点名？
  ├─ 是 → dynamicSpells（纯法术名；注意 +30 偏移）
  └─ 否 → 能否写成「/cast + 一段条件/法术名」？
            ├─ 是 → staticSpells[index]
            └─ 否 → specialSpells[index]（完整宏文本）
```

同 index 既要特殊宏又想保留备注：special 写生效文本，static 删掉或改成注释。

## 9. 修改检查清单（AI 改完自检）

1. 职业键是否为正确的 `UnitClassBase` 字符串？
2. 三个字段是否都在（空也要 `{}`）？
3. 若改了 `dynamicSpells` 长度：是否意识到 static/special 实际热键整体平移？
4. static 值是否**没有**多余的前导 `/cast `（双物品续行除外）？
5. special 值是否**自带** `/` 命令？
6. 同序号 special 是否故意覆盖 static？被覆盖项是否已注释说明？
7. 条件语法是否与现有条目一致（`[@unit]`、`[spec:N]`、`[known:id]`、`[group:...]`）？
8. 法术名是否与游戏客户端中文一致？
9. 提醒：战斗中不会更新安全按钮；需脱战 `/reload` 验证。

## 10. 最小示例

### 10.1 无动态（近战输出职业常见）

```lua
WARRIOR = {
    dynamicSpells = {},
    specialSpells = {},
    staticSpells = {
        [1] = "英勇投掷",
        [38] = "拳击",
        [39] = "[@focus]拳击",
    },
}
```

- `[1]` → 全局 `i=1` → `/cast 英勇投掷`
- `[39]` → `/cast [@focus]拳击`

### 10.2 有动态 + special 覆盖

```lua
PRIEST = {
    dynamicSpells = { "苦修", "快速治疗", "真言术：盾", "愈合祷言", "纯净术", "圣言术：静" },
    -- 动态占 180 槽；此后 index 1 → 全局 i=181
    specialSpells = {
        [36] = "/castsequence reset=0.3 真言术：耀,x",
        [39] = "/stopcasting",
    },
    staticSpells = {
        [1] = "心灵震爆",
        [37] = "item:241304\n/cast item:241305",
        -- [36] / [39] 由 special 接管，不必在 static 重复
    },
}
```

## 11. 与本文相关、但不要在这里改的东西

| 文件 | 职责 | 改宏时 |
|---|---|---|
| `core/macro.lua` | 热键表、展开算法、安全按钮 | 仅当要改分配规则/键池时才动 |
| `main.lua` | `LoadPlayerMacros` 选职业表 | 一般不动 |
| `Keymap.md` | 人读热键 ID 对照 | 键池变更时同步 |
| `core/keybinds.lua` / `config.lua` keymap | 动作条扫描 → 像素协议 | **另一套**按键编码，与 ClassMacros 覆盖绑定无关 |
| `class/*.lua` | ClassBlocks 色块 | 不放宏 |

## 12. 常见错误

| 错误 | 后果 |
|---|---|
| static 写成 `"/cast 火球术"` | 实际变成 `/cast /cast 火球术` |
| special 只写 `"火球术"` | 无 `/` 命令，宏无效 |
| 把点名治疗放进 static | 无法按 raid/party 槽位点名 |
| 在 `dynamicSpells` 中间插入技能却不评估偏移 | 后续所有按键语义错位 |
| 用错职业键（如 `DeathKnight`） | `LoadPlayerMacros` 取不到表，宏不创建 |
| 假设战斗中改表立即生效 | `InCombatLockdown` 直接 return，按钮不更新 |
