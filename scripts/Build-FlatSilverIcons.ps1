[CmdletBinding()]
param([ValidateSet('Silver','Orange')][string]$Palette='Silver')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$paletteName = $Palette.ToLowerInvariant()
$design = Join-Path $root $(if($Palette -eq 'Orange') {'design/flat-orange-icons-20261017'} else {'design/flat-silver-icons-20261014'})
$assets = Join-Path $root 'src/Windows/ControlCenter/Assets/MetalIcons'
New-Item -ItemType Directory -Path $design,$assets -Force | Out-Null

# Original 24-unit silhouettes. No icon library, font glyphs or stock paths.
function Circle([double]$x,[double]$y,[double]$r) {
    "M $($x+$r),$y A $r,$r 0 1 0 $($x-$r),$y A $r,$r 0 1 0 $($x+$r),$y Z"
}
function Box([double]$x,[double]$y,[double]$w,[double]$h) {
    "M $x,$y H $($x+$w) V $($y+$h) H $x Z"
}
function RoundBox([double]$x,[double]$y,[double]$w,[double]$h,[double]$r) {
    "M $($x+$r),$y H $($x+$w-$r) Q $($x+$w),$y $($x+$w),$($y+$r) V $($y+$h-$r) Q $($x+$w),$($y+$h) $($x+$w-$r),$($y+$h) H $($x+$r) Q $x,$($y+$h) $x,$($y+$h-$r) V $($y+$r) Q $x,$y $($x+$r),$y Z"
}
function Diamond([double]$x,[double]$y,[double]$r) { "M $x,$($y-$r) L $($x+$r),$y $x,$($y+$r) $($x-$r),$y Z" }
function Glyph([string]$name,[string[]]$paths) { [pscustomobject]@{name=$name;paths=$paths} }

$icons = @(
    (Glyph 'color' @('M12,2 C6.5,2 2,6.4 2,12 C2,17.8 6.5,22 12,22 H15 C18.4,22 19,18.3 16.6,16.8 C14.7,15.6 15.2,13.7 17.2,13.4 C20.6,12.9 22,11.3 22,8.9 C22,4.8 17.8,2 12,2 Z ' + (Circle 6.4 10.3 1.5) + (Circle 10 6.3 1.5) + (Circle 15.4 6.1 1.5))),
    (Glyph 'rgb-split' @((Box 3 6 4 14),(Box 10 2 4 18),(Box 17 5 4 17))),
    (Glyph 'softening' @((Circle 8.5 12 6.5),(Circle 18.5 5.5 2.3),(Circle 20 12 1.8),(Circle 18.5 18.5 1.3))),
    (Glyph 'glow' @('M12,2 C13.8,8.1 15.9,10.2 22,12 C15.9,13.8 13.8,15.9 12,22 C10.2,15.9 8.1,13.8 2,12 C8.1,10.2 10.2,8.1 12,2 Z')),
    (Glyph 'vignette' @((RoundBox 2 2 20 20 5) + (RoundBox 6.2 6.2 11.6 11.6 2.5))),
    (Glyph 'film-grain' @((Circle 5 5 1.7),(Circle 11 4 2.1),(Circle 18 5 1.5),(Circle 3.5 11 1.3),(Circle 8.5 10 2),(Circle 15 11 2.4),(Circle 21 10 1.1),(Circle 5 17 2.2),(Circle 11 16 1.6),(Circle 19 17 2.1),(Circle 10 21 1.2),(Circle 15 21 1.3))),
    (Glyph 'scanlines' @(3,7,11,15,19 | ForEach-Object { Box 2 $_ 20 2.5 })),
    (Glyph 'glitch' @((Box 2 3.5 13 3),(Box 18 3.5 4 3),(Box 5 10.5 8 3),(Box 16 10.5 6 3),(Box 2 17.5 6 3),(Box 11 17.5 10 3))),
    (Glyph 'glitch-burst' @('M12,2 H20 L15,8 H9 Z','M8,10 H19 L13,15 H4 Z','M10,17 H15 L6,22 Z')),
    (Glyph 'dot-matrix' @(foreach($y in @(5,12,19)) { foreach($x in @(5,12,19)) { Circle $x $y 2.4 } })),
    (Glyph 'dot-ripple' @((Circle 12 12 10)+(Circle 12 12 7.8),(Circle 12 12 5.7)+(Circle 12 12 3.6),(Circle 12 12 1.6))),
    (Glyph 'blur' @((Circle 15 12 7),(Box 3 5 8 2.5),(Box 2 10.75 8 2.5),(Box 3 16.5 8 2.5))),
    (Glyph 'distortion' @('M2,7 C6,2.5 9,2.5 13,6 C17,9.5 19,9.5 22,6 V10 C18,13.5 15,13 11,9.5 C7,6 5,6 2,10 Z','M2,15 C6,10.5 9,10.5 13,14 C17,17.5 19,17.5 22,14 V18 C18,21.5 15,21 11,17.5 C7,14 5,14 2,18 Z')),
    (Glyph 'taskbar' @((RoundBox 2 2.5 20 15.5 1.8)+(Box 4.8 5.3 14.4 9.9),(RoundBox 3 20 18 2 1))),
    (Glyph 'audio-reactive' @((Box 2.5 13 2.8 9),(Box 6.6 7 2.8 15),(Box 10.7 2 2.8 20),(Box 14.8 10 2.8 12),(Box 18.9 5 2.8 17))),
    (Glyph 'zoom-tunnel' @((Box 2 2 20 20)+(Box 4.2 4.2 15.6 15.6),(Box 6.5 6.5 11 11)+(Box 8.7 8.7 6.6 6.6),(Box 11 11 2 2))),
    (Glyph 'particles' @((Diamond 8 8 6),(Diamond 19 4.5 2.5),(Diamond 18 17 4),(Diamond 6 20 2))),
    (Glyph 'water-wave' @('M12,2 C10,6 3.5,11 3.5,15.5 C3.5,19.5 7.1,22 12,22 C16.9,22 20.5,19.5 20.5,15.5 C20.5,11 14,6 12,2 Z M6.3,14.2 C9.5,11.2 13.4,17.6 17.7,14.2 V17 C13.4,20.4 9.5,14 6.3,17 Z')),
    (Glyph 'edge-outline' @('M2,2 H10 V5 H5 V10 H2 Z','M14,2 H22 V10 H19 V5 H14 Z','M2,14 H5 V19 H10 V22 H2 Z','M19,14 H22 V22 H14 V19 H19 Z')),
    (Glyph 'pixelate' @((Box 2 3 8 8),(Box 14 2 8 8),(Box 2 14 8 8),(Box 13 14 8 8))),
    (Glyph 'sun-prism' @('M5,19 A7,7 0 0 1 19,19 Z',(Box 10.7 2 2.6 5),'M3,5 L5,3 L8,7 L6,9 Z','M16,7 L19,3 L21,5 L18,9 Z',(Box 2 11 4.5 2.6),(Box 17.5 11 4.5 2.6))),
    (Glyph 'effect-mask' @((Circle 9 9 7)+(Circle 9 9 4.6),(RoundBox 10 10 12 12 1.5))),
    (Glyph 'search' @((Circle 9.5 9.5 7.5)+(Circle 9.5 9.5 4.7),'M14,16.5 L16.5,14 L22,19.5 L19.5,22 Z')),
    (Glyph 'performance' @((RoundBox 2 3 20 15 1.5)+(Box 4.5 5.5 15 10),'M5,11 H8 L10,7.5 L13,13.5 L15,10 H19 V12.5 H16.5 L13,18 L10,12.5 L9.5,13.5 H5 Z',(Box 10 18 4 2),(Box 6 20 12 2))),
    (Glyph 'spectrum' @((Box 2 13 2 5),(Box 5 9 2 9),(Box 8 5 2 13),(Box 11 2 2 16),(Box 14 5 2 13),(Box 17 9 2 9),(Box 20 13 2 5),(Box 2 20 20 2))),
    (Glyph 'text' @('M2,3 H22 V7 H14 V22 H10 V7 H2 Z')),
    (Glyph 'image' @((RoundBox 2 3 20 18 1.5)+(Box 4.5 5.5 15 13),'M4.5,18.5 L10,10 L14,15 L17,12 L20,18.5 Z',(Circle 17 8 1.6))),
    (Glyph 'widget-mask' @('M4.2,18.2 A10,10 0 0 1 18.2,4.2 Z','M5.8,19.8 A10,10 0 0 0 19.8,5.8 Z')),
    (Glyph 'clock' @((Circle 12 12 10)+(Circle 12 12 7.5),'M10.8,5.5 H13.2 V11.3 L18,14.1 L16.8,16.2 L10.8,12.7 Z')),
    (Glyph 'countdown' @((Box 3 2 18 2.7),(Box 3 19.3 18 2.7),'M5,4.7 H19 C19,8.5 16,10.1 14,12 C16,13.9 19,15.5 19,19.3 H5 C5,15.5 8,13.9 10,12 C8,10.1 5,8.5 5,4.7 Z M8.2,5.5 C8.2,8.1 10.3,9 12,10.5 C13.7,9 15.8,8.1 15.8,5.5 Z M8.2,18.5 H15.8 C15.8,15.9 13.7,15 12,13.5 C10.3,15 8.2,15.9 8.2,18.5 Z')),
    (Glyph 'focus' @('M10.5,2.1 A10,10 0 1 0 13.5,2.1 V4.7 A7.5,7.5 0 1 1 10.5,4.7 Z',(Circle 12 12 5)+(Circle 12 12 2.8),(Circle 12 12 1))),
    (Glyph 'shortcut' @('M8.8,10 L5.4,13.4 C2,16.8 7.2,22 10.6,18.6 L14,15.2 L16,17.2 L12.6,20.6 C6.3,26.9 -2.9,17.7 3.4,11.4 L6.8,8 Z','M9.9,8.8 L13.4,5.4 C16.8,2 22,7.2 18.6,10.6 L15.2,14 L17.2,16 L20.6,12.6 C26.9,6.3 17.7,-2.9 11.4,3.4 L8,6.8 Z','M8.2,13.8 L13.8,8.2 L15.8,10.2 L10.2,15.8 Z')),
    (Glyph 'widgets' @((RoundBox 2 2 8.5 8.5 1.5),(RoundBox 13.5 2 8.5 8.5 1.5),(RoundBox 2 13.5 8.5 8.5 1.5),(RoundBox 13.5 13.5 8.5 8.5 1.5))),
    (Glyph 'settings' @((Box 2 5 20 2),(Box 2 11 20 2),(Box 2 17 20 2),(RoundBox 5 3 3.5 6 1),(RoundBox 15 9 3.5 6 1),(RoundBox 8 15 3.5 6 1))),
    (Glyph 'compare' @((Box 2 3 8 18)+(Box 4 5 4 14),(Box 14 3 8 18)+(Box 16 5 4 14),(Box 11 2 2 20))),
    (Glyph 'layers' @('M12,2 L22,8 L12,14 L2,8 Z M12,5 L6.8,8 L12,11 L17.2,8 Z','M2,13 L12,19 L22,13 V16 L12,22 L2,16 Z'))
)

$stops = if($Palette -eq 'Orange') {
    # Use the same silhouettes and grain, with a narrow white highlight over
    # warm orange. The highlight is part of the fill, never an outer glow.
    @(@(0,'#FFB050'),@(.12,'#FFE9CB'),@(.20,'#FFFFFF'),@(.29,'#FFC06A'),@(.46,'#FF8A20'),@(.70,'#FF7118'),@(.84,'#FFAD42'),@(1,'#E85D12'))
} else {
    @(@(0,'#E6E9EA'),@(.20,'#FFFFFF'),@(.44,'#BCC3C6'),@(.65,'#E7EBED'),@(.82,'#FBFDFD'),@(1,'#CDD3D5'))
}
$silver = [Windows.Media.LinearGradientBrush]::new()
$silver.MappingMode = [Windows.Media.BrushMappingMode]::Absolute
$silver.StartPoint = [Windows.Point]::new(1,1)
$silver.EndPoint = [Windows.Point]::new(23,21)
foreach($stop in $stops) { $silver.GradientStops.Add([Windows.Media.GradientStop]::new([Windows.Media.ColorConverter]::ConvertFromString($stop[1]),$stop[0])) }
$silver.Freeze()
$grain = [Windows.Media.DrawingGroup]::new()
$grain.Children.Add([Windows.Media.GeometryDrawing]::new([Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromArgb(20,40,48,52)),$null,[Windows.Media.EllipseGeometry]::new([Windows.Point]::new(.45,.7),.028,.028)))
$grain.Children.Add([Windows.Media.GeometryDrawing]::new([Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromArgb(40,255,255,255)),$null,[Windows.Media.EllipseGeometry]::new([Windows.Point]::new(1.4,1.55),.025,.025)))
$grainBrush = [Windows.Media.DrawingBrush]::new($grain)
$grainBrush.TileMode = [Windows.Media.TileMode]::Tile
$grainBrush.ViewportUnits = $grainBrush.ViewboxUnits = [Windows.Media.BrushMappingMode]::Absolute
$grainBrush.Viewbox = $grainBrush.Viewport = [Windows.Rect]::new(0,0,2,2)
$grainBrush.Freeze()
$manifest = @()
for($atlasIndex=0;$atlasIndex -lt 2;$atlasIndex++) {
    $name = if($atlasIndex -eq 0) {"effects-$paletteName"} else {"widgets-$paletteName"}
    $first = $atlasIndex*25
    $last = [Math]::Min($first+25,$icons.Count)
    $visual = [Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $svg = [Text.StringBuilder]::new()
    [void]$svg.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" width="1500" height="1500" viewBox="0 0 1500 1500"><defs><linearGradient id="silver" gradientUnits="userSpaceOnUse" x1="1" y1="1" x2="23" y2="21">')
    foreach($stop in $stops) { [void]$svg.AppendLine("<stop offset=`"$($stop[0])`" stop-color=`"$($stop[1])`"/>") }
    [void]$svg.AppendLine('</linearGradient><pattern id="grain" patternUnits="userSpaceOnUse" width="2" height="2"><circle cx=".45" cy=".7" r=".028" fill="#283034" opacity=".0784"/><circle cx="1.4" cy="1.55" r=".025" fill="white" opacity=".157"/></pattern></defs>')
    for($index=$first;$index -lt $last;$index++) {
        $icon = $icons[$index]
        $cell = $index-$first
        $column = $cell%5
        $row = [Math]::Floor($cell/5)
        $x = $column*300+6
        $y = $row*300+6
        $dc.PushTransform([Windows.Media.TranslateTransform]::new($x,$y))
        $dc.PushTransform([Windows.Media.ScaleTransform]::new(12,12))
        [void]$svg.AppendLine("<g id=`"$($icon.name)`" transform=`"translate($x $y) scale(12)`">")
        foreach($path in $icon.paths) {
            $geometry = [Windows.Media.Geometry]::Parse('F0 '+$path)
            $geometry.Freeze()
            $dc.DrawGeometry($silver,$null,$geometry)
            $dc.PushClip($geometry)
            $dc.DrawRectangle($grainBrush,$null,[Windows.Rect]::new(0,0,24,24))
            $dc.Pop()
            [void]$svg.AppendLine("<path d=`"$path`" fill=`"url(#silver)`" fill-rule=`"evenodd`"/><path d=`"$path`" fill=`"url(#grain)`" fill-rule=`"evenodd`"/>")
        }
        [void]$svg.AppendLine('</g>')
        $dc.Pop();$dc.Pop()
        $manifest += [pscustomobject]@{name=$icon.name;index=$index;atlas="$name.png";column=$column;row=$row;cellSize=300}
    }
    $dc.Close()
    [void]$svg.AppendLine('</svg>')
    [IO.File]::WriteAllText((Join-Path $design "$name.svg"),$svg.ToString(),[Text.UTF8Encoding]::new($false))
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(1500,1500,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $assets "$name.png"))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Copy-Item -LiteralPath (Join-Path $assets "$name.png") -Destination (Join-Path $design "$name.png") -Force
    Write-Output "Created $name.png: 1500x1500, $($last-$first) original flat $paletteName icons."
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $design 'atlas-map.json') -Encoding UTF8
