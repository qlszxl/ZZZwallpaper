param([string]$HostExe='',[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$HostExe) {$HostExe=Join-Path $root 'artifacts/native/WallpaperHost.exe'}
if(!$OutputDirectory) {$OutputDirectory=Join-Path $root 'artifacts/test-results'}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$samples=Join-Path $root 'artifacts/samples'
New-Item -ItemType Directory -Force $samples | Out-Null
$ffmpeg=Join-Path $root 'third_party/ffmpeg/bin/ffmpeg.exe'
$results=[System.Collections.Generic.List[object]]::new()
$cases=@(
    @{name='AVI MPEG-4';file='mpeg4.avi';codec='mpeg4';extra=@()},
    @{name='MKV FFV1';file='ffv1.mkv';codec='ffv1';extra=@()},
    @{name='WebM VP8';file='vp8.webm';codec='libvpx';extra=@('-deadline','realtime')},
    @{name='WebM VP9';file='vp9.webm';codec='libvpx-vp9';extra=@('-deadline','realtime')},
    @{name='MP4 AV1';file='av1.mp4';codec='libsvtav1';extra=@('-preset','12')},
    @{name='MOV MJPEG';file='mjpeg.mov';codec='mjpeg';extra=@()},
    @{name='TS MPEG-2';file='mpeg2.ts';codec='mpeg2video';extra=@()},
    @{name='OGV Theora';file='theora.ogv';codec='libtheora';extra=@()}
)
foreach($case in $cases) {
    $path=Join-Path $samples $case.file
    $extra=$case.extra
    & $ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=320x180:rate=30' -t 1 -an -c:v $case.codec @extra -y $path 2> (Join-Path $OutputDirectory 'encode.log')
    if($LASTEXITCODE -ne 0) {throw "Fixture generation failed: $($case.name)"}
    $probe=(& $HostExe --probe --software --file $path | ConvertFrom-Json)
    $lines=@(& $HostExe --smoke 3 --software --file $path)
    $exitCode=$LASTEXITCODE
    $events=@($lines | ForEach-Object {$_ | ConvertFrom-Json})
    $last=$events[-1]
    $passed=$probe.ok -and $exitCode -eq 0 -and $last.event -eq 'stopped' -and $last.frames -ge 75 -and $last.loops -ge 2
    $results.Add([pscustomobject]@{case=$case.name;passed=$passed;codec=$probe.codec;frames=$last.frames;loops=$last.loops;error=($events | Where-Object event -eq 'error' | Select-Object -ExpandProperty error)})
    Write-Output "$($case.name): $passed, $($last.frames) frames, $($last.loops) loops"
}
$invalid=Join-Path $samples 'invalid.mp4'
[IO.File]::WriteAllText($invalid,'This is intentionally not a video.')
$invalidProbe=(& $HostExe --probe --file $invalid | ConvertFrom-Json)
$results.Add([pscustomobject]@{case='Corrupt source rejected';passed=(!$invalidProbe.ok -and $LASTEXITCODE -ne 0);error=$invalidProbe.error})
$results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'native-tests.json') -Encoding utf8
& $HostExe --capabilities | Set-Content (Join-Path $OutputDirectory 'ffmpeg-capabilities.json') -Encoding utf8
if($results.Where({!$_.passed}).Count -gt 0) {throw 'Native media tests failed; inspect native-tests.json.'}
