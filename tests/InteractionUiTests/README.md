# 2026-10-11 交互回归

此入口编译真实 MainWindow 和控件模板，使用指定结果目录中的独立 SQLite 数据库与合成素材，不加载生产 App 启动逻辑、不读取默认用户配置、不启动壁纸、组件宿主或 MCP 管道。它通过 WinUI 的真实 FocusManager、弹出层和 Composition visual 树检查焦点转移、描边归属、旧描边隐藏及描边内边界。

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.nuget/packages'
$targets = (Resolve-Path tests/InteractionUiTests/InteractionUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/interaction-ui-build
```

使用输出目录的 `ZZZwallpaper.Launcher.exe`，第一个参数为一个新的绝对结果目录。默认在屏幕外运行后退出，成功输出 `results.json`，失败输出 `failure.txt` 或 `unhandled.txt`。PNG 由 RenderTargetBitmap 记录 XAML 布局；原生 Composition hand-in visual 的检查依据其实际颜色、尺寸、裁剪与描边属性，屏幕绘制效果仍应通过下述交互窗口检查。

添加 `--interactive` 可打开并保留可见的隔离测试窗口，用于真实鼠标悬停、点击、键盘及截图验证。两个应用按钮使用测试处理器模拟忙碌状态，播放按钮的原始处理器已移除，不启动播放宿主。其他编辑仅写入该次运行的测试数据库。关闭窗口会释放测试数据连接。

此测试入口不可发布，发行构建不得传入该 `CustomAfterMicrosoftCommonTargets`。构建与生产编译共享中间目录时请顺序执行，避免 XAML 编译产物相互覆盖。
