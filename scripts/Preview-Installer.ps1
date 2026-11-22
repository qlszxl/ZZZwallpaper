[CmdletBinding()]
param([string]$OutputDirectory)

# Builds a browser-only review. Does not invoke ISCC, Setup, an app, registry,
# shortcuts, startup registration, or any writes to the selected install path.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory=Join-Path $root 'artifacts/installer-preview' }
$output=[IO.Path]::GetFullPath($OutputDirectory)
$messages=[ordered]@{}
foreach($line in [IO.File]::ReadAllLines((Join-Path $root 'installer/FlowMessages.isl'))) {
    if($line -match '^([A-Za-z][A-Za-z0-9]+)=(.*)$') { $messages[$Matches[1]]=$Matches[2].Replace('%n',"`n") }
}
$product=Get-Content -LiteralPath (Join-Path $root 'installer/Product.json') -Raw | ConvertFrom-Json
$version=[string]$product.version
$payloadMiB=$null
$reportPath=Join-Path $root "artifacts/installer/$version/preflight-report.json"
if(Test-Path -LiteralPath $reportPath) { $payloadMiB=(Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json).payloadMiB }
$config=[ordered]@{
    version=$version; channel=($product.displayVersion -replace '\s+v[\d.]+$',''); previewOnly=$true
    # Show the conventional user-profile location, not the preview process's
    # potentially redirected/sandboxed LOCALAPPDATA. Setup resolves its own
    # {localappdata} known folder when an actual installation is later run.
    defaultDirectory=(Join-Path $env:USERPROFILE 'AppData/Local/Programs/ZZZwallpaper').Replace('/','\')
    payloadMiB=$payloadMiB; messages=$messages
}
$json=($config | ConvertTo-Json -Depth 6 -Compress).Replace('<','\u003c').Replace('>','\u003e').Replace('&','\u0026')
$template=[IO.File]::ReadAllText((Join-Path $root 'installer/preview/index.html'))
if(!$template.Contains('__PREVIEW_CONFIG__')) {throw 'Preview configuration placeholder missing.'}
New-Item -ItemType Directory -Path $output,(Join-Path $output 'assets') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $output 'index.html'),$template.Replace('__PREVIEW_CONFIG__',$json),[Text.UTF8Encoding]::new($false))
foreach($name in @('preview.css','preview.js')) { Copy-Item -LiteralPath (Join-Path $root "installer/preview/$name") -Destination $output -Force }
Copy-Item -LiteralPath (Join-Path $root 'src/Windows/ControlCenter/Assets/Branding/wordmark.png') -Destination (Join-Path $output 'assets/wordmark.png') -Force
foreach($weight in @('Heavy','Medium')) {
    Copy-Item -LiteralPath (Join-Path $root "src/Windows/ControlCenter/Assets/Fonts/SourceHanSansSC-$weight.otf") -Destination (Join-Path $output "assets/SourceHanSansSC-$weight.otf") -Force
}
Copy-Item -LiteralPath (Join-Path $root 'src/Windows/ControlCenter/Assets/Fonts/LICENSE.txt') -Destination (Join-Path $output 'assets/SourceHanSans-OFL.txt') -Force
Write-Output "Preview ready: $output/index.html"
Write-Output 'Preview only: no software installation or system changes were performed.'
