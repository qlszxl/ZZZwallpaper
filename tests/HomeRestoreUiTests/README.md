# 首页恢复与共享控件回归

编译真实 MainWindow，使用独立数据库与屏幕外窗口，检查 1440、1100、960 像素宽度下的封面、播放按钮布局、焦点和调用，以及五个导航入口。测试也覆盖共享开关的胶囊轮廓与滑杆数值操作。

```powershell
$env:DOTNET_CLI_HOME=Join-Path $PWD '.dotnet'
$env:NUGET_PACKAGES=Join-Path $PWD '.nuget/packages'
$targets=(Resolve-Path tests/HomeRestoreUiTests/HomeRestoreUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/home-restore-ui-build
```

以结果目录的绝对路径为参数运行该构建的 `ZZZwallpaper.Launcher.exe`。使用测试数据，所有播放按钮均替换为计数处理器，不启动壁纸或组件宿主，不使用生产 App 启动逻辑。成功写入 `results.json`、尺寸记录及布局 PNG；失败写入 `failure.txt`。截图仅记录 XAML，Composition 描边依据实际 visual 属性检查，实际安装和交互效果仍由用户测试。

不要同时执行界面测试构建和生产构建；生产构建不得传入测试 targets，并须确认入口为 `LumaWall.ControlCenter.Program.Main`，不包含回归测试类型。
