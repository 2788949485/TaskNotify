# TaskNotify 全自动任务完成通知程序开发文档

## 1. 文档信息

- 项目名称：TaskNotify
- 项目定位：Windows 自动任务完成通知工具
- 文档版本：V2.0
- 目标平台：Windows 10、Windows 11
- 推荐技术栈：C#、WPF、.NET 10、Windows App SDK、SQLite
- 核心模式：后台自动检测
- 用户操作要求：安装和首次配置一次，后续运行任务时不需要输入 TaskNotify 命令

------

# 2. 项目概述

TaskNotify 是一个常驻 Windows 系统托盘的轻量级程序。

它用于自动识别用户正在执行的长时间任务，例如：

- Python 数据处理
- Python 模型训练
- Node.js 项目构建
- npm、pnpm、yarn 脚本
- ffmpeg 视频转码
- PowerShell 脚本
- CMD 批处理任务
- Claude Code 问题处理
- Codex CLI 任务
- VS Code 构建、测试和终端任务
- 数据库导入导出
- 文件批量处理
- 其他长时间运行的命令行程序

当任务完成、失败、超时或者需要用户处理时，TaskNotify 自动弹出 Windows 通知。

用户正常执行：

```powershell
python process_data.py
```

而不是：

```powershell
tasknotify run -- python process_data.py
```

用户正常使用：

```powershell
claude
```

Claude 完成工作后，TaskNotify 自动通知。

------

# 3. 产品目标

## 3.1 核心目标

TaskNotify 应当实现：

1. 安装后自动随 Windows 启动。
2. 自动监听进程启动和退出。
3. 自动判断进程是否属于用户任务。
4. 自动过滤系统服务和编辑器后台进程。
5. 自动识别 Python、Node.js、ffmpeg 等常见任务。
6. 自动接入 Claude Code Hooks。
7. 自动接入 PowerShell。
8. 通过 VS Code 扩展监控任务和终端命令。
9. 根据运行时间决定是否提醒。
10. 区分“确定成功”“确定失败”“仅知道进程结束”和“推测完成”。
11. 保存任务历史记录。
12. 提供低误报、平衡和广泛三种检测模式。
13. 不要求用户改变原来的命令使用习惯。

## 3.2 产品边界

第一版不承诺对所有软件都能精确判断业务是否成功。

对于没有以下能力的第三方程序：

- Hook
- 插件接口
- 退出码
- 日志接口
- 输出文件
- 状态事件

TaskNotify 只能判断：

- 进程是否已经结束
- 程序是否长时间没有活动
- 输出文件是否停止变化

这种场景下，通知应显示：

```text
任务进程已经结束
结果状态暂时无法确认
```

不得错误显示为：

```text
任务执行成功
```

------

# 4. 用户体验

## 4.1 安装过程

安装程序提供以下选项：

```text
TaskNotify 安装设置

✓ 开机自动启动
✓ 启用常见开发进程检测
✓ 安装 PowerShell 自动检测模块
✓ 检测并接入 Claude Code
✓ 检测并安装 VS Code 扩展
□ 启用广泛进程检测
```

安装完成后，TaskNotify 在系统托盘运行。

## 4.2 日常使用

用户继续使用原来的方式运行程序：

```powershell
python process.py
npm run build
ffmpeg -i input.mp4 output.mp4
claude
```

TaskNotify 自动完成：

```text
检测任务开始
    ↓
判断是否属于用户任务
    ↓
记录运行时间
    ↓
跟踪主进程和子进程
    ↓
判断完成、失败或等待操作
    ↓
显示 Windows 通知
```

## 4.3 通知示例

### 精确成功

```text
Python 数据处理已完成

process_data.py
用时：18 分 32 秒
退出码：0
```

### 精确失败

```text
Node.js 构建失败

npm run build
用时：2 分 17 秒
退出码：1
```

### 只知道进程结束

```text
Python 任务已经结束

train.py
用时：43 分 18 秒
结果状态暂时无法确认
```

### Claude 等待操作

```text
Claude Code 需要你的操作

正在等待命令执行权限
项目：D:\Projects\DatabaseTool
```

### 推测任务完成

```text
文件处理程序已停止活动

程序已经连续 30 秒没有明显活动，
任务可能已经完成，请检查结果。
```

------

# 5. 总体设计原则

## 5.1 多通道自动检测

系统不得只依赖单一进程监控。

采用以下检测通道：

```text
官方 Hook
Shell 自动集成
IDE 插件
进程启动/退出事件
退出码
子进程树
输出文件
窗口状态
CPU 和磁盘活动
```

## 5.2 检测可信度分级

每次检测结果都必须包含可信度。

| 等级 | 检测来源            | 可显示结果           |
| ---- | ------------------- | -------------------- |
| A    | 官方 Hook           | 完成、失败、等待操作 |
| A    | IDE 官方事件        | 完成、失败           |
| A    | Shell 集成及退出码  | 成功、失败           |
| B    | 进程退出码          | 成功、失败           |
| B    | 完成标记文件        | 完成                 |
| C    | 进程消失            | 进程已结束           |
| C    | 输出文件稳定        | 文件可能生成完成     |
| D    | CPU、磁盘、窗口状态 | 任务可能完成         |

通知文案必须与可信度对应。

## 5.3 不使用 OCR 作为默认方式

第一版不通过以下方式判断完成：

- 截屏识别
- OCR 读取终端
- 模拟鼠标点击
- 定时扫描屏幕文字

这些方式误报率高，并且可能涉及隐私。

------

# 6. 系统架构

```text
┌─────────────────────────────────────────────┐
│                  任务来源                    │
│                                             │
│ Python / Node / ffmpeg / PowerShell / CMD   │
│ Claude Code / Codex / VS Code / GUI 软件    │
└─────────────────────┬───────────────────────┘
                      │
        ┌─────────────┼────────────────┐
        │             │                │
        ▼             ▼                ▼
┌────────────┐ ┌──────────────┐ ┌──────────────┐
│进程事件监控 │ │专用集成模块   │ │文件活动监控   │
│WMI / ETW   │ │Claude/VS Code│ │File Watcher  │
└─────┬──────┘ └──────┬───────┘ └──────┬───────┘
      │               │                │
      └───────────────┼────────────────┘
                      ▼
┌─────────────────────────────────────────────┐
│              Detection Engine               │
│                                             │
│ 进程识别 / 父子关系 / 规则匹配 / 可信度计算  │
└─────────────────────┬───────────────────────┘
                      ▼
┌─────────────────────────────────────────────┐
│               Task Manager                  │
│                                             │
│ 状态机 / 去重 / 聚合 / 超时 / 任务关联       │
└───────────────┬─────────────────────────────┘
                │
       ┌────────┴────────┐
       ▼                 ▼
┌──────────────┐  ┌───────────────────────┐
│ SQLite       │  │ Windows App Notification│
│ 任务历史      │  │ 通知、按钮、声音        │
└──────────────┘  └───────────────────────┘
```

------

# 7. 项目结构

```text
TaskNotify.sln

src/
├─ TaskNotify.Desktop/
│  ├─ Views/
│  ├─ ViewModels/
│  ├─ Tray/
│  ├─ Notifications/
│  └─ App.xaml
│
├─ TaskNotify.Core/
│  ├─ Models/
│  ├─ Enums/
│  ├─ StateMachine/
│  ├─ Detection/
│  ├─ Rules/
│  └─ Interfaces/
│
├─ TaskNotify.ProcessMonitor/
│  ├─ Wmi/
│  ├─ Etw/
│  ├─ ProcessTree/
│  ├─ ProcessMetadata/
│  └─ ActivityMonitor/
│
├─ TaskNotify.Integrations/
│  ├─ ClaudeCode/
│  ├─ PowerShell/
│  ├─ VsCode/
│  ├─ Python/
│  ├─ Node/
│  ├─ Ffmpeg/
│  └─ Generic/
│
├─ TaskNotify.Ipc/
│  ├─ NamedPipeServer/
│  ├─ NamedPipeClient/
│  └─ Protocol/
│
├─ TaskNotify.Infrastructure/
│  ├─ Database/
│  ├─ Repositories/
│  ├─ Settings/
│  ├─ Startup/
│  └─ Logging/
│
├─ TaskNotify.HookReceiver/
│  └─ Program.cs
│
└─ TaskNotify.VsCodeExtension/
   ├─ src/
   ├─ package.json
   └─ extension.ts

tests/
├─ TaskNotify.Core.Tests/
├─ TaskNotify.ProcessMonitor.Tests/
├─ TaskNotify.Integrations.Tests/
└─ TaskNotify.EndToEndTests/
```

------

# 8. 自动检测模式

## 8.1 精确模式

只启用：

- Claude Code Hook
- PowerShell 集成
- VS Code 扩展
- 完成标记文件
- 已知程序官方接口

特点：

- 误报最低
- 支持的软件较少
- 可以较准确判断成功和失败

## 8.2 平衡模式

启用：

- 精确模式所有能力
- 常见进程监控
- 父子进程判断
- 命令行规则
- 运行时间阈值
- 常见后台程序过滤

特点：

- 推荐作为默认模式
- 用户无需额外操作
- 可以识别大部分开发任务
- 少量任务只能显示“进程结束”

## 8.3 广泛模式

额外启用：

- 用户自定义程序
- CPU 活动监控
- 磁盘活动监控
- 窗口标题规则
- 输出目录监控
- 未知长进程提醒

特点：

- 覆盖面最大
- 误报可能增加
- 推测类通知必须明确标注“可能完成”

------

# 9. Windows 进程自动监控

## 9.1 第一版实现方式

第一版使用 WMI 进程事件：

```text
Win32_ProcessStartTrace
Win32_ProcessStopTrace
```

`Win32_ProcessStartTrace` 会在新进程启动时提供进程 ID、父进程 ID、进程名称和会话 ID，可用于建立进程父子关系。

C# 使用：

```csharp
ManagementEventWatcher
```

订阅进程开始和结束事件。`ManagementEventWatcher` 用于监听符合指定 WMI 查询的事件。

## 9.2 后续 ETW 模式

第二阶段可以增加 ETW 进程事件监听器。

ETW 是 Windows 提供的事件跟踪机制，可以动态启用或关闭事件捕获，并用于消费系统和应用程序产生的跟踪事件。

建议：

```text
MVP：WMI
高性能版本：ETW
WMI 失败时：定时快照补偿
```

## 9.3 进程开始处理

收到进程开始事件后：

```text
读取 PID
    ↓
读取父 PID
    ↓
读取进程名称
    ↓
尝试读取命令行
    ↓
尝试读取可执行文件路径
    ↓
查找祖先进程
    ↓
执行排除规则
    ↓
执行识别规则
    ↓
决定是否创建候选任务
```

候选任务不立即通知，只进入观察状态。

## 9.4 进程结束处理

收到结束事件后：

```text
查找对应候选任务
    ↓
检查是否仍有相关子进程
    ↓
计算运行时间
    ↓
判断退出状态是否可用
    ↓
执行通知规则
    ↓
保存任务记录
```

## 9.5 进程快照补偿

为避免遗漏事件，每 30 秒执行一次轻量进程快照：

```text
当前进程列表
    ↓
和内存中的进程表比较
    ↓
发现新增进程
    ↓
补充开始事件
    ↓
发现消失进程
    ↓
补充结束事件
```

快照仅作为补偿，不作为主要检测方式。

------

# 10. 进程识别规则

## 10.1 默认监控程序

```text
python.exe
pythonw.exe
py.exe
node.exe
npm.exe
npm.cmd
pnpm.exe
pnpm.cmd
yarn.exe
yarn.cmd
ffmpeg.exe
java.exe
dotnet.exe
msbuild.exe
devenv.exe
claude.exe
codex.exe
gemini.exe
ollama.exe
```

## 10.2 默认可信程序类型

### Python

识别：

```text
python script.py
python -m module
py script.py
```

排除：

```text
python language_server.py
python debugpy
python pylance
python jupyter-kernel
python pre-commit
```

### Node.js

识别：

```text
npm run build
npm run test
pnpm build
yarn build
node script.js
```

排除：

```text
VS Code extension host
TypeScript language server
Electron 后台进程
Webpack 常驻开发服务器
Vite 开发服务器
```

### ffmpeg

默认认为：

- 运行超过 10 秒属于候选任务
- 退出后提醒
- 可以读取退出码时判断成功或失败

### Java 和 dotnet

只在满足以下条件时监控：

- 父进程为终端或 IDE
- 命令行包含 jar、build、test、publish、run 等特征
- 运行超过通知阈值

## 10.3 父进程规则

优先监控由以下程序启动的任务：

```text
WindowsTerminal.exe
powershell.exe
pwsh.exe
cmd.exe
Code.exe
devenv.exe
pycharm64.exe
idea64.exe
claude.exe
codex.exe
```

示例：

```text
WindowsTerminal.exe
└─ pwsh.exe
   └─ python.exe
      └─ ffmpeg.exe
```

系统应将 Python 和 ffmpeg 归为同一任务组，避免连续弹出两个通知。

## 10.4 祖先进程限制

最多向上查找 8 层父进程。

记录：

```text
PID
ParentPID
ProcessName
ExecutablePath
CommandLineHash
SessionID
StartTime
```

不需要永久保存完整进程树。

------

# 11. 后台进程过滤

## 11.1 固定排除规则

默认排除：

- Windows 系统进程
- Windows 服务
- 杀毒软件
- 浏览器子进程
- Electron 渲染进程
- IDE 语言服务器
- 调试适配器
- 自动更新程序
- 常驻开发服务器
- 文件监听器
- 用户明确忽略的程序

## 11.2 命令行关键词排除

```text
language-server
language_server
pylance
debugpy
extensionHost
tsserver
watch
dev-server
vite --host
webpack serve
electron
update
telemetry
```

关键词不能单独作为最终依据，必须结合：

- 进程名称
- 父进程
- 运行时间
- 是否常驻
- 用户历史行为

## 11.3 常驻进程识别

满足任一条件，可标记为常驻程序：

1. 连续运行超过 8 小时。
2. 过去 7 天内每天都会自动启动。
3. Windows 启动后自动出现。
4. 命令行包含 server、daemon、watch、serve。
5. 用户手动选择“永不提醒此程序”。

常驻程序默认不发送完成通知。

------

# 12. 自动任务聚合

## 12.1 聚合目的

同一个任务可能产生多个进程：

```text
npm.cmd
└─ node.exe
   └─ tsc.exe
```

如果分别通知，会产生重复提醒。

## 12.2 任务组

定义：

```csharp
public sealed class ProcessTaskGroup
{
    public Guid Id { get; set; }

    public int RootProcessId { get; set; }

    public HashSet<int> ProcessIds { get; set; } = [];

    public string Source { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public DetectionConfidence Confidence { get; set; }

    public TaskState State { get; set; }
}
```

## 12.3 聚合规则

进程满足以下条件时归入同一组：

- 属于直接父子进程
- 祖先进程相同
- 启动时间相差不超过 5 秒
- 工作目录相同
- 命令链相互关联
- 由同一个 IDE Task 启动

## 12.4 完成条件

只有满足以下条件才结束任务组：

```text
根进程已经结束
并且
组内没有仍在运行的重要子进程
```

后台辅助进程可以配置为忽略。

------

# 13. PowerShell 自动集成

## 13.1 目标

用户正常输入：

```powershell
python process.py
```

TaskNotify 在后台自动获得：

- 命令文本
- 开始时间
- 结束时间
- `$LASTEXITCODE`
- `$?`
- 当前目录

用户不需要改变命令。

## 13.2 安装方式

安装独立模块：

```text
%LOCALAPPDATA%\TaskNotify\Integrations\PowerShell\
TaskNotify.PowerShell.psm1
```

在 PowerShell Profile 中只添加一行：

```powershell
Import-Module "$env:LOCALAPPDATA\TaskNotify\Integrations\PowerShell\TaskNotify.PowerShell.psm1"
```

PowerShell 设置如需跨会话保持，需要写入 Profile 脚本。

## 13.3 集成策略

PowerShell 模块负责：

1. 保存原始 `prompt` 函数。
2. 在命令提交时记录命令和时间。
3. 在下一次显示 Prompt 时认为上一条命令已经返回。
4. 读取 `$LASTEXITCODE` 和 `$?`。
5. 将事件通过 Named Pipe 发送给 TaskNotify。
6. 调用原始 `prompt`。
7. 保持用户原来的主题和 Prompt 样式。

## 13.4 PSReadLine 使用原则

PSReadLine 提供命令历史设置和 `AddToHistoryHandler` 等配置项，但 TaskNotify 不得直接读取并长期保存用户完整历史文件。官方接口可读取当前 PSReadLine 选项，其中包括历史路径和历史处理器信息。

TaskNotify 只处理当前命令，默认保存经过脱敏的命令摘要。

## 13.5 安全要求

不得破坏：

- Oh My Posh
- Starship
- Conda 自动激活
- 用户自定义 Prompt
- PowerShell 补全
- PSReadLine 快捷键
- 虚拟环境显示

安装前必须：

1. 备份 Profile。
2. 检查是否已经安装。
3. 只添加带有标记的代码块。
4. 卸载时只删除对应代码块。

标记格式：

```powershell
# TASKNOTIFY-BEGIN
Import-Module "..."
# TASKNOTIFY-END
```

------

# 14. CMD 自动检测

CMD 缺少与 PowerShell 相同的稳定命令生命周期扩展能力。

第一版对 CMD 采用：

- 进程启动/结束监控
- 父子进程分析
- 运行时间阈值
- 退出进程判断

第一版不修改：

```text
HKEY_CURRENT_USER\Software\Microsoft\Command Processor\AutoRun
```

避免影响用户现有 CMD 环境。

第二阶段可以开发基于 ConPTY 的增强终端代理，但不得作为默认方案。

------

# 15. Claude Code 自动集成

## 15.1 安装效果

TaskNotify 安装器自动检测：

```text
%USERPROFILE%\.claude\settings.json
```

发现 Claude Code 后，询问用户是否安装集成。

安装一次后，用户正常使用 Claude，无需运行其他命令。

## 15.2 Hook 事件

接入：

```text
SessionStart
Stop
StopFailure
Notification
SessionEnd
```

Claude Code Hooks 可以在生命周期事件发生时执行命令。`Notification` 事件会在 Claude 等待输入或权限时触发，并向 Hook 提供 `session_id`、`cwd`、`message` 和 `notification_type` 等字段。

## 15.3 Hook 接收程序

安装：

```text
TaskNotify.HookReceiver.exe
```

Claude 配置：

```json
{
  "hooks": {
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "\"C:\\Program Files\\TaskNotify\\TaskNotify.HookReceiver.exe\" claude"
          }
        ]
      }
    ],
    "StopFailure": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "\"C:\\Program Files\\TaskNotify\\TaskNotify.HookReceiver.exe\" claude"
          }
        ]
      }
    ],
    "Notification": [
      {
        "matcher": "",
        "hooks": [
          {
            "type": "command",
            "command": "\"C:\\Program Files\\TaskNotify\\TaskNotify.HookReceiver.exe\" claude"
          }
        ]
      }
    ]
  }
}
```

Hook 配置必须与用户原有 `hooks` 内容合并，不得覆盖已有配置。官方文档同样要求在已有 `hooks` 节点下增加同级事件配置，而不是替换整个对象。

## 15.4 事件映射

| Claude 事件                      | TaskNotify 事件           |
| -------------------------------- | ------------------------- |
| SessionStart                     | `task.started`            |
| Stop                             | `task.completed`          |
| StopFailure                      | `task.failed`             |
| Notification + permission_prompt | `task.waiting_permission` |
| Notification + idle_prompt       | `task.waiting_input`      |
| SessionEnd                       | `task.session_ended`      |

## 15.5 隐私规则

默认不保存：

- 用户完整 Prompt
- Claude 完整回答
- transcript 文件内容
- API Key
- 工具调用参数正文

默认只保存：

- 会话 ID 的哈希
- 工作目录
- 事件类型
- 开始和结束时间
- 通知类型
- 用户允许保存的摘要

------

# 16. VS Code 自动集成

## 16.1 扩展功能

开发 TaskNotify VS Code 扩展，监听：

```text
任务开始
任务结束
任务进程开始
任务进程结束
终端命令开始
终端命令结束
```

VS Code Extension API 提供：

- `onDidStartTask`
- `onDidEndTask`
- `onDidStartTaskProcess`
- `onDidEndTaskProcess`

其中 `onDidEndTaskProcess` 可以在任务底层进程结束时提供事件。

VS Code 还提供终端 Shell Execution 开始和结束事件，但终端必须启用 Shell Integration 才会产生相应事件。

## 16.2 扩展处理流程

```text
VS Code Task 开始
    ↓
生成任务 ID
    ↓
发送 task.started
    ↓
Task 结束
    ↓
读取退出码
    ↓
发送 completed 或 failed
```

## 16.3 终端命令处理

```text
终端 Shell Execution 开始
    ↓
读取命令摘要
    ↓
记录终端 ID 和时间
    ↓
终端 Shell Execution 结束
    ↓
读取退出码
    ↓
运行时间达到阈值后通知
```

## 16.4 去重

如果 VS Code 扩展和系统进程监控同时发现同一个任务：

```text
VS Code 官方事件优先
系统进程事件作为补充
不生成第二个任务
```

关联依据：

- PID
- 启动时间
- 工作目录
- 命令摘要
- VS Code 窗口 ID

------

# 17. 文件处理自动检测

## 17.1 适用场景

用于：

- Excel 报表生成
- 图片批量导出
- 视频渲染
- 数据导出
- 模型结果生成
- 第三方 GUI 软件

## 17.2 用户配置

用户只需配置一次：

```text
程序：DataProcessor.exe
输出目录：D:\Output
文件类型：*.xlsx
稳定时间：10 秒
```

后续自动监控。

## 17.3 文件完成判断

同时满足：

1. 文件已创建。
2. 文件大小连续 N 秒不变。
3. 最后修改时间连续 N 秒不变。
4. 文件可以正常读取。
5. 文件不再被其他进程独占。
6. 对应程序处于空闲或已经退出。

结果可信度：

```text
进程退出 + 文件稳定：B
只有文件稳定：C
```

## 17.4 完成标记文件

支持用户或第三方程序创建：

```text
.finished
.done
success.flag
```

检测到完成标记时，可信度提高为 B。

------

# 18. CPU 和磁盘活动推测

## 18.1 使用限制

CPU 和磁盘活动只能作为辅助信息，不能单独判断成功。

## 18.2 推测规则

示例：

```text
程序已经运行超过 2 分钟
并且
之前 CPU 使用率明显高于 10%
并且
CPU 降到 1% 以下持续 30 秒
并且
磁盘写入接近 0
并且
窗口仍然存在
```

结果：

```text
状态：PossiblyCompleted
可信度：D
```

通知必须显示：

```text
任务可能已经完成
```

## 18.3 防误报

以下程序不得使用 CPU 空闲判断：

- 服务器
- 下载工具
- 等待网络响应的程序
- 数据库服务
- IDE
- 浏览器
- 消息软件
- 训练过程中存在长时间等待的程序

------

# 19. 检测评分系统

## 19.1 评分模型

每个候选任务计算分数。

### 加分项

| 条件                              | 分数 |
| --------------------------------- | ---- |
| 官方 Hook                         | +100 |
| VS Code Task 事件                 | +100 |
| PowerShell 精确事件               | +90  |
| 已知任务进程                      | +30  |
| 父进程为终端                      | +20  |
| 父进程为 IDE                      | +15  |
| 命令包含 build/test/train/process | +20  |
| 运行超过 30 秒                    | +15  |
| 存在输出文件                      | +10  |
| 用户此前允许提醒                  | +30  |

### 减分项

| 条件                     | 分数 |
| ------------------------ | ---- |
| 系统进程                 | -100 |
| 服务进程                 | -100 |
| IDE 语言服务器           | -80  |
| 调试适配器               | -60  |
| 命令包含 watch/serve     | -40  |
| 运行不足 5 秒            | -30  |
| 浏览器或 Electron 子进程 | -50  |
| 用户设置为忽略           | -100 |

## 19.2 分数结果

```text
80 分以上：精确任务
50～79 分：高概率任务
30～49 分：候选任务，只记录
低于 30 分：忽略
```

分数不是完成可信度。

需要分别保存：

```text
TaskProbability
CompletionConfidence
```

------

# 20. 任务状态机

```csharp
public enum TaskState
{
    Candidate,
    Running,
    WaitingForInput,
    WaitingForPermission,
    PossiblyCompleted,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    EndedUnknown,
    Ignored
}
```

状态流转：

```text
Candidate
   ↓
Running
   ├─ WaitingForInput
   ├─ WaitingForPermission
   ├─ PossiblyCompleted
   ├─ Succeeded
   ├─ Failed
   ├─ Cancelled
   ├─ TimedOut
   ├─ EndedUnknown
   └─ Ignored
```

终态：

```text
Succeeded
Failed
Cancelled
TimedOut
EndedUnknown
Ignored
```

------

# 21. 核心数据结构

```csharp
public sealed class DetectedTask
{
    public Guid Id { get; set; }

    public string Source { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public TaskState State { get; set; }

    public DetectionConfidence Confidence { get; set; }

    public int ProbabilityScore { get; set; }

    public int? RootProcessId { get; set; }

    public string? ProcessName { get; set; }

    public string? CommandSummary { get; set; }

    public string? WorkingDirectory { get; set; }

    public DateTimeOffset DetectedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public int? ExitCode { get; set; }

    public string? ResultMessage { get; set; }

    public string? OpenPath { get; set; }

    public string? LogPath { get; set; }

    public string? CorrelationKey { get; set; }

    public string MetadataJson { get; set; } = "{}";
}
```

可信度：

```csharp
public enum DetectionConfidence
{
    Unknown = 0,
    Inferred = 1,
    ProcessConfirmed = 2,
    ExitCodeConfirmed = 3,
    IntegrationConfirmed = 4
}
```

------

# 22. 通知规则

## 22.1 默认时间阈值

| 类型        | 默认阈值 |
| ----------- | -------- |
| Claude Code | 0 秒     |
| 失败任务    | 0 秒     |
| 等待权限    | 0 秒     |
| ffmpeg      | 10 秒    |
| npm build   | 10 秒    |
| Python      | 20 秒    |
| Node.js     | 20 秒    |
| 未知进程    | 60 秒    |
| 推测完成    | 120 秒   |

## 22.2 通知优先级

```text
失败
    ↓
等待权限
    ↓
等待输入
    ↓
确定完成
    ↓
进程结束
    ↓
推测完成
```

## 22.3 合并规则

同一任务在 5 秒内出现多个结果时：

```text
失败优先于完成
官方事件优先于进程推测
退出码优先于文件推测
文件完成优先于 CPU 推测
```

## 22.4 通知冷却

同一任务：

- 10 秒内最多弹出一次通知
- 等待权限事件除外
- 同一权限事件重复触发时合并

------

# 23. Windows 通知

采用 Windows App SDK App Notifications。

该 API 支持 WPF 应用发送本地通知、处理用户点击，并可以在通知中提供操作按钮。

TaskNotify Desktop 必须使用普通用户权限运行，因为 Windows App SDK 当前不支持由提升为管理员权限的应用发送或接收 App Notifications。

## 23.1 通知按钮

支持：

```text
打开项目
打开输出目录
查看日志
复制错误
忽略此程序
以后总是提醒
稍后提醒
```

## 23.2 通知安全

通知参数只传：

```text
taskId
action
```

示例：

```text
action=open-output&taskId=xxxxxxxx
```

不得把未经验证的命令直接写入通知操作参数。

------

# 24. 桌面界面

## 24.1 左侧菜单

```text
任务中心
正在运行
最近完成
自动检测
程序规则
集成管理
通知设置
隐私设置
系统设置
```

## 24.2 自动检测页面

```text
自动检测模式

○ 精确模式
● 平衡模式
○ 广泛模式

自动检测来源

✓ PowerShell
✓ Claude Code
✓ VS Code
✓ Python
✓ Node.js
✓ ffmpeg
✓ 其他常见长任务
□ 未知程序
□ 文件活动
□ CPU 空闲推测
```

## 24.3 程序规则页面

每条规则包括：

```text
程序名称
可执行文件路径
命令关键词
父进程
最短运行时间
是否通知成功
是否通知失败
是否认为是常驻程序
可信度策略
```

## 24.4 学习操作

在历史任务右键菜单提供：

```text
以后总是提醒此类任务
以后不再提醒此程序
将此程序标记为后台服务
将此程序标记为数据处理工具
修改通知阈值
```

------

# 25. 数据库设计

## 25.1 DetectedTasks

```sql
CREATE TABLE DetectedTasks (
    Id TEXT PRIMARY KEY,
    Source TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    State INTEGER NOT NULL,
    Confidence INTEGER NOT NULL,
    ProbabilityScore INTEGER NOT NULL,
    RootProcessId INTEGER NULL,
    ProcessName TEXT NULL,
    CommandSummary TEXT NULL,
    WorkingDirectory TEXT NULL,
    DetectedAt TEXT NOT NULL,
    StartedAt TEXT NULL,
    EndedAt TEXT NULL,
    ExitCode INTEGER NULL,
    ResultMessage TEXT NULL,
    OpenPath TEXT NULL,
    LogPath TEXT NULL,
    CorrelationKey TEXT NULL,
    MetadataJson TEXT NOT NULL
);
```

## 25.2 ProcessEvents

```sql
CREATE TABLE ProcessEvents (
    Id TEXT PRIMARY KEY,
    TaskId TEXT NULL,
    ProcessId INTEGER NOT NULL,
    ParentProcessId INTEGER NULL,
    ProcessName TEXT NOT NULL,
    EventType TEXT NOT NULL,
    EventTime TEXT NOT NULL,
    FOREIGN KEY(TaskId) REFERENCES DetectedTasks(Id)
);
```

## 25.3 DetectionRules

```sql
CREATE TABLE DetectionRules (
    Id TEXT PRIMARY KEY,
    RuleName TEXT NOT NULL,
    ProcessPattern TEXT NULL,
    CommandPattern TEXT NULL,
    ParentPattern TEXT NULL,
    MinimumDurationSeconds INTEGER NOT NULL,
    ScoreAdjustment INTEGER NOT NULL,
    Action INTEGER NOT NULL,
    IsEnabled INTEGER NOT NULL,
    IsUserCreated INTEGER NOT NULL
);
```

## 25.4 Integrations

```sql
CREATE TABLE Integrations (
    Id TEXT PRIMARY KEY,
    IntegrationType TEXT NOT NULL,
    IsInstalled INTEGER NOT NULL,
    IsEnabled INTEGER NOT NULL,
    Version TEXT NULL,
    ConfigPath TEXT NULL,
    LastCheckedAt TEXT NULL
);
```

------

# 26. 隐私与安全

## 26.1 默认不采集

- 源代码内容
- 数据文件内容
- 完整终端输出
- 完整命令历史
- Claude 对话内容
- 浏览器内容
- 屏幕截图
- 剪贴板内容
- 键盘输入
- API Key
- 密码

## 26.2 命令脱敏

保存前过滤：

```text
--password
--passwd
--token
--api-key
--secret
Authorization
OPENAI_API_KEY
ANTHROPIC_API_KEY
DATABASE_URL
```

示例：

```text
原命令：
python upload.py --token abcdefg

保存结果：
python upload.py --token ***
```

## 26.3 本地运行

第一版所有数据保存在本机：

```text
%LOCALAPPDATA%\TaskNotify\
```

默认不上传云端。

## 26.4 配置修改安全

修改以下文件前必须备份：

- PowerShell Profile
- Claude settings.json
- VS Code 用户设置

写入时使用：

```text
读取
    ↓
解析
    ↓
合并
    ↓
写入临时文件
    ↓
验证
    ↓
原子替换
```

不得直接字符串覆盖。

------

# 27. 安装与卸载

## 27.1 安装器职责

1. 安装 Desktop。
2. 注册系统托盘程序。
3. 配置用户级开机启动。
4. 安装 Windows 通知注册信息。
5. 初始化 SQLite。
6. 可选安装 PowerShell 模块。
7. 可选安装 Claude Hooks。
8. 可选安装 VS Code 扩展。
9. 创建配置备份。

## 27.2 开机启动

使用当前用户级启动项，不要求管理员权限。

## 27.3 卸载

卸载时：

1. 停止后台程序。
2. 删除开机启动。
3. 移除 TaskNotify PowerShell 标记块。
4. 从 Claude 配置中移除 TaskNotify 创建的 Hook。
5. 卸载 VS Code 扩展。
6. 询问是否保留历史记录。
7. 不影响用户其他配置。

------

# 28. 异常恢复

## 28.1 监控器中断

WMI 或 ETW 监听异常时：

```text
写入错误日志
    ↓
重启监控器
    ↓
执行进程快照
    ↓
恢复候选任务
```

## 28.2 程序重启

TaskNotify 重启后：

1. 读取未结束任务。
2. 检查对应 PID 是否存在。
3. PID 存在则恢复 Running。
4. PID 不存在则标记 EndedUnknown。
5. 不自动发送成功通知。

## 28.3 集成损坏

设置页面显示：

```text
Claude Code 集成：异常
PowerShell 集成：正常
VS Code 扩展：版本过旧
```

提供：

```text
检测
修复
重新安装
查看配置
```

------

# 29. 性能要求

空闲状态目标：

```text
CPU：接近 0%
内存：不高于 150 MB
磁盘：只在事件发生时写入
启动时间：3 秒以内
```

运行状态：

```text
进程事件处理：100 毫秒内
普通规则匹配：50 毫秒内
单次快照：不阻塞 UI
通知延迟：2 秒以内
```

限制：

```text
最多跟踪候选进程：500
最多同时任务组：100
单条事件最大：256 KB
默认历史保留：90 天
默认日志保留：14 天
```

------

# 30. 测试计划

## 30.1 Python

测试：

```powershell
python success.py
python failed.py
python long_task.py
python parent_process.py
```

验证：

- 正确识别
- 短任务不提醒
- 长任务提醒
- 崩溃任务不显示成功
- 子进程不会重复通知

## 30.2 Node.js

测试：

```powershell
npm run build
npm run test
npm run dev
```

验证：

- build 完成后通知
- test 失败后通知
- dev 常驻服务不提醒
- VS Code 后台 Node 进程被过滤

## 30.3 ffmpeg

测试：

- 正常转码
- 输入文件不存在
- 用户取消
- ffmpeg 由 Python 启动

## 30.4 PowerShell

测试：

- 普通 Prompt
- Oh My Posh
- Conda 环境
- 多行命令
- 管道命令
- Ctrl+C
- `$LASTEXITCODE`
- 内置 PowerShell 命令
- 多窗口并发

## 30.5 Claude Code

测试：

- Stop
- StopFailure
- permission_prompt
- idle_prompt
- 多会话
- 重复 Hook
- 配置已有其他 Hooks
- TaskNotify 未运行
- Hook Receiver 异常

## 30.6 VS Code

测试：

- Task 运行
- Task 失败
- 集成终端 Python
- 集成终端 npm
- 多终端
- Shell Integration 未启用
- VS Code 关闭
- 调试模式

## 30.7 误报测试

连续运行 8 小时，检查：

- 浏览器
- 微信
- VS Code
- 系统更新
- 杀毒软件
- Node 语言服务器
- Python LSP
- Electron 子进程

平衡模式目标：

```text
普通办公 8 小时内误报不超过 1 次
```

------

# 31. 验收标准

## 31.1 无命令使用

用户安装 TaskNotify 后直接执行：

```powershell
python long_task.py
```

要求：

- 不需要添加 `tasknotify run`
- 自动识别 Python 任务
- 达到时间阈值后，结束时自动通知

## 31.2 Claude Code

用户直接执行：

```powershell
claude
```

要求：

- 回答完成后自动通知
- 等待权限时自动通知
- 无需用户手动调用 TaskNotify
- 不覆盖已有 Claude Hooks

## 31.3 VS Code

用户从 VS Code 运行任务。

要求：

- 自动记录开始和结束
- 可取得退出码时正确区分成功和失败
- 不与系统进程检测重复通知

## 31.4 误报控制

平衡模式下：

- 不提醒浏览器子进程
- 不提醒 VS Code 语言服务器
- 不提醒常驻开发服务器
- 不提醒运行不足默认阈值的普通命令

## 31.5 可信度文案

要求：

- 有退出码 0 才可以显示“执行成功”
- 有明确失败事件才显示“执行失败”
- 只知道进程消失时显示“进程已结束”
- CPU 推测只能显示“可能完成”

------

# 32. 开发阶段

## 第一阶段：后台进程检测 MVP

开发：

1. WPF 托盘程序。
2. WMI 进程启动和结束监听。
3. 进程父子关系。
4. Python、Node、ffmpeg 规则。
5. 时间阈值。
6. Windows 通知。
7. SQLite 历史记录。
8. 基础误报过滤。

阶段结果：

```text
不输入额外命令，也能自动提醒常见长任务。
```

## 第二阶段：精确集成

开发：

1. PowerShell 自动模块。
2. Claude Code Hooks。
3. VS Code 扩展。
4. IPC。
5. 任务去重和聚合。
6. 退出码精确判断。

阶段结果：

```text
主要开发工具能够准确区分成功、失败和等待操作。
```

## 第三阶段：智能规则

开发：

1. 可信度系统。
2. 任务评分。
3. 用户学习规则。
4. 常驻程序识别。
5. 自定义进程规则。
6. 文件输出监控。

## 第四阶段：广泛检测

开发：

1. ETW 监控。
2. CPU 和磁盘活动。
3. 窗口状态。
4. GUI 软件适配器。
5. 多显示器和全屏通知规则。

------

# 33. Codex 第一阶段开发指令

```text
请开发一个名为 TaskNotify 的 Windows 桌面程序。

目标：
用户正常运行 Python、Node.js、ffmpeg 等程序时，不需要输入任何额外命令，TaskNotify 在后台自动检测长时间运行的任务，并在进程结束后发送 Windows 通知。

技术要求：
1. C#、WPF、.NET 10。
2. 使用 MVVM。
3. 使用依赖注入。
4. 使用 SQLite。
5. 使用 Windows App SDK App Notifications。
6. 使用 ManagementEventWatcher 监听 Win32_ProcessStartTrace 和 Win32_ProcessStopTrace。
7. 程序常驻系统托盘。
8. 不使用管理员权限。
9. 支持 CancellationToken。
10. 启用 Nullable。
11. 所有异步方法使用 async/await。
12. 不使用全局静态 Service Locator。

第一阶段实现：
1. 监听进程启动和结束。
2. 记录 PID、父 PID、进程名、开始时间和结束时间。
3. 建立父子进程关系。
4. 自动识别 python.exe、node.exe 和 ffmpeg.exe。
5. 默认运行超过 20 秒才提醒。
6. 排除 VS Code Extension Host、语言服务器和 Electron 子进程。
7. 同一父子进程链只生成一个任务。
8. 无法确认成功时只能显示“进程已结束”，不能显示“成功”。
9. 保存最近 90 天的任务历史。
10. 提供正在运行、历史记录和自动检测设置页面。

请先完成项目结构、核心模型、状态机、WMI 监听器、规则引擎和单元测试，不要一次完成全部 UI。
```

------

# 34. 最终产品定义

TaskNotify 的默认使用方式应当是：

```text
安装一次
    ↓
开启自动检测
    ↓
用户继续按照原有习惯运行程序
    ↓
TaskNotify 自动识别任务
    ↓
任务结束后通知
```

不要求用户每次输入：

```powershell
tasknotify run ...
```

核心实现采用：

```text
进程自动监听
+ PowerShell 一次性集成
+ Claude Code Hook
+ VS Code 扩展
+ 文件活动监控
+ 可信度分级
+ 误报过滤
```

其中：

- Claude Code、VS Code 和 PowerShell 可以实现较高准确度的自动检测。
- Python、Node.js 和 ffmpeg 可以通过进程、父子关系和运行时间自动识别。
- 无官方接口的软件只能判断进程结束或推测任务完成。
- 系统必须如实展示检测可信度，不能把“进程结束”等同于“任务成功”。