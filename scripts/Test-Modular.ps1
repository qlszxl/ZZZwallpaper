param([switch]$SkipBuild,[switch]$IncludeInstallerLifecycle,[switch]$IncludeNative)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$run=Join-Path $root ('artifacts/test-results/run-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $root '.dotnet'
$env:NUGET_PACKAGES=Join-Path $root '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:MSBUILDDISABLENODEREUSE='1'
$env:TEMP=Join-Path $run 'temp';$env:TMP=$env:TEMP
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
$projects=@('ApplicationTests','StorageTests','PresentationTests','CoreTests','AutomationTests','MaterialTests','LibraryLayoutTests','WidgetContentTests','RuntimeResponsivenessTests','WindowPlacementTests','DesktopHitTestTests','MenuVolumeTests')
if($IncludeInstallerLifecycle){$projects+='InstallerTests'}
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'Test-ModuleBoundaries.ps1')
    foreach($name in $projects){
        $project=Join-Path $root "tests/$name/$name.csproj"
        if(!$SkipBuild){
            dotnet restore $project --source $env:NUGET_PACKAGES --disable-parallel -m:1 -p:NuGetAudit=false *> (Join-Path $run "$name.restore.log")
            if($LASTEXITCODE -ne 0){throw "$name restore failed; see $run"}
            dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false *> (Join-Path $run "$name.build.log")
            if($LASTEXITCODE -ne 0){throw "$name build failed; see $run"}
        }
        [xml]$xml=Get-Content -LiteralPath $project -Raw
        $framework=[string]$xml.Project.PropertyGroup.TargetFramework
        $rid=[string]$xml.Project.PropertyGroup.RuntimeIdentifier
        $binary=Join-Path $root "tests/$name/bin/Release/$framework"
        if($rid){$binary=Join-Path $binary $rid}
        $binary=Join-Path $binary "$name.exe"
        if(!(Test-Path -LiteralPath $binary)){throw "Missing compiled test: $binary"}
        if($name -in @('MaterialTests','InstallerTests')){
            & $binary (Join-Path $run ($name+'-fixture')) *> (Join-Path $run "$name.run.log")
        }else{& $binary *> (Join-Path $run "$name.run.log")}
        if($LASTEXITCODE -ne 0){throw "$name failed; see $run/$name.run.log"}
        Write-Output "$name passed."
    }
    if($IncludeNative){
        & (Join-Path $PSScriptRoot 'Test-NativeModules.cmd') *> (Join-Path $run 'native.run.log')
        if($LASTEXITCODE -ne 0){throw "Native module tests failed; see $run"}
        Write-Output 'Native module tests passed.'
    }
    Write-Output "All requested checks passed. Logs: $run"
} finally {Pop-Location}
