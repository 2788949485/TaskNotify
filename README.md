# TaskNotify

> **A lightweight Windows desktop app that automatically detects long-running development tasks and notifies you via Windows toast and (optionally) email when they finish, fail, or need your attention.**
>
> 轻量级 Windows 桌面应用:自动检测长时间运行的开发任务,在完成、失败、超时或需要确认时通过 Windows 通知(可选邮件)提醒你。

---

## 目录 / Table of Contents

- [功能特性](#功能特性)
- [架构总览](#架构总览)
- [快速开始](#快速开始)
- [集成接入](#集成接入)
- [通知系统](#通知系统)
- [邮件通知](#邮件通知)
- [检测规则与配置](#检测规则与配置)
- [隐私](#隐私)
- [开发指南](#开发指南)
- [常见问题](#常见问题)

---

## 功能特性

### 检测 / Detection
- **WMI 进程监控** — 通过 Windows WMI 实时捕获进程启动/退出
- **快照补偿** — 15 秒轮询兜底,防止 WMI 事件丢失
- **父子进程聚合** — 把相关进程(如 `python.exe` → `ffmpeg.exe`)归并为单个任务
- **PID 复用保护** — 通过 `ProcessIdentity` 防止复用 PID 导致的任务串扰
- **三种检测模式** — `Precise` / `Balanced` / `Broad`,扫描范围逐级放宽
- **WezTerm / Alacritty 支持** — 终端父进程正则覆盖现代终端

### 集成 / Integrations
- **Claude Code** — 官方 Hook 集成,接收 SessionStart / Stop / StopFailure / PermissionRequest / Notification 事件
- **OpenAI Codex** — Hook 转发每轮完成与权限请求
- **PowerShell** — Profile 模块追踪命令生命周期
- **VS Code** — 扩展转发 Task 与终端事件(默认关闭)
- **Hermes Agent** — 直接 IPC 集成 AI Agent 任务

### 通知 / Notifications
- **Windows 原生 Toast** — 使用 `Microsoft.WindowsAppSDK` 的 AppNotification API
- **事件合并** — 同一任务在合并窗口(默认 5 秒)内的多个事件合并成一条通知,只保留最高优先级
- **冷却抑制** — 同一任务冷却期内(默认 10 秒)不重复打扰;`WaitingForPermission` 永远绕过冷却
- **状态化按钮** — 通知携带上下文按钮(打开项目 / 查看日志 / 复制错误 / 忽略此程序 / 以后总是提醒 …),按任务状态智能挑选
- **一键学习** — 按钮直接落库为用户规则,下次同类型任务自动按你的偏好处理
- **邮件并行** — 可选启用 SMTP,完成事件同时发送 HTML 邮件

### 隐私 / Privacy
- **纯本地** — 无云端同步、无遥测、无远程上报
- **命令脱敏** — 自动遮蔽密码、Token、API Key 等敏感串后才落库
- **SMTP 密码加密** — 邮箱密码使用 DPAPI(`CurrentUser` 范围)加密,仅当前 Windows 账户可解密
- **不读取屏幕** — 不截图、不读剪贴板、不录键盘
- **默认拒绝** — 默认忽略系统进程、IDE 语言服务器、Electron 渲染器

---

## 架构总览

```
TaskNotify.Desktop (WPF / 托盘 / 通知 / 邮件)
    │
    ├── TaskMonitorService
    │   ├── WmiProcessMonitor        (WMI 启停追踪)
    │   ├── SnapshotProcessMonitor   (15s 轮询兜底)
    │   └── IntegrationEventListener (Named Pipe 服务端)
    │
    ├── ProcessTaskTracker           (核心:状态机 + 评分)
    ├── NotificationDispatcher       (合并 → 冷却 → 分发)
    │   ├── NotificationMerger       (同任务事件合并)
    │   ├── NotificationCooldown     (冷却抑制)
    │   ├── NotificationBuilder      (Toast 内容 + 按钮)
    │   ├── NotificationActionHandlers (按钮动作派发)
    │   └── EmailNotifier            (可选 SMTP 后台通道)
    ├── LearningActions              (用户规则持久化)
    └── TrayIconService              (托盘图标 + 点击)

TaskNotify.Core
    ├── Tasks/                       (DetectedTask、状态机、TaskCompletionNotice)
    ├── Detection/                   (规则引擎、命令脱敏、检测模式)
    ├── Notifications/               (NotificationMerger / Cooldown / Priority — 纯逻辑)
    ├── Learning/                    (LearningActions)
    ├── Events/                      (进程事件 / 集成事件)
    ├── Performance/、Recovery/      (容量保护 / 任务恢复)

TaskNotify.Infrastructure
    ├── Settings/                    (AppSettings + JsonSettingsStore)
    ├── Email/                       (EmailSettings + EmailPasswordProtector + EmailSettingsStore)
    ├── Repositories/                (SQLite 仓储实现)
    ├── Database/                    (Schema 迁移 + 嵌入式 SQL)
    ├── Logging/                     (文件日志)
    └── SystemInfo/                  (系统启动时间提供者)

TaskNotify.ProcessMonitor           (WMI + 快照,平台相关)
TaskNotify.Ipc                      (Named Pipe 协议:消息、客户端、服务端)
TaskNotify.Integrations
    ├── Claude/                     (Client + SettingsManager + hook-receiver.js)
    ├── Codex/                      (Client + SettingsManager + hook-receiver.js)
    ├── Hermes/                     (Client + SettingsManager + hook-receiver.js)
    └── PowerShell/                 (TaskNotify.psm1 + ProfileInstaller)
```

**架构边界**
- `Core` 不依赖 WPF / Windows SDK / WMI / 外部 API
- `ProcessMonitor` 不含 UI 与通知逻辑
- `Ipc` 纯协议定义,平台无关
- `Infrastructure` 仅依赖 `Core`(SQLite / JSON / 邮件)
- `Integrations` 仅依赖 `Core` + `Ipc`
- `Desktop` 是表现层,依赖所有下层

---

## 快速开始

### 前置要求
- Windows 10 / 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (可选) [Node.js](https://nodejs.org/) — 用于 Claude / Codex / Hermes 的 Hook 接收器

### 构建运行

```powershell
git clone <repository-url>
cd TaskNotify
dotnet build
dotnet run --project src/TaskNotify.Desktop
```

应用启动后会驻留在系统托盘,立即开始监控。

### 基本用法

直接运行你的命令即可,无需任何包装:

```powershell
python train_model.py
npm run build
ffmpeg -i input.mp4 output.avi
```

任务结束(并满足最短通知时长)后,Windows 通知会自动弹出。

---

## 集成接入

### Claude Code

右键托盘图标 → **安装 Claude Code 集成**。TaskNotify 会:
1. 备份现有 `~/.claude/settings.json`
2. 合并官方事件 Hook(`SessionStart` / `Stop` / `StopFailure` / `PermissionRequest` / `Notification`),不替换你已有的 Hook
3. 把 Node.js 接收器复制到 `%LOCALAPPDATA%\TaskNotify\`

在 Claude Code 中执行 `/hooks` 验证安装。完成后每次会话事件都会触发 TaskNotify 通知。

**事件 → 通知映射**

| 事件 | 触发通知 | 通知文案示例 |
|---|---|---|
| `TaskStarted` | 否(任务进行中) | — |
| `TaskSucceeded` | 是 | ✅ "Refactor auth 已结束 · 用时 02:14 · 执行成功" |
| `TaskFailed` | 是 | ❌ "Tests 已结束 · 用时 00:30 · 执行失败" |
| `TaskWaitingForPermission` | 是(绕过冷却) | 🔐 "Deploy 需要你处理 · 用时 00:05 · 需要用户确认" |
| `TaskWaitingForInput` | 是 | ⌨️ "Question 需要你处理 · 等待用户输入" |
| `TaskCancelled` | 是 | ⏹️ "Build 已结束 · 已取消" |
| `TaskTimedOut` | 是 | ⏱️ "Process 已结束 · 已超时" |

**SDK 用法(自定义 Hook 开发)**

```csharp
using TaskNotify.Integrations.Claude;

var client = new ClaudeTaskNotifyClient();
await client.NotifyStartedAsync("session-123", "Refactor auth module");
await client.NotifySucceededAsync("session-123");
await client.NotifyWaitingForPermissionAsync("session-123", "Push to main?");
await client.NotifyFailedAsync("session-123", "Type error in UserService");
```

### OpenAI Codex

右键托盘图标 → **安装 Codex 集成**。TaskNotify 会备份并合并 `~/.codex/hooks.json`。在 Codex 中执行 `/hooks` 信任 TaskNotify 命令 Hook。

| 事件 | 通知 |
|---|---|
| `UserPromptSubmit` | 否(进行中) |
| `PermissionRequest` | ✅ 等待权限确认 |
| `Stop` | ✅ 本轮完成 |
| 进程退出但无 `Stop` | ✅ 结果未知 |

```csharp
using TaskNotify.Integrations.Codex;

var client = new CodexTaskNotifyClient();
await client.NotifyStartedAsync("task-123", "Build project");
await client.NotifySucceededAsync("task-123");
await client.NotifyFailedAsync("task-123", "Build failed");
```

### PowerShell

通过 `PowerShellProfileInstaller` 把 `TaskNotify.psm1` 注入 PowerShell Profile,自动追踪每条命令的生命周期。卸载时只移除 TaskNotify 块,保留其他配置。

### VS Code

扩展转发 VS Code Task 与终端事件。默认在 `EnabledSources` 中关闭,需手动启用。

### Hermes Agent

通过 `HermesTaskNotifyClient` 直接发送事件,适合自研 Agent 接入。

---

## 通知系统

通知不是"事件一到就弹"。所有 `TaskCompletionNotice` 都会经过 `NotificationDispatcher` 调度,避免噪声。

### 合并窗口(Merge Burst)

同一任务在窗口(默认 **5 秒**)内的多个事件合并成一条通知,只保留优先级最高的那个。

**优先级表**

| 状态 | 优先级 |
|---|---|
| `Failed` | 100 |
| `WaitingForPermission` | 90 |
| `WaitingForInput` | 80 |
| `Succeeded` | 70 |
| `Cancelled` / `TimedOut` | 60 |
| `EndedUnknown` | 50 |
| `PossiblyCompleted` | 40 |

例如:任务先 `PossiblyCompleted` 后 `Failed`,窗口内只弹出 `Failed` 通知。

### 冷却(Cooldown)

同一任务在冷却期(默认 **10 秒**)内的后续通知被抑制。例外:`WaitingForPermission` 永远绕过冷却(必须让用户操作)。

### 通知按钮

每条通知根据 `TaskState` 智能挑选按钮子集:

| 状态 | 按钮 |
|---|---|
| `Failed` | 打开项目 / 查看日志 / 复制错误 / 以后总是提醒 / 忽略此程序 / 稀后 |
| `Succeeded` | 打开项目 / 打开输出目录 / 查看日志 / 忽略此程序 / 稀后 |
| `WaitingForPermission` / `WaitingForInput` | 打开项目 / 稀后 |
| `PossiblyCompleted` | 打开项目 / 打开输出目录 / 稀后 |
| 其他 | 打开项目 / 稀后 |

点击按钮触发 `LearningActions` 把偏好落库为用户规则,下次同类型任务自动按你的选择处理。

---

## 邮件通知

完成事件除系统通知外,可并行发送 HTML 邮件。在设置页 **通知设置 → 邮箱通知** 中配置。

### 字段

| 字段 | 说明 |
|---|---|
| SMTP 服务器 | 如 `smtp.gmail.com` |
| 端口 | 默认 `587`(SMTP submission + STARTTLS) |
| SSL/TLS | 默认开启 |
| 用户名 | 通常就是邮箱地址 |
| 密码 | 用 DPAPI 加密存储到 `email.json`,仅当前 Windows 用户可解密 |
| 发件人 / 显示名 | 显示名默认 `TaskNotify` |
| 收件人 | 多地址用逗号、分号或换行分隔 |
| 标题前缀 | 默认 `[TaskNotify]` |

### 行为细节

- **独立配置文件** — SMTP 凭证存储在 `%LOCALAPPDATA%\TaskNotify\email.json`,与通用 `settings.json` 隔离
- **后台通道** — `EmailNotifier` 用 `Channel<TaskCompletionNotice>` 异步排队,绝不阻塞 Toast 通道
- **重试** — 单次发送失败后等 5 秒重试一次,超时 30 秒
- **队列上限** — 64 条,溢出丢弃(避免离线后堆积)
- **测试发送** — 设置页"测试发送"按钮同步返回结果,用于验证 SMTP 配置
- **诊断日志** — 失败时写入 `%LOCALAPPDATA%\TaskNotify\email.log`
- **禁用兜底** — `Enabled = false` 时直接跳过,不触网

---

## 检测规则与配置

### 检测模式

| 模式 | 说明 |
|---|---|
| `Precise` | 不依赖 WMI 推断,只接收集成 Hook 事件 |
| `Balanced`(默认) | Python / Node / ffmpeg + 终端/IDE 父进程 + 任务关键词 + 30 秒阈值 |
| `Broad` | Balanced + Java / .NET / 原生构建(cl、cargo、cmake…) |

### 内置评分(Balanced 模式)

- Python / Node / npm / ffmpeg:基础分 +30
- 终端父进程(WindowsTerminal / wezterm-gui / alacritty / powershell / pwsh / cmd / Gateway):+20
- IDE 父进程(Code / devenv / pycharm64 / idea64):+15
- 任务关键词(build / test / train / process):+20
- 运行超过 30 秒:+15

### 通知阈值(最短时长)

| 进程 | 默认最短时长 |
|---|---|
| `ffmpeg.exe` | 10 秒 |
| `python.exe` / `node.exe` / `npm.exe` | 20 秒 |
| 其他 | 60 秒 |

在设置页可按进程名覆盖阈值。

### `AppSettings` 字段一览

存储在 `%LOCALAPPDATA%\TaskNotify\settings.json`:

| 字段 | 默认值 | 说明 |
|---|---|---|
| `DetectionMode` | `Balanced` | 检测模式 |
| `EnabledSources` | wmi/claude/codex/hermes/powershell = true,vscode = false | 各集成开关 |
| `NotificationThresholdsSeconds` | `{}` | 进程级最短时长覆盖 |
| `NotificationCooldownSeconds` | `10` | 同任务冷却 |
| `MergeBurstSeconds` | `5` | 同任务合并窗口 |
| `NotifyOnWaitingForPermission` | `true` | 等待权限时是否通知(绕过冷却) |
| `NotifyOnPossiblyCompleted` | `false` | 推测完成时是否通知(可能嘈杂) |
| `Privacy.ExtraSensitivePatterns` | `[]` | 额外的脱敏正则 |
| `Privacy.ClearHistoryOnExit` | `false` | 退出时清空历史 |
| `Privacy.DisableHistory` | `false` | 完全不落库 |
| `Performance.MaxTrackedProcesses` | `0`(用默认) | 最大并发跟踪数 |
| `Performance.MaxConcurrentTaskGroups` | `0` | 最大任务组数 |
| `Performance.HistoryRetentionDays` | `0`(默认 90) | 历史保留天数 |

---

## 隐私

### 采集内容
- 进程名、可执行路径、命令行(脱敏后)
- 父进程名
- 任务时长
- 事件类型与时间戳
- 集成提供的简短摘要

### 不采集内容
- ❌ 源代码 / 数据文件内容
- ❌ 完整终端输出 / 命令历史
- ❌ Claude 提示词与完整响应
- ❌ 浏览器内容 / 截图 / 剪贴板 / 键盘输入
- ❌ 密码、API Key、Token(自动遮蔽)

### 存储位置

| 路径 | 内容 |
|---|---|
| `%LOCALAPPDATA%\TaskNotify\settings.json` | 通用偏好 |
| `%LOCALAPPDATA%\TaskNotify\email.json` | SMTP 配置(密码经 DPAPI 加密) |
| `%LOCALAPPDATA%\TaskNotify\tasknotify.db` | SQLite 任务历史 |
| `%LOCALAPPDATA%\TaskNotify\logs\` | 应用日志(默认保留 14 天) |
| `%LOCALAPPDATA%\TaskNotify\email.log` | 邮件失败诊断日志 |

历史默认保留 90 天,日志 14 天,均可配置。无云端同步、无遥测。

---

## 开发指南

### 构建

```powershell
dotnet build                                     # 构建整个解决方案
dotnet build src/TaskNotify.Core                # 构建单个项目
dotnet test tests/TaskNotify.Core.Tests          # 运行单元测试
dotnet publish src/TaskNotify.Desktop -c Release -o ./publish  # 发布
```

### 项目结构

```
TaskNotify/
├── src/
│   ├── TaskNotify.Core/              # 领域模型、状态机、规则、通知调度纯逻辑
│   ├── TaskNotify.ProcessMonitor/    # WMI + 快照进程监控
│   ├── TaskNotify.Ipc/               # Named Pipe 协议
│   ├── TaskNotify.Infrastructure/    # SQLite / JSON 设置 / 邮件 / 日志
│   ├── TaskNotify.Integrations/      # Claude / Codex / Hermes / PowerShell / VS Code
│   └── TaskNotify.Desktop/           # WPF UI + 托盘 + 通知 + 邮件
├── tests/
│   └── TaskNotify.Core.Tests/        # 单元测试与集成测试
├── TaskNotify.sln
└── README.md
```

### 添加新集成

1. 在 `TaskNotify.Integrations/<NewIntegration>/` 下创建 Client 与 SettingsManager
2. 用 `IntegrationPipeClient` 发送 `IntegrationPipeMessage`
3. 把外部事件映射到 `IntegrationTaskAction`
4. 在 `AppSettings.EnabledSources` 注册开关
5. 补充 README 与 hook-receiver 脚本

---

## 常见问题

**Q: 任务结束没收到通知?**
A: 检查:
1. 托盘图标是否在运行
2. 任务是否达到最短时长(python/node/npm 默认 20 秒,其他 60 秒)
3. 进程是否被内置 `Ignore` 规则命中(语言服务器、调试器、`watch` / `dev-server`)
4. 同任务是否在冷却期内
5. 查看 `%LOCALAPPDATA%\TaskNotify\logs\` 确认事件是否被捕获

**Q: 通知太频繁?**
A: 调整 `NotificationCooldownSeconds`(加大冷却)、`MergeBurstSeconds`(加大合并窗口),或在设置中切换到 `Precise` 模式只接收集成事件。

**Q: 邮件发不出去?**
A:
1. 设置页点击"测试发送",查看返回结果
2. 检查 `%LOCALAPPDATA%\TaskNotify\email.log` 中的失败原因
3. 确认 `Enabled` 已勾选,SMTP 主机/发件人/收件人三项非空
4. 部分邮箱(如 Gmail)需使用应用专用密码,而非账户登录密码

**Q: SMTP 密码安全吗?**
A: 使用 Windows DPAPI `CurrentUser` 范围加密,密文存储在 `email.json`。仅同一 Windows 账户在本机可解密;换用户、换机器、复制文件均无法还原。

**Q: 如何卸载集成?**
A: 各集成都有独立卸载入口,保留你原有的配置不丢失。Claude/Codex/Hermes 通过托盘菜单卸载,PowerShell 通过 `PowerShellProfileInstaller.Uninstall()`。

**Q: 支持 macOS / Linux 吗?**
A: 不支持。当前依赖 WMI、Windows AppNotification、DPAPI。移植需要替换进程监控后端、通知 API 与密钥保护方案。
