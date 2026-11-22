param([Parameter(Mandatory)][string]$Runtime,[Parameter(Mandatory)][string]$PreviousRuntime,[Parameter(Mandatory)][string]$ProtectedKey,[Parameter(Mandatory)][string]$Work)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$Runtime=(Resolve-Path -LiteralPath $Runtime).Path
$PreviousRuntime=(Resolve-Path -LiteralPath $PreviousRuntime).Path
$Work=[IO.Path]::GetFullPath($Work)
if(Test-Path -LiteralPath $Work){throw 'Use a fresh test directory.'}
[IO.Directory]::CreateDirectory($Work)|Out-Null
$env:ZZZWALLPAPER_FEATURE_PACK_ROOT=Join-Path $Work 'installed'
$env:ZZZWALLPAPER_BASE_DIRECTORY=$null
$env:DOTNET_CLI_HOME=Join-Path $root '.dotnet'
$env:NUGET_PACKAGES=Join-Path $root '.nuget/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$test=Join-Path $root 'tests/FeaturePackTests'
foreach($project in @('Fixture','Check')){
    $projectPath=Join-Path $test "$project/$project.csproj"
    & dotnet build $projectPath -c Release -m:1 --source $env:NUGET_PACKAGES -p:UseSharedCompilation=false -p:NuGetAudit=false "-p:RuntimeDir=$Runtime" -o (Join-Path $Work $project)
    if($LASTEXITCODE){throw "Test build failed: $project"}
}
$pack=[IO.Directory]::CreateDirectory((Join-Path $Work 'sign-addon')).FullName
$compiler=Get-ChildItem -Path "${env:ProgramFiles(x86)}/Windows Kits/10/bin/*/x64/fxc.exe" | Sort-Object FullName | Select-Object -Last 1
& $compiler.FullName /nologo /T ps_5_0 /E main /Fo (Join-Path $pack 'invert.cso') (Join-Path $test 'Fixture/invert.hlsl')
if($LASTEXITCODE){throw 'Test shader compilation failed.'}
Copy-Item -LiteralPath (Join-Path $test 'Fixture/expansion.json'),(Join-Path $Work 'Fixture/Fixture.dll') -Destination $pack
& (Join-Path $PSScriptRoot 'Build-FeaturePack.ps1') -Directory $pack -OutputFile (Join-Path $Work 'addon.zip') -ProtectedKey $ProtectedKey
$bundle=Join-Path $Runtime 'ExpansionPacks/base'
Copy-Item -LiteralPath (Join-Path $Runtime 'ExpansionPacks') -Destination (Join-Path $Work 'Check/ExpansionPacks') -Recurse
$upgrade=Join-Path $Work 'sign-upgrade'
Copy-Item -LiteralPath $bundle -Destination $upgrade -Recurse
$manifest=Join-Path $upgrade 'expansion.json'
$spec=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
$spec.version='1.1.1'
$testSpec=Get-Content -LiteralPath (Join-Path $pack 'expansion.json') -Raw|ConvertFrom-Json
$spec.effects+=@($testSpec.effects)
$spec.widgets+=@($testSpec.widgets)
Copy-Item -LiteralPath (Join-Path $pack 'invert.cso'),(Join-Path $pack 'Fixture.dll') -Destination $upgrade
$spec|ConvertTo-Json -Depth 20|Set-Content -LiteralPath $manifest -Encoding utf8
& (Join-Path $PSScriptRoot 'Build-FeaturePack.ps1') -Directory $upgrade -OutputFile (Join-Path $Work 'base-upgrade.zip') -ProtectedKey $ProtectedKey
foreach($phase in @('install','upgraded')){
    & dotnet (Join-Path $Work 'Check/WidgetContentTests.dll') $Work $Runtime $PreviousRuntime $phase
    if($LASTEXITCODE){throw "Feature pack lifecycle failed: $phase"}
}
$source=Join-Path $root 'src/Windows/ControlCenter/Assets/EffectPreviews/calibration.png'
foreach($name in @('baseline','addon')){
    $fx=(Get-Content -LiteralPath (Join-Path $Work "$name.wire") -Raw)
    & (Join-Path $Runtime 'WallpaperHost.exe') --file $source --image --headless --render-frame (Join-Path $Work "$name.bmp") --rect 0 0 640 360 --effects $fx
    if($LASTEXITCODE){throw "Native rendering failed: $name"}
}
foreach($build in @($PreviousRuntime,$Runtime)){
    $name=if($build -eq $Runtime){'legacy-current.bmp'}else{'legacy-previous.bmp'}
    $fx=Get-Content -LiteralPath (Join-Path $Work 'legacy.wire') -Raw
    & (Join-Path $build 'WallpaperHost.exe') --file $source --image --headless --render-frame (Join-Path $Work $name) --rect 0 0 640 360 --effects $fx
    if($LASTEXITCODE){throw 'Legacy rendering failed.'}
}
if((Get-FileHash -LiteralPath (Join-Path $Work 'legacy-current.bmp')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Work 'legacy-previous.bmp')).Hash){throw 'Previous effects render differently after migration.'}
Write-Output 'PASS previous and current renderer produce identical frames for saved effects'
Add-Type -AssemblyName System.Drawing
$original=[Drawing.Bitmap]::new((Join-Path $Work 'baseline.bmp'))
$effect=[Drawing.Bitmap]::new((Join-Path $Work 'addon.bmp'))
try{
    $checked=0
    foreach($y in 40,100,180,250){foreach($x in 25,160,320,440,550){
        $a=$original.GetPixel($x,$y);$b=$effect.GetPixel($x,$y)
        if([Math]::Abs($a.R+$b.R-255) -gt 2 -or [Math]::Abs($a.G+$b.G-255) -gt 2 -or [Math]::Abs($a.B+$b.B-255) -gt 2){throw "Packaged shader mismatch at $x,$y"}
        $checked++
    }}
    Write-Output "PASS packaged shader output inverts all $checked sampled pixels"
}finally{$original.Dispose();$effect.Dispose()}
