# TaskNotify

Windows 后台任务完成通知工具。用户继续运行 `python script.py`、`npm run build` 等原有命令，TaskNotify 在后台检测长时间任务。

当前已完成第一批基础能力：任务状态机、可信度约束、数据驱动检测规则、命令脱敏，以及可取消的 WMI 进程事件监听器。

```powershell
dotnet test TaskNotify.sln
```
