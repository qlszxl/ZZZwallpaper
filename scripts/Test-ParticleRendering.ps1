$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root 'artifacts/particle-tests'
$hostPath=Join-Path $root 'artifacts/native/WallpaperHost.exe'
$ffmpeg=Join-Path $root 'third_party/ffmpeg/bin/ffmpeg.exe'
$source=Join-Path $output 'black.mkv'
& $ffmpeg -v error -y -f lavfi -i 'color=c=black:s=640x360:r=30' -t 1 -an -c:v ffv1 $source
if($LASTEXITCODE -ne 0){throw 'Fixture encoding failed.'}
$names=@('none','style-0','style-1','style-2','style-3','style-4','style-6','star-random','star-random-pair','star-sub','star-sub-random','random-single','random-pair','sub-only','two-layers','tint-off','tint-on','sub-tint-off','sub-tint-on')
$results=@{}
foreach($name in $names){
 $wire=Get-Content -LiteralPath (Join-Path $output ($name+'.wire')) -Raw
 $frame=Join-Path $output ($name+'.bmp')
 $json=& $hostPath --software --file $source --rect 0 0 640 360 --effects $wire --render-frame $frame
 if($LASTEXITCODE -ne 0){throw ($name+': '+$json)}
 $results[$name]=@{hash=(Get-FileHash -LiteralPath $frame).Hash;render=($json|ForEach-Object {$_|ConvertFrom-Json}|Where-Object event -eq 'rendered'|Select-Object -Last 1)}
}
foreach($name in $names|Where-Object {$_ -ne 'none'}){if($results[$name].hash -eq $results.none.hash){throw ('Invisible particle fixture: '+$name)}}
if($results.'random-single'.hash -ne $results.'style-1'.hash){throw 'Single candidate random appearance differs from fixed appearance.'}
if($results.'tint-off'.hash -ne $results.'style-1'.hash -or $results.'tint-on'.hash -eq $results.'style-1'.hash){throw 'Primary tint switch must preserve original pixels when off and tint when on.'}
if($results.'sub-tint-off'.hash -ne $results.'sub-only'.hash -or $results.'sub-tint-on'.hash -eq $results.'sub-only'.hash){throw 'Secondary tint switch must preserve original pixels when off and tint when on.'}
if($results.'random-pair'.hash -notin @($results.'style-1'.hash,$results.'style-4'.hash)){throw 'Random appearance selected an unselected style.'}
if($results.'two-layers'.render.passes -ne ($results.'style-1'.render.passes+1)){throw 'Independent layer should add exactly one instanced draw pass.'}
if(@($names|Where-Object {$_ -like 'style-*'}|ForEach-Object {$results[$_].hash}|Select-Object -Unique).Count -ne 6){throw 'Standard particle styles are not distinct.'}
if($results.'star-random'.hash -ne $results.'style-6'.hash){throw 'Star style must render identically as fixed and sole random appearance.'}
if($results.'star-random-pair'.hash -notin @($results.'style-1'.hash,$results.'style-6'.hash)){throw 'Star style random pair selected an unselected appearance.'}
if($results.'star-sub'.hash -ne $results.'star-sub-random'.hash){throw 'Star style must render identically in fixed and random secondary modes.'}
$results|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'render-results.json') -Encoding utf8
Write-Output 'PASS: Off-screen fixtures; six standard styles; star in fixed/random primary and secondary layers; original colors with tint off; bounded random selection; one extra pass for the independent layer.'
$rotationResults=@{}
foreach($layer in @('','sub-')) {
 foreach($mode in @('random','uniform','zero')) {
  $name='rotation-'+$layer+$mode
  $wire=Get-Content -LiteralPath (Join-Path $output ($name+'.wire')) -Raw
  foreach($capture in @(1,8)) {
   $frame=Join-Path $output ($name+'-'+$capture+'.bmp')
   $json=& $hostPath --software --file $source --rect 0 0 640 360 --effects $wire --render-frame $frame --capture-frame $capture
   if($LASTEXITCODE -ne 0){throw ($name+': '+$json)}
   $rotationResults[$name+'-'+$capture]=(Get-FileHash -LiteralPath $frame).Hash
  }
 }
 $prefix='rotation-'+$layer
 if($rotationResults[$prefix+'zero-1'] -ne $rotationResults[$prefix+'zero-8']){throw 'Zero rotation must stay still across captured frames.'}
 foreach($mode in @('random','uniform')){if($rotationResults[$prefix+$mode+'-1'] -eq $rotationResults[$prefix+$mode+'-8']){throw ('Rotation did not animate: '+$prefix+$mode)}}
 if($rotationResults[$prefix+'random-1'] -ne $rotationResults[$prefix+'uniform-1']){throw 'Rotation mode changed initial angles.'}
 if($rotationResults[$prefix+'random-8'] -eq $rotationResults[$prefix+'uniform-8']){throw 'Rotation switch did not change actual rendering.'}
}
$rotationResults|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $output 'rotation-render-results.json') -Encoding utf8
Write-Output 'PASS: 12 multi-frame captures; independent primary/secondary rotation switches change animation, retain birth angles, and preserve zero-speed stillness.'
