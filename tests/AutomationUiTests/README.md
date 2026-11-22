# MCP 真实 UI 输入回归

编译真实 MainWindow 和自动化处理器，使用新的工作区结果目录及隔离 SQLite 数据库。不会启动正式 MCP 管道或读取默认个人配置。测试会在交互桌面显示自己的窗口，发送真实鼠标/键盘输入，并短暂创建自己的专注计时组件；完成后关闭窗口与组件宿主。运行期间不要操作测试窗口。

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.nuget/packages'
$targets = (Resolve-Path tests/AutomationUiTests/AutomationUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/automation-ui-build
```

使用输出目录的 `ZZZwallpaper.Launcher.exe` 启动，唯一参数是一个新的绝对结果目录。需要普通交互桌面：无前台的隔离桌面只能验证结构与身份，输入测试会按产品保护规则拒绝。测试入口获取自己的前台窗口不改变产品的校验规则。

成功输出 `results.json`、逐项 `checks.txt` 以及界面 PNG；失败输出 `failure.txt` 或 `unhandled.txt`。涵盖强制 GC 后控件身份、固定快照分页、真实开关/拖动/按键/中文输入/双向滚动、目标变化/弹层/页面变化拒绝及实际计时组件按钮。文本和内层滚动区是放在效果编辑器中的测试夹具，其输入走生产处理器；开关和亮度滑块是产品原有控件。

只验证运行环境实际 DPI。多屏负坐标的归一化有独立单元检查，实际混合 DPI 和热插拔仍需要对应设备复测。测试专用入口不得进入发行构建；发布时不得传入 `CustomAfterMicrosoftCommonTargets`。
