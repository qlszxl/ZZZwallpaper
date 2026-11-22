# 固定操作区与滚动条回归

此入口编译真实 MainWindow 和共享控件模板，使用独立 SQLite 目录与屏幕外窗口。24 个素材为测试引用，24 个文本组件全部禁用；不启动生产 App、桌面壁纸、组件宿主或 MCP。

覆盖三档窗口宽度下的固定添加区、效果详情与组件详情、作用对象弹窗的禁用和焦点返回、长素材名称、迁移和清空的作用范围，以及共享和框架原生滚动条的闲置隐藏、唤醒、动效、嵌套隔离和卸载。检查各页面、对象/添加弹窗、下拉列表与长菜单中实际生成的滚动条。

```powershell
$env:DOTNET_CLI_HOME=Join-Path $PWD '.dotnet'
$env:NUGET_PACKAGES=Join-Path $PWD '.nuget/packages'
$targets=(Resolve-Path tests/PinnedChromeUiTests/PinnedChromeUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/pinned-chrome-ui-build
```

运行输出目录的 `ZZZwallpaper.Launcher.exe`，将一个新的绝对目录作为参数。成功输出 `results.json`、逐项检查和布局 PNG；失败写入 `failure.txt`。不要同时构建 UI 测试和生产载荷。生产构建必须移除自定义 targets，确认程序集入口为生产 Program.Main 且不含 Regression 类型。真实安装、鼠标拖拽和系统级 DPI/文字缩放仍由用户测试。
