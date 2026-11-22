#requires -Version 7
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Get-TaskbarDependencies.ps1')
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ffmpeg.lock.json') -Raw | ConvertFrom-Json
$thirdParty = Join-Path $root 'third_party'
$archive = Join-Path $thirdParty $pin.name
$destination = Join-Path $thirdParty 'ffmpeg'
$ready = Join-Path $destination '.archive-sha256'
$guard = [IO.File]::Open((Join-Path $thirdParty '.ffmpeg-restore.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    if (!(Test-Path -LiteralPath $archive -PathType Leaf)) {
        $download = Join-Path $thirdParty ([IO.Path]::GetRandomFileName())
        try {
            try { Invoke-WebRequest -Uri $pin.url -OutFile $download }
            catch {
                if (!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -notin 401,403,404 -or !(Get-Command gh -ErrorAction SilentlyContinue)) { throw }
                gh release download $pin.releaseTag --repo $pin.repository --pattern $pin.name --output $download --clobber
                if ($LASTEXITCODE -ne 0) { throw "Download the locked FFmpeg ZIP from $($pin.url) into $thirdParty and rerun." }
            }
            if ((Get-FileHash -LiteralPath $download).Hash -ne $pin.sha256) { throw 'Downloaded FFmpeg archive SHA-256 mismatch.' }
            [IO.File]::Move($download,$archive)
        } finally { [IO.File]::Delete($download) }
    }
    if ((Get-FileHash -LiteralPath $archive).Hash -ne $pin.sha256) { throw "FFmpeg archive SHA-256 mismatch: $archive" }
    if (!$Force -and (Test-Path -LiteralPath $ready) -and (Get-Content -LiteralPath $ready -Raw).Trim() -eq $pin.sha256 -and
        (Test-Path -LiteralPath "$destination/include/libavcodec/avcodec.h") -and (Test-Path -LiteralPath "$destination/lib/avcodec.lib") -and
        (Test-Path -LiteralPath "$destination/bin/avcodec-62.dll")) { return }
    [IO.File]::Delete($ready)
    $unpacked = Join-Path $thirdParty "unpacked/$($pin.sha256)"
    Expand-Archive -LiteralPath $archive -DestinationPath $unpacked -Force
    $sources = @(Get-ChildItem -LiteralPath $unpacked -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'include/libavcodec/avcodec.h') })
    if ($sources.Count -ne 1) { throw 'Expected one FFmpeg development package.' }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Get-ChildItem -LiteralPath $sources[0].FullName | Copy-Item -Destination $destination -Recurse -Force
    [IO.File]::WriteAllText($ready,$pin.sha256)
    Write-Output "FFmpeg archive verified: $($pin.sha256)"
} finally { $guard.Dispose() }
