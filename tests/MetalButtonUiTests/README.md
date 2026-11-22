# 银色按钮启动资源回归

测试入口编译真实 App.xaml、MainWindow 和按钮模板，替换 App.xaml.cs 的生产启动流程。使用指定目录内的独立数据库，在屏幕外自动运行和退出；不读取默认用户数据，不启动壁纸、桌面组件宿主或 MCP 服务。

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.nuget/packages'
$targets = (Resolve-Path tests/MetalButtonUiTests/MetalButtonUiTests.targets).Path
dotnet publish src/Windows/ControlCenter/ControlCenter.csproj -c Release --no-restore -p:Platform=x64 "-p:CustomAfterMicrosoftCommonTargets=$targets" -o artifacts/metal-button-ui-test
```

用输出目录的 ZZZwallpaper.Launcher.exe 启动，将新的绝对结果目录作为唯一参数。也可通过开发启动器 DevLaunch.exe 启动测试目录内的 ZZZwallpaper.exe；直接运行可能继承开发工具的包身份，导致 ms-appx 路径错误。

成功写入 results.json；失败写入 failure.txt，资源查找失败另记 resources.txt。覆盖 App 与 MainWindow 初始化、1000／1200／1440／2560 像素窗口宽度及往返缩放、原生合成材质加载、字体资源、按钮自动化名称、独立焦点框、真实添加弹窗及返回焦点。验证导航文字和图标放大 20% 后保持原宽度、增加高度且不溢出，银色按钮高度保持细长且多次布局后稳定，正文放大可增高、恢复字号后回到细长比例。1440 宽度下输出两个按钮和侧栏的布局截图；这些截图用于尺寸和排版检查，不替代真实鼠标下动态反光的人工验收。DPI 为实际测试环境的系统缩放，不代表其他 DPI 或高对比度切换已验证。

测试还检查按钮中段的大字内容、覆盖范围和光层边缘，并从四个方向驱动材质的指针目标。逐像素检查验证亮光移动时暗纹保持固定，鼠标离开与重新进入时动画连续。禁用、隐藏、窗口失活或关闭动效时应暂停动画，恢复后应能继续播放。

测试构建不能发布。测试和生产编译共享中间目录，必须顺序执行；正式发布不得传入 CustomAfterMicrosoftCommonTargets，发布后须用 PE 元数据确认入口为 Program.Main，且不含 MetalButtonRegressionProgram。

