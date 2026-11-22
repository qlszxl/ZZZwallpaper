# 源码与发布材料

更新日期：2026-11-22。许可选择：本项目原创程序代码采用 AGPL-3.0-only，完整原文见源码根目录 `LICENSE`；安装包中为 `licenses/AGPL-3.0.txt`。许可范围见 `NOTICE.md`。

## 当前状态

v1.0.0 安装包对应的项目源码见[版本标签](https://github.com/qlszxl/ZZZwallpaper/tree/v1.0.0)和同版本的[项目源码压缩包](https://github.com/qlszxl/ZZZwallpaper/releases/download/v1.0.0/ZZZwallpaper-1.0.0-source.zip)。草稿附件仅对具备草稿访问权限的人可见，私有仓库的标签也需要仓库访问权限。

解压源码包后，可将根目录 `.git-revision` 中的提交号与安装包验证报告中的源码提交号比较。项目源码包包含 Git 跟踪的构建材料。下文列出另行获取的依赖及待补齐的第三方材料。应用代码位于 `src/Shared`、`src/Windows`；构建脚本位于 `scripts`，安装器及配置位于 `installer`。依赖版本记录在项目文件、`packages.lock.json`、`scripts/ffmpeg.lock.json` 和 `third_party/licenses/NuGet-NOTICES.txt`。

## 交付范围

- 提供修改、构建、安装和运行所需的源码、工程配置、脚本及相关资料，按 AGPL 第 1、6 条核对对应源码和适用的安装信息。
- 构建入口：`scripts/Build-Modular.ps1`、`scripts/Build-Installer.ps1`。保留其调用的脚本、原生工程、资源生成脚本和锁定文件；所需工具及依赖获取方式应可重复使用。
- 源码包不包含用户数据库、日志、账号凭据、授权目录、个人媒体、测试现场或无权再分发的素材。程序构建所需资源应提供具有明确许可的版本或可构建的替代品，不能仅删除必要资源而使源码无法构建。
- FFmpeg 使用 2026-10-01 的 BtbN win64 lgpl-shared 8.1 构建。v1.0.0 同时提供 [FFmpeg 源码](https://github.com/qlszxl/ZZZwallpaper/releases/download/v1.0.0/FFmpeg-330caae0c1-source.zip)、[BtbN 构建脚本与补丁](https://github.com/qlszxl/ZZZwallpaper/releases/download/v1.0.0/BtbN-FFmpeg-Builds-e88e49f624-source.zip)和[依赖源码](https://github.com/qlszxl/ZZZwallpaper/releases/download/v1.0.0/FFmpeg-330caae0c1-dependency-sources.zip)。依赖包的 `manifest.json` 列出修订号、源码散列及原始二进制散列；`configure.txt` 记录实际程序报告的配置，`README.txt` 说明构建入口和各归档的用法。FFmpeg 报告的 LGPL 适用于其核心配置；`avformat-62.dll` 还包含经 Chromaprint 链入的 FFTW，分发该组合时应遵守 GPL v3。组件声明见安装目录 `licenses/FFmpeg-DEPENDENCY-NOTICES.txt`。
- Windows App SDK 1.6.250205002 由 ControlCenter 以 WindowsAppSDKSelfContained 方式部署。微软说明允许未打包应用将框架文件放在自定义安装器内。接收者可在安装前阅读并同意微软条款；再分发者须遵守包内许可第 3 节的功能、下游协议、赔偿及品牌等条件。项目原创代码的 AGPL 第 7 条附加许可见 NOTICE.md。WinUI 模板、.NET 运行时和 WebView2 SDK 的许可分别见随包声明。[微软自包含部署说明](https://learn.microsoft.com/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- 内置媒体及游戏相关图片、动图等仍需逐项确认来源和再分发授权。本项目代码采用 AGPL 不解决这些素材的权利问题。

以上未完成事项保留在发布清单中，不因本次文档更新而标为完成。联系方式：https://github.com/qlszxl/ZZZwallpaper/issues。

依据：[GNU AGPL v3](https://www.gnu.org/licenses/agpl-3.0.en.html)、[GNU GPL/AGPL FAQ](https://www.gnu.org/licenses/gpl-faq.html)、[FFmpeg 法律说明](https://ffmpeg.org/legal.html)；微软包的实际条款见随包 `WindowsAppSDK-LICENSE.txt` 和 `WindowsAppSDK-NOTICE.txt`。
