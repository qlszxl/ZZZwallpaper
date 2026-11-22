param([switch]$SkipDependencies,[switch]$Zip,[string]$OutputDirectory,[switch]$SkipRuntimeMetadata,[switch]$CleanPackage)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $product = Get-Content -LiteralPath 'installer/Product.json' -Raw | ConvertFrom-Json
    $output=if($OutputDirectory) {[IO.Path]::GetFullPath($OutputDirectory)} else {Join-Path $root 'artifacts/windows-x64'}
    if ($CleanPackage -and (Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) {
        throw 'CleanPackage requires a new or empty output directory. Existing files will not be reused.'
    }
    $env:DOTNET_CLI_HOME=Join-Path $root '.dotnet'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $env:NUGET_PACKAGES=Join-Path $root '.nuget/packages'
    $restoreArgs=@('-m:1','--locked-mode','--source',$env:NUGET_PACKAGES,'--disable-parallel','-p:NuGetAudit=false')
    $publishArgs=@('-m:1','-p:UseSharedCompilation=false','-p:DebugType=None','-p:DebugSymbols=false',"-p:PathMap=$root=/_/ZZZwallpaper")
    $sourceCommit = & (Join-Path $PSScriptRoot 'Get-SourceRevision.ps1')
    if ($sourceCommit) { $publishArgs += "-p:SourceRevisionId=$sourceCommit" }
    else { $publishArgs += '-p:IncludeSourceRevisionInInformationalVersion=false' }
    $widgetOutput=if($CleanPackage) {Join-Path $root ('artifacts/build-widget-' + [guid]::NewGuid().ToString('N'))} else {Join-Path $root 'artifacts/widget-host-zzzwallpaper'}
    if(!$SkipDependencies) {& (Join-Path $PSScriptRoot 'Get-Dependencies.ps1')}
    & (Join-Path $PSScriptRoot 'Build-Native.cmd')
    if($LASTEXITCODE -ne 0) {throw 'Native build failed.'}
    dotnet restore 'src/Windows/WidgetHost/WidgetHost.csproj' @restoreArgs
    if($LASTEXITCODE -ne 0) {throw 'Widget dependencies restore failed.'}
    dotnet publish 'src/Windows/WidgetHost/WidgetHost.csproj' -c Release --no-restore @publishArgs -o $widgetOutput
    if($LASTEXITCODE -ne 0) {throw 'Widget host build failed.'}
    $baseProject='src/Windows/BaseWidgets/BaseWidgets.csproj'
    dotnet restore $baseProject @restoreArgs
    if($LASTEXITCODE -ne 0){throw 'Base widget provider restore failed.'}
    dotnet build $baseProject -c Release --no-restore @publishArgs
    if($LASTEXITCODE -ne 0){throw 'Base widget provider build failed.'}
    Copy-Item -LiteralPath 'src/Windows/BaseWidgets/bin/Release/net8.0-windows/win-x64/ZZZwallpaper.BaseWidgets.dll','artifacts/native/TaskbarHost.exe' -Destination 'artifacts/native/ExpansionPacks/base' -Force
    dotnet restore 'src/Windows/ControlCenter/ControlCenter.csproj' @restoreArgs
    if($LASTEXITCODE -ne 0) {throw 'Dependency restore failed.'}
    dotnet publish 'src/Windows/ControlCenter/ControlCenter.csproj' -c Release --no-restore @publishArgs -p:Platform=x64 "-p:WidgetHostPublishDirectory=$widgetOutput" -o $output
    if($LASTEXITCODE -ne 0) {throw 'Control center build failed.'}
    dotnet restore 'src/Windows/McpBridge/McpBridge.csproj' @restoreArgs -m:1
    if($LASTEXITCODE -ne 0) {throw 'MCP dependencies restore failed.'}
    dotnet publish 'src/Windows/McpBridge/McpBridge.csproj' -c Release --no-restore @publishArgs -m:1 -o (Join-Path $output 'Mcp')
    if($LASTEXITCODE -ne 0) {throw 'MCP bridge build failed.'}
    New-Item -ItemType Directory -Force (Join-Path $output 'licenses') | Out-Null
    $taskbarNotices=Join-Path $output 'licenses/TranslucentTB'
    New-Item -ItemType Directory -Force $taskbarNotices | Out-Null
    Get-ChildItem -LiteralPath 'third_party/translucenttb' -File | Where-Object {$_.Extension -ne '.dll'} | Copy-Item -Destination $taskbarNotices
    Copy-Item 'src/Windows/ControlCenter/Assets/Fonts/LICENSE.txt' (Join-Path $output 'licenses/SourceHanSans-OFL.txt')
    Copy-Item 'src/Windows/ControlCenter/Assets/Fonts/SairaCondensed-OFL.txt' (Join-Path $output 'licenses/SairaCondensed-OFL.txt')
    Copy-Item 'src/Windows/ControlCenter/Assets/Fonts/Bungee-OFL.txt' (Join-Path $output 'licenses/Bungee-OFL.txt')
    Copy-Item 'third_party/ffmpeg/LICENSE.txt' (Join-Path $output 'licenses/FFmpeg-LICENSE.txt')
    Copy-Item 'third_party/zxing/LICENSE.txt' (Join-Path $output 'licenses/ZXing-Apache-2.0.txt')
    Copy-Item 'third_party/zxing/THANKS.txt' (Join-Path $output 'licenses/ZXing-THANKS.txt')
    Copy-Item 'docs/THIRD-PARTY-NOTICES.md' (Join-Path $output 'licenses/THIRD-PARTY-NOTICES.md')
    Copy-Item 'docs/运行说明.md' (Join-Path $output '运行说明.md')
    Copy-Item 'docs/MCP使用说明.md' (Join-Path $output 'MCP使用说明.md')
    Copy-Item 'third_party/mcp/LICENSE.txt' (Join-Path $output 'licenses/MCP-Apache-2.0.txt')
    Copy-Item 'scripts/ffmpeg.lock.json' (Join-Path $output 'licenses/ffmpeg.lock.json')
    # Keep portable runtime notices identical to the installer's offline originals.
    & (Join-Path $PSScriptRoot 'Update-DependencyNotices.ps1')
    [xml]$legalProject=Get-Content -LiteralPath 'src/Windows/InstallerPreview/InstallerPreview.csproj' -Raw
    $legalRuntimeVersion=[string]$legalProject.Project.PropertyGroup.RuntimeFrameworkVersion
    foreach($document in $legalProject.SelectNodes('//LegalDocument')) {
        $relative=$document.GetAttribute('Include').Replace('$(RuntimeFrameworkVersion)',$legalRuntimeVersion)
        $original=[IO.Path]::GetFullPath((Join-Path (Join-Path $root 'src/Windows/InstallerPreview') $relative))
        $destination=Join-Path $output $document.GetAttribute('Destination')
        New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $original -Destination $destination -Force
    }
    if(!$SkipRuntimeMetadata -and !$CleanPackage) {& (Join-Path $output 'WallpaperHost.exe') --capabilities | Set-Content (Join-Path $output 'ffmpeg-capabilities.json') -Encoding utf8}
    Copy-Item 'src/Shared/schema/*.schema.json' $output
    @{sourceCommit=$sourceCommit;version=$product.version;releaseChannel=$product.releaseChannel;publisher=$product.publisher;supportUrl=$product.supportUrl;cleanPackage=[bool]$CleanPackage;baseDevelopmentVersion=$product.baseDevelopmentVersion;builtUtc=[DateTime]::UtcNow.ToString('O');playbackVersion=2;effectsVersion=40;effectsWireVersion=38;featurePackApi=2;legacyEffectsWireVersion=25;widgetsVersion=18;shareTransportVersion=1;shareDocumentVersion=1;shareQrVersion=1;mcpDefaultEnabled=$false;mcpSdkVersion='2.2.0';mcpAgentPromptVersion=3;agentGuideVersion=51;sharedAudioAnalysisVersion=1;decoderControlVersion=2;posterSelectionVersion=4} | ConvertTo-Json | Set-Content (Join-Path $output 'build-info.json') -Encoding utf8
    if($Zip) {Compress-Archive -Path "$output/*" -DestinationPath (Join-Path $root "artifacts/ZZZwallpaper-$($product.version)-$($product.releaseChannel)-windows-x64.zip") -Force}
    Write-Output "Ready: $output/ZZZwallpaper.Launcher.exe"
} finally {Pop-Location}
