---
title: Shigure 功能地图
summary: Shigure C# WinForms 项目的源码级入口、运行数据流、功能文档导航与跨项目契约索引。
aliases:
  - Shigure MOC
  - Shigure 源码地图
tags:
  - project/shigure
  - doc/moc
  - area/architecture
project: Shigure
doc_type: moc
status: current
authority: source-derived
up: "[[docs/00-导航/00-Shingen-知识库首页]]"
related:
  - "[[docs/10-系统/00-Shingen-双项目系统全景]]"
  - "[[docs/40-跨项目/00-Shingen-跨项目契约-MOC]]"
  - "[[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]"
source_files:
  - Shigure/App/Program.cs
  - Shigure/App/ShigureRuntimeFactory.cs
  - Shigure/Runtime/ShigureRuntime.cs
  - Shigure/UI/MainForm.cs
  - Shigure/Shigure.csproj
source_symbols:
  - Program.Main
  - ShigureRuntimeFactory.Create
  - ShigureRuntime.RunAsync
  - MainForm
verified_at: 2026-08-09
---

# Shigure 功能地图

> [!abstract] AI 快速摘要
> Shigure 是一个仅面向 Windows 的 .NET 10 WinForms 消费端。它从魔兽世界客户区顶端截取 Fuyutsui 输出的精确 RGB 像素，转换成 `GameState`，按模块、条件、动态单位和 Keymap 选出动作，再以 `PostMessage` 向标题完全匹配的窗口发送按键。项目使用手工构造器注入，没有第三方 NuGet 依赖，也没有测试项目；运行时事实应以源码和本知识库为准，而不是只依赖旧 README/CLAUDE。

## 图谱位置

- 上级：[[docs/00-导航/00-Shingen-知识库首页]]、[[docs/10-系统/00-Shingen-双项目系统全景]]
- 跨项目入口：[[docs/40-跨项目/00-Shingen-跨项目契约-MOC]]
- 插件入口：[[docs/20-Fuyutsui/00-Fuyutsui-MOC]]
- 项目原始说明：[[Shigure/README]]、[[Shigure/CLAUDE]]、[[Shigure/打包说明]]

## 一条完整执行链

```text
Program.Main
  -> 随机副本重启 / AppPaths 基目录
  -> MainForm + RuntimeSessionCoordinator
  -> ShigureRuntime
  -> PixelScanner 截屏并解码 1..510
  -> StateBuilder 构建 GameState
  -> LogicRegistry / ModuleLogic 求值
  -> KeymapService 解析动作
  -> KeySender.PostMessage
```

插件与程序之间不是 API 调用关系，而是三个文件/像素契约：

1. 实时状态：[[docs/40-跨项目/01-Shingen-像素生产消费契约]]。
2. 状态布局：[[docs/40-跨项目/02-Shingen-ClassBlocks到config同步契约]]。
3. 宏与按键：[[docs/40-跨项目/03-Shingen-ClassMacros到keymap与按键契约]]。

## 按功能阅读

| 页面 | 回答的问题 | 稳定入口 |
|---|---|---|
| [[docs/30-Shigure/01-Shigure-启动随机副本与会话协调]] | 程序如何启动、为什么复制自身、运行时如何重启和停止 | `Program.Main`, `RuntimeSessionCoordinator` |
| [[docs/30-Shigure/02-Shigure-像素扫描与协议解码]] | 屏幕像素如何成为行数据、动作条数据和治疗吸收数据 | `PixelScanner.ScanScreenData` |
| [[docs/30-Shigure/03-Shigure-配置合并与GameState构建]] | `config` 如何合并，步骤号如何映射为强类型状态 | `ConfigService`, `StateBuilder.Build` |
| [[docs/30-Shigure/04-Shigure-运行循环触发模式与快照]] | Switch/Click/Hold 的语义、节流与 UI 快照如何工作 | `ShigureRuntime.RunAsync` |
| [[docs/30-Shigure/05-Shigure-模块存储匹配与版本迁移]] | 模块 JSON 如何加载、选择、保存和迁移 | `ModuleStore` |
| [[docs/30-Shigure/06-Shigure-规则条件与特殊动作]] | 规则如何短路、条件语法有哪些限制、特殊动作如何处理 | `ModuleLogic.Evaluate` |
| [[docs/30-Shigure/07-Shigure-动态单位数量与公式]] | 动态目标、统计和数值调整按什么顺序求值 | `UnitSelector`, `FormulaEvaluator` |
| [[docs/30-Shigure/08-Shigure-Keymap解析与按键发送]] | 单位/法术/宏条件怎样变成 Windows 消息 | `KeymapService`, `KeySender` |
| [[docs/30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]] | Lua 子集怎样 round-trip，配置和键位怎样重新生成 | `ClassBlocksStore`, `ClassMacrosStore`, converters |
| [[docs/30-Shigure/10-Shigure-UI功能地图与数据所有权]] | 各 WinForms 页面拥有或修改什么数据 | `MainForm`, `StatusForm`, editors |
| [[docs/30-Shigure/11-Shigure-本地数据路径构建与验证]] | 数据从哪里读取、如何打包构建、目前怎样验证 | `AppPaths`, `Shigure.csproj` |

## 核心数据流与所有权

| 数据 | 权威生产者 | Shigure 内的读取者 | 持久化位置 |
|---|---|---|---|
| 实时战斗状态 | Fuyutsui 像素输出 | `PixelScanner` | 不持久化 |
| 状态步骤映射 | ClassBlocks Lua，经转换器生成 | `ConfigService` / `StateBuilder` | `config/*.json` |
| 法术宏到键位 | ClassMacros Lua，经转换器生成 | `KeymapService` | `keymap/*.json` |
| 行为规则 | 模块编辑器或手工 JSON | `ModuleStore` / `ModuleLogic` | `module/*.json` |
| UI 缓存与偏好 | WinForms 界面 | WinForms 界面 | `cache/` |
| 运行状态 | `ShigureRuntime` 单循环 | `StatusForm` | `RenderSnapshot`，不持久化 |

## 必须记住的不变量

- 顶行协议有效步骤为 `1..510`；步骤 1 是锚点，2/3 是职业/专精。
- `ModuleDefinition.CurrentUnitMappingVersion` 当前是 **3**；README 曾写 2，已于 2026-08-09 修正，旧副本仍需警惕该历史值。
- 扫描器和按键发送器都只以**完全相同的窗口标题**定位目标，没有进程身份绑定。
- 模块、条件和公式都是受限数据解释器，不执行 C#；模块仍可直接指定要发送的 Hotkey。
- 主运行状态由 `ShigureRuntime` 的单一循环拥有；UI 命令通过并发队列进入该循环。
- `module.Enabled` 当前不参与运行时模块筛选；只有规则和数值调整的 `Enabled` 被执行器检查。
- 配置转换、Keymap 转换和 Lua 表重写不是跨文件事务；中途失败可能留下部分更新。

## 原始说明与源码的已知偏差

| 旧说明 | 源码事实 |
|---|---|
| README 曾写 `UnitMappingVersion = 2`（2026-08-09 已修正） | 当前常量为 3，并包含 2→3 的通道宏条件迁移；不要从旧副本恢复旧值。 |
| README 概括“缺失/非数字比较为 false” | 配置中存在但像素缺失的数值已在 `StateBuilder` 中变为 0，随后可能满足 `< 50`。真正无法解析的值才按 false 处理。 |
| README 概括组员统计都会跳过角色 0/生命 0 | 各选择器约束不同；光环、角色、驱散、吸收路径没有统一执行这两个过滤。 |
| README 把旧稀疏 ClassBlocks 称为只读 | Store 实际对旧专精返回空数据并拒绝保存，不能视作完整旧格式查看器。 |
| README 参数列表未列 `--module` | `AppOptions.Parse` 支持该参数。 |
| 模块 JSON 有 `Enabled` | 当前匹配逻辑不读取它，不能用于停用整个模块。 |
| README 支持规则 `Hotkey`/`Step` | 执行器支持，但模块编辑器保存时会把这两项写为空，手工值会丢失。 |
| CLAUDE 断言完全符合服务条款 | 源码只能证明其技术行为，无法证明第三方条款合规；README 的风险提示更符合事实边界。 |

## 修改导航

- 改像素颜色、步骤上限或吸收网格：同时查看 [[docs/30-Shigure/02-Shigure-像素扫描与协议解码]] 和 [[docs/40-跨项目/01-Shingen-像素生产消费契约]]。
- 改 Fuyutsui 状态字段：同时查看 [[docs/30-Shigure/03-Shigure-配置合并与GameState构建]] 和 [[docs/30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]。
- 改单位编号或宏条件：同时查看 [[docs/30-Shigure/05-Shigure-模块存储匹配与版本迁移]]、[[docs/30-Shigure/08-Shigure-Keymap解析与按键发送]] 和跨项目宏契约。
- 改并发或关闭流程：先看 [[docs/30-Shigure/01-Shigure-启动随机副本与会话协调]]、[[docs/30-Shigure/04-Shigure-运行循环触发模式与快照]]、[[docs/30-Shigure/10-Shigure-UI功能地图与数据所有权]]。

## 源码索引

- `Shigure/App/Program.cs:5-43`：进程入口与对象图。
- `Shigure/App/ShigureRuntimeFactory.cs:27-40`：每个运行会话的依赖装配。
- `Shigure/Runtime/RuntimeDependencies.cs:3-54`：扫描、状态、逻辑、输出和触发端口。
- `Shigure/Runtime/ShigureRuntime.cs:92-328`：主循环、触发与决策发送。
- `Shigure/UI/MainForm.cs:74-194`：UI 组合、启动和关闭。
- `Shigure/Shigure.csproj:3-26`：框架、资源和复制规则。

## 知识图谱链接

- 上游总览：[[docs/10-系统/00-Shingen-双项目系统全景]]
- 实时数据生产者入口：[[docs/20-Fuyutsui/03-Fuyutsui-状态块与编码入口]]
- 生产者细节：[[docs/20-Fuyutsui/02-Fuyutsui-事件与刷新调度]]
- 相关原始资料：[[Shigure/README]]、[[Shigure/CLAUDE]]
