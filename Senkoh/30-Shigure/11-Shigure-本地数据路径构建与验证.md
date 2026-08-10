---
title: Shigure 本地数据、路径、构建与验证
summary: 汇总业务根目录解析、config/keymap/module/cache 与插件路径、项目构建资产、验证方法和当前无自动化测试的事实。
aliases:
  - Shigure 数据目录
  - Shigure 构建验证
tags:
  - project/shigure
  - doc/feature
  - area/build
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[docs/30-Shigure/00-Shigure-MOC]]"
related:
  - "[[docs/30-Shigure/01-Shigure-启动随机副本与会话协调]]"
  - "[[Shigure/打包说明]]"
source_files:
  - Shingen.slnx
  - Shigure/Shigure.csproj
  - Shigure/Shigure.slnx
  - Shigure/Infrastructure/AppPaths.cs
  - Shigure/Infrastructure/ConfigService.cs
  - Shigure/Input/KeymapCatalog.cs
  - Shigure/Modules/ModuleStore.cs
  - Shigure/Infrastructure/UiCacheStore.cs
  - Shigure/Infrastructure/WowAddonLocator.cs
  - .gitignore
source_symbols:
  - AppPaths.BaseDirectory
  - ConfigService.ResolveConfigPath
  - KeymapCatalog.ResolveKeymapFilePath
  - ModuleStore
  - UiCacheStore
  - WowAddonLocator.FindAddonRoot
verified_at: 2026-08-09
---

# Shigure 本地数据、路径、构建与验证

> [!abstract] AI 快速摘要
> Shigure 的业务根目录通常是正式 EXE 所在目录；随机副本子进程通过环境变量仍指回该目录，而不是临时 EXE 目录。程序从这里读取 `config`、`keymap`、`module` 和 `cache`，再通过游戏进程路径回溯定位外部 Fuyutsui Lua。项目是无第三方 NuGet 引用的 `net10.0-windows` WinForms WinExe。仓库没有测试项目，可靠验证需要源码静态核对、`dotnet build` 和 Windows 下的端到端手工协议测试。

## 图谱位置

- 上级：[[docs/30-Shigure/00-Shigure-MOC]]
- 启动/随机副本：[[docs/30-Shigure/01-Shigure-启动随机副本与会话协调]]
- 打包资料：[[Shigure/打包说明]]
- 系统边界：[[docs/10-系统/00-Shingen-双项目系统全景]]

## 范围与非范围

本页是磁盘与验证地图，不定义每种 JSON/Lua 的完整业务语义。文档核对只以手写源码和业务数据为权威，明确排除 `bin/`、`obj/` 等生成物。

## 基目录解析

```text
正常正式进程
  AppPaths.BaseDirectory = AppContext.BaseDirectory

随机副本子进程
  AppPaths.BaseDirectory = 父进程写入的原始目录环境变量
  AppContext.BaseDirectory = 随机临时目录（仅运行依赖）
```

这使随机 EXE 可以加载临时目录 DLL，同时配置和用户数据仍落在正式安装目录。环境变量属于信任边界：若外部预先设置/篡改，可能改变业务数据根目录。

## 本地数据地图

| 位置 | 内容/所有者 | 读取者 | 写入者 |
|---|---|---|---|
| `config/common.json` | 协议固定字段 | `ConfigService` | 打包内容/人工维护 |
| `config/<class>.json` | 13 职业和专精步骤映射、法术元数据 | `ConfigService`, `StateBuilder` | `FuyutsuiConfigConverter` |
| `keymap/*.json` | unit/spell/macroCondition→hotkey | `KeymapService`, `KeymapCatalog` | `FuyutsuiKeymapConverter` |
| `module/**/*.json` | 匹配、规则、动态值和公式 | `ModuleStore` | 模块编辑器/人工 |
| `cache/` | 窗口和 UI 偏好 | UI 基础设施 | MainForm/各 UI |
| Fuyutsui `class/*.lua` | ClassBlocks 源数据 | ClassBlocks Store/转换器 | 插件与 ClassBlocks 编辑器 |
| Fuyutsui ClassMacros Lua | 宏源数据 | ClassMacros Store/转换器 | 插件数据与宏编辑器 |
| 专属系统临时根 | 随机 EXE 和顶层运行依赖 | OS loader | `RandomizedExecutableLauncher` |

模块目录支持递归 JSON；保存的新模块使用清洗后的顶层文件名。Keymap 相对路径基于 Keymap 目录，绝对路径也被接受；`.yml`/`.yaml` 会规范化为 `.json`，缺失时回退默认 `keymap.json`。

## 外部插件路径回溯

插件根不是相对 `AppPaths` 固定计算，而是：窗口标题 → HWND → PID → 进程 EXE 路径 → 沿父目录逐级寻找 `Interface/AddOns/Fuyutsui`。因此便携安装和不同游戏目录可工作，但目标窗口标题、进程查询权限和标准 WoW 子目录结构都必须成立。

## 项目和构建资产

- 仓库根解决方案是 `Shingen.slnx`，从总根运行 `dotnet build .\Shingen.slnx`；`Shigure/Shigure.slnx` 继续留给组件目录和一键打包脚本使用。
- 输出类型：`WinExe`。
- Target Framework：`net10.0-windows`。
- UI：`UseWindowsForms=true`，入口为 STA。
- 开启 nullable 与 implicit usings。
- 应用版本在项目文件中为 1.2.1，并被模块编辑器保存到模块元数据。
- 项目没有 `PackageReference`；核心只依赖 .NET/WinForms/System.Drawing 和 Win32 P/Invoke。
- 项目文件声明嵌入 UI assets，并把配置、Keymap、模块等运行数据按规则复制到输出。
- 实际打包策略还应对照 [[Shigure/打包说明]]；随机启动器只复制正式输出目录**顶层**运行依赖到临时目录。

## 当前验证能力

仓库中没有单元测试、集成测试或测试项目；CLAUDE 也明确这一点。推荐验证层次：

1. **静态核对**：只检查源码、项目文件和受版本控制的数据，排除 `bin/obj`。
2. **构建**：在已安装 .NET 10 Windows SDK 的 Windows 环境，从仓库根运行 `dotnet build .\Shingen.slnx`。
3. **数据健全性**：确认 common + 13 职业 JSON 齐全、模块 JSON 可加载、Keymap 不超过/完整覆盖 273 位置、ClassBlocks 最终步骤不超过 510。
4. **端到端扫描**：打开 Fuyutsui 和目标游戏窗口，检查步骤 1、职业/专精、计数条和六行吸收。
5. **决策/输出**：用无风险场景检查模块匹配、规则短路、三触发模式、延迟以及目标窗口消息权限。
6. **round-trip 备份验证**：对 Lua 编辑前先备份，比较保存前后目标 table 和生成 JSON，特别检查 legacy/mixed 格式。

没有测试项目意味着“构建成功”不能证明跨项目协议正确；像素颜色、步骤顺序、273 键池和单位编号都需要生产者/消费者联合验证。

## 安全与信任边界

- 模块、config、Keymap 和 Lua 都是受信任的本地输入；解析器不执行 C# 或 Lua 代码，但错误数据可以改变决策和直接 Hotkey 输出。
- 扫描器和发送器仅以标题识别窗口，同标题进程可能被读取或接收消息。
- 屏幕捕获读取可见桌面像素，可能受遮挡/覆盖影响。
- Lua Store 和批量转换器缺少完整事务/自动备份；重要文件应外部版本控制或备份。
- 临时目录清理会递归删除按源 EXE 路径哈希隔离的专属根内容；修改路径算法时必须重新审计删除边界。
- 技术实现本身不能证明第三方服务条款允许自动化；风险说明应以 [[Shigure/README]] 的免责声明为准，而非法律结论。

## 失败模式与排障

| 症状 | 优先检查 |
|---|---|
| 正式目录有配置但随机进程找不到 | 原始目录环境变量和 `AppPaths.BaseDirectory` |
| 发布后缺 DLL/数据 | csproj CopyToOutput 规则与随机启动器顶层复制清单 |
| 配置模式意外回退 | `config/` 是否存在；存在即要求全套 13 职业，否则不会回退单文件 |
| 指定 Keymap 未生效 | 相对/绝对解析、扩展名改写、默认回退和当前专精 |
| 模块突然消失 | 单文件 JSON 解析失败被静默跳过 |
| 找不到 Fuyutsui | 窗口/PID 查询和祖先目录标准路径 |
| 文档与输出 DLL 行为冲突 | 生成物可能陈旧；以当前源码重新构建验证 |

## 修改影响

- 改 `AppPaths` 或随机环境变量会影响所有本地数据；先做迁移/兼容，再改启动器。
- 新增数据目录必须决定是否随构建复制、是否属于用户可写数据、是否由随机副本读取原目录。
- 引入 NuGet 或额外 native 文件要同步 csproj、发布说明和随机复制清单。
- 增加测试时优先覆盖纯逻辑组件：条件/公式、迁移、Keymap 转换、Lua round-trip；屏幕和 Win32 层可用端口接口隔离。

## 源码索引

- `Shigure/Shigure.csproj:3-26`：框架、WinForms、版本、assets 与数据复制。
- `Shigure/Infrastructure/AppPaths.cs:5-25`：正式/随机进程基目录。
- `Shigure/Infrastructure/ConfigService.cs:25-88`：config 目录与 13 职业要求。
- `Shigure/Input/KeymapCatalog.cs:125-145`：Keymap 路径与回退。
- `Shigure/Modules/ModuleStore.cs:281-443`、`634-650`：模块目录、文件生命周期和路径边界。
- `Shigure/Infrastructure/WowAddonLocator.cs:10-130`：插件路径回溯。
- `Shigure/App/RandomizedExecutableLauncher.cs:69-173`：临时清理和复制清单。

## 知识图谱链接

- 启动路径：[[docs/30-Shigure/01-Shigure-启动随机副本与会话协调]]
- 数据同步：[[docs/30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]
- 跨项目契约：[[docs/40-跨项目/00-Shingen-跨项目契约-MOC]]
- 原始项目资料：[[Shigure/README]]、[[Shigure/CLAUDE]]、[[Shigure/打包说明]]
