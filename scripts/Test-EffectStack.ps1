param([string]$Python='python',[switch]$SkipNativeBuild)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$run=Join-Path $root ('artifacts/effect-stack-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $root '.dotnet'
$env:NUGET_PACKAGES=Join-Path $root '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:MSBUILDDISABLENODEREUSE='1'
$env:TEMP=Join-Path $run 'temp';$env:TMP=$env:TEMP
[IO.Directory]::CreateDirectory($env:TEMP) | Out-Null
Push-Location $root
try {
    if(!$SkipNativeBuild) {
        & (Join-Path $PSScriptRoot 'Build-Native.cmd') *> (Join-Path $run 'native-build.log')
        if($LASTEXITCODE -ne 0){throw "Native build failed: $run"}
    }
    & (Join-Path $PSScriptRoot 'Build-EffectStackTests.cmd') *> (Join-Path $run 'gpu-build.log')
    if($LASTEXITCODE -ne 0){throw "GPU test build failed: $run"}
    $project='tests/EffectStackRenderFixtures/EffectStackRenderFixtures.csproj'
    dotnet restore $project --source $env:NUGET_PACKAGES --disable-parallel -m:1 -p:NuGetAudit=false *> (Join-Path $run 'fixture-restore.log')
    if($LASTEXITCODE -ne 0){throw "Fixture restore failed: $run"}
    dotnet build $project -c Release --no-restore -m:1 -p:UseSharedCompilation=false *> (Join-Path $run 'fixture-build.log')
    if($LASTEXITCODE -ne 0){throw "Fixture build failed: $run"}
    & 'tests/EffectStackRenderFixtures/bin/Release/net8.0/EffectStackRenderFixtures.exe' $run *> (Join-Path $run 'fixture-generate.log')
    if($LASTEXITCODE -ne 0){throw "Fixture generation failed: $run"}
    # This program owns an invisible window and uses synthetic frames only.
    & 'artifacts/effect-stack-tests/EffectStackGpuTests.exe' $run *> (Join-Path $run 'gpu-render.log')
    if($LASTEXITCODE -ne 0){throw "GPU fixture render failed: $run"}
    & $Python (Join-Path $PSScriptRoot 'Verify-EffectStackPixels.py') $run *> (Join-Path $run 'pixels.log')
    if($LASTEXITCODE -ne 0){throw "Pixel verification failed: $run"}
    Write-Output "Effect stack pixel checks passed: $run"
} finally {Pop-Location}
