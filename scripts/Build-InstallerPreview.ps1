[CmdletBinding()]
param([string]$OutputDirectory)

# Builds an independent WPF EXE with an in-memory flow only. Never invokes Setup,
# the wallpaper app, a browser, or the real installer build script.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/installer-ui' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$product = Get-Content -LiteralPath (Join-Path $root 'installer/Product.json') -Raw | ConvertFrom-Json
$version = [string]$product.version
if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'Unexpected project version.' }
$previewProject = Join-Path $root 'src/Windows/InstallerPreview/InstallerPreview.csproj'
& (Join-Path $PSScriptRoot 'Sync-InstallerPreviewTheme.ps1')
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $root '.nuget/packages'

# The same .NET 8.0.8 desktop runtime is already cached for the main project.
# Restore locally so preview generation does not install or download tooling.
dotnet restore $previewProject --source $env:NUGET_PACKAGES --disable-parallel -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Native preview restore failed. No software was installed.' }
dotnet publish $previewProject -c Release --no-restore -p:Version=$version -o $output
if ($LASTEXITCODE -ne 0) { throw 'Native preview compilation failed.' }
$executable = Join-Path $output 'ZZZwallpaper.Setup.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Native preview EXE was not generated.' }
# The project embeds and publishes matching legal originals. Check every copy
# before treating this folder as a complete UI review package.
[xml]$nativeProject = Get-Content -LiteralPath $previewProject -Raw
$runtimeVersion = [string]$nativeProject.Project.PropertyGroup.RuntimeFrameworkVersion
$licenseOutput = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
$licenseCopies = [ordered]@{
    "microsoft.netcore.app.runtime.win-x64/$runtimeVersion/LICENSE.TXT" = 'DotNet-Runtime-LICENSE.txt'
    "microsoft.netcore.app.runtime.win-x64/$runtimeVersion/THIRD-PARTY-NOTICES.TXT" = 'DotNet-Runtime-THIRD-PARTY-NOTICES.txt'
    "microsoft.windowsdesktop.app.runtime.win-x64/$runtimeVersion/LICENSE" = 'WindowsDesktop-Runtime-LICENSE.txt'
}
foreach ($license in $licenseCopies.GetEnumerator()) {
    $licenseSource = Join-Path $env:NUGET_PACKAGES $license.Key
    if (!(Test-Path -LiteralPath $licenseSource -PathType Leaf)) { throw "Missing runtime license: $licenseSource" }
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $licenseOutput $license.Value) -Force
}
foreach ($document in $nativeProject.SelectNodes('//LegalDocument')) {
    $include = $document.GetAttribute('Include').Replace('$(RuntimeFrameworkVersion)', $runtimeVersion)
    $original = [IO.Path]::GetFullPath((Join-Path (Split-Path $previewProject -Parent) $include))
    $published = Join-Path $output $document.GetAttribute('Destination')
    if (!(Test-Path -LiteralPath $published -PathType Leaf)) { throw "Missing legal document: $published" }
    if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash) {
        throw "Legal document differs from its original: $published"
    }
}
@"
Third-party components included in ZZZwallpaper.Setup

.NET Runtime $runtimeVersion - .NET Foundation and Contributors
License: DotNet-Runtime-LICENSE.txt
Additional notices: DotNet-Runtime-THIRD-PARTY-NOTICES.txt

Windows Desktop Runtime $runtimeVersion - .NET Foundation and Contributors
License: WindowsDesktop-Runtime-LICENSE.txt

Source Han Sans SC 2.005R (Heavy and Medium) - Adobe
License: SourceHanSans-OFL.txt

These notices cover the standalone installer UI package. The full wallpaper
application has its own dependency notices and release-source requirements.
See 第三方许可.txt and THIRD-PARTY-NOTICES.md for the application reference list.
"@ | Set-Content -LiteralPath (Join-Path $licenseOutput 'THIRD-PARTY-NOTICES.txt') -Encoding UTF8
@"
ZZZwallpaper $version 安装器

运行 ZZZwallpaper.Setup.exe，保留整个文件夹。
当前执行后端为空实现，不安装或启动软件。
"@ | Set-Content -LiteralPath (Join-Path $output '使用说明.txt') -Encoding UTF8
[ordered]@{
    product='ZZZwallpaper.Setup'; version=$version; releaseChannel=$product.releaseChannel; previewOnly=$true
    ui='native-wpf'; selfContained=$true; realInstallerBackend=$false
    theme='ControlCenter'; themeRevision=3; palette='shared lime'; themeTokensSynchronized=$true
    backend='NoOpInstallerBackend'; scenarioControls=$false
    legalDocumentsEmbedded=$true; legalDocumentCopiesVerified=$true; agreementPreselected=$false
    licenseScope='AGPL-3.0-only for original program code; third-party terms retained'; legalStatus="$($product.releaseChannel)-$version"
    runtimeLicenseFiles=@($licenseCopies.Values)
    builtUtc=[DateTime]::UtcNow.ToString('O')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'preview-info.json') -Encoding UTF8
Write-Output "Native preview ready: $executable"
Write-Output 'Build only: not launched, not installed; no real installation backend is included.'
