# 2026-10-10 报告回归

此入口编译真实 MainWindow 和自动化处理器，使用指定输出目录中的独立 SQLite 数据库及合成图片。不会启动壁纸、组件宿主、正式 MCP 管道或读取默认用户配置。

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.nuget/packages'
$targets = (Resolve-Path tests/ReportUiTests/ReportUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/report-ui-build
```

通过输出目录的 `ZZZwallpaper.Launcher.exe` 启动，唯一参数是一个新的绝对结果目录。必须使用启动器，以免继承打包开发工具的应用身份而错误解析 `ms-appx` 资源。

程序在屏幕外创建窗口并自动退出。成功输出 `results.json` 和素材库、配置分享截图；失败输出 `failure.txt` 或 `unhandled.txt`。检查范围包括三档窗口宽度、详情开关与多选、末尾素材滚动定位、文件大小边界、实际编组处理器响应与数据库往返、布局排序和分享文案。当前运行环境的 DPI 之外，需要另行在对应系统缩放下执行。

此测试专用构建不可发布。发行构建不得传入 `CustomAfterMicrosoftCommonTargets`。
