[CmdletBinding()]
param([string]$SourceDirectory,[string]$CompilerPath,[switch]$CheckOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$product=Get-Content -LiteralPath (Join-Path $root 'installer/Product.json') -Raw | ConvertFrom-Json
$version=[string]$product.version
if($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$' -or $product.releaseChannel -notin @('test','stable')) { throw 'Invalid product version or release channel.' }
$parsedVersion=[version]$version
$fileVersion=if($parsedVersion.Revision -lt 0) {"$version.0"} else {$version}
$channel = $product.releaseChannel
$packageVersion = if ($channel -eq 'stable') { $version } else { "$version-$channel" }
$output=Join-Path $root "artifacts/installer/$packageVersion"
New-Item -ItemType Directory -Path $output -Force | Out-Null
if(!$SourceDirectory) {
    $SourceDirectory=Join-Path $root ('artifacts/clean-build-'+[guid]::NewGuid().ToString('N'))
    & (Join-Path $PSScriptRoot 'Build-Modular.ps1') -OutputDirectory $SourceDirectory
}
$source=(Resolve-Path -LiteralPath $SourceDirectory).Path.TrimEnd('\')
$buildInfo=Get-Content -LiteralPath (Join-Path $source 'build-info.json') -Raw | ConvertFrom-Json
$sourceCommit = & (Join-Path $PSScriptRoot 'Get-SourceRevision.ps1')
if ($buildInfo.sourceCommit -ne $sourceCommit) { throw 'Runtime and installer come from different source revisions.' }
$revisionArgs = if ($sourceCommit) { "-p:SourceRevisionId=$sourceCommit" } else { '-p:IncludeSourceRevisionInInformationalVersion=false' }
if($buildInfo.version -ne $version -or $buildInfo.releaseChannel -ne $channel -or !$buildInfo.cleanPackage) { throw 'A fresh CleanPackage build with matching version and channel is required.' }
if($buildInfo.architecture -ne 'modular' -or $buildInfo.dataDirectory -ne 'LumaWall' -or $buildInfo.appId -ne 'D82EA01A-D414-4393-BE4D-7AD3371671C0') { throw 'A unified ZZZwallpaper runtime build with the original product identity is required.' }
foreach($relative in @('ZZZwallpaper.Launcher.exe','ZZZwallpaper.dll','Widgets/ZZZwallpaper.WidgetHost.dll','Mcp/ZZZwallpaper.Mcp.dll')) {
    $actual=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source $relative)).FileVersion
    if($actual -ne $fileVersion) { throw "Wrong binary version: $relative ($actual)" }
    if ($sourceCommit -and $relative.EndsWith('.dll')) {
        $binaryRevision = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source $relative)).ProductVersion
        if ($binaryRevision -ne "$version+$sourceCommit") { throw "Wrong source revision: $relative ($binaryRevision)" }
    }
}
$stage=Join-Path $output ('payload-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$excluded=[Collections.Generic.List[object]]::new()
$currentSchemas=@("video-effects.v$($buildInfo.effectsVersion).schema.json","widgets.v$($buildInfo.widgetsVersion).schema.json")
foreach($schema in $currentSchemas){if(!(Test-Path -LiteralPath (Join-Path $source $schema) -PathType Leaf)){throw "Missing current schema: $schema"}}
$allowedRoot=@('build-info.json','ZZZwallpaper.deps.json','ZZZwallpaper.runtimeconfig.json','运行说明.md','MCP使用说明.md','WindowsAppRuntime.png')+$currentSchemas
$baseManifests=@('ExpansionPacks/base/expansion.json','Widgets/ExpansionPacks/base/expansion.json','Mcp/ExpansionPacks/base/expansion.json')
$baseDigest=(Get-FileHash -LiteralPath (Join-Path $root 'src/Shared/Contracts/Assets/ExpansionPacks/base.json')).Hash
foreach($manifest in $baseManifests) {
    if((Get-FileHash -LiteralPath (Join-Path $source $manifest) -ErrorAction Stop).Hash -ne $baseDigest) {throw "Wrong bundled base pack: $manifest"}
}
$allowedRoot+=$baseManifests
$items=@(Get-ChildItem -LiteralPath $source -Recurse -Force)
if(@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Reparse points are not allowed in the clean payload.' }
foreach($file in @($items | Where-Object { !$_.PSIsContainer } | Sort-Object FullName)) {
    $relative=$file.FullName.Substring($source.Length+1).Replace('\','/')
    if($relative -match '(?i)(^|/)(ZZZwallpaper\.Warehouse[^/]*|media\.zip|warehouse-particles[^/]*|builtins\.rgba\.br)$|(^|/)Assets/(Warehouse|ScanCards|Particles)/') {throw "Optional warehouse data in base installation: $relative"}
    if($relative -in $baseManifests -and (Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json).widgets.id -contains 'Warehouse'){throw "Optional component registered in base installation: $relative"}
    if($relative -match '(^|/)(data|logs?|diagnostics|cache|posters|samples|tmp|screenshots|\.git)(/|$)' -or
       $file.Extension -in @('.db','.sqlite','.sqlite3','.log','.dmp','.mp4','.mkv','.webm','.bak','.user','.ini') -or
       $file.Name -match '^(settings|preferences|user.config|appsettings|config)\.') { throw "User/test data in build: $relative" }
    $exclude=$file.Extension -eq '.pdb' -or $relative.StartsWith('design/') -or
       $relative -in @('验证记录.md','使用说明.md','ffmpeg-capabilities.json','Assets/Branding/manifest.json','Assets/EffectPreviews/generation.json','Assets/WidgetIcons/prompts.json') -or
       ($relative -match '^[^/]+\.schema\.json$' -and $relative -notin $currentSchemas)
    if($exclude) { $excluded.Add([pscustomobject]@{path=$relative;reason='Development metadata, obsolete schema, or debug artifact'}); continue }
    $allowed=$file.Extension -in @('.dll','.exe','.pri','.winmd','.mui') -or $relative -in $allowedRoot -or
       $relative -match '^(Widgets|Mcp)/[^/]+\.(deps|runtimeconfig)\.json$' -or
       $relative -in @('Mcp/AGENTS.md','legal/UserAgreement.txt','legal/PrivacyNotice.txt') -or $relative.StartsWith('licenses/') -or
       ($relative -match '^(Widgets/)?Assets/' -and ($file.Extension -in @('.png','.jpg','.jpeg','.svg','.otf','.ttf','.ico') -or
           $relative -match '/Fonts/(LICENSE\.txt|SairaCondensed-OFL\.txt|Bungee-OFL\.txt|manifest\.json)$' -or $relative -eq 'Assets/WidgetIcons/glyph-manifest.json')) -or
       ($relative.StartsWith('Microsoft.UI.Xaml/Assets/') -and $file.Extension -in @('.png','.html'))
    if(!$allowed) { throw "File is not on the runtime allowlist: $relative" }
    if($file.Extension -in @('.txt','.md','.json','.html') -and !$relative.StartsWith('licenses/')) {
        $text=[IO.File]::ReadAllText($file.FullName)
        if($text.Contains($root,[StringComparison]::OrdinalIgnoreCase) -or $text.Contains($env:USERPROFILE,[StringComparison]::OrdinalIgnoreCase)) {
            throw "Developer machine path in payload text: $relative"
        }
    }
    $destination=Join-Path $stage $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}

# The exact legal originals used by the reader are included in the installed app.
$previewProject=Join-Path $root 'src/Windows/InstallerPreview/InstallerPreview.csproj'
& (Join-Path $PSScriptRoot 'Sync-InstallerPreviewTheme.ps1')
$env:DOTNET_CLI_HOME=Join-Path $root '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $root '.nuget/packages'
dotnet restore $previewProject --source $env:NUGET_PACKAGES --source (Join-Path $root 'third_party/build-packages') --disable-parallel -p:NuGetAudit=false -p:PublishSingleFile=true
if($LASTEXITCODE -ne 0) { throw 'Installer UI restore failed.' }
$uninstallerOutput=Join-Path $output ('uninstaller-'+[guid]::NewGuid().ToString('N'))
dotnet publish $previewProject $revisionArgs -c Release --no-restore -o $uninstallerOutput -p:AssemblyName=ZZZwallpaper.Uninstall -p:InstallerEnginePath= -p:InstallerPackageInfoPath= -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE -ne 0) { throw 'Native uninstaller UI build failed.' }
Copy-Item -LiteralPath (Join-Path $uninstallerOutput 'ZZZwallpaper.Uninstall.exe') -Destination (Join-Path $stage 'ZZZwallpaper.Uninstall.exe')
[xml]$nativeProject=Get-Content -LiteralPath $previewProject -Raw
$runtimeVersion=[string]$nativeProject.Project.PropertyGroup.RuntimeFrameworkVersion
foreach($document in $nativeProject.SelectNodes('//LegalDocument')) {
    $include=$document.GetAttribute('Include').Replace('$(RuntimeFrameworkVersion)',$runtimeVersion)
    $original=[IO.Path]::GetFullPath((Join-Path (Split-Path $previewProject -Parent) $include))
    $destination=Join-Path $stage $document.GetAttribute('Destination')
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $original -Destination $destination -Force
    if((Get-FileHash -LiteralPath $original).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw 'Legal file copy failed.' }
}
@{appId='D82EA01A-D414-4393-BE4D-7AD3371671C0';version=$version;releaseChannel=$channel;publisher=$product.publisher;agreementVersion=$product.agreementVersion} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'install-identity.json') -Encoding UTF8
$required=@('ExpansionPacks/base/BasePlayback.dll','ExpansionPacks/base/TaskbarHost.exe','ExpansionPacks/base/ZZZwallpaper.BaseWidgets.dll','ZZZwallpaper.Launcher.exe','ZZZwallpaper.Uninstall.exe','ZZZwallpaper.exe','ZZZwallpaper.dll','Application.dll','Storage.dll','Contracts.dll','运行说明.md','resources.pri','WallpaperHost.exe','AudioSpectrumHost.exe','TaskbarHost.exe','TaskbarSupport/ExplorerTAP.dll','licenses/TranslucentTB/LICENSE.md','licenses/TranslucentTB/TranslucentTB-2026.2-source.zip',
    'coreclr.dll','hostfxr.dll','Microsoft.UI.Xaml.dll','Microsoft.WindowsAppRuntime.dll','avcodec-62.dll','avformat-62.dll','avutil-60.dll','avfilter-11.dll','swscale-9.dll','swresample-6.dll',
    'Widgets/ZZZwallpaper.WidgetHost.exe','Widgets/coreclr.dll','Widgets/hostfxr.dll','Mcp/ZZZwallpaper.Mcp.exe','Mcp/coreclr.dll','Mcp/hostfxr.dll','Mcp/AGENTS.md',
    'Assets/Fonts/SourceHanSansSC-Heavy.otf','Assets/Fonts/SourceHanSansSC-Medium.otf','Assets/Branding/App.ico','Assets/Branding/wordmark.png','legal/UserAgreement.txt','legal/PrivacyNotice.txt',
    'Widgets/Assets/Fonts/SairaCondensed-Black.ttf','Widgets/Assets/Fonts/SairaCondensed-Medium.ttf','Widgets/Assets/Fonts/SairaCondensed-OFL.txt','Widgets/Assets/Branding/wordmark.png','licenses/SairaCondensed-OFL.txt',
    'Assets/Fonts/Bungee-Regular.ttf','Assets/Fonts/Bungee-OFL.txt','Widgets/Assets/Fonts/Bungee-Regular.ttf','Widgets/Assets/Fonts/Bungee-OFL.txt','licenses/Bungee-OFL.txt')
foreach($file in $required) { if(!(Test-Path -LiteralPath (Join-Path $stage $file) -PathType Leaf)) { throw "Missing runtime file: $file" } }
$files=@(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject][ordered]@{path=$_.FullName.Substring($stage.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
$manifest=Join-Path $output 'payload-manifest.json'
@{sourceCommit=$sourceCommit;product=$product.product;version=$version;releaseChannel=$channel;cleanPackage=$true;files=$files} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest -Encoding UTF8
Copy-Item -LiteralPath $manifest -Destination (Join-Path $stage 'install-payload-manifest.json')
$bytes=[long](($files | Measure-Object bytes -Sum).Sum)
$report=[ordered]@{sourceCommit=$sourceCommit;version=$version;displayVersion=$product.displayVersion;publisher=$product.publisher;supportUrl=$product.supportUrl;cleanPackage=$true;
    payloadFiles=$files.Count;payloadBytes=$bytes;excluded=@($excluded.ToArray());actualInstallationRun=$false;source=$source;staging=$stage;builtUtc=[DateTime]::UtcNow.ToString('O')}
$reportPath=Join-Path $output 'build-report.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
if($CheckOnly) { Write-Output "Clean payload verified: $stage ($($files.Count) files)"; return }
if(!$CompilerPath) { $CompilerPath=Join-Path $root 'third_party/inno-setup-6.7.3/tools/ISCC.exe' }
$CompilerPath=(Resolve-Path -LiteralPath $CompilerPath).Path
$signature=Get-AuthenticodeSignature -LiteralPath $CompilerPath
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B.V.') { throw 'Unexpected installer compiler signature.' }
$compilerVersion='6.7.3'
# ISCC and ISCmplr intentionally have 0.0.0.0 PE versions. Engine.iss checks
# the compiler's own VER preprocessor constant instead of those PE fields.
$fileList=Join-Path $output ('PayloadFiles-'+[guid]::NewGuid().ToString('N')+'.iss')
# Generate an explicit file list from the audited manifest, never Source: payload\*.
$lines=@(foreach($entry in @($files)+@(@{path='install-payload-manifest.json'})) {
    $sourceFile=(Join-Path $stage $entry.path).Replace('"','""')
    $subdirectory=[IO.Path]::GetDirectoryName($entry.path.Replace('/','\'))
    $target=if($subdirectory) {'{app}\'+$subdirectory} else {'{app}'}
    $relative=$entry.path.Replace('/','\').Replace("'","''").Replace('"','""')
    'Source: "'+$sourceFile+'"; DestDir: "'+$target+'"; Flags: ignoreversion; BeforeInstall: BeforePayloadFile('''+$relative+'''); AfterInstall: AfterPayloadFile'
})
$lines | Set-Content -LiteralPath $fileList -Encoding UTF8
$engineOutput=Join-Path $output ('engine-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $engineOutput | Out-Null
$arguments=@('/Qp',"/DPayloadDir=$stage","/DPayloadFiles=$fileList","/DPayloadCount=$($lines.Count)","/DAppVersion=$version","/DAppDisplayVersion=$($product.displayVersion)","/DAppFileVersion=$fileVersion","/DAgreementVersion=$($product.agreementVersion)","/DAppSupportURL=$($product.supportUrl)","/O$engineOutput",(Join-Path $root 'installer/Engine.iss'))
& $CompilerPath @arguments *> (Join-Path $output 'compiler.log')
if($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $output 'compiler.log') -Tail 35; throw 'Installer engine compilation failed.' }
$engine=Join-Path $engineOutput 'ZZZwallpaper-Engine.exe'
$packageInfo=Join-Path $output 'package-info.json'
@{sourceCommit=$sourceCommit;version=$version;engineSha256=(Get-FileHash -LiteralPath $engine).Hash.ToLowerInvariant();payloadBytes=$bytes;payloadFiles=$files.Count;
  payloadManifestSha256=(Get-FileHash -LiteralPath $manifest).Hash.ToLowerInvariant();cleanPackage=$true} | ConvertTo-Json | Set-Content -LiteralPath $packageInfo -Encoding UTF8
$bootstrapOutput=Join-Path $output ('bootstrap-'+[guid]::NewGuid().ToString('N'))
dotnet publish $previewProject $revisionArgs -c Release --no-restore -o $bootstrapOutput -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false "-p:InstallerEnginePath=$engine" "-p:InstallerPackageInfoPath=$packageInfo"
if($LASTEXITCODE -ne 0) { throw 'Unified installer build failed.' }
$setup=Join-Path $output "ZZZwallpaper-$packageVersion-windows-x64-setup.exe"
Copy-Item -LiteralPath (Join-Path $bootstrapOutput 'ZZZwallpaper.Setup.exe') -Destination $setup -Force
$hash=(Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath "$setup.sha256" -Encoding ASCII
$report['installer']=$setup
$report['installerSha256']=$hash
$report['installerBytes']=(Get-Item -LiteralPath $setup).Length
$report['installerSignature']=(Get-AuthenticodeSignature -LiteralPath $setup).Status.ToString()
$report['engineCompilerVersion']=$compilerVersion
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output "Unified clean installer built (not installed): $setup"
