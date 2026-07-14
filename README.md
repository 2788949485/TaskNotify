# TaskNotify

> **A lightweight Windows desktop app for automatic detection of long-running development tasks with Windows notifications.**
>
> 一个轻量级 Windows 桌面应用，用于自动检测长时间运行的开发任务并发送 Windows 通知。

---

## Table of Contents / 目录

- [Features](#features) / [功能特性](#功能特性)
- [Architecture](#architecture) / [架构](#架构)
- [Quick Start](#quick-start) / [快速开始](#快速-start)
- [Installation & Setup](#installation--setup) / [安装与配置](#安装与配置)
- [Integrations](#integrations) / [集成](#集成)
  - [Claude Code](#claude-code)
  - [OpenAI Codex](#openai-codex)
  - [PowerShell](#powershell)
  - [VS Code](#vs-code)
  - [Hermes Agent](#hermes-agent)
- [Configuration](#configuration) / [配置](#配置)
- [Privacy](#privacy) / [隐私](#隐私)
- [Development](#development) / [开发](#开发)

---

## Features / 功能特性

### Detection / 检测
- **WMI Process Monitoring** — Monitors process start/exit via Windows WMI
- **Snapshot Compensation** — 15-second polling backup when WMI events are lost
- **Parent-Child Aggregation** — Groups related processes (e.g., `python.exe` → `ffmpeg.exe`) into a single task
- **PID Reuse Protection** — Prevents merging different tasks with recycled PIDs
- **Detection Rules** — Data-driven scoring system with built-in and user-defined rules

### Integrations / 集成
- **Claude Code** — Official hook integration for task lifecycle events
- **OpenAI Codex** — Hook-based task event forwarding
- **PowerShell** — Profile module for command lifecycle tracking
- **VS Code** — Extension for task and terminal event forwarding
- **Hermes Agent** — Direct IPC integration for AI agent tasks

### Notifications / 通知
- **Windows App Notifications** — Native desktop notifications with balloon tips
- **Dynamic Status** — Shows success/failure/pending based on evidence confidence
- **Notification Click** — Double-click to open main window and locate the task
- **Deduplication** — Same logical task from multiple sources produces one notification

### Privacy / 隐私
- **Local Only** — No cloud sync, no telemetry, no remote logging
- **Command Sanitization** — Automatically masks passwords, tokens, API keys before storage
- **No Screen Reading** — Does not capture screenshots, clipboard, or keystrokes
- **Default Deny** — Ignores system processes, IDE language servers, electron renderers by default

---

## Architecture / 架构

```
TaskNotify.Desktop (WPF / System Tray / Notifications)
    │
    ├── TaskMonitorService
    │   ├── WmiProcessMonitor     (WMI start/stop traces)
    │   ├── SnapshotProcessMonitor (15s polling backup)
    │   └── IntegrationEventListener (Named Pipe server)
    │
    ├── ProcessTaskTracker        (Core: state machine, scoring)
    ├── TaskHistoryViewModel      (UI data binding)
    └── TrayIconService           (Balloon tips, click handling)

TaskNotify.Integrations
    ├── Claude/ClaudeTaskNotifyClient      (SDK for Claude Code hooks)
    ├── Claude/ClaudeSettingsManager       (settings.json config)
    ├── Codex/CodexTaskNotifyClient        (SDK for Codex hooks)
    ├── Hermes/HermesTaskNotifyClient      (SDK for Hermes Agent)
    ├── PowerShell/TaskNotify.psm1         (PowerShell module)
    └── PowerShell/PowerShellProfileInstaller (Profile management)

TaskNotify.Ipc
    ├── IntegrationPipeServer              (Named Pipe listener)
    ├── IntegrationPipeClient              (Named Pipe sender)
    └── IntegrationPipeMessage             (Protocol definition)

TaskNotify.Core
    ├── ProcessTaskTracker                 (Event handling, state machine)
    ├── DetectedTask                       (Task model)
    ├── DetectionRuleEngine                (Scoring rules)
    ├── TaskStateMachine                   (State transitions)
    ├── CompletionConfidence               (Evidence reliability levels)
    └── CommandSanitizer                   (Sensitive data masking)
```

---

## Quick Start / 快速开始

### Prerequisites / 前置要求
- Windows 10 or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or Runtime for pre-built binaries)
- [Node.js](https://nodejs.org/) (for Claude Code hook receiver)

### Build & Run / 构建与运行

```powershell
# Clone and build
git clone <repository-url>
cd TaskNotify
dotnet build

# Run the desktop app
dotnet run --project src/TaskNotify.Desktop
```

The app will appear in your system tray. It starts monitoring immediately.

---

## Installation & Setup / 安装与配置

### Basic Usage / 基本用法

After starting TaskNotify Desktop, no additional configuration is needed for basic process monitoring:

```powershell
# Just run your command normally — TaskNotify detects it automatically
python process.py
npm run build
ffmpeg -i input.mp4 output.avi
```

No wrapper commands needed. No manual reporting.

### Enable Claude Code Integration / 启用 Claude Code 集成

#### Step 1: Start TaskNotify Desktop / 启动桌面程序

```powershell
& "D:\zhuomian\TaskNotify\src\TaskNotify.Desktop\bin\Debug\net8.0-windows\TaskNotify.Desktop.exe"
```

Or build and run:
```powershell
dotnet run --project src/TaskNotify.Desktop
```

#### Step 2: Install Claude Code Hook / 安装 Claude Code 挂钩

Right-click the TaskNotify tray icon and select **安装 Claude Code 集成**. TaskNotify copies
the receiver to the current user's local application data directory, backs up
`~/.claude/settings.json`, and merges official `SessionStart`, `Stop`, `StopFailure`,
`PermissionRequest`, and `Notification` hooks without replacing existing hooks.

#### Step 3: Restart Claude Code / 重启 Claude Code

Start a new Claude Code session, then use `/hooks` to verify the installed user hooks.

Now when you run Claude Code tasks, you'll receive TaskNotify desktop notifications.

---

## Integrations / 集成

### Claude Code

**What it does / 作用:**
When Claude Code starts a task, finishes, needs user input, or encounters an error, it sends a standardized event to TaskNotify via Named Pipe.

**Events / 事件:**
| Event | Description | Notification |
|---|---|---|
| `TaskStarted` | Claude begins a new task | None (task running) |
| `TaskSucceeded` | Task completed successfully | ✅ "Claude: Build — Success · 00:45" |
| `TaskFailed` | Task failed with error | ✅ "Claude: Tests — Failed · 30s" |
| `TaskWaitingForPermission` | Needs user approval | ✅ "Claude: Deploy — Waiting for permission" |
| `TaskWaitingForInput` | Needs user response | ✅ "Claude: Question — Waiting for input" |
| `TaskCancelled` | Task was cancelled | ✅ "Claude: Build — Cancelled" |
| `TaskTimedOut` | Task exceeded time limit | ✅ "Claude: Process — Timed out" |

**SDK Usage (for hook developers) / SDK 用法:**
```csharp
using TaskNotify.Integrations.Claude;

var client = new ClaudeTaskNotifyClient();
await client.NotifyStartedAsync("session-123", "Refactor auth module");
await client.NotifySucceededAsync("session-123");
await client.NotifyWaitingForPermissionAsync("session-123", "Push to main?");
await client.NotifyFailedAsync("session-123", "Type error in UserService");
```

### OpenAI Codex

Codex CLI can forward task events to TaskNotify via a hook receiver.

**Setup / 设置:**

Right-click the TaskNotify tray icon and select **安装 Codex 集成**. TaskNotify backs up
and merges its handlers into `~/.codex/hooks.json`. In Codex, run `/hooks` once to
review and trust the TaskNotify command hooks, as required by Codex's hook security model.

**Events / 事件:**
| Event | Description | Notification |
|---|---|---|
| `UserPromptSubmit` | Codex begins a turn | None (task running) |
| `PermissionRequest` | Codex needs user approval | ✅ Waiting for permission |
| `Stop` | Codex finishes a turn | ✅ Turn completed |
| Process exit without `Stop` | Codex ended without a confirmed result | ✅ Result unknown |

**SDK Usage / SDK 用法:**
```csharp
using TaskNotify.Integrations.Codex;

var client = new CodexTaskNotifyClient();
await client.NotifyStartedAsync("task-123", "Build project");
await client.NotifySucceededAsync("task-123");
await client.NotifyFailedAsync("task-123", "Build failed");
```

### PowerShell

Installs a PowerShell module into your profile that forwards command events.

**Install / 安装:**
```powershell
# Add to your PowerShell profile (preserves existing content)
# See PowerShellProfileInstaller.Install() for programmatic use
```

**Uninstall / 卸载:**
```powershell
# Removes only the TaskNotify block, preserves everything else
# See PowerShellProfileInstaller.Uninstall() for programmatic use
```

### VS Code

Extension that forwards VS Code Task lifecycle events to TaskNotify.

### Hermes Agent

Direct IPC integration for AI agent tasks. Use `HermesTaskNotifyClient` to send events.

---

## Configuration / 配置

### Detection Rules / 检测规则

Built-in rules define default detection behavior. User rules override built-in rules.

**Built-in Balanced Mode / 内置平衡模式:**
- Python, Node, npm, ffmpeg: score +30
- Terminal parent process: score +20
- IDE parent process: score +15
- Task keywords (build/test/train/process): score +20
- Running >30s: score +15

**Notification Thresholds / 通知阈值:**
| Process | Minimum Duration |
|---|---|
| ffmpeg.exe | 10 seconds |
| python.exe, node.exe, npm.exe | 20 seconds |
| Others | 60 seconds |

### User Rules / 用户规则

Users can customize detection via the settings UI:
- "Always notify for this task type"
- "Never notify for this executable"
- "Mark as long-running service"
- "Mark as data processing program"
- "Change minimum notification duration"

---

## Privacy / 隐私

### What is collected / 采集内容
- Process name, executable path, command line (sanitized)
- Parent process name
- Task duration
- Event type and timestamp
- Short summary (user-provided by integrations)

### What is NOT collected / 不采集内容
- ❌ Source code
- ❌ Data file contents
- ❌ Full terminal output
- ❌ Complete command history
- ❌ Claude prompts or full responses
- ❌ Browser content
- ❌ Screenshots
- ❌ Clipboard content
- ❌ Keyboard input
- ❌ Passwords, API keys, tokens (automatically masked)

### Storage / 存储
- All data stored locally in SQLite
- Task history retention: 90 days (configurable)
- Log retention: 14 days (configurable)
- No cloud sync, no telemetry, no remote logging

---

## Development / 开发

### Project Structure / 项目结构

```
TaskNotify/
├── src/
│   ├── TaskNotify.Core/           # Domain models, state machine, rules
│   ├── TaskNotify.ProcessMonitor/ # WMI + Snapshot process monitoring
│   ├── TaskNotify.Ipc/            # Named Pipe protocol (framing, messages)
│   ├── TaskNotify.Integrations/   # Claude, Codex, Hermes, PowerShell, VS Code
│   └── TaskNotify.Desktop/        # WPF UI, system tray, notifications
├── tests/
│   └── TaskNotify.Core.Tests/     # Unit tests
├── .gitignore
└── README.md
```

### Architecture Boundaries / 架构边界

```
TaskNotify.Core        — NO dependencies on WPF, Windows SDK, WMI, external APIs
TaskNotify.ProcessMonitor — NO direct UI or notification logic
TaskNotify.Ipc          — Pure protocol, platform-agnostic message definitions
TaskNotify.Integrations — Depends on Core + Ipc only
TaskNotify.Desktop      — WPF presentation layer, depends on all layers
```

### Building / 构建

```powershell
# Build entire solution
dotnet build

# Build individual project
dotnet build src/TaskNotify.Core

# Run tests
dotnet test tests/TaskNotify.Core.Tests

# Publish for distribution
dotnet publish src/TaskNotify.Desktop -c Release -o ./publish
```

### Adding a New Integration / 添加新集成

1. Create event handler in `TaskNotify.Integrations/<NewIntegration>/`
2. Use `IntegrationPipeClient` to send `IntegrationPipeMessage`
3. Map external event types to `IntegrationTaskAction`
4. Document in README.md

---

## License / 许可证

MIT License

---

## Contributing / 贡献

Pull requests welcome. Please ensure:
1. All builds pass (`dotnet build`)
2. Architecture boundaries are respected
3. No sensitive data is logged or committed
4. User configurations are preserved on upgrade

---

# TaskNotify (中文版)

> **轻量级 Windows 桌面应用，自动检测长时间运行的开发任务并在完成/失败/超时时发送 Windows 通知。**

## 核心设计原则 / Core Principles

1. **零配置启动** — 启动后自动监控，无需包装命令
2. **证据驱动** — 通知文案严格基于检测证据的可信度
3. **误报优先降低** — 平衡模式下默认忽略系统进程、IDE 语言服务器等
4. **本地优先** — 所有数据存储在本地，无云端同步
5. **隐私保护** — 自动脱敏密码、Token、API Key

## 快速开始 / Quick Start

### 安装依赖 / Install Dependencies

```powershell
# .NET 8.0 SDK
dotnet --version

# Node.js (for Claude Code hook)
node --version
```

### 构建运行 / Build & Run

```powershell
dotnet build
dotnet run --project src/TaskNotify.Desktop
```

### 基本使用 / Basic Usage

```powershell
# 直接运行你的命令即可，无需任何包装
python process.py
npm run build
```

## Claude Code 集成详细步骤 / Claude Code Integration Steps

### 前提条件 / Prerequisites

1. TaskNotify Desktop 正在运行
2. Claude Code 已安装 (`npm install -g @anthropic-ai/claude-code`)
3. Node.js 可用

### 一键安装 / One-Step Install

右键单击 TaskNotify 托盘图标，选择 **安装 Claude Code 集成**。安装器只合并
TaskNotify 自己的官方事件 Hook，并在修改前备份现有 `settings.json`。

### 验证安装 / Verify

在 Claude Code 中执行 `/hooks`，确认 `SessionStart`、`Stop`、`StopFailure`、
`PermissionRequest` 和 `Notification` 下显示来自 `User` 的 TaskNotify 命令 Hook。

### 测试 / Test

```powershell
# 启动 Claude Code 并提问，完成后应收到通知
claude "Write a hello world script in Python"
# → 收到 Windows 通知: "Claude Code session ended · Success · 00:XX"
```

## 常见问题 / FAQ

**Q: 为什么我的任务没有通知？**
A: 检查以下几点：
1. TaskNotify Desktop 是否在运行（右下角托盘图标）
2. 任务是否超过最短通知时长（python.exe 为 20 秒）
3. 进程是否被内置排除规则忽略（如语言服务器、调试器）
4. 查看日志确认事件是否被捕获

**Q: 通知太多怎么办？**
A: 可以在设置中将检测模式改为"严格模式"，或添加用户规则忽略特定进程。

**Q: 如何卸载集成？**
A: 各集成均有独立的卸载方法，保留用户原有配置不变。

**Q: Codex 会有通知吗？**
A: 会。安装并在 `/hooks` 中信任 TaskNotify Hook 后，每轮完成和权限请求都会通知；若进程异常退出但没有官方 `Stop` 事件，只显示结果未知。

**Q: 支持 macOS/Linux 吗？**
A: 当前仅支持 Windows（使用 WMI 和 Windows 通知 API）。Linux/macOS 版本需要替换监控后端。
