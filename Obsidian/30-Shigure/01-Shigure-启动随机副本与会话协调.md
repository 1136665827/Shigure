---
title: Shigure 启动、随机副本与会话协调
summary: 解释 Windows 入口、参数解析、随机临时可执行文件、手工依赖装配和可取消运行会话的完整生命周期。
aliases:
  - Shigure 启动流程
  - RuntimeSessionCoordinator
tags:
  - project/shigure
  - doc/feature
  - area/lifecycle
project: Shigure
doc_type: feature
status: current
authority: source-derived
up: "[[30-Shigure/00-Shigure-MOC]]"
related:
  - "[[30-Shigure/04-Shigure-运行循环触发模式与快照]]"
  - "[[30-Shigure/11-Shigure-本地数据路径构建与验证]]"
source_files:
  - App/Program.cs
  - App/AppOptions.cs
  - App/RandomizedExecutableLauncher.cs
  - App/RuntimeSessionCoordinator.cs
  - App/ShigureRuntimeFactory.cs
  - Infrastructure/AppPaths.cs
  - Infrastructure/WowProcessLocator.cs
source_symbols:
  - Program.Main
  - AppOptions.FromArgs
  - RandomizedExecutableLauncher.TryRelaunch
  - RuntimeSessionCoordinator.RestartAsync
  - ShigureRuntimeFactory.Create
verified_at: 2026-08-10
---

# Shigure 启动、随机副本与会话协调

> [!abstract] AI 快速摘要
> `Program.Main` 在 STA 线程中启动 WinForms。首次从正式目录运行时，程序把自身及顶层运行依赖复制到专属临时目录，用随机文件名重启；子进程仍通过环境变量把业务数据根目录指回原目录。UI 不直接拥有后台任务，而是通过 `RuntimeSessionCoordinator` 串行地停止旧会话、构造新 `ShigureRuntime` 并转发带会话 ID 的事件。

## 图谱位置

- 上级：[[30-Shigure/00-Shigure-MOC]]
- 相关运行循环：[[30-Shigure/04-Shigure-运行循环触发模式与快照]]
- 相关路径规则：[[30-Shigure/11-Shigure-本地数据路径构建与验证]]

## 范围与非范围

本页覆盖进程入口、命令行、随机副本、对象装配、启动/重启/停止和关闭顺序。不展开扫描协议、规则求值或 UI 控件布局；它们分别见像素、运行循环和 UI 页面。

## 输入与输出

| 输入 | 处理 | 输出 |
|---|---|---|
| 命令行参数 | `AppOptions.FromArgs` | 触发键、模式、模块、逻辑/渲染间隔 |
| `wow_process.txt` | `WowProcessLocator` | 每次查询时的目标进程名和最靠前候选可见窗口 |
| 当前可执行文件和环境变量 | `TryRelaunch` / `AppPaths` | 随机名称子进程，以及稳定的业务数据根目录 |
| UI 的启动/重启/停止请求 | `RuntimeSessionCoordinator` | 唯一活动会话、快照事件、失败/停止事件 |
| 共享 `ModuleStore` 和触发键读取器 | `ShigureRuntimeFactory` | 每个会话一套配置、Keymap、扫描、状态、逻辑和输出对象 |

支持的选项以源码为准：`--toggle`、`--mode`、`--module`、`--logic-ms`、`--render-ms`。`--window` 已移除；目标进程改由 `wow_process.txt` 配置。模式不识别时回退到 `Switch`；逻辑间隔最低 50 ms，渲染间隔最低 100 ms。

## 执行链路

1. `Program.Main` 检查 Windows，调用 WinForms 初始化，并先尝试随机副本重启。
2. 父进程计算基于源 EXE 完整路径 SHA-256 前 16 位的临时根目录，清理其中旧内容，创建随机目录和随机 EXE 名。
3. 它只复制应用目录顶层的 EXE、DLL、PDB、`.deps.json`、`.runtimeconfig.json`，给新 EXE 追加随机标记，然后转发原参数启动。
4. 子进程通过 `SHIGURE_RANDOMIZED_PROCESS=1` 避免再次重启，并通过原始目录环境变量让 `AppPaths.BaseDirectory` 指回正式数据目录。
5. `Program.Main` 手工构造共享存储、触发键读取器、`WowProcessLocator`、运行时工厂、协调器和 `MainForm`。项目没有 DI 容器。
6. UI 请求运行时，协调器递增 request version，在 `SemaphoreSlim` 下停止旧会话，然后 `Task.Run` 新会话。
7. 新请求比旧请求晚到时，以 request version 实现“最新请求获胜”；旧事件还会被 UI 的 session ID 过滤。
8. 停止时取消令牌、等待任务结束、退订事件并释放运行时；窗口关闭还会等待待处理配置同步。

## 核心数据与不变量

- 随机子进程**不复制业务数据目录**；`config`、`keymap`、`cache` 仍由原始根目录提供，模块由我的文档目录 `{MyDocuments}/Shigure/module` 提供。
- `SHIGURE_RANDOMIZED_PROCESS=1` 是防递归开关。外部若伪造相关环境变量，也会改变启动和路径行为。
- 清理范围是按源 EXE 路径哈希隔离的专属临时根目录；清理会递归删除其所有子项并吞掉 IO/权限异常。
- 每次会话构造新的 `ConfigService`、`KeymapService`、`PixelScanner`、`StateBuilder`、`KeySender` 和 `LogicRegistry`；`ModuleStore` 与触发键读取器跨会话共享。
- 会话状态有两种概念：`HasSession` 可在任务已结束但尚未清理时仍为真，`IsRunning` 才表示任务正在运行。
- 协调器序列化生命周期操作；运行时内部的数据所有权见 [[30-Shigure/04-Shigure-运行循环触发模式与快照]]。

## 失败模式与排障

| 症状 | 可能原因 | 检查入口 |
|---|---|---|
| 没有生成随机副本 | 非 Windows、当前文件不是 `.exe`、已是子进程或复制/启动失败 | `TryRelaunch` 的返回和异常处理 |
| 子进程找不到配置 | 原始目录环境变量缺失或被外部覆盖 | `AppPaths.BaseDirectory` |
| 重启后仍显示旧状态 | 新旧会话事件交错 | 协调器 request version 与 UI session ID 过滤 |
| 关闭窗口卡住 | 运行时停止或配置更新队列尚未完成 | `MainForm` 关闭逻辑和 `StopCurrentLockedAsync` |
| 模块文件改了但会话没看到 | 会话创建时才重新加载模块 | 通过 UI 重启会话 |

## 修改影响

- 改随机目录或复制清单，会影响打包、单文件发布、依赖加载和安全清理边界，必须同步 [[30-Shigure/11-Shigure-本地数据路径构建与验证]]。
- 新增运行时依赖，应先在 `RuntimeDependencies` 定义窄接口，再由 `ShigureRuntimeFactory` 组装；不要让 UI 直接构造后台细节。
- 改协调器时必须保留三层防陈旧机制：生命周期互斥、request version 最新获胜、UI session ID 过滤。
- 改关闭顺序要同时检查配置同步队列，见 [[30-Shigure/09-Shigure-Fuyutsui配置宏编辑与同步]]。

## 源码索引

- `App/Program.cs:5-43`：Windows/STA 入口和完整对象图。
- `App/AppOptions.cs:3-72`：模式、默认值、参数解析和间隔下限。
- `App/RandomizedExecutableLauncher.cs:8-67`：父/子进程判定和重启。
- `App/RandomizedExecutableLauncher.cs:69-173`：旧临时内容清理、随机路径与复制清单。
- `App/RandomizedExecutableLauncher.cs:245-253`：追加随机标记。
- `Infrastructure/AppPaths.cs:5-25`：原始业务根目录恢复。
- `Infrastructure/WowProcessLocator.cs`：运行期目标进程配置和窗口选择。
- `App/ShigureRuntimeFactory.cs:27-40`：每会话依赖装配。
- `App/RuntimeSessionCoordinator.cs:106-238`：串行重启、取消、事件和清理。

## 知识图谱链接

- 上游应用入口：[[30-Shigure/00-Shigure-MOC]]
- 下游主循环：[[30-Shigure/04-Shigure-运行循环触发模式与快照]]
- 相关 UI 生命周期：[[30-Shigure/10-Shigure-UI功能地图与数据所有权]]
- 原始说明：`README.md`、`CLAUDE.md`
