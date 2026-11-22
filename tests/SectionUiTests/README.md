# 参数分组与顶部操作回归

编译真实 MainWindow，继承固定入口、滚动条和弹窗回归，并覆盖全部 22 类效果、10 类组件、组件组及其成员、主/子频谱、故障集群和动态音频规则的标题、图标框架与底纹。检查品牌底纹开关、预览展开、宽窄窗口布局、分享选项横向卡片的换行与点击区域、全选时跳过不支持的效果，以及移动到顶部的移除按钮只删除当前项目且可撤销。

```powershell
$env:DOTNET_CLI_HOME=Join-Path $PWD '.dotnet'
$env:NUGET_PACKAGES=Join-Path $PWD '.nuget/packages'
$targets=(Resolve-Path tests/SectionUiTests/SectionUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/sections-ui-build
```

运行输出目录中的 `ZZZwallpaper.Launcher.exe`，将新的绝对结果目录作为唯一参数。启动器会退出，测试在子进程中继续；以 `results.json` 或 `failure.txt` 判断完成。成功结果包括断言、分组清单及截图。

使用项目内独立 SQLite 和屏幕外窗口；所有组件保持禁用，不运行生产启动流程、不启动壁纸或组件宿主。与其他 WinUI 测试及生产构建串行编译；生产构建不传测试 targets，并核对正式入口和程序集类型。安装升级、鼠标手感和系统 DPI 缩放由人工验收。
