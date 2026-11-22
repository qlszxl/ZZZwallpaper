param([string]$OutputDirectory,[switch]$Zip,[switch]$SkipTests)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts/windows-x64-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$destination=[IO.Path]::GetFullPath($OutputDirectory)
if(!$destination.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Modular build output must stay inside the independent project directory.'
}
$env:DOTNET_CLI_HOME=Join-Path $root '.dotnet'
$env:NUGET_PACKAGES=Join-Path $root '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:MSBUILDDISABLENODEREUSE='1'
$env:TEMP=Join-Path $root 'artifacts/build-temp'
$env:TMP=$env:TEMP
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
if(!$SkipTests) { & (Join-Path $PSScriptRoot 'Test-ModuleBoundaries.ps1') }
& (Join-Path $PSScriptRoot 'Build-Windows.ps1') -SkipDependencies -SkipRuntimeMetadata -CleanPackage -OutputDirectory $destination
$infoPath=Join-Path $destination 'build-info.json'
$info=Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
$info | Add-Member -NotePropertyName architecture -NotePropertyValue 'modular' -Force
$info | Add-Member -NotePropertyName dataDirectory -NotePropertyValue 'LumaWall' -Force
$info | Add-Member -NotePropertyName appId -NotePropertyValue 'D82EA01A-D414-4393-BE4D-7AD3371671C0' -Force
$info | Add-Member -NotePropertyName productName -NotePropertyValue 'ZZZwallpaper' -Force
$info | Add-Member -NotePropertyName presetPackageVersion -NotePropertyValue 1 -Force
$info | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $infoPath -Encoding utf8
foreach($name in @('Application.dll','Storage.dll','ZZZwallpaper.Launcher.exe','ZZZwallpaper.dll','WallpaperHost.exe','Widgets/ZZZwallpaper.WidgetHost.dll','ExpansionPacks/base/BasePlayback.dll','ExpansionPacks/base/ZZZwallpaper.BaseWidgets.dll','ExpansionPacks/base/TaskbarHost.exe','Mcp/ZZZwallpaper.Mcp.dll')){
    if(!(Test-Path -LiteralPath (Join-Path $destination $name) -PathType Leaf)){throw "Missing runtime module: $name"}
}
if($Zip){Compress-Archive -Path (Join-Path $destination '*') -DestinationPath ($destination+'.zip')}
Write-Output "ZZZwallpaper runtime ready: $destination"
