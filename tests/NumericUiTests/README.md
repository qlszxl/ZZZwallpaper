# 直接数值输入回归

该入口编译真实 MainWindow、数值控件及颜色选择器，使用独立 SQLite 目录和屏幕外窗口，不运行正式 App 启动逻辑、不启动壁纸或组件宿主。

覆盖三个区域设置的数字解析、整数/小数/正负数和范围、连续键入不提前提交、Enter/Tab/失焦提交、Esc 取消、非法及超长粘贴、禁用取消、动态边界、滑杆同步、RGB/HEX、实际效果保存和一次撤销/重做、页面切换、组件位置及播放秒数。逐项结果包含原生输入的前台窗口和进程防护断言。

```powershell
$env:DOTNET_CLI_HOME=Join-Path $PWD '.dotnet'
$env:NUGET_PACKAGES=Join-Path $PWD '.nuget/packages'
$targets=(Resolve-Path tests/NumericUiTests/NumericUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/numeric-ui-build
```

运行输出目录的 `ZZZwallpaper.Launcher.exe`，将新的绝对结果目录作为唯一参数。测试需要桌面交互权限，会临时聚焦自己的屏幕外窗口并向该进程发送键盘输入；每次输入前核对前台窗口，按下和抬起在同一批次中发送，完成后恢复原前台窗口。不要在限制桌面访问的沙箱内运行原生输入阶段。失败写入 `failure.txt`，成功写入 `results.json`、`checks.txt` 和截图。

UI 测试和生产构建必须串行；生产构建不传自定义 targets，并验证入口为生产 Program.Main、程序集不含 Regression 类型。真实安装升级、鼠标选择/标签拖拽、输入法和系统 DPI 缩放仍需人工验收。
