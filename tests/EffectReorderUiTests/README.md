# 效果卡片排序离屏 UI 回归

此入口编译真实 App.xaml、MainWindow 和效果卡片控件，替换生产 App.xaml.cs，使用结果目录内新建的 SQLite 数据库和 ControlCenterServices。测试 targets 排除生产窗口位置恢复/保存文件，替换为空操作，避免构造期间的 presenter.Restore 激活路径；WinUI 内部窗口创建保持原样，不安装创建钩子。MainWindow 构造返回后，仅对其 HWND 设置并检查 WS_EX_NOACTIVATE，使用 SWP_NOACTIVATE 定位屏幕外，再以 AppWindow.Show(false) 显示。保留前台窗口断言，activation-guard.json 记录构造、定位、显示和检查阶段的焦点及样式。测试不移动鼠标、不注入键鼠事件、不切换或恢复其他程序焦点；不创建壁纸、组件、桌面菜单控制器或 MCP 服务。

测试使用生产 BeginEffectCardDrag、MoveEffectCardDrag、EndEffectCardDrag 方法驱动真实 WinUI 卡片：至少七张卡片跨行移动、源卡片跟随、其他卡片让位、拖动预览不改草稿、松手一次提交、取消复原、编辑目标变化与页面切换取消。关闭动效时检查实际 Composition Translation 的目的位置和复原；开启动效时检查真实控件复用、源卡片浮起和预览状态，并记录 XAML 布局 PNG。Composition 属性读取不代表动画插值帧，因此测试不宣称覆盖动画插值像素、真实 PointerPressed/捕获/释放事件链或实际屏幕上的动画观感。

测试还检查同一位置的拖动能复用 Visual 和动画对象，取消后会清理缓存与鼠标采样回调，滚动后的缓存坐标与 WinUI 变换一致，窗口布局变化会取消拖动。

在模块化副本目录中，串行执行测试构建：

```powershell
$env:DOTNET_CLI_HOME=Join-Path $PWD '.dotnet'
$env:NUGET_PACKAGES=Join-Path $PWD '.nuget/packages'
$targets=(Resolve-Path tests/EffectReorderUiTests/EffectReorderUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/effect-reorder-ui-build
```

通过该输出目录的 ZZZwallpaper.Launcher.exe 启动，将新的绝对结果目录作为唯一参数。成功输出 results.json、checks.txt、layout-states.json 和四张布局图；失败输出 failure.txt。只写结果目录中的测试数据。

不要与生产构建并行：两者共享 XAML 中间目录。测试入口不能进入发布包；正式构建去掉 CustomAfterMicrosoftCommonTargets，确认入口为 Program.Main 且程序集不含 EffectReorderRegressionProgram。也不要用生产程序的 --data-root 代替这个隔离入口，因为生产单实例检查早于数据目录参数解析。
